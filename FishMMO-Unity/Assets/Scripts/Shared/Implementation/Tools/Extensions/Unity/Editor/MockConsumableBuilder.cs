using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Creates the mock consumables used for testing: knowledge scrolls and resource potions.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The project shipped no consumable at all, because <see cref="ConsumableTemplate"/> and
	/// <see cref="ScrollConsumableTemplate"/> were both abstract with nothing deriving from them.
	/// With <see cref="KnowledgeScrollTemplate"/> and <see cref="ResourceConsumableTemplate"/> in
	/// place, this fills the folder with something to test against.
	/// </para>
	/// <para>
	/// Written as a generator rather than by hand because each asset also has to be registered as
	/// an addressable in the shared static group — that is how every template reaches a running
	/// client, and an asset that only exists on disk resolves to nothing at runtime. Re-running it
	/// updates the existing assets in place rather than making duplicates.
	/// </para>
	/// </remarks>
	public static class MockConsumableBuilder
	{
		private const string OUTPUT_FOLDER = "Assets/Templates/Entity/Items/Consumables";
		private const string ABILITY_FOLDER = "Assets/Templates/Entity/Abilities";
		private const string ATTRIBUTE_FOLDER = "Assets/Templates/Entity/CharacterAttributes";

		[MenuItem("FishMMO/Templates/Create Mock Consumables")]
		public static void Create()
		{
			EnsureFolder(OUTPUT_FOLDER);

			// ── Resource potions ────────────────────────────────────
			CharacterAttributeTemplate health = FindAttribute("Health");
			CharacterAttributeTemplate mana = FindAttribute("Mana");
			CharacterAttributeTemplate stamina = FindAttribute("Stamina");

			MakePotion("Minor Healing Potion", ConsumableType.Potion, 25, 1.0f, 10.0f,
				new[] { (health, 150) });

			MakePotion("Minor Mana Potion", ConsumableType.Potion, 25, 1.0f, 10.0f,
				new[] { (mana, 120) });

			MakePotion("Traveler's Rations", ConsumableType.Food, 8, 2.5f, 30.0f,
				new[] { (health, 60), (stamina, 90) });

			/* One potion that restores everything, for the case a tester wants a single item that
			 * refills the bars rather than three. */
			MakePotion("Tideborn Restorative", ConsumableType.Potion, 120, 1.5f, 60.0f,
				new[] { (health, 400), (mana, 400), (stamina, 400) });

			// ── Knowledge scrolls ───────────────────────────────────
			MakeScroll("Scroll of Lesser Fireball", 60,
				new[] { "Lesser Fireball" },
				new string[0]);

			MakeScroll("Scroll of Lesser Flame", 60,
				new[] { "Lesser Flame" },
				new string[0]);

			/* Effects, which no scroll could teach before: crafting needs both halves and only a
			 * merchant could supply this one. */
			MakeScroll("Scroll of Projectile Motion", 45,
				new string[0],
				new[] { "Projectile Forward Move Event" });

			MakeScroll("Scroll of Flame Impact", 45,
				new string[0],
				new[] { "Lesser Fire Damage", "Fire Impact FX Event" });

			/* A scroll carrying a whole recipe's worth of knowledge — the fastest way to put a
			 * character in front of a crafter with something to combine. */
			MakeScroll("Codex of the Burning Sphere", 200,
				new[] { "Lesser Fireball", "Lesser Flame" },
				new[] { "Projectile Forward Move Event", "Lesser Fire Damage", "Minor Increase Armor Event" });

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			Debug.Log("[MockConsumables] done");
		}

		/// <summary>Creates or updates one resource potion.</summary>
		private static void MakePotion(string name, ConsumableType type, int price, float useTime, float cooldown,
			(CharacterAttributeTemplate resource, int amount)[] restores)
		{
			ResourceConsumableTemplate template = LoadOrCreate<ResourceConsumableTemplate>(name);

			template.ConsumableType = type;
			template.Price = price;
			template.ActivationTime = useTime;
			template.Cooldown = cooldown;
			template.ChargeCost = 1;

			/* Stackable, and that is not decoration: ConsumableTemplate.CanConsume requires
			 * IsStackable, which is MaxStackSize > 1. A consumable authored at 1 can never be
			 * consumed at all. */
			template.MaxStackSize = 20;
			template.IsIdentifiable = false;
			template.Generate = false;

			template.Restores = new List<ResourceRestoration>();
			foreach ((CharacterAttributeTemplate resource, int amount) in restores)
			{
				if (resource == null)
				{
					continue;
				}
				template.Restores.Add(new ResourceRestoration { Resource = resource, Amount = amount });
			}

			Save(template);
		}

		/// <summary>Creates or updates one knowledge scroll.</summary>
		private static void MakeScroll(string name, int price, string[] abilityNames, string[] eventNames)
		{
			KnowledgeScrollTemplate template = LoadOrCreate<KnowledgeScrollTemplate>(name);

			template.ConsumableType = ConsumableType.Scroll;
			template.Price = price;
			template.ActivationTime = 2.0f;
			template.Cooldown = 3.0f;
			template.ChargeCost = 1;
			template.MaxStackSize = 5;
			template.IsIdentifiable = false;
			template.Generate = false;

			template.AbilityTemplates = new List<BaseAbilityTemplate>();
			foreach (string abilityName in abilityNames)
			{
				BaseAbilityTemplate ability = Find<BaseAbilityTemplate>(ABILITY_FOLDER, abilityName);
				if (ability != null)
				{
					template.AbilityTemplates.Add(ability);
				}
				else
				{
					Debug.LogWarning($"[MockConsumables] '{name}' names a base ability that does not exist: {abilityName}");
				}
			}

			template.AbilityEvents = new List<AbilityEvent>();
			foreach (string eventName in eventNames)
			{
				AbilityEvent abilityEvent = Find<AbilityEvent>(ABILITY_FOLDER, eventName);
				if (abilityEvent != null)
				{
					template.AbilityEvents.Add(abilityEvent);
				}
				else
				{
					Debug.LogWarning($"[MockConsumables] '{name}' names an effect that does not exist: {eventName}");
				}
			}

			Save(template);
		}

		/// <summary>Loads the asset if it exists, otherwise creates it.</summary>
		private static T LoadOrCreate<T>(string name) where T : ScriptableObject
		{
			string path = $"{OUTPUT_FOLDER}/{name}.asset";
			T existing = AssetDatabase.LoadAssetAtPath<T>(path);
			if (existing != null)
			{
				return existing;
			}

			T created = ScriptableObject.CreateInstance<T>();
			AssetDatabase.CreateAsset(created, path);
			return created;
		}

		/// <summary>Marks the asset dirty and registers it with addressables.</summary>
		private static void Save(ScriptableObject template)
		{
			EditorUtility.SetDirty(template);
			RegisterAddressable(template);
		}

		/// <summary>
		/// Puts the asset in the shared static group under its own name.
		/// </summary>
		/// <remarks>
		/// The same registration <c>AddressableCachedScriptableObjectEditor</c> performs when a
		/// template's inspector is opened. Done here too, because an asset created by a script is
		/// never inspected and would otherwise be invisible at runtime — <c>Get&lt;T&gt;(id)</c>
		/// resolves nothing that the boot-time loader did not load.
		/// </remarks>
		private static void RegisterAddressable(ScriptableObject template)
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null)
			{
				Debug.LogError("[MockConsumables] Addressable Asset Settings not found.");
				return;
			}

			string path = AssetDatabase.GetAssetPath(template);
			string guid = AssetDatabase.AssetPathToGUID(path);

			AddressableAssetGroup group = settings.FindGroup(Constants.SharedStaticLabel) ?? settings.DefaultGroup;
			AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group);
			if (entry == null)
			{
				Debug.LogError($"[MockConsumables] could not create an addressable entry for {template.name}.");
				return;
			}

			entry.address = template.name;
			if (!entry.labels.Contains(Constants.SharedStaticLabel))
			{
				settings.AddLabel(Constants.SharedStaticLabel);
				entry.labels.Add(Constants.SharedStaticLabel);
			}
			settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
		}

		/// <summary>Finds a character attribute template by name.</summary>
		private static CharacterAttributeTemplate FindAttribute(string name)
		{
			CharacterAttributeTemplate template = Find<CharacterAttributeTemplate>(ATTRIBUTE_FOLDER, name);
			if (template == null)
			{
				Debug.LogWarning($"[MockConsumables] no character attribute named {name}.");
			}
			return template;
		}

		/// <summary>Finds an asset of a type by exact name, under a folder.</summary>
		private static T Find<T>(string folder, string name) where T : Object
		{
			foreach (string guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { folder }))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (Path.GetFileNameWithoutExtension(path) == name)
				{
					return AssetDatabase.LoadAssetAtPath<T>(path);
				}
			}
			return null;
		}

		/// <summary>Creates the output folder when it does not exist.</summary>
		private static void EnsureFolder(string folder)
		{
			if (AssetDatabase.IsValidFolder(folder))
			{
				return;
			}

			string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
			string leaf = Path.GetFileName(folder);
			EnsureFolder(parent);
			AssetDatabase.CreateFolder(parent, leaf);
		}
	}
}
