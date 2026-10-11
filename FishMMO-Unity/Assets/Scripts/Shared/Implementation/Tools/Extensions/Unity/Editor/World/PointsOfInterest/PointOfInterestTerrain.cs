#if UNITY_EDITOR
using System;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Editing a generated scene's terrain tiles in scene metres: the pads the planner asks for, and whatever a shaper
	/// cuts or raises. Every edit reads and writes only the samples it changes.
	/// </summary>
	public static class PointOfInterestTerrain
	{
		/// <summary>
		/// Rewrites every heightmap sample within <paramref name="area"/> (scene metres, x/z) to
		/// <paramref name="edit"/>(east, north, metres) and returns how many changed. Heights are clamped to each tile's
		/// span. Samples under a terrain hole are left alone when <paramref name="skipHoles"/> is set.
		/// </summary>
		public static int EditHeights(Terrain[,] terrains, TerrainTilePlan plan, Rect area, Func<float, float, float, float> edit, bool skipHoles = true)
		{
			int changed = 0;
			float halfW = plan.WidthMetres * 0.5f, halfD = plan.DepthMetres * 0.5f;
			for (int tz = 0; tz < plan.CountZ; tz++)
			{
				for (int tx = 0; tx < plan.CountX; tx++)
				{
					Terrain terrain = terrains[tx, tz];
					if (terrain == null || terrain.terrainData == null)
					{
						continue;
					}
					TerrainData data = terrain.terrainData;
					Vector3 origin = terrain.transform.position;
					Vector3 size = data.size;
					var tile = new Rect(origin.x, origin.z, size.x, size.z);
					if (!tile.Overlaps(area))
					{
						continue;
					}
					int res = data.heightmapResolution;
					float step = size.x / (res - 1);
					int x0 = Mathf.Clamp(Mathf.FloorToInt((area.xMin - origin.x) / step), 0, res - 1);
					int x1 = Mathf.Clamp(Mathf.CeilToInt((area.xMax - origin.x) / step), 0, res - 1);
					int z0 = Mathf.Clamp(Mathf.FloorToInt((area.yMin - origin.z) / step), 0, res - 1);
					int z1 = Mathf.Clamp(Mathf.CeilToInt((area.yMax - origin.z) / step), 0, res - 1);
					int w = x1 - x0 + 1, d = z1 - z0 + 1;
					if (w <= 0 || d <= 0)
					{
						continue;
					}
					float[,] heights = data.GetHeights(x0, z0, w, d);
					bool[,] solid = null;
					int holesRes = data.holesResolution;
					if (skipHoles)
					{
						solid = data.GetHoles(0, 0, holesRes, holesRes);
					}
					bool any = false;
					for (int j = 0; j < d; j++)
					{
						for (int i = 0; i < w; i++)
						{
							int hx = x0 + i, hz = z0 + j;
							if (solid != null)
							{
								int ux = Mathf.Min(holesRes - 1, hx), uz = Mathf.Min(holesRes - 1, hz);
								if (!solid[uz, ux])
								{
									continue;
								}
							}
							float east = origin.x + hx * step, north = origin.z + hz * step;
							if (!area.Contains(new Vector2(east, north)))
							{
								continue;
							}
							float metres = origin.y + heights[j, i] * size.y;
							float next = edit(east, north, metres);
							if (Mathf.Abs(next - metres) < 1e-4f)
							{
								continue;
							}
							heights[j, i] = Mathf.Clamp01((next - origin.y) / Mathf.Max(1e-3f, size.y));
							any = true;
							changed++;
						}
					}
					if (any)
					{
						data.SetHeights(x0, z0, heights);
					}
				}
			}
			return changed;
		}

		/// <summary>
		/// Flattens a pad: the ground inside its radius eased to its height by <paramref name="flatness"/>, easing back to
		/// the ground over its blend. Holes are kept. Returns the samples changed.
		/// </summary>
		public static int FlattenPad(Terrain[,] terrains, TerrainTilePlan plan, PointOfInterestPad pad, float flatness = 1f)
		{
			float reach = pad.Radius + Mathf.Max(Mathf.Max(0.01f, pad.Blend), MaxEmbankmentMetres);
			var area = new Rect(pad.Centre.x - reach, pad.Centre.z - reach, reach * 2f, reach * 2f);
			float height = pad.Height;
			flatness = Mathf.Clamp01(flatness);
			return EditHeights(terrains, plan, area, (east, north, metres) =>
			{
				float dx = east - pad.Centre.x, dz = north - pad.Centre.z;
				float target = PadHeight(Mathf.Sqrt(dx * dx + dz * dz), metres, pad.Radius, pad.Blend, height);
				return Mathf.Lerp(metres, target, flatness);
			});
		}

		/// <summary>The steepest cut or fill slope a pad leaves at its edge (tan 31°): steep enough to stay close, shallow enough to read as ground.</summary>
		public const float EmbankmentSlope = 0.6f;

		/// <summary>
		/// How far past its radius a pad may reshape the ground, metres. The last quarter fades back to the natural ground, so
		/// the edit never ends in a step even where the bank has not met a steep hillside by then.
		/// </summary>
		public const float MaxEmbankmentMetres = 160f;

		/// <summary>
		/// The ground at a distance from a pad's centre: the pad's height inside its radius; past it the natural ground held to
		/// within a cut-and-fill cone that rises or falls at <see cref="EmbankmentSlope"/> from the pad's edge, with the first
		/// <paramref name="blend"/> metres rounded so the edge is not a kink.
		/// </summary>
		/// <remarks>
		/// A pad used to ease into the ground over a fixed 8–15 m, so a big pad on a slope stood in a circular cliff or sat in a
		/// circular trench as tall as the slope's fall across it (the rings around towns on the Flo Monolith probe map,
		/// 2026-10-10). The cone meets the ground wherever the ground is within its slope, so a pad on gentle ground changes
		/// nothing past its rim and one on a steep hillside gets a bank as wide as it needs.
		/// </remarks>
		public static float PadHeight(float distance, float ground, float radius, float blend, float height)
		{
			if (distance <= radius)
			{
				return height;
			}
			/* The rim is a parabolic fillet: the bank's slope ramps from level at the rim to EmbankmentSlope over the blend,
			 * then holds, so the edge is rounded and nowhere steeper than the bank. */
			float out_ = distance - radius;
			float b = Mathf.Max(0.01f, blend);
			float run = out_ < b ? out_ * out_ / (2f * b) : out_ - b * 0.5f;
			float slack = run * EmbankmentSlope;
			float banked = Mathf.Clamp(ground, height - slack, height + slack);
			// And hand back to the natural ground over the reach's last quarter: the edit stops there, so it must end on it.
			float fadeStart = radius + MaxEmbankmentMetres * 0.75f;
			if (distance > fadeStart)
			{
				float t = Mathf.Clamp01((distance - fadeStart) / (MaxEmbankmentMetres * 0.25f));
				banked = Mathf.Lerp(banked, ground, t * t * (3f - 2f * t));
			}
			return banked;
		}

		/// <summary>A pad's pull at a distance from its centre: 1 inside its radius, easing (smoothstep) to 0 over its blend.</summary>
		public static float PadWeight(float distance, float radius, float blend)
		{
			if (distance <= radius)
			{
				return 1f;
			}
			if (blend <= 0f || distance >= radius + blend)
			{
				return 0f;
			}
			float t = 1f - (distance - radius) / blend;
			return t * t * (3f - 2f * t);
		}

		/// <summary>
		/// Cuts holes where <paramref name="hole"/>(east, north) is true within <paramref name="area"/> (a carved cave's
		/// mouth). Returns the hole texels cut.
		/// </summary>
		public static int CutHoles(Terrain[,] terrains, TerrainTilePlan plan, Rect area, Func<float, float, bool> hole)
		{
			int cut = 0;
			for (int tz = 0; tz < plan.CountZ; tz++)
			{
				for (int tx = 0; tx < plan.CountX; tx++)
				{
					Terrain terrain = terrains[tx, tz];
					if (terrain == null || terrain.terrainData == null)
					{
						continue;
					}
					TerrainData data = terrain.terrainData;
					Vector3 origin = terrain.transform.position;
					var tile = new Rect(origin.x, origin.z, data.size.x, data.size.z);
					if (!tile.Overlaps(area))
					{
						continue;
					}
					int res = data.holesResolution;
					float step = data.size.x / res;
					bool[,] solid = data.GetHoles(0, 0, res, res);
					bool any = false;
					for (int z = 0; z < res; z++)
					{
						float north = origin.z + (z + 0.5f) * step;
						if (north < area.yMin || north > area.yMax)
						{
							continue;
						}
						for (int x = 0; x < res; x++)
						{
							float east = origin.x + (x + 0.5f) * step;
							if (east < area.xMin || east > area.xMax || !solid[z, x] || !hole(east, north))
							{
								continue;
							}
							solid[z, x] = false;
							any = true;
							cut++;
						}
					}
					if (any)
					{
						data.SetHoles(0, 0, solid);
					}
				}
			}
			return cut;
		}

		/// <summary>True where the tiles have a hole at (east, north).</summary>
		public static Func<float, float, bool> HoleQuery(Terrain[,] terrains, TerrainTilePlan plan)
		{
			int countX = plan.CountX, countZ = plan.CountZ;
			var maps = new bool[countX, countZ][,];
			var any = false;
			for (int tz = 0; tz < countZ; tz++)
			{
				for (int tx = 0; tx < countX; tx++)
				{
					TerrainData data = terrains[tx, tz] != null ? terrains[tx, tz].terrainData : null;
					if (data == null)
					{
						continue;
					}
					int res = data.holesResolution;
					bool[,] solid = data.GetHoles(0, 0, res, res);
					bool holed = false;
					foreach (bool s in solid)
					{
						if (!s)
						{
							holed = true;
							break;
						}
					}
					if (holed)
					{
						maps[tx, tz] = solid;
						any = true;
					}
				}
			}
			if (!any)
			{
				return null;
			}
			return (east, north) =>
			{
				int tx = Mathf.Clamp(Mathf.FloorToInt((east + plan.WidthMetres * 0.5f) / plan.TileMetres), 0, countX - 1);
				int tz = Mathf.Clamp(Mathf.FloorToInt((north + plan.DepthMetres * 0.5f) / plan.TileMetres), 0, countZ - 1);
				bool[,] solid = maps[tx, tz];
				if (solid == null)
				{
					return false;
				}
				int res = solid.GetLength(0);
				float u = (east + plan.WidthMetres * 0.5f - tx * plan.TileMetres) / plan.TileMetres;
				float v = (north + plan.DepthMetres * 0.5f - tz * plan.TileMetres) / plan.TileMetres;
				int x = Mathf.Clamp((int)(u * res), 0, res - 1), z = Mathf.Clamp((int)(v * res), 0, res - 1);
				return !solid[z, x];
			};
		}
	}
}
#endif
