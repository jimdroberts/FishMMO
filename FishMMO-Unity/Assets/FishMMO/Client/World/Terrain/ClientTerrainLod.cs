using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Client
{
	/// <summary>
	/// The level of detail the client draws Unity's own terrain heightmaps at: every terrain tile a play session sees is drawn
	/// at <see cref="HeightmapPixelError"/> (or its own coarser error), whatever the cut stored on it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why.</b> Unity's terrain picks each patch's level of detail per camera and per shadow cascade until the drawn surface
	/// is within <see cref="Terrain.heightmapPixelError"/> pixels of the heightmap; at the cut's 2 pixels (SceneGenerator's
	/// TerrainPixelError) that is many small patches to cull and draw across a scene's tiles (TerrainManager.CullAllTerrains
	/// in the profiler, 0.46 ms over Moron 2's sixteen tiles). Four pixels halves the patches across, and a two-pixel
	/// difference in a silhouette is not seen at the distances where the coarser levels are chosen.
	/// </para>
	/// <para>
	/// <b>Play only, nothing stored.</b> The terrain assets and the scenes keep the cut's value: edit mode (the cut, the world
	/// map's overhead capture, the render probes, which set their own errors) never sees this. Each tile is set once, when its
	/// scene has loaded, so anything that sets a tile's error later in play (a probe, a settings screen) keeps its own; what it
	/// had is put back when play ends in the editor. Collision is untouched: a terrain collider always uses the full heightmap.
	/// </para>
	/// </remarks>
	public static class ClientTerrainLod
	{
		/// <summary>
		/// The screen-space error (pixels) the client draws every terrain heightmap at, at the finest: Unity's terrain where it draws
		/// the ground, and the level field of the client's own heightmap draw (TerrainHeightmapSystem) where that does.
		/// </summary>
		public const float HeightmapPixelError = 4f;

		private static readonly List<Terrain> active = new List<Terrain>();
		private static readonly List<Terrain> taken = new List<Terrain>();
		private static readonly List<float> authored = new List<float>();
		private static bool hooked;

		/// <summary>The error a tile authored at <paramref name="authoredError"/> is drawn at: never finer than <see cref="HeightmapPixelError"/>.</summary>
		public static float DrawnPixelError(float authoredError)
		{
			return Mathf.Max(authoredError, HeightmapPixelError);
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			// With domain reload off, the last play session's state would still be here.
			Restore();
			Unhook();
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Hook()
		{
			if (hooked || !Application.isPlaying)
			{
				return;
			}
			hooked = true;
			SceneManager.sceneLoaded += OnSceneLoaded;
#if UNITY_EDITOR
			UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
			Apply();
		}

		private static void Unhook()
		{
			if (!hooked)
			{
				return;
			}
			hooked = false;
			SceneManager.sceneLoaded -= OnSceneLoaded;
#if UNITY_EDITOR
			UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
		}

#if UNITY_EDITOR
		private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange change)
		{
			if (change == UnityEditor.PlayModeStateChange.ExitingPlayMode)
			{
				Restore();
				Unhook();
			}
		}
#endif

		private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			Apply();
		}

		/// <summary>Sets every active tile not yet seen to its drawn error, and forgets tiles that are gone.</summary>
		private static void Apply()
		{
			for (int i = taken.Count - 1; i >= 0; i--)
			{
				if (taken[i] == null)
				{
					taken.RemoveAt(i);
					authored.RemoveAt(i);
				}
			}
			Terrain.GetActiveTerrains(active);
			for (int i = 0; i < active.Count; i++)
			{
				Terrain terrain = active[i];
				if (terrain == null || taken.Contains(terrain))
				{
					continue;
				}
				float own = terrain.heightmapPixelError;
				taken.Add(terrain);
				authored.Add(own);
				terrain.heightmapPixelError = DrawnPixelError(own);
			}
			active.Clear();
		}

		/// <summary>Puts every tile's own error back.</summary>
		private static void Restore()
		{
			for (int i = 0; i < taken.Count; i++)
			{
				if (taken[i] != null)
				{
					taken[i].heightmapPixelError = authored[i];
				}
			}
			taken.Clear();
			authored.Clear();
		}
	}
}
