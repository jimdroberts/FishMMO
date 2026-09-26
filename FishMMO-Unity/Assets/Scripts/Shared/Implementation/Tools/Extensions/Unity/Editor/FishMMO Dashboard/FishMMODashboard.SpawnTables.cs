#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using FishMMO.Server.Implementation.World.SceneServer.Spawner;

namespace FishMMO.Shared
{
	/// <summary>
	/// The Spawn Tables category: a read-only view of the server-only tables baked from the world
	/// scenes' spawners, and the button that rebakes them.
	/// </summary>
	/// <remarks>
	/// A table is build output. Spawners are authored on <see cref="ObjectSpawner"/> components in
	/// the scenes, stripped from every shipped scene, and baked by <see cref="SpawnTableBaker"/> on
	/// scene save and before every addressables or game build. Editing a table by hand would be
	/// overwritten by the next bake, so this view shows what the server will run and sends edits
	/// back to the scene.
	/// </remarks>
	public partial class FishMMODashboard
	{
		/// <summary>Sidebar name of the category.</summary>
		private const string SPAWN_TABLES_CATEGORY = "Spawn Tables";

		/// <summary>Colour of problem text.</summary>
		private static readonly Color SpawnWarningColor = new Color(1f, 0.75f, 0.4f, 1f);

		/// <summary>Colour of text that would stop a build.</summary>
		private static readonly Color SpawnBlockingColor = new Color(1f, 0.45f, 0.45f, 1f);

		/// <summary>The result of the last bake run from the dashboard, or null.</summary>
		private SpawnBakeResult lastSpawnBake;

		/// <summary>What a dashboard bake reported.</summary>
		private sealed class SpawnBakeResult
		{
			public DateTime Time;
			public int Spawners;
			public List<string> Problems = new List<string>();
			public List<string> Blocking = new List<string>();
			public string Error;
		}

		/// <summary>
		/// Adds the Spawn Tables category to the World group.
		/// </summary>
		private void RegisterSpawnTablesCategory()
		{
			categories.Add(new TemplateCategory
			{
				DisplayName = SPAWN_TABLES_CATEGORY,
				Group = "World",
				DefaultAssetDirectory = SpawnTableBaker.TableFolder,
				// No AssetType and no CreateAsset: tables come from the bake, never the create button.
				LoadAssets = () => new List<UnityEngine.Object>(LoadSpawnTables()),
				CountAssets = () => LoadSpawnTables().Count,
			});
		}

		/// <summary>
		/// Every baked table in the table folder.
		/// </summary>
		private static List<SceneSpawnTable> LoadSpawnTables()
		{
			List<SceneSpawnTable> tables = new List<SceneSpawnTable>();
			if (!AssetDatabase.IsValidFolder(SpawnTableBaker.TableFolder))
			{
				return tables;
			}

			string[] guids = AssetDatabase.FindAssets($"t:{nameof(SceneSpawnTable)}", new[] { SpawnTableBaker.TableFolder });
			for (int i = 0; i < guids.Length; i++)
			{
				SceneSpawnTable table = AssetDatabase.LoadAssetAtPath<SceneSpawnTable>(AssetDatabase.GUIDToAssetPath(guids[i]));
				if (table != null)
				{
					tables.Add(table);
				}
			}
			return tables;
		}

		/// <summary>
		/// True when the Spawn Tables category is the one on screen.
		/// </summary>
		private bool IsSpawnTablesCategorySelected()
		{
			return selectedCategoryIndex >= 0 &&
				selectedCategoryIndex < categories.Count &&
				categories[selectedCategoryIndex].DisplayName == SPAWN_TABLES_CATEGORY;
		}

		// ── Inspector: overview ─────────────────────────────────────────────────────────

