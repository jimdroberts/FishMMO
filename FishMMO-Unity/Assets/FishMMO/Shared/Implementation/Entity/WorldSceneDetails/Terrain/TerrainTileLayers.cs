using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Which of the scene's terrain layers a tile's alphamap channels hold, when the tile carries only the layers it paints
	/// (<see cref="TerrainTileLayerMap"/>): channel <c>c</c> is scene layer <see cref="SceneLayers"/>[c], the slice of the
	/// scene's texture arrays and the id its index map names.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>On the tile's own object</b>, beside its <see cref="Terrain"/>, so it follows the tile whatever terrain data the tile is
	/// given (a client or server build swaps in a copy). Written by the cut (TerrainTileLayerSync.Compact), refreshed whenever
	/// the scene's layers are brought in line (TerrainArraySetup.SyncProvenance) and by a client build. A tile without one, or
	/// whose map no longer fits its alphamaps, reads channel as scene layer: every scene cut before 2026-10-27.
	/// </para>
	/// <para>
	/// <b>In the editor the binder's provenance decides</b> (<see cref="EditorResolve"/>): it names every layer the arrays hold, so
	/// a layer the terrain tools appended to a tile by hand is read right before anything is saved. A player has no provenance
	/// (it is EditorOnly) and reads the map stored here — and only on a tile whose alphamaps a client build kept; every other
	/// tile draws its stored index map, which already names scene layers.
	/// </para>
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class TerrainTileLayers : MonoBehaviour
	{
		[Tooltip("The scene layer (texture-array slice) each alphamap channel of this tile holds, in channel order. Written by the cut; empty reads channel as scene layer.")]
		public int[] SceneLayers = new int[0];

#if UNITY_EDITOR
		/// <summary>
		/// Set by the editor (TerrainTileLayerSync): a tile's map from its binder's provenance, or null where there is none to ask
		/// (then the stored map is read). Null outside the editor.
		/// </summary>
		public static System.Func<Terrain, int[]> EditorResolve;
#endif

		/// <summary>
		/// A tile's scene layer per alphamap channel, or null when channel and scene layer are the same number (a tile carrying the
		/// whole palette, or one without alphamaps).
		/// </summary>
		public static int[] Of(Terrain terrain)
		{
			TerrainData data = terrain != null ? terrain.terrainData : null;
			int layers = data != null ? data.alphamapLayers : 0;
			if (layers <= 0)
			{
				return null;
			}
#if UNITY_EDITOR
			if (EditorResolve != null)
			{
				int[] resolved = EditorResolve(terrain);
				if (resolved != null && resolved.Length == layers)
				{
					return TerrainTileLayerMap.IsIdentity(resolved) ? null : resolved;
				}
			}
#endif
			TerrainTileLayers own = terrain.GetComponent<TerrainTileLayers>();
			int[] stored = own != null ? own.SceneLayers : null;
			if (stored == null || stored.Length != layers || TerrainTileLayerMap.IsIdentity(stored))
			{
				return null;
			}
			return stored;
		}
	}
}
