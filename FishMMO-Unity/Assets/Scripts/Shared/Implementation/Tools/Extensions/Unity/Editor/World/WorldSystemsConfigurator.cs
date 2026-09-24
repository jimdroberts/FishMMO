#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>What the world scene details rebuild does about a scene that is not set up.</summary>
	public enum WorldSystemsMode
	{
		/// <summary>
		/// Do not check at all: rebuild the cache and nothing else.
		/// </summary>
		/// <remarks>
		/// What every caller but the dashboard gets, and deliberately. An audit finding is not a
		/// fault in the rebuild, but it is logged at its own severity — so a scene that lost its
		/// settings component would turn every test that rebuilds the cache red, and blame the
		/// rebuild for it. The check is a thing you ask for.
		/// </remarks>
		Off = 0,

		/// <summary>Look and say so in the console. Nothing is opened, nothing is written.</summary>
		Report = 1,
		/// <summary>
		/// Put missing components in, and ask about everything else scene by scene.
		/// </summary>
		/// <remarks>
		/// A scene that is missing its World Scene Settings or its World Day Night Cycle is not
		/// missing an opinion, it is missing a part — there is nothing to ask, so those go in. What
		/// still asks is anything that would overrule a choice somebody made: a cycle switched off,
		/// a directional light they placed.
		/// </remarks>
		Prompt = 2,
		/// <summary>Configure everything fixable without asking.</summary>
		FixAll = 3,
	}

	/// <summary>
	/// Rides along with the world scene details rebuild, checks every world scene against the
	/// weather, cloud, sky and climate systems, and offers to configure the ones that are not.
	/// </summary>
	/// <remarks>
	/// <para>
	/// It hangs off the rebuild because the rebuild is already the one operation that visits every
	/// world scene, and because a scene's placement — which is most of what the audit reasons from
	/// — is only known once the cache has measured it.
	/// </para>
	/// <para>
	/// <b>The default is <see cref="WorldSystemsMode.Report"/>, and the build path never leaves
	/// it.</b> A client build rebuilds this cache twice, once with the baked world maps present and
	/// once after removing them; a prompt there would stall a headless build, and a scene saved
	/// mid-build would change the very scenes being built. Only a person pressing the dashboard
	/// button gets asked.
	/// </para>
	/// </remarks>
	public static class WorldSystemsConfigurator
	{
		/// <summary>Facts gathered from the scan, by scene name.</summary>
		private static readonly Dictionary<string, SceneWorldFacts> gathered = new Dictionary<string, SceneWorldFacts>();
		private static bool collecting;

		/// <summary>Starts listening to the rebuild's scene scan.</summary>
		public static void BeginCollecting()
		{
			if (collecting)
			{
				return;
			}
			gathered.Clear();
			WorldSceneScan.Scanned += OnSceneScanned;
			collecting = true;
		}

		/// <summary>Stops listening. Safe to call when it never started.</summary>
		public static void EndCollecting()
		{
			if (!collecting)
			{
				return;
			}
			WorldSceneScan.Scanned -= OnSceneScanned;
			collecting = false;
		}

		private static void OnSceneScanned(Scene scene)
		{
			SceneWorldFacts facts = SceneWorldSystems.Read(scene);
			gathered[facts.SceneName] = facts;
		}

		/// <summary>
		/// The verdict on every scene the last scan saw, worst first.
		/// </summary>
		/// <remarks>
		/// Resolved after the scan rather than during it because the atlas entry, the body and the
		/// scene's measured size all come from assets, and the size in particular comes from the
		/// cache the rebuild has only just finished writing.
		/// </remarks>
		public static List<WorldSystemProblem> Resolve()
		{
			var problems = new List<WorldSystemProblem>();
			WorldSceneDetailsCache cache = WorldEditorAssets.SceneDetails();
			HashSet<string> dungeons = WorldEditorAssets.DungeonSceneNames();
			SolarSystemProfile system = WorldEditorAssets.FindFirst<SolarSystemProfile>();
			WorldBody home = system != null ? system.HomeWorld : null;
			// Looked up once for the whole pass: it is a project-wide question, not a per-scene one.
			FishMMO.Shared.Biomes.ClimateSettings authoredClimate = SceneWorldSystems.OnlyAuthoredClimate();

			// The renderer passes and anything else that is true of the project rather than of one
			// scene, contributed by editor assemblies this one cannot reference.
			problems.AddRange(WorldSystemsAudit.ProjectProblems());

			WorldAtlasScene.EditorLookup.Invalidate();
			foreach (KeyValuePair<string, SceneWorldFacts> pair in gathered)
			{
				WorldAtlasScene entry = WorldAtlasScene.Find(pair.Key);
				// Null Body on an entry means the home world, exactly as WorldSceneSettings reads it.
				WorldBody body = entry != null && entry.Body != null ? entry.Body : home;
				problems.AddRange(WorldSystemsAudit.Problems(
					pair.Value, entry, body, dungeons.Contains(pair.Key),
					WorldEditorAssets.SceneSizeKm(cache, pair.Key), authoredClimate));
			}

			problems.Sort((a, b) =>
			{
				int bySeverity = a.Severity.CompareTo(b.Severity);
				return bySeverity != 0 ? bySeverity : string.CompareOrdinal(a.SceneName, b.SceneName);
			});
			return problems;
		}

		/// <summary>
		/// Reports the findings, and — in <see cref="WorldSystemsMode.Prompt"/> or
		/// <see cref="WorldSystemsMode.FixAll"/> — configures the scenes that want it.
		/// </summary>
		/// <returns>True when a scene was saved, so the caller knows to rebuild the cache again.</returns>
		public static bool Apply(List<WorldSystemProblem> problems, WorldSystemsMode mode)
		{
			if (mode == WorldSystemsMode.Off)
			{
				return false;
			}

			if (problems == null || problems.Count == 0)
			{
				Debug.Log("[World systems] Every world scene is set up for weather, clouds and the sky.");
				return false;
			}

			Report(problems);

			/* A prompt needs somebody to answer it, and a scene save during a build would change
			 * the scenes being built. Both come back to Report rather than failing, so the audit
			 * still says what it found. */
			if (mode != WorldSystemsMode.Report && Application.isBatchMode)
			{
				Debug.Log("[World systems] Batch mode: reporting only. Run FishMMO Dashboard → World → World Scene Details to configure these.");
				return false;
			}

			if (mode == WorldSystemsMode.Report)
			{
				return false;
			}

			bool changed = false;
			var savedScenes = new List<string>();

			// The project's own findings first: a scene that is set up perfectly still shows no
			// clouds if the renderer has no cloud pass, so fixing that before the scenes means the
			// next run has one fewer reason to complain.
			foreach (WorldSystemProblem problem in problems)
			{
				if (!problem.IsProjectWide || !problem.CanFix || problem.ProjectFix == null)
				{
					continue;
				}
				if (mode == WorldSystemsMode.Prompt && !EditorUtility.DisplayDialog(
					"Configure world systems",
					$"The project {problem.Message}\n\n  • {problem.Remedy}\n\nWould you like to add them now?",
					"Add them", "Skip"))
				{
					continue;
				}
				/* Not counted as a change: "changed" means the scene details cache is now out of
				 * date, and the cache is read from scenes. A renderer asset is not one. */
				Debug.Log(problem.ProjectFix()
					? $"[World systems] {problem.Remedy}"
					: $"[World systems] Could not finish: {problem.Remedy}");
			}

			foreach (KeyValuePair<string, List<WorldSystemProblem>> scene in FixableByScene(problems))
			{
				List<WorldSystemProblem> automatic = OnlyAutomatic(scene.Value, true);
				List<WorldSystemProblem> optional = OnlyAutomatic(scene.Value, false);
				var apply = new List<WorldSystemProblem>(automatic);

				/* Said before the scene is written, not after, and only when there is something to
				 * say: the components are going in at their defaults because there is nowhere on a
				 * globe to take them from. Somebody who sees this and meant the scene to be
				 * somewhere can stop, place it, and run this again — the fix is idempotent. */
				if (automatic.Count > 0 && mode == WorldSystemsMode.Prompt && Unattached(automatic))
				{
					EditorUtility.DisplayDialog(
						"World systems",
						$"\"{scene.Key}\" is not attached to a celestial body.\n\n" +
						"It will be set up with default sky and time settings — latitude 0, longitude 0, and the client's fallback sky.\n\n" +
						"Place it on a body in World → World Atlas and run this again to wire it to its real position.",
						"OK");
				}

				if (optional.Count > 0 && mode == WorldSystemsMode.Prompt)
				{
					int answer = Ask(scene.Key, optional);
					if (answer == 2)
					{
						Debug.Log("[World systems] Stopped at your request; nothing further was changed.");
						break;
					}
					if (answer == 0)
					{
						apply.AddRange(optional);
					}
				}
				else if (mode != WorldSystemsMode.Prompt)
				{
					apply.AddRange(optional);
				}

				if (apply.Count == 0)
				{
					continue;
				}

				int fixes = SceneWorldSystems.Fix(scene.Key, apply, out bool savedScene);
				if (fixes > 0)
				{
					Debug.Log($"[World systems] Configured {scene.Key}: {fixes} change(s).");
				}
				if (savedScene)
				{
					// Only a saved scene can have changed what the cache should say: the cache is
					// built by reading scenes, and the atlas and climate fixes touch neither.
					changed = true;
					savedScenes.Add(scene.Key);
				}
			}

			if (savedScenes.Count > 0)
			{
				/* Said plainly because a Unity scene save rewrites much more than the change: field
				 * renames, serialization upgrades and component repairs all land in the same diff.
				 * Anyone about to commit this needs to know which files to read. */
				Debug.LogWarning(
					$"[World systems] {savedScenes.Count} scene file(s) were rewritten: {string.Join(", ", savedScenes)}. " +
					"A Unity save rewrites more of a scene than the change itself, so read the diff before committing.");
				AssetDatabase.SaveAssets();
			}
			return changed;
		}

		/// <summary>The fixable problems, grouped by scene, in the order the scenes were scanned.</summary>
		private static List<KeyValuePair<string, List<WorldSystemProblem>>> FixableByScene(List<WorldSystemProblem> problems)
		{
			var order = new List<string>();
			var byScene = new Dictionary<string, List<WorldSystemProblem>>();
			foreach (WorldSystemProblem problem in problems)
			{
				if (!problem.CanFix || problem.IsProjectWide)
				{
					continue;
				}
				if (!byScene.TryGetValue(problem.SceneName, out List<WorldSystemProblem> list))
				{
					list = new List<WorldSystemProblem>();
					byScene.Add(problem.SceneName, list);
					order.Add(problem.SceneName);
				}
				list.Add(problem);
			}

			var result = new List<KeyValuePair<string, List<WorldSystemProblem>>>(order.Count);
			foreach (string sceneName in order)
			{
				result.Add(new KeyValuePair<string, List<WorldSystemProblem>>(sceneName, byScene[sceneName]));
			}
			return result;
		}

		/// <summary>The automatic half of a scene's fixes, or the half that still asks.</summary>
		private static List<WorldSystemProblem> OnlyAutomatic(List<WorldSystemProblem> problems, bool automatic)
		{
			var result = new List<WorldSystemProblem>();
			foreach (WorldSystemProblem problem in problems)
			{
				if (problem.Automatic == automatic)
				{
					result.Add(problem);
				}
			}
			return result;
		}

		/// <summary>Whether any of these findings says the scene has no place on a body.</summary>
		private static bool Unattached(List<WorldSystemProblem> problems)
		{
			foreach (WorldSystemProblem problem in problems)
			{
				if (problem.Unattached)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Asks about one scene. 0 configure it, 1 skip it, 2 stop asking.</summary>
		private static int Ask(string sceneName, List<WorldSystemProblem> fixable)
		{
			var message = new StringBuilder();
			message.Append('"').Append(sceneName).Append("\" does not have the weather, cloud and sky systems set up:\n\n");
			foreach (WorldSystemProblem problem in fixable)
			{
				message.Append("  • ").Append(problem.Remedy).Append('\n');
			}

			bool writesScene = false;
			foreach (WorldSystemProblem problem in fixable)
			{
				writesScene |= problem.WritesScene;
			}
			if (writesScene)
			{
				message.Append("\nThis saves the scene file. Unity rewrites more of a scene than the change itself, so read the diff before committing.");
			}
			message.Append("\n\nWould you like to add them now?");

			return EditorUtility.DisplayDialogComplex(
				"Configure world systems", message.ToString(), "Add them", "Skip this scene", "Stop asking");
		}

		/// <summary>Writes the findings to the console, one line each, at their own severity.</summary>
		private static void Report(List<WorldSystemProblem> problems)
		{
			int errors = 0;
			int warnings = 0;
			foreach (WorldSystemProblem problem in problems)
			{
				string line = problem.IsProjectWide
					? $"[World systems] The project {problem.Message} — {problem.Remedy}"
					: $"[World systems] {problem.SceneName} {problem.Message} — {problem.Remedy}";
				switch (problem.Severity)
				{
					case WorldSystemSeverity.Error:
						errors++;
						Debug.LogError(line);
						break;
					case WorldSystemSeverity.Warning:
						warnings++;
						Debug.LogWarning(line);
						break;
					default:
						Debug.Log(line);
						break;
				}
			}
			/* The measured ground, printed whether or not anything is wrong with it. The derived
			 * lapse rate is kelvin per METRE, so a scene's vertical range decides how much it cools
			 * from its lowest ground to its highest — and a stitched landmass is one slope, so the
			 * tile count is worth seeing beside it. */
			foreach (KeyValuePair<string, SceneWorldFacts> pair in gathered)
			{
				SceneWorldFacts facts = pair.Value;
				Debug.Log(facts.TerrainTiles > 0
					? $"[World systems] {pair.Key}: {facts.TerrainTiles} terrain tile(s), {facts.LandmassHeightMetres:0} m of vertical range, {facts.DirectorAreaSquareKm:0.##} km²."
					: $"[World systems] {pair.Key}: no terrain.");
			}

			Debug.Log($"[World systems] {problems.Count} finding(s) across {gathered.Count} scene(s): {errors} error(s), {warnings} warning(s).");
		}
	}
}
#endif
