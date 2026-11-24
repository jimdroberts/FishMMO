#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>What an NPC at a point of interest is there to do.</summary>
	public enum PointOfInterestNpcRole : byte
	{
		Banker = 0,
		Merchant = 1,
		/// <summary>An ability crafter today; any crafting service later.</summary>
		Crafter = 2,
		Guard = 3,
		Townsfolk = 4,
		/// <summary>The hostile pack a camp, den or lair spawns.</summary>
		MonsterPack = 5,
		/// <summary>One stronger hostile leading a pack.</summary>
		Elite = 6,
		/// <summary>Sells the weapons (the general merchant no longer does): at the smithy.</summary>
		Weaponsmith = 7,
		/// <summary>Sells the armour and shields: at the smithy, beside the weaponsmith where there is one smithy.</summary>
		Armorsmith = 8,
	}

	/// <summary>One weighted NPC prefab a role can draw.</summary>
	[Serializable]
	public class PointOfInterestNpcChoice
	{
		/// <summary>An NPC prefab (its root carries <see cref="NPC"/>).</summary>
		public GameObject Prefab;
		[Min(0f)] public float Weight = 1.0f;
	}

	/// <summary>
	/// One row: which NPCs fill a role for a race (or a race category, or anyone).
	/// </summary>
	[Serializable]
	public class PointOfInterestNpcEntry
	{
		/// <summary>The race's naming key (<see cref="RaceTemplate.NamingKey"/>, "woodelf"); empty matches any race.</summary>
		public string Race;
		/// <summary>The race's <see cref="RaceTemplate.Category"/> ("Humanoid"); empty matches any category.</summary>
		public string RaceCategory;
		public PointOfInterestNpcRole Role;
		public List<PointOfInterestNpcChoice> Choices = new List<PointOfInterestNpcChoice>();

		/// <summary>
		/// Build a copy of each prefab for the site's race through <see cref="NPCPrefabFactory"/> (an NPC's model, faction
		/// and name come from its race), saved under <see cref="PointOfInterestNpcTable.ClonedFolder"/> and reused after.
		/// Off: the prefab spawns as authored, whatever the site's race.
		/// </summary>
		public bool CloneForRace;

		/// <summary>How specifically the row names the race: 2 the race itself, 1 its category, 0 anyone; −1 no match.</summary>
		public int Specificity(string raceKey, string raceCategory)
		{
			bool raceSet = !string.IsNullOrWhiteSpace(Race);
			bool categorySet = !string.IsNullOrWhiteSpace(RaceCategory);
			if (raceSet && !string.Equals(Race.Trim(), raceKey ?? string.Empty, StringComparison.OrdinalIgnoreCase))
			{
				return -1;
			}
			if (categorySet && !string.Equals(RaceCategory.Trim(), raceCategory ?? string.Empty, StringComparison.OrdinalIgnoreCase))
			{
				return -1;
			}
			return raceSet ? 2 : categorySet ? 1 : 0;
		}
	}

	/// <summary>
	/// Which NPC prefab fills each role at a generated site, by the site's race: the table the spawner features read.
	/// </summary>
	/// <remarks>
	/// <para><b>Resolution.</b> The most specific row wins — the race's own row, else its category's, else the wildcard;
	/// among equally specific rows the first. A role nothing answers resolves to nothing: the feature still places its
	/// spawner, empty, and logs it, so a designer can fill it (unknown → null).</para>
	/// <para><b>Defaults.</b> Written in code (<see cref="FillDefaults"/>) so the generator works before anyone authors the
	/// asset: the shipped human banker, merchant and ability crafter serve every race; orcs fill an orc site's pack
	/// (weighted 0.5 / 0.3 / 0.2, the dungeon's mix) and its elite; every other race gets the guards, townsfolk and generated monsters of <see cref="SiteRows"/>.
	/// <see cref="CreateAsset"/> writes the defaults to <see cref="AssetPath"/> for editing; once it exists it wins.</para>
	/// </remarks>
	[CreateAssetMenu(fileName = "Point of Interest NPC Table", menuName = "FishMMO/World/Point of Interest NPC Table")]
	public class PointOfInterestNpcTable : ScriptableObject
	{
		public const string AssetPath = PointOfInterestTemplate.Folder + "/Point of Interest NPC Table.asset";

		/// <summary>Where race copies made for <see cref="PointOfInterestNpcEntry.CloneForRace"/> rows are saved.</summary>
		public const string ClonedFolder = "Assets/Prefabs/Shared/Entity/NPCs/Generated";

		public const string BankerPath = "Assets/Prefabs/Shared/Entity/NPCs/Interactables/Human/Banker/HumanBanker.prefab";
		public const string MerchantPath = "Assets/Prefabs/Shared/Entity/NPCs/Interactables/Human/Merchants/GeneralGoods/HumanGeneralMerchant.prefab";
		public const string CrafterPath = "Assets/Prefabs/Shared/Entity/NPCs/Interactables/Human/AbilityCrafter/HumanAbilityCrafter.prefab";
		/// <summary>The smiths (the Commerce module's "Build smiths" makes them from the general merchant).</summary>
		public const string WeaponsmithPath = "Assets/Prefabs/Shared/Entity/NPCs/Interactables/Human/Merchants/Weaponsmith/HumanWeaponsmith.prefab";
		public const string ArmorsmithPath = "Assets/Prefabs/Shared/Entity/NPCs/Interactables/Human/Merchants/Armorsmith/HumanArmorsmith.prefab";
		public const string OrcPath = "Assets/Prefabs/Shared/Entity/NPCs/Monsters/Orcs/an orc.prefab";
		public const string OrcWarriorPath = "Assets/Prefabs/Shared/Entity/NPCs/Monsters/Orcs/an orc warrior.prefab";
		public const string OrcMagePath = "Assets/Prefabs/Shared/Entity/NPCs/Monsters/Orcs/an orc mage.prefab";
		/// <summary>A settlement's guard and townsperson, and a camp's raider (SiteNpcBuilder), each copied for the site's race.</summary>
		public const string GuardPath = "Assets/Prefabs/Shared/Entity/NPCs/Settlement/a guard.prefab";
		public const string TownspersonPath = "Assets/Prefabs/Shared/Entity/NPCs/Settlement/a townsperson.prefab";
		public const string RaiderPath = "Assets/Prefabs/Shared/Entity/NPCs/Settlement/a raider.prefab";
		/// <summary>The generated monsters (WildlifeBuilder.Monster).</summary>
		public const string MonsterFolder = "Assets/Prefabs/Shared/Entity/NPCs/Wildlife";
		/// <summary>A beast race with no generated body of its own draws from these.</summary>
		private static readonly (string Name, float Weight)[] PackMonsters =
			{ ("a dire wolf", 0.3f), ("a dire hound", 0.2f), ("a giant rat", 0.3f), ("a dire boar", 0.2f) };
		private static readonly (string Name, float Weight)[] EliteMonsters =
			{ ("a sabre cat", 0.4f), ("a rat king", 0.3f), ("a dire bear", 0.3f) };
		/// <summary>Beast races (naming key) and the generated monster that is their pack, then their elite.</summary>
		private static readonly (string Race, string Pack, string Elite)[] BeastRaces =
		{
			("direwolf", "a dire wolf", "a dire wolf"), ("direhound", "a dire hound", "a dire hound"),
			("frostwolf", "a frost wolf", "a frost wolf"), ("giantrat", "a giant rat", "a rat king"),
			("ratking", "a giant rat", "a rat king"), ("direboar", "a dire boar", "a dire boar"),
			("sabrecat", "a sabre cat", "a sabre cat"), ("cavebear", "a dire bear", "a dire bear"),
			("giantbat", "a giant bat", "a giant bat"),
		};

		public List<PointOfInterestNpcEntry> Entries = new List<PointOfInterestNpcEntry>();

		/// <summary>The authored table at <see cref="AssetPath"/>, or a fresh in-memory one holding the defaults.</summary>
		public static PointOfInterestNpcTable LoadOrDefaults()
		{
			PointOfInterestNpcTable table = AssetDatabase.LoadAssetAtPath<PointOfInterestNpcTable>(AssetPath);
			if (table != null)
			{
				return table;
			}
			table = CreateInstance<PointOfInterestNpcTable>();
			table.hideFlags = HideFlags.DontSave;
			table.FillDefaults();
			return table;
		}

		/// <summary>Replaces the rows with the code defaults (see remarks).</summary>
		public void FillDefaults()
		{
			Entries = new List<PointOfInterestNpcEntry>
			{
				Row(null, PointOfInterestNpcRole.Banker, (BankerPath, 1.0f)),
				Row(null, PointOfInterestNpcRole.Merchant, (MerchantPath, 1.0f)),
				Row(null, PointOfInterestNpcRole.Crafter, (CrafterPath, 1.0f)),
				Row(null, PointOfInterestNpcRole.Weaponsmith, (WeaponsmithPath, 1.0f)),
				Row(null, PointOfInterestNpcRole.Armorsmith, (ArmorsmithPath, 1.0f)),
				Row("orc", PointOfInterestNpcRole.MonsterPack, (OrcPath, 0.5f), (OrcWarriorPath, 0.3f), (OrcMagePath, 0.2f)),
				Row("orc", PointOfInterestNpcRole.Elite, (OrcWarriorPath, 1.0f)),
			};
			Entries.AddRange(SiteRows());
		}

		/// <summary>
		/// The rows for the roles that had no prefab before (basic NPCs for the empty spawners;
		/// races spawn by biome, as points of interest do). A site's race is the biome's
		/// (PointOfInterestStage.PickRace), and its NPCs follow it: guards and townsfolk copied for the race; a beast race's camp
		/// or lair its own generated monster; any other race's a raider copied for it (goblins at a goblin camp); a beast race
		/// with no body of its own, the generated beasts. A race's own authored row (orcs) still wins.
		/// </summary>
		public static List<PointOfInterestNpcEntry> SiteRows()
		{
			var rows = new List<PointOfInterestNpcEntry>
			{
				Clone(Row(null, PointOfInterestNpcRole.Guard, (GuardPath, 1.0f))),
				Clone(Row(null, PointOfInterestNpcRole.Townsfolk, (TownspersonPath, 1.0f))),
				Clone(Row(null, PointOfInterestNpcRole.MonsterPack, (RaiderPath, 1.0f))),
				Clone(Row(null, PointOfInterestNpcRole.Elite, (RaiderPath, 1.0f))),
				Category(Row(null, PointOfInterestNpcRole.MonsterPack, Monsters(PackMonsters)), "Beast"),
				Category(Row(null, PointOfInterestNpcRole.Elite, Monsters(EliteMonsters)), "Beast"),
			};
			foreach ((string race, string pack, string elite) in BeastRaces)
			{
				rows.Add(Row(race, PointOfInterestNpcRole.MonsterPack, ($"{MonsterFolder}/{pack}.prefab", 1.0f)));
				rows.Add(Row(race, PointOfInterestNpcRole.Elite, ($"{MonsterFolder}/{elite}.prefab", 1.0f)));
			}
			return rows;
		}

		/// <summary>The service NPCs added after the table was first written (the smiths), for <see cref="AddMissingSiteRows"/>.</summary>
		public static List<PointOfInterestNpcEntry> ServiceRows()
		{
			return new List<PointOfInterestNpcEntry>
			{
				Row(null, PointOfInterestNpcRole.Weaponsmith, (WeaponsmithPath, 1.0f)),
				Row(null, PointOfInterestNpcRole.Armorsmith, (ArmorsmithPath, 1.0f)),
			};
		}

		private static PointOfInterestNpcEntry Clone(PointOfInterestNpcEntry row)
		{
			row.CloneForRace = true;
			return row;
		}

		private static PointOfInterestNpcEntry Category(PointOfInterestNpcEntry row, string category)
		{
			row.RaceCategory = category;
			return row;
		}

		private static (string Path, float Weight)[] Monsters((string Name, float Weight)[] monsters)
		{
			var paths = new (string, float)[monsters.Length];
			for (int i = 0; i < monsters.Length; i++)
			{
				paths[i] = ($"{MonsterFolder}/{monsters[i].Name}.prefab", monsters[i].Weight);
			}
			return paths;
		}

		/// <summary>
		/// Adds each <see cref="SiteRows"/> row whose role, race and category no row with a prefab answers yet, keeping every
		/// authored row; an authored row for the same key left empty is replaced. Returns how many were added.
		/// </summary>
		public int AddMissingSiteRows()
		{
			int added = 0;
			var rows = SiteRows();
			rows.AddRange(ServiceRows());
			foreach (PointOfInterestNpcEntry row in rows)
			{
				if (row.Choices.Count == 0)
				{
					continue;
				}
				bool Same(PointOfInterestNpcEntry e) => e != null && e.Role == row.Role &&
					string.Equals((e.Race ?? "").Trim(), row.Race ?? "", StringComparison.OrdinalIgnoreCase) &&
					string.Equals((e.RaceCategory ?? "").Trim(), row.RaceCategory ?? "", StringComparison.OrdinalIgnoreCase);
				if (Entries.Exists(e => Same(e) && HasChoices(e)))
				{
					continue;
				}
				Entries.RemoveAll(Same);
				Entries.Add(row);
				added++;
			}
			return added;
		}

		private static PointOfInterestNpcEntry Row(string race, PointOfInterestNpcRole role, params (string Path, float Weight)[] prefabs)
		{
			var entry = new PointOfInterestNpcEntry { Race = race ?? string.Empty, RaceCategory = string.Empty, Role = role };
			foreach ((string path, float weight) in prefabs)
			{
				GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
				if (prefab != null)
				{
					entry.Choices.Add(new PointOfInterestNpcChoice { Prefab = prefab, Weight = weight });
				}
				else
				{
					Debug.LogWarning($"[PointOfInterestNpcTable] Default prefab '{path}' is missing; the {role} row skips it.");
				}
			}
			return entry;
		}

		/// <summary>The row that answers a role for a race, or null.</summary>
		public PointOfInterestNpcEntry Find(string raceKey, string raceCategory, PointOfInterestNpcRole role)
		{
			PointOfInterestNpcEntry best = null;
			int bestScore = -1;
			foreach (PointOfInterestNpcEntry entry in Entries)
			{
				if (entry == null || entry.Role != role || !HasChoices(entry))
				{
					continue;
				}
				int score = entry.Specificity(raceKey, raceCategory);
				if (score > bestScore)
				{
					best = entry;
					bestScore = score;
				}
			}
			return best;
		}

		/// <summary>
		/// The prefabs (and weights) that fill a role for a race, copied for the race when the row asks; empty when nothing
		/// answers, which the caller logs and leaves the spawner empty for.
		/// </summary>
		/// <param name="race">The site's race; null for none.</param>
		/// <param name="role">The role.</param>
		/// <param name="notes">Receives a line when nothing answers; may be null.</param>
		public List<PointOfInterestNpcChoice> Resolve(RaceTemplate race, PointOfInterestNpcRole role, List<string> notes = null)
		{
			var result = new List<PointOfInterestNpcChoice>();
			string raceKey = race != null ? race.NamingKey : string.Empty;
			string category = race != null ? race.Category : string.Empty;
			PointOfInterestNpcEntry entry = Find(raceKey, category, role);
			if (entry == null)
			{
				notes?.Add($"No NPC for {role} (race '{raceKey}', category '{category}'): spawner left empty.");
				return result;
			}
			foreach (PointOfInterestNpcChoice choice in entry.Choices)
			{
				if (choice?.Prefab == null || choice.Weight <= 0.0f)
				{
					continue;
				}
				GameObject prefab = entry.CloneForRace && race != null ? CloneForRace(choice.Prefab, race, notes) : choice.Prefab;
				if (prefab != null)
				{
					result.Add(new PointOfInterestNpcChoice { Prefab = prefab, Weight = choice.Weight });
				}
			}
			return result;
		}

		/// <summary>
		/// A copy of <paramref name="basePrefab"/> for <paramref name="race"/>, made once by <see cref="NPCPrefabFactory"/>
		/// and reused; the base itself when the copy cannot be made.
		/// </summary>
		public static GameObject CloneForRace(GameObject basePrefab, RaceTemplate race, List<string> notes = null)
		{
			NPCRecipe recipe = NPCPrefabFactory.RecipeFrom(basePrefab);
			if (recipe == null || race == null)
			{
				return basePrefab;
			}
			recipe.Race = race;
			recipe.Name = RaceCopyName(race, basePrefab.name);
			recipe.Folder = $"{ClonedFolder}/{WorldEditorAssets.Sanitize(race.name)}";
			string path = NPCPrefabFactory.TargetPath(recipe);
			GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
			if (existing != null)
			{
				return existing;
			}
			// A copy made under the old name ("Elf a raider") is renamed, GUID kept, so the scenes that spawn it still do.
			string old = $"{recipe.Folder}/{race.name} {basePrefab.name}.prefab";
			if (AssetDatabase.LoadAssetAtPath<GameObject>(old) != null && RenameCopy(old, recipe.Name, notes))
			{
				return AssetDatabase.LoadAssetAtPath<GameObject>(path);
			}
			var problems = new List<string>();
			if (!NPCPrefabFactory.Validate(recipe, problems))
			{
				notes?.Add($"Could not copy '{basePrefab.name}' for {race.name}: {string.Join("; ", problems)}. Using the base prefab.");
				return basePrefab;
			}
			try
			{
				return NPCPrefabFactory.Create(recipe);
			}
			catch (Exception ex)
			{
				notes?.Add($"Could not copy '{basePrefab.name}' for {race.name}: {ex.Message}. Using the base prefab.");
				return basePrefab;
			}
		}

		/// <summary>
		/// A race copy's name, as its nameplate shows it: "an elf raider" from Elf and "a raider", "a frost giant guard" from
		/// Frost Giant and "a guard" (it was "Elf a raider"). The base's own article is dropped, the race lowered
		/// and an article chosen for it.
		/// </summary>
		public static string RaceCopyName(RaceTemplate race, string baseName)
		{
			string noun = (baseName ?? string.Empty).Trim();
			foreach (string article in new[] { "a ", "an ", "the " })
			{
				if (noun.StartsWith(article, StringComparison.OrdinalIgnoreCase))
				{
					noun = noun.Substring(article.Length);
					break;
				}
			}
			string phrase = $"{race.name.Trim().ToLowerInvariant()} {noun}".Trim();
			return ("aeiou".IndexOf(char.ToLowerInvariant(phrase[0])) >= 0 ? "an " : "a ") + phrase;
		}

		/// <summary>Renames a prefab in place (GUID kept) and its Addressables entry's address with it.</summary>
		public static bool RenameCopy(string path, string newName, List<string> notes)
		{
			string error = AssetDatabase.RenameAsset(path, newName);
			if (!string.IsNullOrEmpty(error))
			{
				notes?.Add($"Could not rename '{path}' to '{newName}': {error}");
				return false;
			}
			var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
			UnityEditor.AddressableAssets.Settings.AddressableAssetEntry entry =
				settings != null ? settings.FindAssetEntry(AssetDatabase.AssetPathToGUID($"{Path.GetDirectoryName(path).Replace('\\', '/')}/{newName}.prefab")) : null;
			if (entry != null)
			{
				entry.SetAddress(newName);
			}
			return true;
		}

		/// <summary>The race whose naming key is <paramref name="namingKey"/>, or null. Scans the assets (the runtime cache is empty in the editor).</summary>
		public static RaceTemplate FindRace(string namingKey)
		{
			if (string.IsNullOrWhiteSpace(namingKey))
			{
				return null;
			}
			string[] guids = AssetDatabase.FindAssets("t:RaceTemplate");
			Array.Sort(guids, StringComparer.Ordinal);
			foreach (string guid in guids)
			{
				RaceTemplate race = AssetDatabase.LoadAssetAtPath<RaceTemplate>(AssetDatabase.GUIDToAssetPath(guid));
				if (race != null && string.Equals(race.NamingKey, namingKey.Trim(), StringComparison.OrdinalIgnoreCase))
				{
					return race;
				}
			}
			return null;
		}

		/// <summary>Draws one choice by weight. Deterministic in <paramref name="random"/>.</summary>
		public static GameObject Pick(IReadOnlyList<PointOfInterestNpcChoice> choices, DeterministicRNG random)
		{
			if (choices == null || choices.Count == 0)
			{
				return null;
			}
			float total = 0.0f;
			foreach (PointOfInterestNpcChoice choice in choices)
			{
				total += Mathf.Max(0.0f, choice.Weight);
			}
			float roll = (random != null ? random.NextFloat() : 0.0f) * total;
			foreach (PointOfInterestNpcChoice choice in choices)
			{
				roll -= Mathf.Max(0.0f, choice.Weight);
				if (roll < 0.0f)
				{
					return choice.Prefab;
				}
			}
			return choices[choices.Count - 1].Prefab;
		}

		private static bool HasChoices(PointOfInterestNpcEntry entry)
		{
			foreach (PointOfInterestNpcChoice choice in entry.Choices)
			{
				if (choice?.Prefab != null && choice.Weight > 0.0f)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Writes the default table to <see cref="AssetPath"/> for editing. Leaves an existing asset alone.</summary>
		[DashboardTool(DashboardToolAttribute.Maintenance, "Create POI NPC Table", Section = "Points of Interest", Order = 11,
			Tooltip = "Writes the point-of-interest NPC role table with its defaults (human service NPCs, orc packs) for editing. An existing table is left alone.")]
		public static void CreateAsset()
		{
			if (AssetDatabase.LoadAssetAtPath<PointOfInterestNpcTable>(AssetPath) != null)
			{
				Debug.Log($"[PointOfInterestNpcTable] '{AssetPath}' already exists; left as authored.");
				return;
			}
			string folder = Path.GetDirectoryName(AssetPath).Replace('\\', '/');
			if (!AssetDatabase.IsValidFolder(folder))
			{
				string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
				if (!AssetDatabase.IsValidFolder(parent))
				{
					AssetDatabase.CreateFolder(Path.GetDirectoryName(parent).Replace('\\', '/'), Path.GetFileName(parent));
				}
				AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
			}
			PointOfInterestNpcTable table = CreateInstance<PointOfInterestNpcTable>();
			table.FillDefaults();
			AssetDatabase.CreateAsset(table, AssetPath);
			AssetDatabase.SaveAssets();
			Debug.Log($"[PointOfInterestNpcTable] Wrote '{AssetPath}'.");
		}
	}
}
#endif
