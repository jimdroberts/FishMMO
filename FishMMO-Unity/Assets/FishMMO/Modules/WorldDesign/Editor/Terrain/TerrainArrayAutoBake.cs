#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Keeps every open scene's terrain arrays current with no manual step: on opening a scene, on
	/// saving one, after a script reload and whenever the project changes (a LOCAL texture added, a
	/// layer's art replaced), each open scene with a binder is checked and rebaked if stale.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The check is cheap — resolve the layers, hash what they point at, compare with the hash in the
	/// baked set — so it runs freely; the bake itself only runs when the hash differs, which is the
	/// cache that keeps an ordinary scene open from costing anything.
	/// </para>
	/// <para>
	/// <b>It also follows hand edits.</b> A layer added, removed or reordered with Unity's terrain tools
	/// changes what the arrays must hold; the binder's provenance is brought in line first
	/// (<see cref="TerrainArraySetup.SyncProvenance"/>), which dirties the scene like any other edit.
	/// </para>
	/// <para>
	/// <b>It never runs</b> in batch mode (test runs open world scenes by the dozen, and a build bakes
	/// explicitly), in play mode, while compiling or importing, while any build or the world map bake
	/// is running, or while another bake is. Those are deferred rather than dropped where they are
	/// transient.
	/// </para>
	/// </remarks>
	[InitializeOnLoad]
	public static class TerrainArrayAutoBake
	{
		private static int suspended;
		private static bool scheduled;

		/// <summary>True while a client build runs; set by the build tool around its bake and build.</summary>
		public static bool BuildRunning { get; set; }

		static TerrainArrayAutoBake()
		{
			EditorSceneManager.sceneOpened += (scene, mode) => Schedule();
			EditorSceneManager.sceneSaved += scene => Schedule();
			EditorApplication.projectChanged += Schedule;
			// The binder's BindAll bakes through this, for callers in assemblies that cannot see this one.
			TerrainArrayBinder.EditorBakeIfStale = BakeIfStale;
			Schedule();
		}

		/// <summary>Stops the automatic checks until <see cref="Resume"/>; nests.</summary>
		public static void Suspend() => suspended++;

		/// <summary>Undoes one <see cref="Suspend"/>.</summary>
		public static void Resume()
		{
			suspended = Mathf.Max(0, suspended - 1);
			if (suspended == 0)
			{
				Schedule();
			}
		}

		/// <summary>Bakes every stale binder in one scene, synchronously, without touching its provenance. Returns how many were baked.</summary>
		public static int BakeIfStale(Scene scene)
		{
			if (TerrainArrayBaker.IsBusy || EditorApplication.isPlayingOrWillChangePlaymode)
			{
				return 0;
			}
			int baked = 0;
			foreach (TerrainArrayBinder binder in TerrainArrayBaker.BindersIn(scene))
			{
				if (TerrainArrayBaker.IsStale(binder, out _) && TerrainArrayBaker.Bake(binder).Baked)
				{
					baked++;
				}
			}
			return baked;
		}

		private static void Schedule()
		{
			if (scheduled)
			{
				return;
			}
			scheduled = true;
			EditorApplication.delayCall += Run;
		}

		private static void Run()
		{
			scheduled = false;
			if (Application.isBatchMode || suspended > 0 || BuildRunning || BuildPipeline.isBuildingPlayer
				|| EditorApplication.isPlayingOrWillChangePlaymode || FishMMO.Shared.WorldMaps.WorldMapBaker.IsBusy)
			{
				return;
			}
			if (EditorApplication.isCompiling || EditorApplication.isUpdating || TerrainArrayBaker.IsBusy)
			{
				// Transient: look again once it has settled.
				Schedule();
				return;
			}

			for (int i = 0; i < SceneManager.sceneCount; i++)
			{
				Scene scene = SceneManager.GetSceneAt(i);
				if (!scene.isLoaded)
				{
					continue;
				}
				foreach (TerrainArrayBinder binder in TerrainArrayBaker.BindersIn(scene))
				{
					TerrainArraySetup.SyncProvenance(binder);
					if (TerrainArrayBaker.IsStale(binder, out _))
					{
						TerrainArrayBaker.Bake(binder);
					}
				}
			}
		}
	}
}
#endif
