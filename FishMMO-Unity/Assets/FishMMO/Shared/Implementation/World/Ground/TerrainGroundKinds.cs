using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A terrain tile's ground, as a coarse grid of <see cref="SurfaceKind"/>s: what a server, which keeps no terrain layers or
	/// splat maps (ServerTerrainStripper), reads to know whether a point is grass, forest floor, rock or snow. Baked by the server
	/// build from the tile's own splat maps and layer names (<see cref="SurfaceNames"/>, the footsteps' table) just before it
	/// strips them, onto the tile's object.
	/// </summary>
	/// <remarks>
	/// <para>
	/// 2026-11-21 (Jim: "sample ground texture for wildlife spawns"): the wildlife spawners ask it so a deer appears on grass and
	/// forest floor, never on bare rock (<see cref="WildlifeRules.LivesOn"/>).
	/// </para>
	/// <para>
	/// <b>No re-cut.</b> Nothing is stored in the scene file: the build adds it to its own copy of each scene. In the editor (play
	/// mode) the tile still has its splat maps, and <see cref="TryRead"/> reads those instead.
	/// </para>
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class TerrainGroundKinds : MonoBehaviour
	{
		/// <summary>The finest grid kept: 256² bytes (64 kB) a tile, metres-wide cells on a generated scene's tiles.</summary>
		public const int MaxResolution = 256;

		/// <summary>Cells along each side.</summary>
		[HideInInspector] public int Resolution;

		/// <summary>The dominant kind of each cell, rows along z, <see cref="Resolution"/>² of them.</summary>
		[HideInInspector] public byte[] Kinds = Array.Empty<byte>();

		/// <summary>True when the grid is whole.</summary>
		public bool HasMap => Resolution > 0 && Kinds != null && Kinds.Length == Resolution * Resolution;

		/// <summary>The kind at a normalised point of the tile (0..1 each way), nearest cell.</summary>
		public SurfaceKind At(float u, float v)
		{
			int x = Mathf.Clamp((int)(Mathf.Clamp01(u) * Resolution), 0, Resolution - 1);
			int y = Mathf.Clamp((int)(Mathf.Clamp01(v) * Resolution), 0, Resolution - 1);
			return (SurfaceKind)Kinds[y * Resolution + x];
		}

		/// <summary>The grid a tile of this splat-map resolution gets.</summary>
		public static int ResolutionFor(int alphamapResolution) => Mathf.Clamp(alphamapResolution, 1, MaxResolution);

		/// <summary>Each painted layer's kind, by its name (<see cref="SurfaceNames.OfName"/>); dirt for a layer that says nothing.</summary>
		public static SurfaceKind[] KindsOf(TerrainLayer[] layers)
		{
			if (layers == null)
			{
				return Array.Empty<SurfaceKind>();
			}
			var kinds = new SurfaceKind[layers.Length];
			for (int i = 0; i < layers.Length; i++)
			{
				kinds[i] = SurfaceNames.OfName(layers[i] != null ? layers[i].name : null, SurfaceKind.Dirt);
			}
			return kinds;
		}

		/// <summary>
		/// The grid from a tile's splat maps (<paramref name="maps"/>[y, x, layer], as <c>TerrainData.GetAlphamaps</c> gives them):
		/// each cell the kind with the most weight over the splat texels it covers. Pure, so it is tested on its own.
		/// </summary>
		public static byte[] Bake(float[,,] maps, SurfaceKind[] layerKinds, int resolution)
		{
			if (maps == null || layerKinds == null || resolution <= 0)
			{
				return Array.Empty<byte>();
			}
			int rows = maps.GetLength(0), columns = maps.GetLength(1), layers = Mathf.Min(maps.GetLength(2), layerKinds.Length);
			var result = new byte[resolution * resolution];
			var weight = new float[Enum.GetValues(typeof(SurfaceKind)).Length];
			for (int cy = 0; cy < resolution; cy++)
			{
				int y0 = cy * rows / resolution, y1 = Mathf.Max(y0 + 1, (cy + 1) * rows / resolution);
				for (int cx = 0; cx < resolution; cx++)
				{
					int x0 = cx * columns / resolution, x1 = Mathf.Max(x0 + 1, (cx + 1) * columns / resolution);
					Array.Clear(weight, 0, weight.Length);
					for (int y = y0; y < y1 && y < rows; y++)
					{
						for (int x = x0; x < x1 && x < columns; x++)
						{
							for (int l = 0; l < layers; l++)
							{
								weight[(int)layerKinds[l]] += maps[y, x, l];
							}
						}
					}
					int best = 0;
					for (int k = 1; k < weight.Length; k++)
					{
						if (weight[k] > weight[best])
						{
							best = k;
						}
					}
					result[cy * resolution + cx] = (byte)best;
				}
			}
			return result;
		}

		/// <summary>Bakes <paramref name="data"/>'s splat maps into a grid; false when it has none to read.</summary>
		public static bool TryBake(TerrainData data, out int resolution, out byte[] kinds)
		{
			resolution = 0;
			kinds = Array.Empty<byte>();
			if (data == null || data.alphamapLayers <= 0 || data.terrainLayers == null || data.terrainLayers.Length == 0)
			{
				return false;
			}
			int alpha = data.alphamapResolution;
			resolution = ResolutionFor(alpha);
			kinds = Bake(data.GetAlphamaps(0, 0, alpha, alpha), KindsOf(data.terrainLayers), resolution);
			return kinds.Length > 0;
		}

		/// <summary>
		/// The ground kind of a tile at a world point: from its own splat maps where it has them (the editor), else from the baked
		/// grid on its object (a server build). False when it has neither.
		/// </summary>
		public static bool TryRead(Terrain terrain, Vector3 world, out SurfaceKind kind)
		{
			kind = SurfaceKind.Dirt;
			TerrainData data = terrain != null ? terrain.terrainData : null;
			if (data == null)
			{
				return false;
			}
			Vector3 origin = terrain.GetPosition();
			float u = (world.x - origin.x) / Mathf.Max(1f, data.size.x);
			float v = (world.z - origin.z) / Mathf.Max(1f, data.size.z);
			if (terrain.TryGetComponent(out TerrainGroundKinds baked) && baked.HasMap)
			{
				kind = baked.At(u, v);
				return true;
			}
			TerrainLayer[] painted = data.terrainLayers;
			int layers = data.alphamapLayers;
			if (layers <= 0 || painted == null || painted.Length == 0)
			{
				return false;
			}
			int res = data.alphamapResolution;
			int x = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(u) * (res - 1)), 0, res - 1);
			int y = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(v) * (res - 1)), 0, res - 1);
			float[,,] maps = data.GetAlphamaps(x, y, 1, 1);
			float best = -1f;
			for (int l = 0; l < layers && l < painted.Length; l++)
			{
				if (maps[0, 0, l] > best)
				{
					best = maps[0, 0, l];
					kind = SurfaceNames.OfName(painted[l] != null ? painted[l].name : null, SurfaceKind.Dirt);
				}
			}
			return best >= 0f;
		}
	}
}
