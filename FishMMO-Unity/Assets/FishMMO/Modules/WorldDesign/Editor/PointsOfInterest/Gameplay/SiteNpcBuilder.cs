#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using FishMMO.Server.Implementation.World.SceneServer.AI;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Makes the settlement NPCs the points of interest's Guard and Townsfolk spawners were left empty without (2026-11-22,
	/// Jim: "add some basic NPCs (guards, townsfolk, monster packs) for the empty spawners"), and adds the rows that use them
	/// and the generated monsters (WildlifeBuilder.Monster) to the point-of-interest NPC table.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Guard:</b> a copy of the orc warrior (its melee kit) as a human with the standard melee AI, not aggressive: it fights
	/// what attacks it and its town. <b>Townsperson:</b> a copy of the general merchant with no service and the
	/// townsfolk AI (idles, wanders a little, flees). Both are BASES: the table's rows copy them for each site's race
	/// (<see cref="PointOfInterestNpcEntry.CloneForRace"/>), so an elf town gets elf guards. <b>Raider:</b> the warrior again,
	/// aggressive and keeping its loot: the pack of a camp or lair whose race is not a beast (goblins at a goblin camp).
	/// </para>
	/// <para>
	/// Run after "Build wildlife" (the monsters are its prefabs); then re-cut, so the sites' spawners are filled. Idempotent:
	/// existing prefabs and authored table rows are kept.
	/// </para>
	/// </remarks>
	public static class SiteNpcBuilder
	{
		private const string ArchetypeFolder = "Assets/FishMMO/Modules/NPCs/Templates/AI/Archetypes";
		private const string HumanRacePath = "Assets/FishMMO/Templates/Entity/Races/Humanoid/Human.asset";

		[DashboardTool(DashboardToolAttribute.Maintenance, "Build site NPCs", Section = "Points of Interest", Order = 12,
			Tooltip = "Makes the guard, townsperson and raider bases and adds the guard, townsfolk, raider and monster rows to the point-of-interest NPC table (run Build wildlife first). Then re-cut to fill the sites' spawners.")]
		public static void BuildFromDashboard() => Debug.Log("[Site NPCs]\n  " + string.Join("\n  ", Build()));

		/// <summary><c>-executeMethod FishMMO.Shared.WorldDesign.SiteNpcBuilder.BuildBatch</c>: exits 0 unless something failed.</summary>
		public static void BuildBatch()
		{
			List<string> notes = Build();
			Debug.Log("[Site NPCs]\n  " + string.Join("\n  ", notes));
			if (Application.isBatchMode)
			{
				EditorApplication.Exit(notes.Exists(n => n.StartsWith("FAILED")) ? 1 : 0);
			}
		}

		public static List<string> Build()
		{
			var notes = new List<string>();
			RaceTemplate human = AssetDatabase.LoadAssetAtPath<RaceTemplate>(HumanRacePath);
			if (human == null)
			{
				notes.Add($"FAILED: no human race at {HumanRacePath}.");
				return notes;
			}
			Ensure(PointOfInterestNpcTable.GuardPath, PointOfInterestNpcTable.OrcWarriorPath, human, "Enemy - Melee", aggressive: false, keepLoot: false, notes);
			Ensure(PointOfInterestNpcTable.TownspersonPath, PointOfInterestNpcTable.MerchantPath, human, "Civilian - Townsfolk", aggressive: false, keepLoot: false, notes);
			// A camp's or lair's fighters, for any race that is not a beast: attacks on sight and drops the warrior's loot.
			Ensure(PointOfInterestNpcTable.RaiderPath, PointOfInterestNpcTable.OrcWarriorPath, human, "Enemy - Melee", aggressive: true, keepLoot: true, notes);

			RenameRaceCopies(notes);

			PointOfInterestNpcTable table = AssetDatabase.LoadAssetAtPath<PointOfInterestNpcTable>(PointOfInterestNpcTable.AssetPath);
			if (table == null)
			{
				notes.Add($"No table at {PointOfInterestNpcTable.AssetPath}: the generator uses the code defaults, which carry the new rows.");
				return notes;
			}
			int added = table.AddMissingSiteRows();
			if (added > 0)
			{
				EditorUtility.SetDirty(table);
				AssetDatabase.SaveAssets();
			}
			notes.Add($"Point-of-interest NPC table: {added} row(s) added ({table.Entries.Count} in all).");
			foreach (PointOfInterestNpcEntry row in PointOfInterestNpcTable.SiteRows())
			{
				if (row.Choices.Count == 0)
				{
					notes.Add($"FAILED: no prefab for the {row.Role} row (run Build wildlife for the monsters).");
				}
			}
			return notes;
		}

		/// <summary>
		/// Renames the race copies made under the old name ("Elf a raider") to their nameplate name ("an elf raider"), GUIDs
		/// kept, so the scenes' spawners still spawn them without a re-cut.
		/// </summary>
		private static void RenameRaceCopies(List<string> notes)
		{
			if (!AssetDatabase.IsValidFolder(PointOfInterestNpcTable.ClonedFolder))
			{
				return;
			}
			int renamed = 0;
			var races = new Dictionary<string, RaceTemplate>();
			foreach (string guid in AssetDatabase.FindAssets("t:RaceTemplate"))
			{
				RaceTemplate race = AssetDatabase.LoadAssetAtPath<RaceTemplate>(AssetDatabase.GUIDToAssetPath(guid));
				if (race != null)
				{
					races[WorldEditorAssets.Sanitize(race.name)] = race;
				}
			}
			foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PointOfInterestNpcTable.ClonedFolder }))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				string folder = Path.GetFileName(Path.GetDirectoryName(path));
				string name = Path.GetFileNameWithoutExtension(path);
				if (!races.TryGetValue(folder, out RaceTemplate race) || !name.StartsWith(race.name + " a", System.StringComparison.Ordinal))
				{
					continue;
				}
				string baseName = name.Substring(race.name.Length + 1);
				string newName = PointOfInterestNpcTable.RaceCopyName(race, baseName);
				if (newName != name && PointOfInterestNpcTable.RenameCopy(path, newName, notes))
				{
					renamed++;
				}
			}
			AssetDatabase.SaveAssets();
			notes.Add($"Race copies renamed to their nameplate names: {renamed}.");
		}

		/// <summary>The prefab at <paramref name="path"/>, made from <paramref name="sourcePath"/>'s recipe when missing.</summary>
		private static void Ensure(string path, string sourcePath, RaceTemplate race, string archetypeName, bool aggressive, bool keepLoot, List<string> notes)
		{
			if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
			{
				notes.Add($"Kept {path}.");
				return;
			}
			GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
			NPCRecipe recipe = source != null ? NPCPrefabFactory.RecipeFrom(source) : null;
			if (recipe == null)
			{
				notes.Add($"FAILED: {sourcePath} is not an NPC prefab.");
				return;
			}
			AIArchetypeTemplate archetype = AssetDatabase.LoadAssetAtPath<AIArchetypeTemplate>($"{ArchetypeFolder}/{archetypeName}.asset");
			if (archetype == null)
			{
				notes.Add($"FAILED: no AI archetype '{archetypeName}'.");
				return;
			}
			recipe.Name = Path.GetFileNameWithoutExtension(path);
			recipe.Folder = Path.GetDirectoryName(path).Replace('\\', '/');
			recipe.Race = race;
			recipe.Archetype = archetype;
			recipe.IsAggressive = aggressive;
			recipe.IsCharmable = false;
			if (!keepLoot)
			{
				recipe.LootTable = null;
			}
			recipe.Interaction = NPCInteraction.None;
			recipe.MerchantTemplate = null;
			// Abilities are kept as the source has them: the warrior's melee kit, the merchant's none. (Touching the list would
			// make this assembly name the Abilities module's types.)
			recipe.RegisterAddressable = true;
			var problems = new List<string>();
			if (!NPCPrefabFactory.Validate(recipe, problems))
			{
				notes.Add($"FAILED: {path}: {string.Join("; ", problems)}");
				return;
			}
			GameObject created = NPCPrefabFactory.Create(recipe);
			notes.Add(created != null ? $"Made {path} ({archetypeName})." : $"FAILED: could not make {path}.");
		}
	}
}
#endif
