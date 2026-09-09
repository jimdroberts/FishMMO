using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// Renders one card per tooltip-producing type, so every description in the game can be looked
	/// at side by side.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Headless: <c>-executeMethod FishMMO.RenderScratch.TooltipGalleryRender.Render</c> under xvfb
	/// and WITHOUT <c>-quit</c>.
	/// </para>
	/// <para>
	/// Every card goes through the shipped <see cref="UITKTooltipView"/> against real
	/// <see cref="TooltipContent"/> built by the real producers, so what is captured is what a
	/// player sees on hover. Subjects are the project's own assets wherever one exists; the few
	/// that are built in memory are the kinds of thing the project ships no example of, and each
	/// says so where it is created.
	/// </para>
	/// </remarks>
	public static class TooltipGalleryRender
	{
		private const string OUTPUT_DIR = "/home/jim/Dev/FishMMO-Dev/PanelRenders";
		private const string PANEL_SETTINGS_PATH = "Assets/UI Toolkit/PanelSettings.asset";
		private const string THEME_USS = "Assets/Scripts/Client/GUI/FishMMO-Theme.uss";
		private const string VIEW_USS = "Assets/Scripts/Client/GUI/Shared/Tooltip/UITooltipView.uss";
		private const int WIDTH = 1400;
		private const int HEIGHT = 900;
		private const int SETTLE_FRAMES = 40;

		private sealed class Page
		{
			public string Label;
			public List<(string caption, TooltipContent content)> Cards = new List<(string, TooltipContent)>();
		}

		private static readonly List<Page> queue = new List<Page>();
		private static GameObject host;
		private static UIDocument document;
		private static PanelSettings settings;
		private static RenderTexture texture;
		private static Page current;
		private static int framesWaited;

		[MenuItem("FishMMO/UI Toolkit/Render Tooltip Gallery")]
		public static void Render()
		{
			try
			{
				Directory.CreateDirectory(OUTPUT_DIR);
				AbilityFixtures.Load();
				TooltipSubjects.Load();

				queue.Clear();
				queue.Add(new Page { Label = "Tooltips-items", Cards = TooltipSubjects.Items() });
				queue.Add(new Page { Label = "Tooltips-abilities", Cards = TooltipSubjects.Abilities() });
				queue.Add(new Page { Label = "Tooltips-buffs", Cards = TooltipSubjects.Buffs() });

				EditorApplication.update -= Pump;
				EditorApplication.update += Pump;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[TooltipGallery] setup failed: {ex}");
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
						Debug.Log("[TooltipGallery] done");
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
				Debug.Log($"[TooltipGallery] wrote {current.Label}.png");
				Unmount();
				current = null;
			}
			catch (Exception ex)
			{
				EditorApplication.update -= Pump;
				Debug.LogError($"[TooltipGallery] pump failed on {current?.Label}: {ex}");
				Teardown();
				if (Application.isBatchMode) EditorApplication.Exit(1);
			}
		}

		private static void Mount(Page page)
		{
			if (texture == null)
			{
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

			host = new GameObject("Render_" + page.Label) { hideFlags = HideFlags.HideAndDontSave };
			document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;

			VisualElement root = document.rootVisualElement;
			root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(THEME_USS));
			root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(VIEW_USS));
			root.style.flexDirection = FlexDirection.Row;
			root.style.flexWrap = Wrap.Wrap;
			root.style.alignItems = Align.FlexStart;
			root.style.paddingLeft = 12;
			root.style.paddingTop = 12;

			foreach ((string caption, TooltipContent content) in page.Cards)
			{
				root.Add(BuildCard(caption, content));
			}

			root.MarkDirtyRepaint();
		}

		/// <summary>One captioned tooltip, drawn exactly as the hover tooltip draws it.</summary>
		private static VisualElement BuildCard(string caption, TooltipContent content)
		{
			VisualElement column = new VisualElement();
			column.style.width = 320;
			column.style.marginRight = 14;
			column.style.marginBottom = 14;

			Label heading = new Label(caption);
			heading.AddToClassList("fish-col-head");
			heading.style.marginBottom = 4;
			column.Add(heading);

			/* .fish-tooltip is the theme's tooltip surface — the same class the real tooltip box
			 * carries — so the card is the real thing rather than an approximation of it. */
			VisualElement box = new VisualElement();
			box.AddToClassList("fish-tooltip");
			box.style.paddingLeft = 8;
			box.style.paddingRight = 8;
			box.style.paddingTop = 6;
			box.style.paddingBottom = 6;

			UITKTooltipView.Render(box, content);
			column.Add(box);
			return column;
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

	/// <summary>The subjects the gallery describes, one per producer type in the game.</summary>
	public static class TooltipSubjects
	{
		private static readonly List<ScriptableObject> temporary = new List<ScriptableObject>();
		private static bool loaded;

		/// <summary>Registers every template type the gallery reads, as the boot loader would.</summary>
		public static void Load()
		{
			if (loaded)
			{
				return;
			}
			loaded = true;

			RegisterAll<BaseItemTemplate>("Assets/Templates/Entity/Items");
			RegisterAll<ItemAttributeTemplate>("Assets/Templates/Entity/Items");
			RegisterAll<BaseBuffTemplate>("Assets/Templates/Entity/Buffs");
			RegisterAll<CharacterAttributeTemplate>("Assets/Templates/Entity/CharacterAttributes");
			RegisterAll<PremadeAbilityTemplate>("Assets/Templates/Entity/Abilities");
			RegisterAll<AbilityEvent>("Assets/Templates/Entity/Abilities");
		}

		private static void RegisterAll<T>(string folder) where T : ScriptableObject
		{
			foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { folder }))
			{
				T asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
				if (asset is ICachedObject)
				{
					asset.GetType()
						.GetMethod("AddToCache", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
						?.Invoke(asset, new object[] { asset.name });
				}
			}
		}

		/// <summary>Every item-side tooltip: a template, a generated instance, and each item kind.</summary>
		public static List<(string, TooltipContent)> Items()
		{
			List<(string, TooltipContent)> cards = new List<(string, TooltipContent)>();

			WeaponTemplate weapon = First<WeaponTemplate>();
			ArmorTemplate armor = First<ArmorTemplate>();
			ConsumableTemplate consumable = First<ConsumableTemplate>();
			BaseItemTemplate stackable = FirstWhere<BaseItemTemplate>(t => t.MaxStackSize > 1);

			if (weapon != null)
			{
				cards.Add(($"Weapon template — {weapon.name}", weapon.BuildContent()));

				/* A generated instance: the roll is what the player is actually holding, and it is
				 * written by ItemGenerator on top of the template's own description. */
				Item rolled = new Item(9001, 20250909, weapon, 1);
				cards.Add(($"Weapon instance (rolled) — {weapon.name}", rolled.BuildContent()));
			}

			if (armor != null)
			{
				cards.Add(($"Armor template — {armor.name}", armor.BuildContent()));
			}

			if (armor != null)
			{
				Item worn = new Item(9003, 20250101, armor, 1) { Slot = 4 };
				cards.Add(($"Armor instance in a slot — {armor.name}", worn.BuildContent()));
			}

			/* The mock consumables, which are real assets now — see MockConsumableBuilder. The
			 * potion is shown as a held stack because that is where the Amount row comes from. */
			ResourceConsumableTemplate potionTemplate = Find<ResourceConsumableTemplate>("Minor Healing Potion");
			if (potionTemplate != null)
			{
				Item potion = new Item(9002, 0, potionTemplate, 7);
				cards.Add(("Resource consumable — " + potionTemplate.name, potion.BuildContent()));
			}

			ResourceConsumableTemplate restorative = Find<ResourceConsumableTemplate>("Tideborn Restorative");
			if (restorative != null)
			{
				cards.Add(("Multi-resource consumable — " + restorative.name, restorative.BuildContent()));
			}

			KnowledgeScrollTemplate abilityScroll = Find<KnowledgeScrollTemplate>("Scroll of Lesser Fireball");
			if (abilityScroll != null)
			{
				cards.Add(("Knowledge scroll (ability) — " + abilityScroll.name, abilityScroll.BuildContent()));
			}

			KnowledgeScrollTemplate effectScroll = Find<KnowledgeScrollTemplate>("Scroll of Flame Impact");
			if (effectScroll != null)
			{
				cards.Add(("Knowledge scroll (effects) — " + effectScroll.name, effectScroll.BuildContent()));
			}

			KnowledgeScrollTemplate codex = Find<KnowledgeScrollTemplate>("Codex of the Burning Sphere");
			if (codex != null)
			{
				cards.Add(("Knowledge scroll (both) — " + codex.name, codex.BuildContent()));
			}

			if (stackable != null)
			{
				cards.Add(($"Stackable template — {stackable.name}", stackable.BuildContent()));
			}

			return cards;
		}

		/// <summary>Every ability-side tooltip: base ability, effect, crafted ability, override, premade.</summary>
		public static List<(string, TooltipContent)> Abilities()
		{
			List<(string, TooltipContent)> cards = new List<(string, TooltipContent)>();

			AbilityTemplate fireball = AbilityFixtures.Base("Lesser Fireball");
			if (fireball != null)
			{
				cards.Add(($"Base ability — {fireball.name}", fireball.BuildContent()));

				Ability crafted = new Ability(9101, fireball, new List<int>
				{
					AbilityFixtures.Effect("Giant")?.ID ?? 0,
					AbilityFixtures.Effect("Swiftfire")?.ID ?? 0,
				});
				cards.Add(($"Crafted ability — {crafted.Name}", crafted.BuildContent()));

				/* The crafting preview: the same composition, with the deltas the panel draws. */
				AbilitySummary summary = AbilitySummary.Compose(fireball, new List<ITooltip>
				{
					AbilityFixtures.Effect("Giant"),
					AbilityFixtures.Effect("Swiftfire"),
				});
				TooltipContent preview = new TooltipContent();
				summary.BuildTooltip(preview, showDeltas: true);
				cards.Add(("Crafting preview (with deltas)", preview));
			}

			AbilityEvent effect = AbilityFixtures.Effect("Lesser Fire Damage");
			if (effect != null)
			{
				cards.Add(($"Ability effect — {effect.name}", effect.BuildContent()));
			}

			AbilityEvent modifier = AbilityFixtures.Effect("Giant");
			if (modifier != null)
			{
				cards.Add(($"Ability effect (modifier) — {modifier.name}", modifier.BuildContent()));
			}

			if (AbilityFixtures.Overrides.Count > 0)
			{
				AbilityTypeOverrideEventType typeOverride = AbilityFixtures.Overrides[0];
				cards.Add(($"Type override — {typeOverride.name}", typeOverride.BuildContent()));
			}

			PremadeAbilityTemplate premade = First<PremadeAbilityTemplate>();
			if (premade != null)
			{
				cards.Add(($"Premade ability (merchant) — {premade.name}", premade.BuildContent()));
			}

			CharacterAttributeTemplate attribute = FirstWhere<CharacterAttributeTemplate>(t => t.name == "Health")
				?? First<CharacterAttributeTemplate>();
			if (attribute != null)
			{
				cards.Add(($"Character attribute — {attribute.name}", attribute.BuildContent()));
			}

			return cards;
		}

		/// <summary>Every buff kind, including the three the project ships no example of.</summary>
		public static List<(string, TooltipContent)> Buffs()
		{
			List<(string, TooltipContent)> cards = new List<(string, TooltipContent)>();

			foreach (string guid in AssetDatabase.FindAssets("t:BaseBuffTemplate", new[] { "Assets/Templates/Entity/Buffs" }))
			{
				BaseBuffTemplate buff = AssetDatabase.LoadAssetAtPath<BaseBuffTemplate>(AssetDatabase.GUIDToAssetPath(guid));
				if (buff != null)
				{
					cards.Add(($"{buff.GetType().Name} — {buff.name}", buff.BuildContent()));
				}
			}

			/* The kinds with no shipped asset. Each has its own tooltip override, so leaving them
			 * out would mean the gallery could not show whether they render at all. */
			cards.Add(("StateBuffTemplate (mock)", MockState().BuildContent()));
			cards.Add(("DamageNegationBuffTemplate (mock)", MockNegation().BuildContent()));
			cards.Add(("DeflectBuffTemplate (mock)", MockDeflect().BuildContent()));
			cards.Add(("CompositeBuffTemplate (mock)", MockComposite().BuildContent()));

			return cards;
		}

		private static StateBuffTemplate MockState()
		{
			StateBuffTemplate buff = Temp<StateBuffTemplate>("Dazed");
			buff.Description = "Leaves the target unable to act.";
			buff.Flag = CharacterFlags.IsStunned;
			return buff;
		}

		private static DamageNegationBuffTemplate MockNegation()
		{
			DamageNegationBuffTemplate buff = Temp<DamageNegationBuffTemplate>("Stone Ward");
			buff.Description = "Absorbs the next blows to land.";
			buff.Amount = 250;
			return buff;
		}

		private static DeflectBuffTemplate MockDeflect()
		{
			DeflectBuffTemplate buff = Temp<DeflectBuffTemplate>("Parry Stance");
			buff.Description = "Turns aside what comes at the front.";
			buff.DeflectAngleDegrees = 120.0f;
			buff.MaxDeflections = 3;
			return buff;
		}

		private static CompositeBuffTemplate MockComposite()
		{
			CompositeBuffTemplate buff = Temp<CompositeBuffTemplate>("Tideborn Blessing");
			buff.Description = "The reef's favour, in several forms at once.";
			buff.Flags = new List<CharacterFlags> { CharacterFlags.IsFrozen };
			return buff;
		}

		/// <summary>Creates a throwaway template that lives only for this run.</summary>
		private static T Temp<T>(string name) where T : ScriptableObject
		{
			T asset = ScriptableObject.CreateInstance<T>();
			asset.hideFlags = HideFlags.HideAndDontSave;
			asset.name = name;
			temporary.Add(asset);
			return asset;
		}

		/// <summary>Finds an asset of a type by its exact name, anywhere under Templates.</summary>
		private static T Find<T>(string name) where T : ScriptableObject
		{
			return FirstWhere<T>(t => t.name == name);
		}

		private static T First<T>() where T : ScriptableObject
		{
			return FirstWhere<T>(_ => true);
		}

		private static T FirstWhere<T>(Func<T, bool> predicate) where T : ScriptableObject
		{
			foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { "Assets/Templates" }))
			{
				T asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
				if (asset != null && predicate(asset))
				{
					return asset;
				}
			}
			return null;
		}
	}

}