		/// <summary>
		/// The category's landing page: every world scene, whether it has a table, what the
		/// catalogue the server reads holds, and the rebake button.
		/// </summary>
		private void ShowSpawnTablesOverview()
		{
			ClearInspector();

			if (inspectorHeader != null)
			{
				inspectorHeader.text = SPAWN_TABLES_CATEGORY;
			}

			AddSpawnNote("Server-only data baked from the ObjectSpawner components in each world scene. " +
				"Tables rebake when a scene is saved and before every addressables or game build. " +
				"Edit the spawners in the scene; edits made here would be lost on the next bake.");

			List<SceneSpawnTable> tables = LoadSpawnTables();
			SpawnTableCatalogue catalogue = AssetDatabase.LoadAssetAtPath<SpawnTableCatalogue>(SpawnTableBaker.CataloguePath);
			List<string> scenePaths = SpawnTableBaker.FindWorldScenePaths();

			int spawnerTotal = 0;
			for (int i = 0; i < tables.Count; i++)
			{
				spawnerTotal += tables[i].Spawners != null ? tables[i].Spawners.Count : 0;
			}

			VisualElement summary = CreateConstantsSection("Summary");
			AddConstantRow(summary, "World Scenes", scenePaths.Count.ToString());
			AddConstantRow(summary, "Tables", tables.Count.ToString());
			AddConstantRow(summary, "Spawners", spawnerTotal.ToString());
			AddConstantRow(summary, "Catalogue", catalogue != null
				? $"{catalogue.Tables.Count} table(s) — {SpawnTableBaker.CataloguePath}"
				: "missing — rebake to create it");
			AddConstantRow(summary, "Folder", SpawnTableBaker.TableFolder);

			List<string> issues = DescribeCatalogueIssues(tables, catalogue, scenePaths);
			if (issues.Count > 0)
			{
				AddSpawnWarning(summary, issues, SpawnWarningColor);
			}
			inspectorContent.Add(summary);

			VisualElement scenes = CreateConstantsSection("World Scenes");
			for (int i = 0; i < scenePaths.Count; i++)
			{
				string sceneName = Path.GetFileNameWithoutExtension(scenePaths[i]);
				SceneSpawnTable table = tables.Find(t => t.SceneName == sceneName);
				AddConstantRow(scenes, sceneName, table != null
					? $"{table.Spawners.Count} spawner(s)"
					: "no spawners");
			}
			inspectorContent.Add(scenes);

			AddSpawnBakeResultSection();
			AddRebuildSpawnTablesButton();
			AddToolSections(DashboardToolAttribute.SpawnTables);
		}

		/// <summary>
		/// Mismatches between the tables on disk, the catalogue and the world scenes.
		/// </summary>
		private static List<string> DescribeCatalogueIssues(List<SceneSpawnTable> tables, SpawnTableCatalogue catalogue, List<string> scenePaths)
		{
			List<string> issues = new List<string>();
			if (catalogue == null)
			{
				if (tables.Count > 0)
				{
					issues.Add("The catalogue is missing, so the server loads none of these tables.");
				}
				return issues;
			}

			for (int i = 0; i < tables.Count; i++)
			{
				SceneSpawnTable table = tables[i];
				if (!catalogue.Tables.Contains(table))
				{
					issues.Add($"{table.name}: not in the catalogue; the server will not load it.");
				}
				if (!scenePaths.Contains(table.ScenePath))
				{
					issues.Add($"{table.name}: {table.ScenePath} is no longer a world scene.");
				}
			}

			for (int i = 0; i < catalogue.Tables.Count; i++)
			{
				if (catalogue.Tables[i] == null)
				{
					issues.Add($"Catalogue entry {i} is empty.");
				}
			}
			return issues;
		}

		/// <summary>
		/// What the last dashboard bake reported, if one has run since the window opened.
		/// </summary>
		private void AddSpawnBakeResultSection()
		{
			if (lastSpawnBake == null)
			{
				return;
			}

			VisualElement section = CreateConstantsSection("Last Rebuild");
			AddConstantRow(section, "Time", lastSpawnBake.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
			if (lastSpawnBake.Error != null)
			{
				AddSpawnWarning(section, new List<string> { lastSpawnBake.Error }, SpawnBlockingColor);
				inspectorContent.Add(section);
				return;
			}

			AddConstantRow(section, "Spawners Baked", lastSpawnBake.Spawners.ToString());
			AddConstantRow(section, "Problems", lastSpawnBake.Problems.Count.ToString());
			AddConstantRow(section, "Build Blockers", lastSpawnBake.Blocking.Count.ToString());

			if (lastSpawnBake.Blocking.Count > 0)
			{
				List<string> lines = new List<string> { "These stop every build — the spawner would ship to clients:" };
				lines.AddRange(lastSpawnBake.Blocking);
				AddSpawnWarning(section, lines, SpawnBlockingColor);
			}
			if (lastSpawnBake.Problems.Count > 0)
			{
				AddSpawnWarning(section, lastSpawnBake.Problems, SpawnWarningColor);
			}
			inspectorContent.Add(section);
		}

		/// <summary>
		/// The rebake-everything button.
		/// </summary>
		private void AddRebuildSpawnTablesButton()
		{
			Button rebuild = new Button(RebuildSpawnTables);
			rebuild.text = "Rebuild Spawn Tables";
			rebuild.tooltip = "Opens every world scene that is not open, bakes its spawners, and updates the catalogue.";
			rebuild.style.marginTop = 8;
			rebuild.style.height = 28;
			rebuild.style.backgroundColor = new Color(0.24f, 0.43f, 0.24f, 1f);
			rebuild.style.color = new Color(0.75f, 0.95f, 0.75f, 1f);
			rebuild.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode);
			inspectorContent.Add(rebuild);
		}

