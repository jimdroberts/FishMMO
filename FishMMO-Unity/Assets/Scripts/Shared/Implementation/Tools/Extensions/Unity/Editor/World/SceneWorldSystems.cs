#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Reads a world scene's world-systems setup, and puts right what can be put right.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The reading half runs while the world scene details rebuild already has each scene open
	/// (<see cref="WorldSceneScan"/>), so nothing is loaded twice. The fixing half runs afterwards,
	/// on its own, reopening only the scenes that are actually being changed — the rebuild closes
	/// every scene it opens with its changes discarded, so an edit made during the scan would
	/// vanish without a word.
	/// </para>
	/// <para>
	/// <b>Fixing a scene rewrites its <c>.unity</c> file</b>, and Unity rewrites far more of it than
	/// the change itself: field renames, serialization upgrades and component repairs all land in
	/// the same save. That is why scene fixes are never applied without being asked for, never in
	/// batch mode, and never during a build — and why <see cref="Fix"/> reports exactly which scenes
	/// it saved, so the diff can be read afterwards.
	/// </para>
	/// </remarks>
	public static class SceneWorldSystems
	{
		/// <summary>The GameObject the audit puts the scene's world components on.</summary>
		public const string HostObjectName = "World Systems";

		// ── Reading ───────────────────────────────────────────────────

		/// <summary>
		/// What an open scene says about its world systems.
		/// </summary>
		/// <remarks>
		/// Every lookup is scoped to this one scene. The rebuild has the scene it is scanning open
		/// additively alongside whatever else was loaded, so a project-wide
		/// <c>FindFirstObjectByType</c> would happily answer with a component belonging to another
		/// scene and report a scene as configured because its neighbour was.
		/// </remarks>
		public static SceneWorldFacts Read(Scene scene)
		{
			var facts = new SceneWorldFacts { SceneName = scene.name };
			if (!scene.IsValid() || !scene.isLoaded)
			{
				return facts;
			}

			WorldSceneSettings settings = FindIn<WorldSceneSettings>(scene);
			WorldDayNightCycle cycle = FindIn<WorldDayNightCycle>(scene);

			facts.HasSettings = settings != null;
			facts.HasDayNightCycle = cycle != null;
			facts.DayNightEnabled = cycle != null && cycle.DayNightCycle;
			/* Resolved through the component, so it answers with the same precedence the runtime
			 * uses: the atlas entry's, then the scene's own, then the body's. */
			facts.HasClimate = settings != null && settings.Climate != null;

			SceneBiomeMap map = settings != null ? settings.BiomeMap : null;
			facts.HasBiomeMap = map != null;

			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (Light light in root.GetComponentsInChildren<Light>(true))
				{
					if (light != null && light.type == LightType.Directional && light.enabled && light.gameObject.activeInHierarchy)
					{
						facts.DirectionalLights++;
					}
				}
				facts.HasTerrain |= root.GetComponentInChildren<Terrain>(true) != null;
			}

			facts.DirectorAreaSquareKm = DirectorArea(scene, map);

			/* Measured as one landmass. A scene may carry a single Unity terrain or a grid of them
			 * stitched into a continent, and the vertical range that matters is the whole slope
			 * from the lowest tile's floor to the highest tile's ceiling — not whichever tile the
			 * scan happened to reach first. */
			SceneTerrainExtent extent = SceneTerrainExtent.Of(scene);
			facts.TerrainTiles = extent.TileCount;
			facts.LandmassHeightMetres = extent.HeightSpanMetres;
			return facts;
		}

		/// <summary>
		/// The area the weather director would measure for a scene, in square kilometres.
		/// </summary>
		/// <remarks>
		/// The same order <c>WeatherHost.TryGetArea</c> uses: the biome map's authored world size
		/// if it has one, else the union of the scene's terrains. Not the scene's boundaries —
		/// the director never looks at those, so measuring them here would report an area the
		/// running game does not agree with.
		/// </remarks>
		private static float DirectorArea(Scene scene, SceneBiomeMap map)
		{
			if (map != null && map.WorldSize.x > 0f && map.WorldSize.y > 0f)
			{
				return map.WorldSize.x * map.WorldSize.y / 1_000_000f;
			}
			return SceneTerrainExtent.Of(scene).SquareKm;
		}

		/// <summary>The first component of a type belonging to one scene, inactive ones included.</summary>
		private static T FindIn<T>(Scene scene) where T : Component
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

		// ── Fixing ────────────────────────────────────────────────────

		/// <summary>
		/// Applies every fixable problem for one scene.
		/// </summary>
		/// <param name="sceneName">The scene, by name.</param>
		/// <param name="problems">Its problems; the ones that cannot be fixed are skipped.</param>
		/// <param name="savedScene">True when the scene's <c>.unity</c> file was rewritten.</param>
		/// <returns>How many problems were put right.</returns>
		/// <remarks>
		/// The scene is opened here rather than being handed in, because the findings were gathered
		/// from a scene that has since been closed: every component reference from that pass is a
		/// destroyed object by now. Only the findings' names survive, which is exactly why they are
		/// names rather than delegates.
		/// </remarks>
		public static int Fix(string sceneName, IReadOnlyList<WorldSystemProblem> problems, out bool savedScene)
		{
			savedScene = false;
			int fixedCount = 0;
			if (problems == null || problems.Count == 0)
			{
				return 0;
			}

			bool needsScene = false;
			foreach (WorldSystemProblem problem in problems)
			{
				if (problem.CanFix && problem.WritesScene)
				{
					needsScene = true;
					break;
				}
			}

			// Asset-only fixes first: they need no scene, and the atlas entry a scene fix may want
			// to read has to exist before the scene is opened.
			foreach (WorldSystemProblem problem in problems)
			{
				if (!problem.CanFix || problem.WritesScene)
				{
					continue;
				}
				if (FixAsset(problem, sceneName))
				{
					fixedCount++;
				}
			}

			if (!needsScene)
			{
				return fixedCount;
			}

			string path = ScenePath(sceneName);
			if (string.IsNullOrEmpty(path))
			{
				Debug.LogError($"[World systems] '{sceneName}' could not be found on disk; its scene fixes were skipped.");
				return fixedCount;
			}

			/* A scene the person already has open is not ours to close. OpenScene hands back the
			 * scene that is already loaded rather than a second copy, so the CloseScene below would
			 * shut the one they were working in — and if it had unsaved edits, the save here would
			 * commit those too, under the name of a world-systems fix. So an open scene with
			 * unsaved work is left alone and said so, and an open clean one is fixed and left open. */
			Scene alreadyOpen = SceneManager.GetSceneByPath(path);
			bool wasOpen = alreadyOpen.IsValid() && alreadyOpen.isLoaded;
			if (wasOpen && alreadyOpen.isDirty)
			{
				Debug.LogWarning(
					$"[World systems] '{sceneName}' is open with unsaved changes, so it was left alone. " +
					"Save or discard them and run this again.");
				return fixedCount;
			}

			Scene scene = wasOpen ? alreadyOpen : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
			try
			{
				bool changed = false;
				foreach (WorldSystemProblem problem in problems)
				{
					if (!problem.CanFix || !problem.WritesScene)
					{
						continue;
					}
					if (FixScene(problem, scene))
					{
						fixedCount++;
						changed = true;
					}
				}

				if (changed)
				{
					EditorSceneManager.MarkSceneDirty(scene);
					savedScene = EditorSceneManager.SaveScene(scene);
					if (!savedScene)
					{
						Debug.LogError($"[World systems] '{sceneName}' could not be saved; its scene fixes were lost.");
					}
				}
			}
			finally
			{
				if (!wasOpen)
				{
					EditorSceneManager.CloseScene(scene, true);
				}
			}

			return fixedCount;
		}

		/// <summary>A fix that changes assets only, never a scene.</summary>
		private static bool FixAsset(WorldSystemProblem problem, string sceneName)
		{
			switch (problem.Id)
			{
				case WorldSystemCheck.MissingAtlasEntry:
				{
					WorldAtlas atlas = WorldEditorAssets.FindFirst<WorldAtlas>();
					WorldEditorAssets.SyncAtlasScenes(atlas);
					WorldAtlasScene.EditorLookup.Invalidate();
					return WorldAtlasScene.Find(sceneName) != null;
				}

				case WorldSystemCheck.AtlasSizeStale:
				{
					WorldAtlasScene entry = WorldAtlasScene.Find(sceneName);
					Vector2? size = WorldEditorAssets.SceneSizeKm(WorldEditorAssets.SceneDetails(), sceneName);
					if (entry == null || !size.HasValue)
					{
						return false;
					}
					Undo.RecordObject(entry, "Refresh scene size");
					entry.SizeKm = size.Value;
					EditorUtility.SetDirty(entry);
					AssetDatabase.SaveAssets();
					return true;
				}

				case WorldSystemCheck.UnusedClimate:
				{
					/* On the body, not in the scene: a base climate is what every scene on a world
					 * shares, and it is the level the runtime falls back to last, so one assignment
					 * answers for all of them and a scene that wants to differ still overrides it.
					 * It also means this fix touches no scene file. */
					WorldAtlasScene entry = WorldAtlasScene.Find(sceneName);
					WorldBody body = entry != null && entry.Body != null ? entry.Body : HomeWorld();
					ClimateSettings climate = OnlyAuthoredClimate();
					if (body == null || climate == null || body.BaseClimate != null)
					{
						return false;
					}
					Undo.RecordObject(body, "Base climate");
					body.BaseClimate = climate;
					EditorUtility.SetDirty(body);
					AssetDatabase.SaveAssets();
					Debug.Log($"[World systems] {body.name} now uses the authored climate \"{climate.name}\".");
					return true;
				}

				default:
					return false;
			}
		}

		/// <summary>The solar system's home world, or null.</summary>
		public static WorldBody HomeWorld()
		{
			SolarSystemProfile system = WorldEditorAssets.FindFirst<SolarSystemProfile>();
			return system != null ? system.HomeWorld : null;
		}

		/// <summary>
		/// The project's one authored climate, or null when there is none — or more than one.
		/// </summary>
		/// <remarks>
		/// Silent above one on purpose. With several, which a body should use is a choice somebody
		/// has to make, and picking the alphabetically first would look like an answer.
		/// </remarks>
		public static ClimateSettings OnlyAuthoredClimate()
		{
			List<ClimateSettings> all = WorldEditorAssets.FindAll<ClimateSettings>();
			return all.Count == 1 ? all[0] : null;
		}

		/// <summary>A fix that changes the open scene. The caller saves it.</summary>
		private static bool FixScene(WorldSystemProblem problem, Scene scene)
		{
			switch (problem.Id)
			{
				case WorldSystemCheck.MissingSettings:
				{
					// Re-checked rather than trusted: the finding was made before this scene was
					// reopened, and reporting a change that did not happen would dirty the scene
					// and rewrite its file for nothing.
					if (FindIn<WorldSceneSettings>(scene) != null)
					{
						return false;
					}
					/* Nothing to set from the placement. Climate, biome map and client cap are all
					 * overrides here — empty means "whatever my atlas entry and my body say" —
					 * which is what a scene that has just been placed wants. */
					return Host(scene).AddComponent<WorldSceneSettings>() != null;
				}

				case WorldSystemCheck.MissingDayNight:
				{
					if (FindIn<WorldDayNightCycle>(scene) != null)
					{
						return false;
					}
					WorldDayNightCycle cycle = Host(scene).AddComponent<WorldDayNightCycle>();
					/* Nothing else to set from the placement. The cycle reads its latitude,
					 * longitude, heading, body and sky from the scene's atlas entry every time it
					 * is asked, so a scene that later moves on the globe follows without anyone
					 * reopening it. The sky override stays empty on purpose: empty means "the sky
					 * of the world I am standing on". */
					cycle.DayNightCycle = true;
					return true;
				}

				case WorldSystemCheck.DayNightDisabled:
				{
					WorldDayNightCycle cycle = FindIn<WorldDayNightCycle>(scene);
					if (cycle == null || cycle.DayNightCycle)
					{
						return false;
					}
					cycle.DayNightCycle = true;
					return true;
				}

				case WorldSystemCheck.SceneDirectionalLight:
				{
					/* Switched off, never deleted. A directional light in a world scene is almost
					 * always a leftover from before the sky owned the sun — but "almost always" is
					 * not "always", and a deleted light cannot be argued with afterwards. */
					int disabled = 0;
					foreach (GameObject root in scene.GetRootGameObjects())
					{
						foreach (Light light in root.GetComponentsInChildren<Light>(true))
						{
							// The same test the audit counted with, so the fix never touches a light
							// the finding did not report.
							if (light == null || light.type != LightType.Directional
								|| !light.enabled || !light.gameObject.activeInHierarchy)
							{
								continue;
							}
							light.enabled = false;
							disabled++;
							Debug.Log($"[World systems] Switched off the directional light '{light.name}' in {scene.name}; the sky system drives its own.");
						}
					}
					return disabled > 0;
				}

				default:
					return false;
			}
		}

		/// <summary>
		/// Puts the world-system components into a scene that has none, at their defaults.
		/// </summary>
		/// <returns>The GameObject they were put on.</returns>
		/// <remarks>
		/// <para>
		/// The same components the audit adds to a scene somebody forgot to finish, so a generated
		/// scene and a repaired one end up identical. A second definition of "configured" living in
		/// the generator would drift from this one the first time either changed, and the symptom
		/// would be a scene that passes the audit while behaving differently.
		/// </para>
		/// <para>
		/// Nothing is set from the placement beyond the day/night switch: latitude, longitude,
		/// heading, body, sky and climate are all read live from the scene's atlas entry, so a
		/// scene that is later moved on the globe follows without being reopened.
		/// </para>
		/// </remarks>
		public static GameObject Configure(Scene scene, bool wantsSky)
		{
			GameObject host = Host(scene);
			if (FindIn<WorldSceneSettings>(scene) == null)
			{
				host.AddComponent<WorldSceneSettings>();
			}
			if (wantsSky && FindIn<WorldDayNightCycle>(scene) == null)
			{
				host.AddComponent<WorldDayNightCycle>().DayNightCycle = true;
			}
			return host;
		}

		/// <summary>The scene's world-systems GameObject, made at the origin if it has none.</summary>
		private static GameObject Host(Scene scene)
		{
			/* Preferring whatever already carries one of the components keeps a scene that was set
			 * up by hand from ending up with its settings on one object and its day/night cycle on
			 * another — which works, but reads as though one of them was forgotten. */
			WorldSceneSettings settings = FindIn<WorldSceneSettings>(scene);
			if (settings != null)
			{
				return settings.gameObject;
			}
			WorldDayNightCycle cycle = FindIn<WorldDayNightCycle>(scene);
			if (cycle != null)
			{
				return cycle.gameObject;
			}
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				if (root.name == HostObjectName)
				{
					return root;
				}
			}
			var host = new GameObject(HostObjectName);
			SceneManager.MoveGameObjectToScene(host, scene);
			return host;
		}

		/// <summary>A world scene's asset path, by name.</summary>
		public static string ScenePath(string sceneName)
		{
			foreach (string path in WorldEditorAssets.WorldScenePaths())
			{
				if (System.IO.Path.GetFileNameWithoutExtension(path) == sceneName)
				{
					return path;
				}
			}
			return null;
		}
	}
}
#endif
