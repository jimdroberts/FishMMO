#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>Headless post-authoring validation of scene terrain arrays, without re-cutting hand-authored scenes.</summary>
	public static class BiomePipelineVerification
	{
		public static void ValidateFromCommandLine()
		{
			int code = 1;
			try
			{
				int scenes = 0, binders = 0, baked = 0;
				foreach (string path in WorldEditorAssets.WorldScenePaths().OrderBy(p => p, StringComparer.Ordinal))
				{
					var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
					int count = 0;
					foreach (TerrainArrayBinder binder in TerrainArrayBaker.BindersIn(scene))
					{
						count++;
						TerrainArraySetup.SyncProvenance(binder);
						bool stale = TerrainArrayBaker.IsStale(binder, out TerrainArrayPlan plan);
						if (plan.Problems.Count != 0)
							throw new InvalidOperationException(path + ": " + string.Join("; ", plan.Problems));
						if (plan.Layers.Count == 0)
							throw new InvalidOperationException(path + ": terrain binder has no layers.");
						if (stale)
						{
							TerrainArrayBakeReport report = TerrainArrayBaker.Bake(binder);
							if ((!report.Baked && !report.UpToDate) || report.Problems.Count != 0)
								throw new InvalidOperationException(path + ": " + report + "; " + string.Join("; ", report.Problems));
							if (report.Baked) baked++;
						}
						if (TerrainArrayBaker.IsStale(binder, out _))
							throw new InvalidOperationException(path + ": terrain arrays remain stale after baking.");
						Debug.Log($"[Biome pipeline verification] {scene.name}: {plan.Layers.Count} terrain layers, arrays current.");
					}
					EditorSceneManager.SaveScene(scene);
					scenes++; binders += count;
					Debug.Log($"[Biome pipeline verification] {scene.name}: {count} terrain binder(s) checked.");
				}
				AssetDatabase.SaveAssets();
				Debug.Log($"[Biome pipeline verification] Complete: {scenes} scenes, {binders} binders, {baked} sets rebaked.");
				code = 0;
			}
			catch (Exception e) { Debug.LogException(e); }
			if (Application.isBatchMode) EditorApplication.Exit(code);
		}
	}
}
#endif
