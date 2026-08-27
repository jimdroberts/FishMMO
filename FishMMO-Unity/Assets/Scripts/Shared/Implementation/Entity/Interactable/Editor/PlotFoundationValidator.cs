using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// Checks the plots authored in the open scenes for the mistakes that only edit time can catch.
	/// </summary>
	/// <remarks>
	/// Plots are placed by designers and never at runtime, which is what removes the need for
	/// overlap tests during play — but it also means the only opportunity to notice a bad layout is
	/// here. Both faults this looks for are silent at runtime: a duplicate key resolves two
	/// foundations onto one database row, and overlapping plots let two owners build into the same
	/// space.
	///
	/// <para>Runs on demand from the menu, and again on save, because a check nobody remembers to
	/// run is a check that finds problems after they ship.</para>
	/// </remarks>
	public static class PlotFoundationValidator
	{
		private const string MenuPath = "FishMMO/Housing/Validate Plots In Open Scenes";

		/// <summary>
		/// Validates every open scene and reports the result to the console.
		/// </summary>
		[MenuItem(MenuPath)]
		public static void ValidateOpenScenes()
		{
			int problems = Validate(logClean: true);
			if (problems < 1)
			{
				return;
			}

			EditorUtility.DisplayDialog(
				"Plot validation",
				$"Found {problems} problem(s) with the plots in the open scenes.\n\nSee the console for details.",
				"OK");
		}

		/// <summary>
		/// Re-runs validation whenever a scene is saved.
		/// </summary>
		/// <remarks>
		/// Saving is the moment a layout becomes something other people will load, so it is the
		/// last point at which a duplicate key is still cheap to fix. Silent when everything is
		/// fine — a validator that talks on every save trains people to ignore it.
		/// </remarks>
		[InitializeOnLoadMethod]
		private static void SubscribeToSceneSave()
		{
			UnityEditor.SceneManagement.EditorSceneManager.sceneSaved -= OnSceneSaved;
			UnityEditor.SceneManagement.EditorSceneManager.sceneSaved += OnSceneSaved;
		}

		private static void OnSceneSaved(Scene scene)
		{
			Validate(logClean: false);
		}

		/// <summary>
		/// Reports duplicate keys and overlapping footprints across the open scenes.
		/// </summary>
		/// <param name="logClean">Whether to say so when nothing is wrong.</param>
		/// <returns>The number of problems found.</returns>
		private static int Validate(bool logClean)
		{
			int problems = 0;

			for (int i = 0; i < SceneManager.sceneCount; ++i)
			{
				Scene scene = SceneManager.GetSceneAt(i);
				if (!scene.isLoaded)
				{
					continue;
				}

				List<PlotFoundation> foundations = CollectFoundations(scene);
				if (foundations.Count < 1)
				{
					continue;
				}

				problems += ReportUnusableKeys(scene, foundations);
				problems += ReportDuplicateKeys(scene, foundations);
				problems += ReportOverlaps(scene, foundations);
			}

			if (problems < 1 && logClean)
			{
				Debug.Log("[PlotFoundationValidator] Plots in the open scenes are valid.");
			}

			return problems;
		}

		/// <summary>
		/// Finds every plot foundation in a scene, including inactive ones.
		/// </summary>
		/// <remarks>
		/// Inactive objects are included deliberately. A foundation disabled in the editor is still
		/// authored land that somebody will re-enable, and a duplicate key hiding on a disabled
		/// object is exactly the kind that survives review.
		/// </remarks>
		private static List<PlotFoundation> CollectFoundations(Scene scene)
		{
			List<PlotFoundation> found = new List<PlotFoundation>();

			foreach (GameObject root in scene.GetRootGameObjects())
			{
				found.AddRange(root.GetComponentsInChildren<PlotFoundation>(includeInactive: true));
			}

			return found;
		}

		/// <summary>
		/// Reports foundations whose key would leave them unclaimable.
		/// </summary>
		private static int ReportUnusableKeys(Scene scene, List<PlotFoundation> foundations)
		{
			int problems = 0;

			foreach (PlotFoundation foundation in foundations)
			{
				if (string.IsNullOrEmpty(foundation.PlotKey))
				{
					Debug.LogError(
						$"[PlotFoundationValidator] '{foundation.gameObject.name}' in '{scene.name}' has no usable plot key, " +
						$"so it can never be claimed. Keys must be non-empty and at most {PlotIdentity.MaxPlotKeyLength} characters.",
						foundation.gameObject);
					++problems;
				}
			}

			return problems;
		}

		/// <summary>
		/// Reports keys used by more than one foundation in the same scene.
		/// </summary>
		/// <remarks>
		/// The failure this prevents is quiet. Registration inserts one row per distinct key and
		/// ignores conflicts, so two foundations sharing a key both resolve to the <em>same</em>
		/// plot — buying one silently buys the other, and the two can never be owned separately.
		/// Keys are compared canonicalised, so casing differences are caught too.
		/// </remarks>
		private static int ReportDuplicateKeys(Scene scene, List<PlotFoundation> foundations)
		{
			Dictionary<string, PlotFoundation> byKey = new Dictionary<string, PlotFoundation>();
			int problems = 0;

			foreach (PlotFoundation foundation in foundations)
			{
				string key = foundation.PlotKey;
				if (string.IsNullOrEmpty(key))
				{
					continue;
				}

				if (byKey.TryGetValue(key, out PlotFoundation existing))
				{
					Debug.LogError(
						$"[PlotFoundationValidator] '{foundation.gameObject.name}' and '{existing.gameObject.name}' in '{scene.name}' both use the plot key " +
						$"'{key}'. They would share one plot: buying either would buy both.",
						foundation.gameObject);
					++problems;
					continue;
				}

				byKey.Add(key, foundation);
			}

			return problems;
		}

		/// <summary>
		/// Reports plots whose footprints intersect.
		/// </summary>
		/// <remarks>
		/// Overlapping plots put two owners in one space, and nothing at runtime will notice: the
		/// point of authoring plots at edit time is that placement is checked once, here, instead of
		/// on every building action forever.
		///
		/// <para>All three axes are tested, so plots stacked vertically — a terrace above a
		/// shopfront — do not register as overlapping. Only plots that genuinely share space do.</para>
		/// </remarks>
		private static int ReportOverlaps(Scene scene, List<PlotFoundation> foundations)
		{
			int problems = 0;

			for (int i = 0; i < foundations.Count; ++i)
			{
				for (int j = i + 1; j < foundations.Count; ++j)
				{
					Bounds a = foundations[i].Bounds;
					Bounds b = foundations[j].Bounds;

					if (!Overlaps(a, b))
					{
						continue;
					}

					Debug.LogError(
						$"[PlotFoundationValidator] Plots '{foundations[i].PlotKey}' and '{foundations[j].PlotKey}' in '{scene.name}' overlap. " +
						"Two owners would be able to build in the same space.",
						foundations[i].gameObject);
					++problems;
				}
			}

			return problems;
		}

		/// <summary>
		/// True when two plot volumes intersect in all three axes.
		/// </summary>
		/// <remarks>
		/// Edges that merely touch are not an overlap. Plots laid out flush against one another
		/// along a street is the intended arrangement, and reporting it would make the validator
		/// useless in exactly the layout it is meant to encourage.
		/// </remarks>
		private static bool Overlaps(Bounds a, Bounds b)
		{
			return a.min.x < b.max.x && b.min.x < a.max.x &&
				   a.min.y < b.max.y && b.min.y < a.max.y &&
				   a.min.z < b.max.z && b.min.z < a.max.z;
		}
	}
}
