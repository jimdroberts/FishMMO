using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Holds a scene's <see cref="TerrainDetailBake"/> on a client-only object at its root (<see cref="ObjectName"/>), and
	/// lists every loaded scene's while enabled, so the client's grass and detail scatter can find a terrain's baked maps
	/// (<see cref="TileOf"/>) and a texture set's colour copy (<see cref="AlbedoOf"/>).
	/// </summary>
	/// <remarks>
	/// Under a <see cref="ClientOnlyObject"/>, so server builds drop it and the bake with it. The editor never enables it
	/// (it is not an edit-mode script): the bake is only looked up in play.
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class SceneTerrainBake : MonoBehaviour
	{
		/// <summary>The name of the object carrying this, at the scene's root.</summary>
		public const string ObjectName = "Terrain Detail Bake";

		[Tooltip("The scene's baked grass and detail scatter maps (written by the cut).")]
		public TerrainDetailBake Bake;

		private static readonly List<SceneTerrainBake> loaded = new List<SceneTerrainBake>();

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics() => loaded.Clear();

		private void OnEnable()
		{
			if (!loaded.Contains(this))
			{
				loaded.Add(this);
			}
		}

		private void OnDisable() => loaded.Remove(this);

		/// <summary>
		/// The usable baked entry for a terrain's data in any loaded scene, and that scene's bake; null when there is none,
		/// it was baked by another <see cref="TerrainDetailBake.Version"/>, or it was marked stale.
		/// </summary>
		public static TerrainDetailBake.Tile TileOf(TerrainData data, out TerrainDetailBake bake)
		{
			bake = null;
			if (data == null)
			{
				return null;
			}
			for (int i = 0; i < loaded.Count; i++)
			{
				TerrainDetailBake b = loaded[i] != null ? loaded[i].Bake : null;
				if (b == null || b.BakedVersion != TerrainDetailBake.Version)
				{
					continue;
				}
				TerrainDetailBake.Tile tile = b.TileOf(data);
				if (tile != null && !tile.Stale)
				{
					bake = b;
					return tile;
				}
			}
			return null;
		}

		/// <summary>
		/// The baked ground colour map made over exactly these terrains (the same data, standing where they stood), from a
		/// loaded scene of this <see cref="TerrainDetailBake.Version"/>, not stale; null when there is none.
		/// </summary>
		public static TerrainDetailBake.GroundColour GroundOf(IReadOnlyList<Terrain> terrains)
		{
			for (int i = 0; i < loaded.Count; i++)
			{
				TerrainDetailBake b = loaded[i] != null ? loaded[i].Bake : null;
				TerrainDetailBake.GroundColour g = b != null && b.BakedVersion == TerrainDetailBake.Version ? b.Ground : null;
				if (g == null || !g.Baked || g.Stale || g.Terrains == null || g.Positions == null || g.Terrains.Length != g.Positions.Length)
				{
					continue;
				}
				int matched = 0, live = 0;
				foreach (Terrain t in terrains)
				{
					if (t == null || t.terrainData == null)
					{
						continue;
					}
					live++;
					int at = System.Array.IndexOf(g.Terrains, t.terrainData);
					if (at < 0 || (g.Positions[at] - t.GetPosition()).sqrMagnitude > 1e-4f)
					{
						matched = -1;
						break;
					}
					matched++;
				}
				if (matched == g.Terrains.Length && live == matched)
				{
					return g;
				}
			}
			return null;
		}

		/// <summary>A loaded scene's baked colour copy of a texture set's albedo array, or null.</summary>
		public static TerrainDetailBake.Albedo AlbedoOf(Texture2DArray source)
		{
			if (source == null)
			{
				return null;
			}
			for (int i = 0; i < loaded.Count; i++)
			{
				TerrainDetailBake b = loaded[i] != null ? loaded[i].Bake : null;
				if (b == null || b.BakedVersion != TerrainDetailBake.Version)
				{
					continue;
				}
				foreach (TerrainDetailBake.Albedo albedo in b.Albedos)
				{
					if (albedo != null && albedo.Source == source)
					{
						return albedo;
					}
				}
			}
			return null;
		}
	}
}
