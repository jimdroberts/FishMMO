#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.SceneManagement;
using UnityEngine;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Deletes a world scene and everything kept for it alone: its files, its entries in the shared caches and
	/// catalogues, the teleporters in other scenes that lead into it, and its build output and editor caches.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Planned first, then done.</b> <see cref="For"/> lists everything without touching anything, so the
	/// confirmation can show exactly what goes; <see cref="Execute"/> works through that list. Paths are worked
	/// out while the atlas entry still exists, because several of them are found from it.
	/// </para>
	/// <para>
	/// <b>Caches are edited, not rebuilt.</b> Rebuilding the scene details cache or the teleporter cache opens every
	/// world scene, and the teleporter rebuild saves the scenes it touches. Neither fixes the real dangling data
	/// either: the destination IDs on teleporters in OTHER scenes. Those are cleared through
	/// <see cref="AtlasTeleporters.Connect"/>, which saves only the scene it changes. The scene's own entries are
	/// then removed from both caches.
	/// </para>
	/// <para>
	/// <b>What it only warns about:</b> other content that names the scene, such as dungeon, arena and quest templates
	/// and waypoint actions. That content is somebody's decision, so it is listed, not changed.
	/// </para>
	/// </remarks>
	public static class SceneDeletion
	{
		/// <summary>Everything a deletion will remove, worked out before anything is touched.</summary>
		public sealed class Plan
		{
			public string SceneName;
			public string ScenePath;
			public string EntryPath;
			public string WorldFolder;
			/// <summary>Project assets and folders to delete through the asset database, with what each one is.</summary>
			public readonly List<(string Path, string What)> Assets = new List<(string, string)>();
			/// <summary>Files and folders under Library: editor caches, previews, snapshots.</summary>
			public readonly List<(string Path, string What)> LibraryFiles = new List<(string, string)>();
			/// <summary>Re-cut backups of the scene: the only way back after a delete, so they can be kept.</summary>
			public readonly List<string> Backups = new List<string>();
			/// <summary>Teleporters in other scenes that lead into this one; each is disconnected and its scene saved.</summary>
			public readonly List<SceneTeleporterCacheEntry> IncomingTeleporters = new List<SceneTeleporterCacheEntry>();
			/// <summary>Shared caches and catalogues the scene is removed from, as lines for the confirmation.</summary>
			public readonly List<string> Entries = new List<string>();
			/// <summary>Assets elsewhere that name the scene. They are left alone.</summary>
			public readonly List<string> References = new List<string>();
			/// <summary>Every scene name the shared caches are cleared of: the one scene, or every dead one for a sweep.</summary>
			public readonly HashSet<string> Names = new HashSet<string>(StringComparer.Ordinal);
			/// <summary>Spawn tables to take out of the catalogue and the server addressables before they are deleted.</summary>
			public readonly List<string> SpawnTables = new List<string>();
			/// <summary>Scenes to take out of the NavMesh catalogue.</summary>
			public readonly List<string> NavMeshScenes = new List<string>();

			public bool IsEmpty => Assets.Count == 0 && LibraryFiles.Count == 0 && Backups.Count == 0
				&& IncomingTeleporters.Count == 0 && Entries.Count == 0;

			/// <summary>The confirmation text: every item, grouped.</summary>
			public string Describe(bool keepBackups)
			{
				var lines = new List<string>();
				foreach ((string path, string what) in Assets)
				{
					lines.Add(what != null ? $"{path} ({what})" : path);
				}
				foreach (string line in Entries)
				{
					lines.Add(line);
				}
				if (IncomingTeleporters.Count > 0)
				{
					var names = new List<string>();
					foreach (SceneTeleporterCacheEntry t in IncomingTeleporters)
					{
						names.Add($"{t.SceneName}/{t.TeleporterName}");
					}
					lines.Add($"Disconnects {IncomingTeleporters.Count} teleporter(s) leading here, saving their scenes: {string.Join(", ", names)}");
				}
				if (LibraryFiles.Count > 0)
				{
					var whats = new List<string>();
					foreach ((string _, string what) in LibraryFiles)
					{
						whats.Add(what);
					}
					lines.Add($"Library: {string.Join(", ", whats)}");
				}
				if (Backups.Count > 0)
				{
					lines.Add(keepBackups
						? $"Keeps {Backups.Count} re-cut backup(s) in {SceneGenerator.RecutBackupRoot}"
						: $"{Backups.Count} re-cut backup(s) in {SceneGenerator.RecutBackupRoot}");
				}
				string text = "• " + string.Join("\n• ", lines);
				if (References.Count > 0)
				{
					text += "\n\nStill naming it afterwards (left alone, fix by hand):\n• " + string.Join("\n• ", References);
				}
				return text;
			}
		}

		private const string NavMeshCataloguePath = "Assets/Prefabs/Server/SceneServer/SceneNavMeshCatalogue.asset";

		/// <summary>Everything that belongs to the scene of <paramref name="entry"/>. Touches nothing.</summary>
		/// <param name="scenePath">The scene file, when it still exists.</param>
		public static Plan For(WorldAtlasScene entry, string scenePath)
		{
			var plan = new Plan
			{
				SceneName = entry.SceneName,
				ScenePath = scenePath,
				EntryPath = AssetDatabase.GetAssetPath(entry),
			};
			string name = entry.SceneName ?? string.Empty;
			string safe = WorldEditorAssets.Sanitize(name);
			WorldBody owner = entry.Body;
			plan.Names.Add(name);

			AddAsset(plan, scenePath, null);
			if (owner != null)
			{
				plan.WorldFolder = SceneGenerator.WorldFolder(owner);
				AddAsset(plan, SceneGenerator.TerrainFolder(owner, name), "terrain, biome map, props, NavMesh, paths, backdrop");
			}

			// The POI settings, only when it is this scene's own file: one stored elsewhere may be shared.
			string settingsPath = AtlasPointsOfInterest.SettingsPathFor(name);
			if (entry.PointsOfInterest != null && AssetDatabase.GetAssetPath(entry.PointsOfInterest) == settingsPath)
			{
				AddAsset(plan, settingsPath, "point-of-interest settings");
			}
			AddAsset(plan, plan.EntryPath, "atlas entry");

			AddAsset(plan, TerrainArraySet.BakedFolder(name), "baked terrain arrays");
			AddAsset(plan, WorldMapDefinition.BakedAssetPath(name), "baked world map");
			AddAsset(plan, WorldMapDefinition.BakedImagePath(name), "baked world map image");
			string localCopy = $"{LocalArtScope.LocalScenesFolder}/{safe}";
			AddAsset(plan, localCopy, "LOCAL copy");

			string table = SpawnTableBaker.TablePathFor(name);
			if (File.Exists(table))
			{
				plan.SpawnTables.Add(table);
				plan.Assets.Add((table, "spawn table, out of the spawn table catalogue"));
			}

			var navMesh = AssetDatabase.LoadAssetAtPath<FishMMO.Server.Implementation.World.SceneServer.Navigation.SceneNavMeshCatalogue>(NavMeshCataloguePath);
			if (navMesh != null && navMesh.Entries.Exists(e => e != null && e.SceneName == name))
			{
				plan.NavMeshScenes.Add(name);
				plan.Entries.Add("Its entry in the NavMesh catalogue");
			}

			WorldSceneDetailsCache details = WorldEditorAssets.SceneDetails();
			if (details != null && details.Scenes.ContainsKey(name))
			{
				plan.Entries.Add("Its entry in the world scene details cache (waypoints, spawns, boundaries, map)");
			}

			TeleporterCache teleporters = AtlasTeleporters.Cache();
			if (teleporters != null)
			{
				var destinations = DestinationsIn(teleporters, plan.Names);
				int own = 0;
				foreach (SceneTeleporterCacheEntry t in teleporters.Teleporters.Values)
				{
					if (t == null)
					{
						continue;
					}
					if (t.SceneName == name)
					{
						own++;
					}
					else if (!string.IsNullOrEmpty(t.DestinationID) && destinations.Contains(t.DestinationID))
					{
						plan.IncomingTeleporters.Add(t);
					}
				}
				if (destinations.Count > 0 || own > 0)
				{
					plan.Entries.Add($"Its {destinations.Count} destination(s) and {own} teleporter(s) in the teleporter cache");
				}
			}

			AddLibrary(plan, AtlasPreviews.PathOf(name), "atlas preview");
			AddLibrary(plan, AtlasPreviews.FramePathOf(name), null);
			AddLibrary(plan, $"{SceneCutSnapshot.DefaultRoot}/{safe}", "cut snapshots");
			AddLibrary(plan, ShoreFieldCache(scenePath), "shore field cache");
			AddLibrary(plan, ShoreFieldCache(LocalSceneCopyTool.CopyPathFor(name)), "LOCAL shore field cache");

			if (Directory.Exists(SceneGenerator.RecutBackupRoot))
			{
				// Exactly "<name> yyyy-MM-dd HHmmss": a prefix alone would take "Moron 10"'s backups with "Moron 1"'s.
				var pattern = new Regex($"^{Regex.Escape(safe)} \\d{{4}}-\\d{{2}}-\\d{{2}} \\d{{6}}$");
				foreach (string folder in Directory.GetDirectories(SceneGenerator.RecutBackupRoot))
				{
					if (pattern.IsMatch(Path.GetFileName(folder)))
					{
						plan.Backups.Add(folder.Replace('\\', '/'));
					}
				}
			}

			FindReferences(plan);
			return plan;
		}

		/// <summary>Carries the plan out. Returns what could not be done; empty when everything went.</summary>
		public static List<string> Execute(Plan plan, bool keepBackups)
		{
			var failures = new List<string>();

			// Closed first: the asset database will not delete an open scene, and says so only by returning false.
			// Closed without saving, since it is about to go.
			Close(plan.ScenePath);
			foreach (string name in plan.Names)
			{
				Close(LocalSceneCopyTool.CopyPathFor(name));
			}

			// Teleporters elsewhere that lead here: cleared in their scenes, which are saved, before this scene's ids go.
			foreach (SceneTeleporterCacheEntry incoming in plan.IncomingTeleporters)
			{
				if (!AtlasTeleporters.Connect(incoming, null, out string error))
				{
					failures.Add($"Could not disconnect {incoming.SceneName}/{incoming.TeleporterName}: {error}");
				}
			}

			TeleporterCache teleporters = AtlasTeleporters.Cache();
			if (teleporters != null)
			{
				foreach (string id in DestinationsIn(teleporters, plan.Names))
				{
					teleporters.Destinations.Remove(id);
				}
				var keys = new List<string>();
				foreach (KeyValuePair<string, SceneTeleporterCacheEntry> pair in teleporters.Teleporters)
				{
					if (pair.Value != null && plan.Names.Contains(pair.Value.SceneName ?? string.Empty))
					{
						keys.Add(pair.Key);
					}
				}
				foreach (string key in keys)
				{
					teleporters.Teleporters.Remove(key);
				}
				EditorUtility.SetDirty(teleporters);
				AssetDatabase.SaveAssetIfDirty(teleporters);
			}

			// The scene's details, and any interactable teleporter elsewhere that still names it.
			WorldSceneDetailsCache details = WorldEditorAssets.SceneDetails();
			if (details != null)
			{
				foreach (string name in plan.Names)
				{
					details.Scenes.Remove(name);
				}
				foreach (WorldSceneDetails other in details.Scenes.Values)
				{
					if (other == null || other.Teleporters == null)
					{
						continue;
					}
					var keys = new List<string>();
					foreach (KeyValuePair<string, SceneTeleporterDetails> pair in other.Teleporters)
					{
						if (pair.Value != null && plan.Names.Contains(pair.Value.ToScene ?? string.Empty))
						{
							keys.Add(pair.Key);
						}
					}
					foreach (string key in keys)
					{
						other.Teleporters.Remove(key);
					}
				}
				EditorUtility.SetDirty(details);
				AssetDatabase.SaveAssetIfDirty(details);
			}

			// Before the terrain folder goes, so the catalogue entry is removed with its NavMesh's addressable.
			foreach (string name in plan.NavMeshScenes)
			{
				SceneNavMeshCatalogueEditor.Remove(name);
			}

			if (plan.SpawnTables.Count > 0)
			{
				var catalogue = AssetDatabase.LoadAssetAtPath<FishMMO.Server.Implementation.World.SceneServer.Spawner.SpawnTableCatalogue>(SpawnTableBaker.CataloguePath);
				bool changed = false;
				foreach (string path in plan.SpawnTables)
				{
					var table = AssetDatabase.LoadAssetAtPath<FishMMO.Server.Implementation.World.SceneServer.Spawner.SceneSpawnTable>(path);
					if (table != null && catalogue != null && catalogue.Tables.Remove(table))
					{
						changed = true;
					}
					AddressableAssetSettingsDefaultObject.Settings?.RemoveAssetEntry(AssetDatabase.AssetPathToGUID(path));
				}
				if (catalogue != null && changed)
				{
					catalogue.Invalidate();
					EditorUtility.SetDirty(catalogue);
					AssetDatabase.SaveAssetIfDirty(catalogue);
				}
			}

			foreach ((string path, string what) in plan.Assets)
			{
				if (!Exists(path))
				{
					continue;
				}
				if (!AssetDatabase.DeleteAsset(path) || Exists(path))
				{
					failures.Add($"Could not delete {path}{(what != null ? $" ({what})" : string.Empty)}.");
				}
			}

			// The world folder, if this was the last scene in it.
			if (!string.IsNullOrEmpty(plan.WorldFolder) && Directory.Exists(plan.WorldFolder)
				&& Directory.GetFileSystemEntries(plan.WorldFolder).Length == 0)
			{
				AssetDatabase.DeleteAsset(plan.WorldFolder);
			}

			var library = new List<string>();
			foreach ((string path, string _) in plan.LibraryFiles)
			{
				library.Add(path);
			}
			if (!keepBackups)
			{
				library.AddRange(plan.Backups);
			}
			foreach (string path in library)
			{
				try
				{
					if (Directory.Exists(path))
					{
						Directory.Delete(path, true);
					}
					else if (File.Exists(path))
					{
						File.Delete(path);
					}
				}
				catch (Exception e)
				{
					failures.Add($"Could not delete {path}: {e.Message}");
				}
			}

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			WorldAtlasScene.EditorLookup.Invalidate();
			return failures;
		}

		/// <summary>
		/// What scenes deleted before 2026-10-18 left behind, for exactly the names given: their entries in the shared caches
		/// and catalogues, spawn tables, baked arrays and maps, LOCAL copies, previews, snapshots and re-cut backups. Touches nothing.
		/// </summary>
		/// <param name="problems">Names refused because their scene file or atlas entry still exists.</param>
		public static Plan ForDeletedScenes(IEnumerable<string> names, List<string> problems)
		{
			var plan = new Plan();
			HashSet<string> scenes = SceneFileNames();
			foreach (string name in names)
			{
				if (string.IsNullOrWhiteSpace(name))
				{
					continue;
				}
				if (scenes.Contains(name) || WorldAtlasScene.Find(name) != null)
				{
					problems?.Add($"\"{name}\" still has a scene or an atlas entry; delete it from the atlas instead.");
					continue;
				}
				plan.Names.Add(name);
			}
			AddLeftovers(plan);
			return plan;
		}

		/// <summary>
		/// Every orphan at once, for the atlas's Clear orphans: atlas entries whose scene file is gone, and every scene the
		/// teleporter cache, the scene details cache or the NavMesh catalogue still holds with no scene file anywhere, with
		/// all they left behind. Touches nothing.
		/// </summary>
		/// <remarks>
		/// Removing only the atlas entries left a dead scene's teleporters and destinations in the teleporter cache, and
		/// teleporters in live scenes still leading into them. They showed as broken teleporters with no scene to open, and
		/// once the entry was gone nothing named the scene any more, so no later sweep found them either.
		/// </remarks>
		public static Plan ForOrphans()
		{
			var plan = new Plan();
			HashSet<string> files = SceneFileNames();
			var dead = new SortedSet<string>(StringComparer.Ordinal);
			void Consider(string name)
			{
				if (!string.IsNullOrEmpty(name) && !files.Contains(name))
				{
					dead.Add(name);
				}
			}

			// The same test the globe marks red with. A scene that exists only under LOCAL while that folder is switched
			// off loses its entry, as before, but not its LOCAL copy: its leftovers are swept only when no file is left.
			var listed = new HashSet<string>(StringComparer.Ordinal);
			foreach (string path in WorldEditorAssets.WorldScenePaths())
			{
				listed.Add(Path.GetFileNameWithoutExtension(path));
			}
			var entries = new List<(string Path, string What)>();
			foreach (WorldAtlasScene entry in WorldEditorAssets.FindAll<WorldAtlasScene>())
			{
				if (entry == null || string.IsNullOrEmpty(entry.SceneName) || listed.Contains(entry.SceneName))
				{
					continue;
				}
				entries.Add((AssetDatabase.GetAssetPath(entry), $"atlas entry of {entry.SceneName}"));
				Consider(entry.SceneName);
			}

			TeleporterCache teleporters = AtlasTeleporters.Cache();
			if (teleporters != null)
			{
				foreach (TeleporterCacheEntry destination in teleporters.Destinations.Values)
				{
					Consider(destination?.SceneName);
				}
				foreach (SceneTeleporterCacheEntry teleporter in teleporters.Teleporters.Values)
				{
					Consider(teleporter?.SceneName);
				}
			}
			WorldSceneDetailsCache details = WorldEditorAssets.SceneDetails();
			if (details != null)
			{
				foreach (string name in details.Scenes.Keys)
				{
					Consider(name);
				}
			}
			var navMesh = AssetDatabase.LoadAssetAtPath<FishMMO.Server.Implementation.World.SceneServer.Navigation.SceneNavMeshCatalogue>(NavMeshCataloguePath);
			if (navMesh != null)
			{
				foreach (var navEntry in navMesh.Entries)
				{
					Consider(navEntry?.SceneName);
				}
			}

			plan.Names.UnionWith(dead);
			AddLeftovers(plan);
			plan.Assets.InsertRange(0, entries);
			return plan;
		}

		/// <summary>Names of every scene file under the world and LOCAL scene folders, whether or not LOCAL is switched on.</summary>
		private static HashSet<string> SceneFileNames()
		{
			var scenes = new HashSet<string>(StringComparer.Ordinal);
			foreach (string root in new[] { Constants.Configuration.WorldScenePath, Constants.Configuration.LocalScenePath })
			{
				string folder = root.Replace('\\', '/').TrimEnd('/');
				if (Directory.Exists(folder))
				{
					foreach (string file in Directory.GetFiles(folder, "*.unity", SearchOption.AllDirectories))
					{
						scenes.Add(Path.GetFileNameWithoutExtension(file));
					}
				}
			}
			return scenes;
		}

		/// <summary>Adds what the scenes in <see cref="Plan.Names"/> left behind, all of which have no scene file.</summary>
		private static void AddLeftovers(Plan plan)
		{
			if (plan.Names.Count == 0)
			{
				return;
			}

			WorldSceneDetailsCache details = WorldEditorAssets.SceneDetails();
			var inDetails = new List<string>();
			foreach (string name in plan.Names)
			{
				string safe = WorldEditorAssets.Sanitize(name);
				if (details != null && details.Scenes.ContainsKey(name))
				{
					inDetails.Add(name);
				}
				string table = SpawnTableBaker.TablePathFor(name);
				if (File.Exists(table))
				{
					plan.SpawnTables.Add(table);
					plan.Assets.Add((table, $"spawn table of {name}, out of the spawn table catalogue"));
				}
				AddAsset(plan, AtlasPointsOfInterest.SettingsPathFor(name), $"point-of-interest settings of {name}");
				AddAsset(plan, TerrainArraySet.BakedFolder(name), $"baked terrain arrays of {name}");
				AddAsset(plan, WorldMapDefinition.BakedAssetPath(name), $"baked world map of {name}");
				AddAsset(plan, WorldMapDefinition.BakedImagePath(name), $"baked world map image of {name}");
				AddAsset(plan, $"{LocalArtScope.LocalScenesFolder}/{safe}", $"LOCAL copy of {name}");
				AddLibrary(plan, AtlasPreviews.PathOf(name), $"atlas preview of {name}");
				AddLibrary(plan, AtlasPreviews.FramePathOf(name), null);
				AddLibrary(plan, $"{SceneCutSnapshot.DefaultRoot}/{safe}", $"cut snapshots of {name}");
				var pattern = new Regex($"^{Regex.Escape(safe)} \\d{{4}}-\\d{{2}}-\\d{{2}} \\d{{6}}$");
				if (Directory.Exists(SceneGenerator.RecutBackupRoot))
				{
					foreach (string folder in Directory.GetDirectories(SceneGenerator.RecutBackupRoot))
					{
						if (pattern.IsMatch(Path.GetFileName(folder)))
						{
							plan.Backups.Add(folder.Replace('\\', '/'));
						}
					}
				}
			}
			if (inDetails.Count > 0)
			{
				plan.Entries.Add($"World scene details cache entries: {string.Join(", ", inDetails)}");
			}

			var navMesh = AssetDatabase.LoadAssetAtPath<FishMMO.Server.Implementation.World.SceneServer.Navigation.SceneNavMeshCatalogue>(NavMeshCataloguePath);
			if (navMesh != null)
			{
				foreach (string name in plan.Names)
				{
					if (navMesh.Entries.Exists(e => e != null && e.SceneName == name))
					{
						plan.NavMeshScenes.Add(name);
					}
				}
				if (plan.NavMeshScenes.Count > 0)
				{
					plan.Entries.Add($"NavMesh catalogue entries: {string.Join(", ", plan.NavMeshScenes)}");
				}
			}

			TeleporterCache teleporters = AtlasTeleporters.Cache();
			if (teleporters != null)
			{
				HashSet<string> destinations = DestinationsIn(teleporters, plan.Names);
				int own = 0;
				foreach (SceneTeleporterCacheEntry t in teleporters.Teleporters.Values)
				{
					if (t == null)
					{
						continue;
					}
					if (plan.Names.Contains(t.SceneName ?? string.Empty))
					{
						own++;
					}
					else if (!string.IsNullOrEmpty(t.DestinationID) && destinations.Contains(t.DestinationID))
					{
						plan.IncomingTeleporters.Add(t);
					}
				}
				if (destinations.Count > 0 || own > 0)
				{
					plan.Entries.Add($"{destinations.Count} destination(s) and {own} teleporter(s) in the teleporter cache");
				}
			}
		}

		/// <summary>
		/// Batch entry point: <c>-deletedScenes "A;B;C"</c> names the scenes already deleted whose leftovers go. Re-cut backups
		/// are kept unless <c>-sweepBackups</c> is passed. Logs the plan, then what could not be removed.
		/// </summary>
		public static void CleanDeletedScenesBatch()
		{
			string[] args = Environment.GetCommandLineArgs();
			int at = Array.IndexOf(args, "-deletedScenes");
			if (at < 0 || at + 1 >= args.Length)
			{
				Debug.LogError("[Scene cleanup] Pass -deletedScenes \"Name;Name\".");
				return;
			}
			bool sweepBackups = Array.IndexOf(args, "-sweepBackups") >= 0;
			var problems = new List<string>();
			Plan plan = ForDeletedScenes(args[at + 1].Split(';'), problems);
			foreach (string problem in problems)
			{
				Debug.LogWarning($"[Scene cleanup] {problem}");
			}
			if (plan.IsEmpty)
			{
				Debug.Log("[Scene cleanup] Nothing left behind by those scenes.");
				return;
			}
			Debug.Log($"[Scene cleanup] Scenes: {string.Join(", ", plan.Names)}\n{plan.Describe(!sweepBackups)}");
			List<string> failures = Execute(plan, !sweepBackups);
			Debug.Log(failures.Count == 0
				? "[Scene cleanup] Done; nothing failed."
				: $"[Scene cleanup] {failures.Count} thing(s) could not be removed:\n  " + string.Join("\n  ", failures));
		}

		private static HashSet<string> DestinationsIn(TeleporterCache cache, HashSet<string> sceneNames)
		{
			var ids = new HashSet<string>(StringComparer.Ordinal);
			foreach (KeyValuePair<string, TeleporterCacheEntry> pair in cache.Destinations)
			{
				if (pair.Value != null && sceneNames.Contains(pair.Value.SceneName ?? string.Empty))
				{
					ids.Add(pair.Key);
				}
			}
			return ids;
		}

		/// <summary>
		/// Assets that name the scene in a "...SceneName" field: dungeon, arena and quest templates, waypoint actions and
		/// conditions, whatever else holds one. Read as text, skipping what the deletion removes or edits itself.
		/// </summary>
		private static void FindReferences(Plan plan)
		{
			if (string.IsNullOrEmpty(plan.SceneName))
			{
				return;
			}
			var skip = new HashSet<string>(StringComparer.Ordinal) { NavMeshCataloguePath, TeleporterCache.CACHE_FULL_PATH };
			WorldSceneDetailsCache details = WorldEditorAssets.SceneDetails();
			if (details != null)
			{
				skip.Add(AssetDatabase.GetAssetPath(details));
			}
			var pattern = new Regex($"^\\s*-?\\s*\\w*SceneName: ['\"]?{Regex.Escape(plan.SceneName)}['\"]?\\s*$", RegexOptions.Multiline);
			string needle = plan.SceneName;
			foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets" }))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (skip.Contains(path) || IsInside(plan, path) || !path.EndsWith(".asset", StringComparison.Ordinal))
				{
					continue;
				}
				var info = new FileInfo(path);
				// Terrain and mesh data runs to megabytes and never names a scene.
				if (!info.Exists || info.Length > 1 << 20)
				{
					continue;
				}
				string text = File.ReadAllText(path);
				if (text.IndexOf(needle, StringComparison.Ordinal) >= 0 && pattern.IsMatch(text))
				{
					plan.References.Add(path);
				}
			}
		}

		private static bool IsInside(Plan plan, string path)
		{
			foreach ((string doomed, string _) in plan.Assets)
			{
				if (path == doomed || path.StartsWith(doomed + "/", StringComparison.Ordinal))
				{
					return true;
				}
			}
			return false;
		}

		private static void AddAsset(Plan plan, string path, string what)
		{
			if (!string.IsNullOrEmpty(path) && Exists(path))
			{
				plan.Assets.Add((path, what));
			}
		}

		private static void AddLibrary(Plan plan, string path, string what)
		{
			if (!string.IsNullOrEmpty(path) && Exists(path))
			{
				plan.LibraryFiles.Add((path, what ?? Path.GetFileName(path)));
			}
		}

		private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

		/// <summary>Where the water's shore field for a scene is cached (WaterShoreField): keyed by the scene's path.</summary>
		private static string ShoreFieldCache(string scenePath)
		{
			return string.IsNullOrEmpty(scenePath) ? null : $"Library/FishMMO/ShoreField/{Hash128.Compute(scenePath)}.bytes";
		}

		private static void Close(string scenePath)
		{
			if (string.IsNullOrEmpty(scenePath))
			{
				return;
			}
			UnityEngine.SceneManagement.Scene open = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(scenePath);
			if (!open.IsValid())
			{
				return;
			}
			// The editor cannot close its last scene, so that one is replaced by an empty one.
			if (UnityEngine.SceneManagement.SceneManager.sceneCount <= 1)
			{
				EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
			}
			else
			{
				EditorSceneManager.CloseScene(open, true);
			}
		}
	}
}
#endif