		/// <summary>
		/// Bakes every world scene and refreshes the category.
		/// </summary>
		private void RebuildSpawnTables()
		{
			if (EditorApplication.isPlayingOrWillChangePlaymode)
			{
				SetStatus("Spawn tables cannot be rebuilt in play mode.");
				return;
			}

			/* The bake reads open scenes as they are in memory, but a build reads them from disk.
			 * Offer to save first so the tables match what will ship. */
			if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
			{
				SetStatus("Spawn table rebuild cancelled.");
				return;
			}

			SpawnBakeResult result = new SpawnBakeResult { Time = DateTime.Now };
			try
			{
				result.Spawners = SpawnTableBaker.BakeAll(result.Problems, result.Blocking);
			}
			catch (Exception ex)
			{
				result.Error = ex.Message;
				Debug.LogException(ex);
			}
			lastSpawnBake = result;

			for (int i = 0; i < result.Problems.Count; i++)
			{
				Debug.LogWarning($"[SpawnTableBaker] {result.Problems[i]}");
			}

			if (result.Error != null)
			{
				SetStatus($"Spawn table rebuild failed: {result.Error}");
			}
			else if (result.Blocking.Count > 0)
			{
				SetStatus($"Baked {result.Spawners} spawner(s); {result.Blocking.Count} would block a build.");
			}
			else
			{
				SetStatus($"Baked {result.Spawners} spawner(s); {result.Problems.Count} problem(s).");
			}

			RefreshSpawnTablesCategory();
		}

		/// <summary>
		/// Reloads the table list and shows the overview, if the category is still on screen.
		/// </summary>
		private void RefreshSpawnTablesCategory()
		{
			BuildCategoryList();
			if (!IsSpawnTablesCategorySelected())
			{
				return;
			}

			selectedAssetIndex = -1;
			LoadAssetsForCategory(categories[selectedCategoryIndex]);
			RefreshEntityList();
			ShowSpawnTablesOverview();
		}

		// ── Inspector: one table ────────────────────────────────────────────────────────

		/// <summary>
		/// Shows one scene's baked spawners, read-only.
		/// </summary>
		/// <param name="table">The baked table.</param>
		private void ShowSpawnTableInspector(SceneSpawnTable table)
		{
			ClearInspector();

			if (table == null)
			{
				return;
			}

			if (inspectorHeader != null)
			{
				inspectorHeader.text = table.name;
			}

			SpawnTableCatalogue catalogue = AssetDatabase.LoadAssetAtPath<SpawnTableCatalogue>(SpawnTableBaker.CataloguePath);
			List<SpawnerDefinition> spawners = table.Spawners ?? new List<SpawnerDefinition>();

			int maxTotal = 0;
			int initialTotal = 0;
			for (int i = 0; i < spawners.Count; i++)
			{
				if (spawners[i] == null) continue;
				maxTotal += spawners[i].MaxSpawnCount;
				initialTotal += spawners[i].InitialSpawnCount;
			}

			VisualElement summary = CreateConstantsSection("Summary");
			AddConstantRow(summary, "Scene", table.SceneName);
			AddConstantRow(summary, "Scene Path", table.ScenePath);
			AddConstantRow(summary, "Spawners", spawners.Count.ToString());
			AddConstantRow(summary, "Initial / Max Objects", $"{initialTotal} / {maxTotal}");
			AddConstantRow(summary, "In Catalogue", catalogue != null && catalogue.Tables.Contains(table) ? "yes" : "NO — the server will not load it");
			AddConstantRow(summary, "Table", AssetDatabase.GetAssetPath(table));
			inspectorContent.Add(summary);

			AddSpawnNote("Read-only: this is what the scene server runs. Edit the spawner in the scene, then save it or rebake.");

			for (int i = 0; i < spawners.Count; i++)
			{
				AddSpawnerSection(spawners[i], i);
			}

			Button rebakeScene = new Button(() => RebakeSpawnScene(table.ScenePath));
			rebakeScene.text = "Rebake This Scene";
			rebakeScene.style.marginTop = 8;
			rebakeScene.style.height = 28;
			rebakeScene.style.backgroundColor = new Color(0.24f, 0.43f, 0.24f, 1f);
			rebakeScene.style.color = new Color(0.75f, 0.95f, 0.75f, 1f);
			rebakeScene.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode && !string.IsNullOrEmpty(table.ScenePath));
			inspectorContent.Add(rebakeScene);

