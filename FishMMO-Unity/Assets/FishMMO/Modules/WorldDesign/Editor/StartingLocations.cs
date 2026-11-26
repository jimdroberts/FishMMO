#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// Where new characters begin: every capital's respawn point is also a starting location
	/// (<see cref="CharacterInitialSpawnPosition"/>) open to every playable race.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The cut places it (<see cref="RespawnFeature"/> on a capital), so a re-cut keeps it. Starting locations used to be
	/// added by hand to the generated respawn point, and a re-cut, which rebuilds generated objects, dropped them without a
	/// word: character creation then offered no starting location at all.
	/// </para>
	/// <para>
	/// The races are read from <see cref="RaceTemplate.Playable"/> each time, so a race made playable later is offered at the
	/// next placement. For a scene cut before this existed, or after the playable races change, run
	/// <see cref="PlaceInAllWorldScenes"/> (dashboard: Maintenance → World): it marks every world scene's capital, saves the
	/// scenes it changed and rebuilds the world scene details cache, which is what carries the starting locations to the
	/// login server and the character creation screen.
	/// </para>
	/// </remarks>
	public static class StartingLocations
	{
		/// <summary>
		/// The races a new character may be: every <see cref="RaceTemplate"/> with <see cref="RaceTemplate.Playable"/> on,
		/// ordered by name so a placement writes the same list every time.
		/// </summary>
		public static List<RaceTemplate> PlayableRaces()
		{
			var races = new List<RaceTemplate>();
			foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(RaceTemplate)))
			{
				var race = AssetDatabase.LoadAssetAtPath<RaceTemplate>(AssetDatabase.GUIDToAssetPath(guid));
				if (race != null && race.Playable)
				{
					races.Add(race);
				}
			}
			races.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
			return races;
		}

		/// <summary>
		/// Makes <paramref name="point"/> a starting location for <paramref name="races"/> (the playable races when null).
		/// True when anything changed.
		/// </summary>
		public static bool Mark(GameObject point, List<RaceTemplate> races = null)
		{
			races ??= PlayableRaces();
			if (!point.TryGetComponent(out CharacterInitialSpawnPosition start))
			{
				start = point.AddComponent<CharacterInitialSpawnPosition>();
				start.AllowedRaces = new List<RaceTemplate>(races);
				return true;
			}
			if (start.AllowedRaces != null && start.AllowedRaces.SequenceEqual(races))
			{
				return false;
			}
			start.AllowedRaces = new List<RaceTemplate>(races);
			EditorUtility.SetDirty(start);
			return true;
		}

		/// <summary>
		/// Marks the respawn point of every capital in <paramref name="scene"/>. The number of capitals now marked; changed
		/// is true when the scene was modified. A capital without a respawn point is noted and skipped.
		/// </summary>
		public static int PlaceInScene(Scene scene, List<string> notes, out bool changed)
		{
			changed = false;
			ScenePointOfInterestSettings settings = FindComponent<ScenePointOfInterestSettings>(scene);
			if (settings == null || settings.Points == null || settings.Points.Points == null)
			{
				notes.Add($"{scene.name}: no points of interest, so no capital.");
				return 0;
			}

			// Respawn points by name: the cut names a site's "<site> Respawn [<site id>]".
			var respawns = new Dictionary<string, CharacterRespawnPosition>(StringComparer.Ordinal);
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (CharacterRespawnPosition respawn in root.GetComponentsInChildren<CharacterRespawnPosition>(true))
				{
					respawns[respawn.name] = respawn;
				}
			}

			List<RaceTemplate> races = PlayableRaces();
			int marked = 0;
			foreach (PointOfInterestRecord record in settings.Points.Points)
			{
				if (record.Kind != POIType.Capital)
				{
					continue;
				}
				string suffix = $" Respawn [{(uint)record.Id:x8}]";
				CharacterRespawnPosition point = null;
				foreach (KeyValuePair<string, CharacterRespawnPosition> pair in respawns)
				{
					if (pair.Key.EndsWith(suffix, StringComparison.Ordinal))
					{
						point = pair.Value;
						break;
					}
				}
				if (point == null)
				{
					notes.Add($"{scene.name}: the capital '{record.Name}' has no respawn point (its template has no respawn feature); not a starting location.");
					continue;
				}
				if (Mark(point.gameObject, races))
				{
					changed = true;
					notes.Add($"{scene.name}: '{point.name}' is now a starting location for {string.Join(", ", races.Select(r => r.name))}.");
				}
				else
				{
					notes.Add($"{scene.name}: '{point.name}' was already a starting location.");
				}
				marked++;
			}
			if (marked == 0)
			{
				notes.Add($"{scene.name}: no capital with a respawn point; no starting location here.");
			}
			if (changed)
			{
				EditorSceneManager.MarkSceneDirty(scene);
			}
			return marked;
		}

		/// <summary>Marks the capitals of the scenes open in the editor; save them and rebuild the cache after.</summary>
		[DashboardTool(DashboardToolAttribute.Maintenance, "Place starting locations (open scenes)", Section = "World", Order = 22,
			Tooltip = "Makes each open world scene's capital respawn point a starting location for every playable race. Save the scene and rebuild the world scene details cache after.")]
		public static void PlaceInOpenScenes()
		{
			var notes = new List<string>();
			for (int i = 0; i < SceneManager.sceneCount; ++i)
			{
				Scene scene = SceneManager.GetSceneAt(i);
				if (scene.isLoaded && FindComponent<WorldSceneSettings>(scene) != null)
				{
					PlaceInScene(scene, notes, out _);
				}
			}
			Debug.Log("[Starting locations]\n  " + (notes.Count > 0 ? string.Join("\n  ", notes) : "No world scene is open."));
		}

		/// <summary>Marks every world scene's capital, saves the scenes it changed, and rebuilds the world scene details cache.</summary>
		[DashboardTool(DashboardToolAttribute.Maintenance, "Place starting locations (all world scenes)", Section = "World", Order = 23,
			Tooltip = "Opens every world scene, makes its capital's respawn point a starting location for every playable race, saves the scenes it changed and rebuilds the world scene details cache.",
			Confirm = "Open every world scene, mark its capital as a starting location, and rebuild the world scene details cache?")]
		public static void PlaceInAllWorldScenesFromDashboard()
		{
			if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
			{
				return;
			}
			SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
			try
			{
				PlaceInAllWorldScenes(out _);
			}
			finally
			{
				if (setup.Length > 0)
				{
					EditorSceneManager.RestoreSceneManagerSetup(setup);
				}
			}
		}

		/// <summary><c>-executeMethod FishMMO.Shared.StartingLocations.PlaceInAllWorldScenesBatch</c>: exits 0 when it worked.</summary>
		public static void PlaceInAllWorldScenesBatch()
		{
			bool ok = false;
			try
			{
				ok = PlaceInAllWorldScenes(out _);
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
			}
			EditorApplication.Exit(ok ? 0 : 1);
		}

		/// <summary>
		/// Marks every world scene's capital (client backdrops skipped), saves the scenes it changed, and rebuilds the world
		/// scene details cache. True when every scene saved, the cache rebuilt, and at least one starting location exists.
		/// </summary>
		public static bool PlaceInAllWorldScenes(out int total)
		{
			total = 0;
			bool ok = true;
			var notes = new List<string>();
			string folder = Constants.Configuration.WorldScenePath.Replace('\\', '/');
			foreach (string path in DirectoryExtensions.GetAllFiles(folder, ".unity").OrderBy(p => p, StringComparer.Ordinal))
			{
				Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
				WorldSceneSettings settings = FindComponent<WorldSceneSettings>(scene);
				if (settings == null || settings.ClientBackdrop)
				{
					continue;
				}
				total += PlaceInScene(scene, notes, out bool changed);
				if (changed && !EditorSceneManager.SaveScene(scene))
				{
					notes.Add($"FAILED: {scene.name} did not save.");
					ok = false;
				}
			}
			bool cache = WorldSceneDetailsCacheBuilder.Rebuild();
			notes.Add(cache ? "World scene details cache rebuilt." : "FAILED: the world scene details cache rebuild failed.");
			if (total == 0)
			{
				notes.Add("FAILED: no world scene has a capital with a respawn point, so character creation offers no starting location.");
			}
			ok &= cache && total > 0;
			Debug.Log($"[Starting locations] {total} starting location(s), ok={ok}\n  " + string.Join("\n  ", notes));
			return ok;
		}

		private static T FindComponent<T>(Scene scene) where T : Component
		{
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				T found = root.GetComponentInChildren<T>(true);
				if (found != null)
				{
					return found;
				}
			}
			return null;
		}
	}
}
#endif
