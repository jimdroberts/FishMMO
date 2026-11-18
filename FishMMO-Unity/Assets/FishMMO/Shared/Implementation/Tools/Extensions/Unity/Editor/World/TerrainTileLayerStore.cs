using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A terrain tile's own channel → scene layer map (<see cref="TerrainTileLayers"/>), written by the generator's layer sync
	/// and by the client build's layer stripper. Core editor (Phase 4, 2026-11-18).
	/// </summary>
	public static class TerrainTileLayerStore
	{
		/// <summary>
		/// Writes a tile's map on its <see cref="TerrainTileLayers"/> (added where it has none and the map is not the identity).
		/// A null map (channel = scene layer) clears one that is there to the identity of its channels. True when it changed.
		/// </summary>
		public static bool Record(Terrain terrain, int[] sceneLayers)
		{
			if (terrain == null)
			{
				return false;
			}
			TerrainTileLayers own = terrain.GetComponent<TerrainTileLayers>();
			if (own == null && sceneLayers == null)
			{
				return false;
			}
			int[] map = sceneLayers;
			if (map == null)
			{
				int layers = terrain.terrainData != null ? terrain.terrainData.alphamapLayers : 0;
				map = new int[layers];
				for (int c = 0; c < layers; c++)
				{
					map[c] = c;
				}
			}
			if (own == null)
			{
				own = terrain.gameObject.AddComponent<TerrainTileLayers>();
			}
			else if (Same(own.SceneLayers, map))
			{
				return false;
			}
			own.SceneLayers = (int[])map.Clone();
			EditorUtility.SetDirty(own);
			return true;
		}

		private static bool Same(int[] a, int[] b)
		{
			if (a == null || b == null || a.Length != b.Length)
			{
				return false;
			}
			for (int i = 0; i < a.Length; i++)
			{
				if (a[i] != b[i])
				{
					return false;
				}
			}
			return true;
		}
	}
}