			Button rebuildAll = new Button(RebuildSpawnTables);
			rebuildAll.text = "Rebuild All Spawn Tables";
			rebuildAll.style.marginTop = 4;
			rebuildAll.style.height = 26;
			rebuildAll.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode);
			inspectorContent.Add(rebuildAll);

			Button openScene = new Button(() =>
			{
				if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
				{
					EditorSceneManager.OpenScene(table.ScenePath, OpenSceneMode.Single);
				}
			});
			openScene.text = "Open Scene";
			openScene.style.marginTop = 4;
			openScene.style.height = 24;
			openScene.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode && AssetDatabase.LoadAssetAtPath<SceneAsset>(table.ScenePath) != null);
			inspectorContent.Add(openScene);

			Button selectButton = new Button(() =>
			{
				Selection.activeObject = table;
				EditorGUIUtility.PingObject(table);
			});
			selectButton.text = "Select in Project";
			selectButton.style.marginTop = 4;
			selectButton.style.height = 24;
			inspectorContent.Add(selectButton);
		}

		/// <summary>
		/// One spawner: where it is, how it schedules, and what it spawns.
		/// </summary>
		private void AddSpawnerSection(SpawnerDefinition spawner, int index)
		{
			Foldout foldout = new Foldout();
			foldout.value = false;
			foldout.AddToClassList("constants-section");

			if (spawner == null)
			{
				foldout.text = $"#{index} — empty entry";
				inspectorContent.Add(foldout);
				return;
			}

			List<SpawnableSettings> spawnables = spawner.Spawnables ?? new List<SpawnableSettings>();
			foldout.text = $"#{index} {spawner.Name} — {spawner.SpawnType}, max {spawner.MaxSpawnCount}, {spawnables.Count} spawnable(s)";

			List<string> problems = DescribeSpawnerProblems(spawner);
			if (problems.Count > 0)
			{
				foldout.text += "  ⚠";
				foldout.value = true;
				AddSpawnWarning(foldout, problems, SpawnWarningColor);
			}

			AddConstantRow(foldout, "Position", FormatVector(spawner.Position));
			AddConstantRow(foldout, "Rotation", FormatVector(spawner.Rotation.eulerAngles));
			AddConstantRow(foldout, "Initial / Max", $"{spawner.InitialSpawnCount} / {spawner.MaxSpawnCount}");
			AddConstantRow(foldout, "Spawn Type", spawner.SpawnType + (spawner.UniqueSpawnables ? ", unique" : string.Empty));
			AddConstantRow(foldout, "Placement", spawner.RandomSpawnPosition
				? $"random — sphere r {F(spawner.SphereRadius)}, box {FormatVector(spawner.BoundingBoxSize)}"
				: "at the spawner");
			AddConstantRow(foldout, "Initial Respawn", $"{F(spawner.InitialRespawnTime)} s");
			AddConstantRow(foldout, "Respawn Check", $"{F(spawner.RespawnCheckIntervalMinimum)}–{F(spawner.RespawnCheckIntervalMaximum)} s");
			AddConstantRow(foldout, "Random Respawn Time", spawner.RandomRespawnTime ? "yes" : "no");
			AddConstantRow(foldout, "Prewarm Pool", spawner.PrewarmPool ? $"yes, +{spawner.PrewarmHeadroom} headroom" : "no");
			AddConstantRow(foldout, "Any Of (OR)", DescribeConditions(spawner.OrConditions));
			AddConstantRow(foldout, "All Of (AND)", DescribeConditions(spawner.TrueConditions));
			AddConstantRow(foldout, "Pack", spawner.Pack != null && spawner.Pack.Enabled
				? $"yes — {spawner.Pack.Tactic}, ring {F(spawner.Pack.TacticOrbitRadius)} m{(spawner.Pack.FocusTargeting ? ", focus follows the tank" : string.Empty)}"
				: "no");

			for (int i = 0; i < spawnables.Count; i++)
			{
				AddSpawnableRows(foldout, spawner, spawnables[i], i);
			}

			inspectorContent.Add(foldout);
		}

		/// <summary>
		/// The rows for one spawnable entry.
		/// </summary>
		private void AddSpawnableRows(VisualElement parent, SpawnerDefinition spawner, SpawnableSettings settings, int index)
		{
			VisualElement section = CreateConstantsSection(settings == null
				? $"Spawnable {index} — empty"
				: $"Spawnable {index} — {(settings.NetworkObject != null ? settings.NetworkObject.name : "no prefab")}");
			section.style.marginLeft = 8;

			if (settings == null)
			{
				parent.Add(section);
				return;
			}

			settings.ResolveRespawnTimeRange(out float minimum, out float maximum);
			AddConstantRow(section, "Kind", settings is NPCSpawnableSettings ? "NPC" : settings is ItemSpawnableSettings ? "Item" : "Generic");
			AddConstantRow(section, "Prefab", settings.NetworkObject != null ? AssetDatabase.GetAssetPath(settings.NetworkObject) : "— none —");
			AddConstantRow(section, "Spawn Chance", F(settings.SpawnChance));
			AddConstantRow(section, "Respawn", $"{F(minimum)}–{F(maximum)} s");
			AddConstantRow(section, "Y Offset", F(settings.YOffset));

			if (settings is NPCSpawnableSettings npc)
			{
				if (spawner != null && spawner.Pack != null && spawner.Pack.Enabled)
				{
					AddConstantRow(section, "Pack Role", npc.PackRole.ToString());
				}
				AddOverrideRow(section, "Archetype", npc.ArchetypeOverride);
				AddOverrideRow(section, "Loot Table", npc.LootTableOverride);
				AddOverrideRow(section, "Faction", npc.FactionOverride);
				AddOverrideRow(section, "Attribute Bonus", npc.AttributeBonusOverride);
				if (npc.AdditionalAbilities != null && npc.AdditionalAbilities.Count > 0)
				{
					AddConstantRow(section, npc.ReplacePrefabAbilities ? "Abilities (replace)" : "Abilities (add)", npc.AdditionalAbilities.Count.ToString());
				}
				if (!Mathf.Approximately(npc.MinimumScale, 1f) || !Mathf.Approximately(npc.MaximumScale, 1f))
				{
					AddConstantRow(section, "Scale", $"{F(npc.MinimumScale)}–{F(npc.MaximumScale)}");
				}
			}
			else if (settings is ItemSpawnableSettings item)
			{
				AddConstantRow(section, "Item", item.ItemTemplate != null ? item.ItemTemplate.name : "— from rolls / prefab —");
			}

			parent.Add(section);
		}

		/// <summary>
		/// A row for an override field, shown only when the override is set.
		/// </summary>
		private void AddOverrideRow(VisualElement parent, string label, UnityEngine.Object value)
		{
			if (value != null)
			{
				AddConstantRow(parent, label, value.name);
			}
		}

		/// <summary>
		/// What in a baked spawner makes it spawn nothing, or less than it says.
		/// </summary>
		private static List<string> DescribeSpawnerProblems(SpawnerDefinition spawner)
		{
			List<string> problems = new List<string>();
			List<SpawnableSettings> spawnables = spawner.Spawnables;

			if (spawnables == null || spawnables.Count == 0)
			{
				problems.Add("No spawnables — this spawner spawns nothing.");
				return problems;
			}

			bool anyChance = false;
			for (int i = 0; i < spawnables.Count; i++)
			{
				SpawnableSettings settings = spawnables[i];
				if (settings == null)
				{
					problems.Add($"Spawnable {i} is empty.");
					continue;
				}
				if (settings.NetworkObject == null)
				{
					problems.Add($"Spawnable {i} has no prefab.");
				}
				if (settings.SpawnChance > 0f)
				{
					anyChance = true;
				}
			}

			if (!anyChance)
			{
				problems.Add("Every spawn chance is zero.");
			}
			if (spawner.MaxSpawnCount <= 0)
			{
				problems.Add("Max spawn count is zero.");
			}
			if (spawner.InitialSpawnCount > spawner.MaxSpawnCount)
			{
				problems.Add($"Initial count {spawner.InitialSpawnCount} exceeds the max of {spawner.MaxSpawnCount}.");
			}
			return problems;
		}

		/// <summary>
		/// A condition list as its type names.
		/// </summary>
		private static string DescribeConditions(List<RespawnCondition> conditions)
		{
			if (conditions == null || conditions.Count == 0)
			{
				return "—";
			}

			List<string> names = new List<string>(conditions.Count);
			for (int i = 0; i < conditions.Count; i++)
			{
				names.Add(conditions[i] != null ? ObjectNames.NicifyVariableName(conditions[i].GetType().Name) : "empty");
			}
			return string.Join(", ", names);
		}

		/// <summary>
		/// Bakes one scene, opening it additively if it is not open.
		/// </summary>
		private void RebakeSpawnScene(string scenePath)
		{
			if (EditorApplication.isPlayingOrWillChangePlaymode || string.IsNullOrEmpty(scenePath))
			{
				return;
			}
			if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
			{
				SetStatus("Rebake cancelled.");
				return;
			}

			Scene scene = SceneManager.GetSceneByPath(scenePath);
			bool opened = false;
			SceneSpawnTable table = null;
			List<string> problems = new List<string>();
			List<string> blocking = new List<string>();
			try
			{
				if (!scene.IsValid() || !scene.isLoaded)
				{
					scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
					opened = true;
				}
				table = SpawnTableBaker.BakeScene(scene, problems, blocking);
				AssetDatabase.SaveAssets();
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
				SetStatus($"Rebake failed: {ex.Message}");
				return;
			}
			finally
			{
				if (opened && scene.IsValid())
				{
					EditorSceneManager.CloseScene(scene, true);
				}
			}

			for (int i = 0; i < problems.Count; i++)
			{
				Debug.LogWarning($"[SpawnTableBaker] {problems[i]}");
			}

			string sceneName = Path.GetFileNameWithoutExtension(scenePath);
			SetStatus(blocking.Count > 0
				? $"Rebaked {sceneName}; {blocking.Count} spawner(s) would block a build — see the console."
				: $"Rebaked {sceneName}: {(table != null ? table.Spawners.Count : 0)} spawner(s), {problems.Count} problem(s).");

			BuildCategoryList();
			if (!IsSpawnTablesCategorySelected())
			{
				return;
			}

			// A scene whose spawners were all removed loses its table.
			LoadAssetsForCategory(categories[selectedCategoryIndex]);
			RefreshEntityList();
			if (table != null)
			{
				selectedAssetIndex = filteredAssets.IndexOf(table);
				BuildEntityList();
				ShowSpawnTableInspector(table);
			}
			else
			{
				selectedAssetIndex = -1;
				ShowSpawnTablesOverview();
			}
		}

		// ── Helpers ─────────────────────────────────────────────────────────────────────

		/// <summary>
		/// An explanatory line at the top of a page.
		/// </summary>
		private void AddSpawnNote(string text)
		{
			Label note = new Label(text);
			note.AddToClassList("empty-state-label");
			note.style.unityTextAlign = TextAnchor.MiddleLeft;
			note.style.marginBottom = 6;
			inspectorContent.Add(note);
		}

		/// <summary>
		/// A block of coloured problem lines.
		/// </summary>
		private static void AddSpawnWarning(VisualElement parent, List<string> lines, Color color)
		{
			Label warning = new Label(string.Join("\n", lines));
			warning.style.color = color;
			warning.style.whiteSpace = WhiteSpace.Normal;
			warning.style.marginTop = 4;
			warning.style.marginBottom = 4;
			parent.Add(warning);
		}

		private static string F(float value)
		{
			return value.ToString("0.##", CultureInfo.InvariantCulture);
		}

		private static string FormatVector(Vector3 value)
		{
			return $"({F(value.x)}, {F(value.y)}, {F(value.z)})";
		}
	}
}
#endif
