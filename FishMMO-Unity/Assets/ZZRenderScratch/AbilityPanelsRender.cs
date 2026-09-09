using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// Renders the abilities panel, the crafting panel and an ability tooltip to PNGs.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Headless: <c>-executeMethod FishMMO.RenderScratch.AbilityPanelsRender.Render</c> with a
	/// display (xvfb) and WITHOUT <c>-quit</c>; the captures need frames to elapse, so this runs as
	/// an <c>EditorApplication.update</c> pump and exits itself.
	/// </para>
	/// <para>
	/// The character is a real <c>PlayerCharacter</c> from <see cref="Rig"/> carrying a faked
	/// ability controller, seeded with the project's own ability and effect templates. The base
	/// abilities are cloned in memory before their slot counts are raised, so nothing on disk is
	/// touched: the shipped templates allow no additional effects, and an empty crafting panel
	/// shows none of what the panel is for.
	/// </para>
	/// </remarks>
	public static class AbilityPanelsRender
	{
		private const string OUTPUT_DIR = "/home/jim/Dev/FishMMO-Dev/PanelRenders";
		private const string PANEL_SETTINGS_PATH = "Assets/UI Toolkit/PanelSettings.asset";
		private const string ABILITIES_UXML = "Assets/Scripts/Client/GUI/World/Ability/UIAbilities.uxml";
		private const string CRAFT_UXML = "Assets/Scripts/Client/GUI/World/Ability/Crafting/UIAbilityCraft.uxml";
		private const string TOOLTIP_UXML = "Assets/Scripts/Client/GUI/Shared/Tooltip/UITooltip.uxml";
		private const int WIDTH = 1000;
		private const int HEIGHT = 640;

		/// <summary>USS transitions on the theme are ~0.12s; this many frames lets them settle.</summary>
		private const int SETTLE_FRAMES = 40;

		private sealed class Job
		{
			public string Label;
			public string Uxml;
			public Action<GameObject, UIDocument> Populate;
		}

		private static readonly List<Job> queue = new List<Job>();
		private static GameObject host;
		private static UIDocument document;
		private static PanelSettings settings;
		private static RenderTexture texture;
		private static Job current;
		private static int framesWaited;

		[MenuItem("FishMMO/UI Toolkit/Render Ability Panels")]
		public static void Render()
		{
			try
			{
				Directory.CreateDirectory(OUTPUT_DIR);
				AbilityFixtures.Load();

				queue.Clear();
				queue.Add(new Job { Label = "UIAbilities-abilities", Uxml = ABILITIES_UXML, Populate = (h, d) => AbilityPanels.Abilities(h, d, AbilityTabType.Ability) });
				queue.Add(new Job { Label = "UIAbilities-knowledge", Uxml = ABILITIES_UXML, Populate = (h, d) => AbilityPanels.Abilities(h, d, AbilityTabType.Knowledge) });
				queue.Add(new Job { Label = "UIAbilities-knowledge-effects", Uxml = ABILITIES_UXML, Populate = (h, d) => AbilityPanels.Abilities(h, d, AbilityTabType.Knowledge, KnowledgeFilterType.Effects) });
				queue.Add(new Job { Label = "UIAbilityCraft-populated", Uxml = CRAFT_UXML, Populate = (h, d) => AbilityPanels.Craft(h, d, populated: true) });
				queue.Add(new Job { Label = "UIAbilityCraft-empty", Uxml = CRAFT_UXML, Populate = (h, d) => AbilityPanels.Craft(h, d, populated: false) });
				queue.Add(new Job { Label = "UITooltip-ability", Uxml = TOOLTIP_UXML, Populate = AbilityPanels.Tooltip });

				EditorApplication.update -= Pump;
				EditorApplication.update += Pump;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[AbilityRender] setup failed: {ex}");
				if (Application.isBatchMode) EditorApplication.Exit(1);
			}
		}

		private static void Pump()
		{
			try
			{
				if (current == null)
				{
					if (queue.Count == 0)
					{
						EditorApplication.update -= Pump;
						Teardown();
						Debug.Log("[AbilityRender] done");
						if (Application.isBatchMode) EditorApplication.Exit(0);
						return;
					}

					current = queue[0];
					queue.RemoveAt(0);
					framesWaited = 0;
					Mount(current);
					return;
				}

				++framesWaited;
				if (framesWaited < SETTLE_FRAMES)
				{
					document?.rootVisualElement?.MarkDirtyRepaint();
					return;
				}

				Capture(Path.Combine(OUTPUT_DIR, current.Label + ".png"));
				Debug.Log($"[AbilityRender] wrote {current.Label}.png");
				Unmount();
				current = null;
			}
			catch (Exception ex)
			{
				EditorApplication.update -= Pump;
				Debug.LogError($"[AbilityRender] pump failed on {current?.Label}: {ex}");
				Teardown();
				if (Application.isBatchMode) EditorApplication.Exit(1);
			}
		}

		private static void Mount(Job job)
		{
			if (texture == null)
			{
				/* One render target for the whole run. Creating and destroying a RenderTexture per
				 * capture crashes the software GL rasteriser used under xvfb. */
				texture = new RenderTexture(WIDTH, HEIGHT, 24, RenderTextureFormat.ARGB32);
				texture.Create();
			}

			if (settings == null)
			{
				settings = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(PANEL_SETTINGS_PATH));
				settings.hideFlags = HideFlags.HideAndDontSave;
				settings.targetTexture = texture;
				settings.clearColor = true;
				settings.colorClearValue = new Color(0.02f, 0.04f, 0.05f, 1.0f);
			}

			host = new GameObject("Render_" + job.Label) { hideFlags = HideFlags.HideAndDontSave };
			document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(job.Uxml);

			job.Populate?.Invoke(host, document);
			document.rootVisualElement?.MarkDirtyRepaint();
		}

		private static void Unmount()
		{
			if (host != null) UnityEngine.Object.DestroyImmediate(host);
			host = null;
			document = null;
		}

		private static void Capture(string path)
		{
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = texture;
			try
			{
				Texture2D image = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGBA32, false);
				image.ReadPixels(new Rect(0, 0, WIDTH, HEIGHT), 0, 0);
				image.Apply();
				File.WriteAllBytes(path, image.EncodeToPNG());
				UnityEngine.Object.DestroyImmediate(image);
			}
			finally
			{
				RenderTexture.active = previous;
			}
		}

		private static void Teardown()
		{
			Unmount();
			if (settings != null) UnityEngine.Object.DestroyImmediate(settings);
			if (texture != null)
			{
				texture.Release();
				UnityEngine.Object.DestroyImmediate(texture);
			}
			settings = null;
			texture = null;
		}
	}

	/// <summary>
	/// The ability knowledge the rendered panels are populated from.
	/// </summary>
	public static class AbilityFixtures
	{
		/// <summary>Base abilities, cloned so their slot counts can be raised safely.</summary>
		public static readonly List<AbilityTemplate> Bases = new List<AbilityTemplate>();

		/// <summary>Effects, straight from the project.</summary>
		public static readonly List<AbilityEvent> Effects = new List<AbilityEvent>();

		/// <summary>Type overrides, which are templates rather than events.</summary>
		public static readonly List<AbilityTypeOverrideEventType> Overrides = new List<AbilityTypeOverrideEventType>();

		private static bool loaded;

		/// <summary>Loads and registers everything the panels will look up by ID.</summary>
		public static void Load()
		{
			if (loaded)
			{
				return;
			}
			loaded = true;

			/* Slot counts, by base ability name. The shipped templates all ship at zero — crafting
			 * is newer than the content — and a crafting panel with no slots demonstrates nothing.
			 * The clones carry these instead of the assets, so nothing on disk changes. */
			Dictionary<string, byte> slots = new Dictionary<string, byte>
			{
				{ "Lesser Fireball", 3 },
				{ "Lesser Flame", 2 },
				{ "Punch", 1 },
				{ "Mock Chain Lightning", 3 },
				{ "Mock Line Lance", 2 },
				{ "Mock Point Blank Nova", 2 },
			};

			foreach (string guid in AssetDatabase.FindAssets("t:AbilityTemplate", new[] { "Assets/Templates/Entity/Abilities" }))
			{
				AbilityTemplate asset = AssetDatabase.LoadAssetAtPath<AbilityTemplate>(AssetDatabase.GUIDToAssetPath(guid));
				if (asset == null || !slots.TryGetValue(asset.name, out byte allowance))
				{
					continue;
				}

				AbilityTemplate clone = UnityEngine.Object.Instantiate(asset);
				clone.hideFlags = HideFlags.HideAndDontSave;
				clone.name = asset.name;
				clone.AdditionalEventSlots = allowance;

				// The registration the boot-time addressables loader performs.
				clone.AddToCache(clone.name);
				Bases.Add(clone);
			}

			foreach (string guid in AssetDatabase.FindAssets("t:AbilityEvent", new[] { "Assets/Templates/Entity/Abilities/Events" }))
			{
				AbilityEvent asset = AssetDatabase.LoadAssetAtPath<AbilityEvent>(AssetDatabase.GUIDToAssetPath(guid));
				if (asset == null)
				{
					continue;
				}
				asset.AddToCache(asset.name);
				Effects.Add(asset);
			}

			/* Two effects that actually move the numbers, created in memory.
			 *
			 * Every effect the project ships modifies nothing — its cost is entirely in what its
			 * ECA actions do — so a crafting preview built from them has no deltas to draw and
			 * demonstrates none of what the preview is for. These are harness fixtures and are
			 * deliberately not assets: they exist for the capture, not for the game. */
			Effects.Add(DemoEffect<AbilityOnSpawnEvent>("Giant", cooldown: 2.0f, activation: 0.4f, speed: 0.0f, lifeTime: 0.0f, price: 60));
			Effects.Add(DemoEffect<AbilityOnTickEvent>("Swiftfire", cooldown: -0.5f, activation: -0.2f, speed: 8.0f, lifeTime: 0.0f, price: 45));

			foreach (string guid in AssetDatabase.FindAssets("t:AbilityTypeOverrideEventType", new[] { "Assets/Templates/Entity/Abilities" }))
			{
				AbilityTypeOverrideEventType asset = AssetDatabase.LoadAssetAtPath<AbilityTypeOverrideEventType>(AssetDatabase.GUIDToAssetPath(guid));
				if (asset == null)
				{
					continue;
				}
				asset.AddToCache(asset.name);
				Overrides.Add(asset);
			}

			// The currency the crafting panel prices against.
			foreach (string guid in AssetDatabase.FindAssets("t:CharacterAttributeTemplate", new[] { "Assets/Templates/Entity/CharacterAttributes" }))
			{
				CharacterAttributeTemplate template = AssetDatabase.LoadAssetAtPath<CharacterAttributeTemplate>(AssetDatabase.GUIDToAssetPath(guid));
				template?.AddToCache(template.name);
			}
		}

		/// <summary>Builds one in-memory effect that modifies an ability's numbers.</summary>
		private static T DemoEffect<T>(string name, float cooldown, float activation, float speed, float lifeTime, int price)
			where T : AbilityEvent
		{
			T effect = ScriptableObject.CreateInstance<T>();
			effect.hideFlags = HideFlags.HideAndDontSave;
			effect.name = name;
			effect.Cooldown = cooldown;
			effect.ActivationTime = activation;
			effect.Speed = speed;
			effect.LifeTime = lifeTime;
			effect.Price = price;
			effect.AddToCache(effect.name);
			return effect;
		}

		/// <summary>The currency template's deterministic ID, for the crafting panel's inspector field.</summary>
		public static int CurrencyTemplateID()
		{
			foreach (string guid in AssetDatabase.FindAssets("t:CharacterAttributeTemplate", new[] { "Assets/Templates/Entity/CharacterAttributes" }))
			{
				CharacterAttributeTemplate template = AssetDatabase.LoadAssetAtPath<CharacterAttributeTemplate>(AssetDatabase.GUIDToAssetPath(guid));
				if (template != null && template.name == "Currency")
				{
					return template.ID;
				}
			}
			return 0;
		}

		/// <summary>Finds a loaded base ability by name.</summary>
		public static AbilityTemplate Base(string name) => Bases.Find(t => t.name == name);

		/// <summary>Finds a loaded effect by name.</summary>
		public static AbilityEvent Effect(string name) => Effects.Find(t => t.name == name);
	}

	/// <summary>Mounts each ability panel on a rigged character and fills it.</summary>
	public static class AbilityPanels
	{
		/// <summary>
		/// The abilities panel, showing one tab.
		/// </summary>
		public static void Abilities(GameObject h, UIDocument d, AbilityTabType tab,
			KnowledgeFilterType filter = KnowledgeFilterType.All)
		{
			PlayerCharacter character = BuildCharacter(h);

			UITKAbilities panel = h.AddComponent<UITKAbilities>();
			panel.Document = d;
			panel.StartOpen = false;
			Awake(panel);

			panel.Show();
			panel.SetCharacter(character);
			panel.SwitchTab(tab);
			panel.SwitchFilter(filter);

			// Populated details pane rather than its placeholder: what a player sees after a click.
			SelectFirstEntry(panel, tab, filter);
		}

		/// <summary>
		/// The crafting panel, optionally with a recipe already assembled.
		/// </summary>
		/// <param name="populated">
		/// True to build a fireball with a movement effect and a fire-damage effect, which is what
		/// the preview and its deltas exist to show.
		/// </param>
		public static void Craft(GameObject h, UIDocument d, bool populated)
		{
			PlayerCharacter character = BuildCharacter(h);

			UITKAbilityCraft panel = h.AddComponent<UITKAbilityCraft>();
			panel.Document = d;
			panel.StartOpen = false;
			panel.CurrencyTemplateID = AbilityFixtures.CurrencyTemplateID();
			Awake(panel);

			panel.Show();
			panel.SetCharacter(character);

			if (populated)
			{
				AbilityTemplate fireball = AbilityFixtures.Base("Lesser Fireball");
				List<ITooltip> chosen = new List<ITooltip>
				{
					AbilityFixtures.Effect("Giant"),
					AbilityFixtures.Effect("Swiftfire"),
					null,
				};

				/* Reflection, because the recipe is private state the panel owns and the shipped
				 * way in is a modal selector this harness cannot drive. The panel is then asked to
				 * redraw itself exactly as it would after a selection. */
				Set(panel, "selectedMain", fireball);
				Set(panel, "selectedSlotCount", fireball != null ? (int)fireball.AdditionalEventSlots : 0);

				var events = (List<ITooltip>)Field(panel, "selectedEvents").GetValue(panel);
				events.Clear();
				events.AddRange(chosen);
			}

			Invoke(panel, "ApplySelection");
		}

		/// <summary>The cursor tooltip, showing a crafted ability.</summary>
		public static void Tooltip(GameObject h, UIDocument d)
		{
			UITKTooltip panel = h.AddComponent<UITKTooltip>();
			panel.Document = d;
			panel.StartOpen = false;
			Awake(panel);

			AbilityTemplate fireball = AbilityFixtures.Base("Lesser Fireball");
			Ability ability = BuildAbility(1, fireball, new List<int>
			{
				AbilityFixtures.Effect("Projectile Forward Move Event")?.ID ?? 0,
				AbilityFixtures.Effect("Lesser Fire Damage")?.ID ?? 0,
			});

			panel.Open(ability);

			/* Parked where it can be seen. The tooltip normally follows the cursor, and in a
			 * headless capture there is no cursor to follow, so it would otherwise sit in the
			 * corner half off the surface. */
			VisualElement box = d.rootVisualElement?.Q("tooltip-box");
			if (box != null)
			{
				box.style.left = 40;
				box.style.top = 40;
			}
		}

		/// <summary>Builds the character both panels read from.</summary>
		private static PlayerCharacter BuildCharacter(GameObject h)
		{
			PlayerCharacter character = Rig.Build(h);
			FakeAbilities abilities = new FakeAbilities(character);

			FieldInfo field = typeof(BaseCharacter).GetField("Behaviours",
				BindingFlags.NonPublic | BindingFlags.Instance);
			var behaviours = field.GetValue(character) as Dictionary<Type, ICharacterBehaviour>;
			behaviours[typeof(IAbilityController)] = abilities;

			foreach (AbilityTemplate template in AbilityFixtures.Bases)
			{
				abilities.LearnBaseAbility(template);
			}
			foreach (AbilityEvent abilityEvent in AbilityFixtures.Effects)
			{
				abilities.LearnAbilityEvent(abilityEvent);
			}
			foreach (AbilityTypeOverrideEventType typeOverride in AbilityFixtures.Overrides)
			{
				abilities.LearnBaseAbility(typeOverride);
			}

			/* Three finished abilities, each built from a different base and set of effects, so the
			 * Abilities tab shows entries that differ in their numbers rather than three copies of
			 * one row. */
			long id = 500;
			AddAbility(abilities, id++, "Lesser Fireball", "Projectile Forward Move Event", "Lesser Fire Damage");
			AddAbility(abilities, id++, "Lesser Flame", "Minor Fire Damage");
			AddAbility(abilities, id, "Punch", "Basic Physical Damage");

			return character;
		}

		/// <summary>Builds one crafted ability and files it with the controller.</summary>
		private static void AddAbility(FakeAbilities abilities, long id, string baseName, params string[] effectNames)
		{
			AbilityTemplate template = AbilityFixtures.Base(baseName);
			if (template == null)
			{
				return;
			}

			List<int> eventIDs = new List<int>();
			foreach (string effectName in effectNames)
			{
				AbilityEvent effect = AbilityFixtures.Effect(effectName);
				if (effect != null)
				{
					eventIDs.Add(effect.ID);
				}
			}

			Ability ability = BuildAbility(id, template, eventIDs);
			if (ability != null)
			{
				abilities.LearnAbility(ability);
			}
		}

		/// <summary>Constructs an ability, or null when its base did not load.</summary>
		private static Ability BuildAbility(long id, AbilityTemplate template, List<int> eventIDs)
		{
			return template == null ? null : new Ability(id, template, eventIDs);
		}

		/// <summary>
		/// Selects the first row of the visible tab, so the details pane is populated.
		/// </summary>
		/// <remarks>
		/// Through the panel's own <c>Select</c> rather than a synthesised pointer event: a pooled
		/// <c>PointerDownEvent</c> carries no button, and the panel's handler correctly ignores
		/// anything that is not a left click. The click path itself is covered by the panel's
		/// tests; this is a capture, and what it needs is the resulting state.
		/// </remarks>
		private static void SelectFirstEntry(UITKAbilities panel, AbilityTabType tab, KnowledgeFilterType filter)
		{
			FieldInfo field = typeof(UITKAbilities).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic);
			MethodInfo select = typeof(UITKAbilities).GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic);
			if (field == null || select == null)
			{
				return;
			}

			System.Collections.IList entries = field.GetValue(panel) as System.Collections.IList;
			if (entries == null)
			{
				return;
			}

			foreach (object entry in entries)
			{
				FieldInfo tabField = entry.GetType().GetField("Tab", BindingFlags.Instance | BindingFlags.Public);
				FieldInfo kindField = entry.GetType().GetField("Kind", BindingFlags.Instance | BindingFlags.Public);
				if (tabField == null || (AbilityTabType)tabField.GetValue(entry) != tab)
				{
					continue;
				}
				if (filter != KnowledgeFilterType.All && kindField != null &&
					(KnowledgeFilterType)kindField.GetValue(entry) != filter)
				{
					continue;
				}

				select.Invoke(panel, new[] { entry });
				return;
			}
		}

		/// <summary>Runs the private Awake every panel uses to register itself.</summary>
		private static void Awake(UITKControl panel)
		{
			MethodInfo awake = typeof(UITKControl).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
			awake?.Invoke(panel, null);
		}

		private static FieldInfo Field(object target, string name)
		{
			return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
		}

		private static void Set(object target, string name, object value)
		{
			Field(target, name)?.SetValue(target, value);
		}

		private static void Invoke(object target, string name)
		{
			MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
			method?.Invoke(target, null);
		}
	}

	/// <summary>
	/// An ability controller that holds knowledge and abilities, and does nothing else.
	/// </summary>
	/// <remarks>
	/// The panels read the knowledge sets and the ability dictionary, and subscribe to the four
	/// add/remove events; everything past that is casting, which no rendered panel performs. Those
	/// members satisfy the interface and are deliberately inert.
	/// </remarks>
	public sealed class FakeAbilities : IAbilityController
	{
		public FakeAbilities(ICharacter character)
		{
			Character = character;
		}

		public ICharacter Character { get; private set; }
		public bool Initialized => true;
		public void InitializeOnce(ICharacter character) { Character = character; }
		public void OnStartCharacter() { }
		public void OnStopCharacter() { }

		public event Action<Ability> OnAddAbility;
		public event Action<BaseAbilityTemplate> OnAddKnownAbility;
		public event Action<AbilityEvent> OnAddKnownAbilityEvent;
		public event Action<long> OnRemoveAbility;
		public event Action<ICharacter, Item, int, bool> OnConsumableItemChanged;
		public event Func<bool> OnCanManipulate;
		public event Action<string, float, float> OnUpdate;
		public event Action OnInterrupt;
		public event Action OnCancel;
		public event Action OnReset;
		public event Action<uint> OnPredictionMismatch;
		public event Action<long> OnAbilityDenied;

		/// <summary>Nothing here saves, so the flag is stored and ignored.</summary>
		public bool KnowledgeDirty { get; set; }

		public SortedDictionary<long, Ability> KnownAbilities { get; } = new SortedDictionary<long, Ability>();
		public HashSet<int> KnownBaseAbilities { get; } = new HashSet<int>();
		public HashSet<int> KnownAbilityEvents { get; } = new HashSet<int>();
		public HashSet<int> KnownAbilityOnTickEvents { get; } = new HashSet<int>();
		public HashSet<int> KnownAbilityOnHitEvents { get; } = new HashSet<int>();
		public HashSet<int> KnownAbilityOnPreSpawnEvents { get; } = new HashSet<int>();
		public HashSet<int> KnownAbilityOnSpawnEvents { get; } = new HashSet<int>();
		public HashSet<int> KnownAbilityOnDestroyEvents { get; } = new HashSet<int>();

		private readonly Dictionary<int, long> templateToAbilityID = new Dictionary<int, long>();

		public bool KnowsAbility(int abilityID) => KnownBaseAbilities.Contains(abilityID);
		public bool KnowsAbilityEvent(int eventID) => KnownAbilityEvents.Contains(eventID);
		public bool KnowsLearnedAbility(int templateID) => templateToAbilityID.ContainsKey(templateID);

		public bool LearnBaseAbility(BaseAbilityTemplate template)
		{
			if (template == null) { return false; }
			KnownBaseAbilities.Add(template.ID);
			OnAddKnownAbility?.Invoke(template);
			return true;
		}

		public bool LearnBaseAbilities(List<BaseAbilityTemplate> templates = null)
		{
			if (templates == null) { return false; }
			foreach (BaseAbilityTemplate template in templates) { LearnBaseAbility(template); }
			return true;
		}

		public bool LearnAbilityEvent(AbilityEvent abilityEvent)
		{
			if (abilityEvent == null) { return false; }
			KnownAbilityEvents.Add(abilityEvent.ID);
			switch (abilityEvent)
			{
				case AbilityOnTickEvent _: KnownAbilityOnTickEvents.Add(abilityEvent.ID); break;
				case AbilityOnHitEvent _: KnownAbilityOnHitEvents.Add(abilityEvent.ID); break;
				case AbilityOnPreSpawnEvent _: KnownAbilityOnPreSpawnEvents.Add(abilityEvent.ID); break;
				case AbilityOnSpawnEvent _: KnownAbilityOnSpawnEvents.Add(abilityEvent.ID); break;
				case AbilityOnDestroyEvent _: KnownAbilityOnDestroyEvents.Add(abilityEvent.ID); break;
			}
			OnAddKnownAbilityEvent?.Invoke(abilityEvent);
			return true;
		}

		public bool LearnAbilityEvents(List<AbilityEvent> abilityEvents = null)
		{
			if (abilityEvents == null) { return false; }
			foreach (AbilityEvent abilityEvent in abilityEvents) { LearnAbilityEvent(abilityEvent); }
			return true;
		}

		public void LearnAbility(Ability ability, float remainingCooldown = 0.0f)
		{
			if (ability == null) { return; }
			KnownAbilities[ability.ID] = ability;
			if (ability.Template != null) { templateToAbilityID[ability.Template.ID] = ability.ID; }
			OnAddAbility?.Invoke(ability);
		}

		public void RemoveAbility(long referenceID)
		{
			KnownAbilities.Remove(referenceID);
			OnRemoveAbility?.Invoke(referenceID);
		}

		public bool IsActivating => false;
		public bool AbilityQueued => false;
		public float RemainingActivationTime => 0.0f;
		public List<Trigger> OnAbilityActivateTriggers { get; } = new List<Trigger>();
		public List<Trigger> OnAbilityCompleteTriggers { get; } = new List<Trigger>();

		public void Cancel() { }
		public void Interrupt(ICharacter attacker) { }
		public bool Activate(long referenceID, bool isHeld) => false;
		public void ActivateConsumable(Item item) { }
		public bool CanManipulate() => OnCanManipulate?.Invoke() ?? true;
		public AbilityType GetCurrentAbilityType() => AbilityType.None;
		public bool RequiresHeld(long abilityID) => false;
		public void Release() { }

		/// <summary>Raised by nothing here; the panels only subscribe.</summary>
		private void Unused()
		{
			OnConsumableItemChanged?.Invoke(null, null, 0, false);
			OnUpdate?.Invoke(null, 0.0f, 0.0f);
			OnInterrupt?.Invoke();
			OnCancel?.Invoke();
			OnReset?.Invoke();
			OnPredictionMismatch?.Invoke(0);
			OnAbilityDenied?.Invoke(0);
		}
	}
}
