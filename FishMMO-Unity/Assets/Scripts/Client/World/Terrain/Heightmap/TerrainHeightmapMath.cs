using System;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The arithmetic of the client's own heightmap draw (<see cref="TerrainHeightmapSystem"/>): the patch quadtree's layout, the
	/// level fields, which node is drawn, the geomorph and the errors the fields are built from. The GPU runs the same rules
	/// (FishTerrainHeightmap.hlsl / .compute); this is their reference, which the tests drive over whole synthetic scenes.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Layout.</b> A tile of Q quads a side (heightmap resolution Q + 1, Q = 16·2^k) is a quadtree of patches of
	/// <see cref="PatchQuads"/> quads a side: level 0 takes every texel, level L every 2^L-th, the root (level k) the whole tile.
	/// A tile's nodes are stored finest level first, row by row (<see cref="NodeIndex"/>).
	/// </para>
	/// <para>
	/// <b>The level fields</b>, one per step between levels: f_l(p) = log2(distance to p) − g_l(p) − c (<see cref="LodBias"/>), g_l
	/// the log2 of level l+1's error near p (how far its surface misses the heightmap, <see cref="TexelDeviation"/>) less l + 1, kept
	/// per cell of one level-0 patch, dilated (<see cref="Dilate"/>) and raised (<see cref="Raise"/>) so f_(l−1) ≥ f_l − 1. Then
	/// f_l ≥ l + 1 is exactly the engine's own rule — level l+1's error within the pixel error at p's distance — per point rather
	/// than per patch, so the surface keeps to <see cref="ClientTerrainLod.HeightmapPixelError"/> pixels; and the geomorph slides
	/// level l onto l+1 as f_l goes from l + a to l + 1, so it changes level continuously, never in a step.
	/// </para>
	/// </remarks>
	public static class TerrainHeightmapMath
	{
		/// <summary>Quads along a patch's side at every level (FISH_TERRAIN_PATCH_QUADS).</summary>
		public const int PatchQuads = 16;

		/// <summary>Vertices along a patch's side.</summary>
		public const int PatchVertices = PatchQuads + 1;

		/// <summary>Heightmap texels along a level field cell's side: one level-0 patch.</summary>
		public const int CellQuads = PatchQuads;

		/// <summary>The four lists (an indirect command each) the selection fills (FISH_TERRAIN_LIST_*).</summary>
		public const int ListSolid = 0, ListHoled = 1, ListShadowSolid = 2, ListShadowHoled = 3, Lists = 4;

		/// <summary>Where in a level the geomorph starts sliding onto the next (a, 0..1): from f = L + a to L + 1.</summary>
		public const float MorphStart = 0.5f;

		/// <summary>How fast a level field may fall, levels (log2) a cell: slow enough that no patch's vertices pass one another.</summary>
		public const float FieldSlope = 0.25f;

		/// <summary>The least error a level is taken to have (metres): flat ground still takes a level a distance.</summary>
		public const float FieldFloorMetres = 0.005f;

		/// <summary>Cells a field's peaks are spread over (the slope then takes them down by 32 levels: more than any scene spans).</summary>
		public const int DilationReach = 64;

		/// <summary>Metres added over a node's highest texel for the snow the shader lifts the ground by (deep snow at its deepest).</summary>
		public const float LiftPadMetres = 3.5f;

		/// <summary>Quadtree levels of a tile of <paramref name="quads"/> quads a side; 0 when it is not 16·2^k.</summary>
		public static int LevelCount(int quads)
		{
			if (quads < PatchQuads || quads % PatchQuads != 0)
			{
				return 0;
			}
			int across = quads / PatchQuads;
			if ((across & (across - 1)) != 0)
			{
				return 0;
			}
			int levels = 1;
			while (across > 1)
			{
				across >>= 1;
				levels++;
			}
			return levels;
		}

		/// <summary>Nodes along a tile's side at <paramref name="level"/>.</summary>
		public static int NodesAcross(int quads, int level) => (quads / PatchQuads) >> level;

		/// <summary>Where <paramref name="level"/> starts among a tile's nodes: the finer levels' nodes before it.</summary>
		public static int LevelOffset(int quads, int level)
		{
			int offset = 0;
			for (int l = 0; l < level; l++)
			{
				int n = NodesAcross(quads, l);
				offset += n * n;
			}
			return offset;
		}

		/// <summary>Nodes in a tile, every level.</summary>
		public static int NodesPerTile(int quads) => LevelOffset(quads, LevelCount(quads));

		/// <summary>A node's index among a scene's nodes.</summary>
		public static int NodeIndex(int tile, int quads, int level, int x, int z)
		{
			return tile * NodesPerTile(quads) + LevelOffset(quads, level) + z * NodesAcross(quads, level) + x;
		}

		/// <summary>A node of a tile from its place among the tile's nodes (FishTerrainSelect's search).</summary>
		public static void DecodeNode(int local, int quads, out int level, out int x, out int z)
		{
			int levels = LevelCount(quads);
			level = 0;
			int offset = 0;
			int across = NodesAcross(quads, 0);
			while (level + 1 < levels && local >= offset + across * across)
			{
				offset += across * across;
				across >>= 1;
				level++;
			}
			int inLevel = local - offset;
			x = inLevel % across;
			z = inLevel / across;
		}

		/// <summary>
		/// The levels a camera's pixels shift the fields by (c): log2(K / pixel error), K the pixels a metre at a metre's distance
		/// (half the screen's height over the tangent of half the field of view), or for an orthographic camera the pixels a metre
		/// (half the height over its size), which then measures no distance. f_l ≥ l + 1 is then "level l+1's error, seen from
		/// here, is within the pixel error".
		/// </summary>
		public static float LodBias(float pixelError, float pixelHeight, float fieldOfViewDegrees, bool orthographic, float orthographicSize)
		{
			float k = orthographic
				? pixelHeight / (2f * Mathf.Max(orthographicSize, 1e-3f))
				: pixelHeight / (2f * Mathf.Tan(Mathf.Clamp(fieldOfViewDegrees, 1e-3f, 179f) * 0.5f * Mathf.Deg2Rad));
			return Log2(Mathf.Max(k, 1e-6f) / Mathf.Max(pixelError, 1e-3f));
		}

		/// <summary>log2 of the distance the field measures (FishTerrainLogDistance).</summary>
		public static float LogDistance(float distance, bool orthographic) => orthographic ? 0f : Log2(Mathf.Max(distance, 1e-3f));

		/// <summary>The field at a point <paramref name="distance"/> from the camera whose field reads <paramref name="logRough"/> (g_l).</summary>
		public static float Lod(float distance, float logRough, float bias, bool orthographic) => LogDistance(distance, orthographic) - logRough - bias;

		/// <summary>The smallest field over a box whose points read at most <paramref name="logRough"/> (FishTerrainMinLod).</summary>
		public static float MinLod(Vector3 eye, Vector3 lo, Vector3 hi, float logRough, float bias, bool orthographic)
		{
			Vector3 gap = new Vector3(
				Mathf.Max(Mathf.Max(lo.x - eye.x, eye.x - hi.x), 0f),
				Mathf.Max(Mathf.Max(lo.y - eye.y, eye.y - hi.y), 0f),
				Mathf.Max(Mathf.Max(lo.z - eye.z, eye.z - hi.z), 0f));
			return LogDistance(gap.magnitude, orthographic) - logRough - bias;
		}

		/// <summary>Whether a node of <paramref name="level"/> whose smallest field is <paramref name="minLod"/> splits into its children.</summary>
		public static bool Splits(int level, float minLod) => level > 0 && minLod < level;

		/// <summary>
		/// Whether a node is drawn: it does not split, and its parent does (a root: it does not split). <paramref name="parentMinLod"/>
		/// is ignored for a root.
		/// </summary>
		public static bool Drawn(int level, int levels, float minLod, float parentMinLod)
		{
			if (Splits(level, minLod))
			{
				return false;
			}
			return level + 1 >= levels || Splits(level + 1, parentMinLod);
		}

		/// <summary>How far the geomorph has slid a vertex at field <paramref name="lod"/> from level <paramref name="level"/> onto the next (0..1).</summary>
		public static float MorphShare(float lod, int level, float morphStart)
		{
			return Mathf.Clamp01((lod - level - morphStart) / (1f - morphStart));
		}

		/// <summary>
		/// The geomorph (FishTerrainMorph): a vertex of a level-<paramref name="level"/> patch at texel <paramref name="g"/> slides
		/// toward the lower even texel of each coarser grid, step l by <see cref="MorphShare"/> of field f_l at the place it has
		/// reached. <paramref name="lodAt"/> is field f_l at a texel place (tile-local; its height is the caller's).
		/// </summary>
		public static Vector2 Morph(Vector2 g, int level, int levels, float morphStart, Func<Vector2, int, float> lodAt)
		{
			Vector2 p = g;
			for (int l = level; l + 1 < levels; l++)
			{
				float t = MorphShare(lodAt(p, l), l, morphStart);
				if (t <= 0f)
				{
					break;
				}
				float stride = 2 << l;
				p -= new Vector2(p.x - Mathf.Floor(p.x / stride) * stride, p.y - Mathf.Floor(p.y / stride) * stride) * t;
			}
			return p;
		}

		/// <summary>
		/// A patch's triangles: <see cref="PatchQuads"/> squared quads of <see cref="PatchVertices"/> squared vertices (row by row,
		/// x fastest), each split along its (0, 0)–(1, 1) diagonal and wound clockwise seen from above, so it faces up. A fully
		/// slid patch is then the coarser grid split the same way.
		/// </summary>
		public static int[] PatchIndices()
		{
			var indices = new int[PatchQuads * PatchQuads * 6];
			int i = 0;
			for (int z = 0; z < PatchQuads; z++)
			{
				for (int x = 0; x < PatchQuads; x++)
				{
					int v00 = z * PatchVertices + x;
					int v10 = v00 + 1;
					int v01 = v00 + PatchVertices;
					int v11 = v01 + 1;
					indices[i++] = v00; indices[i++] = v11; indices[i++] = v10;
					indices[i++] = v00; indices[i++] = v01; indices[i++] = v11;
				}
			}
			return indices;
		}

		/// <summary>The patch triangulation's height at (u, v) in a quad of corners h00, h10, h01, h11 (FishTerrainQuadHeight).</summary>
		public static float QuadHeight(float h00, float h10, float h01, float h11, float u, float v)
		{
			return u >= v ? h00 + u * (h10 - h00) + v * (h11 - h10) : h00 + v * (h01 - h00) + u * (h11 - h01);
		}

		/// <summary>
		/// How far level <paramref name="level"/>'s surface misses a texel's height (FishTerrainRough), metres: 0 at a texel that
		/// level keeps. <paramref name="heights"/> is the tile's, (quads + 1) squared, row by row, metres.
		/// </summary>
		public static float TexelDeviation(float[] heights, int quads, int x, int z, int level)
		{
			int res = quads + 1;
			int stride = 1 << level;
			int x0 = Math.Min(x / stride * stride, quads - stride);
			int z0 = Math.Min(z / stride * stride, quads - stride);
			float u = (x - x0) / (float)stride, v = (z - z0) / (float)stride;
			float coarse = QuadHeight(heights[z0 * res + x0], heights[z0 * res + x0 + stride], heights[(z0 + stride) * res + x0],
				heights[(z0 + stride) * res + x0 + stride], u, v);
			return Mathf.Abs(heights[z * res + x] - coarse);
		}

		/// <summary>Field g_l of a cell whose texels level l+1 misses by at most <paramref name="deviation"/> metres (FishTerrainRoughLog).</summary>
		public static float FieldValue(float deviation, int field)
		{
			return Mathf.Max(Log2(Mathf.Max(deviation, 1e-6f)), Log2(FieldFloorMetres)) - (field + 1);
		}

		/// <summary>Each field raised to the one below it less 1, cell by cell (FishTerrainFieldRaise): f_(l−1) ≥ f_l − 1 everywhere.</summary>
		public static void Raise(float[][] fields)
		{
			for (int l = 1; l < fields.Length; l++)
			{
				for (int i = 0; i < fields[l].Length; i++)
				{
					fields[l][i] = Mathf.Max(fields[l][i], fields[l - 1][i] - 1f);
				}
			}
		}

		/// <summary>
		/// The field's dilation along one axis (FishTerrainDilateAlong): each cell the most of v(k) − slope·|k − here| within the reach.
		/// </summary>
		public static float[] Dilate(float[] field, int width, int height, bool alongX, float slope, int reach)
		{
			var result = new float[field.Length];
			for (int z = 0; z < height; z++)
			{
				for (int x = 0; x < width; x++)
				{
					float best = field[z * width + x];
					for (int k = 1; k <= reach; k++)
					{
						float fall = slope * k;
						int ax = alongX ? x + k : x, az = alongX ? z : z + k;
						int bx = alongX ? x - k : x, bz = alongX ? z : z - k;
						if (ax < width && az < height)
						{
							best = Mathf.Max(best, field[az * width + ax] - fall);
						}
						if (bx >= 0 && bz >= 0)
						{
							best = Mathf.Max(best, field[bz * width + bx] - fall);
						}
					}
					result[z * width + x] = best;
				}
			}
			return result;
		}

		/// <summary>The field's log2 R at a place in cells (cell centres at i + 0.5), bilinear, clamped (FishTerrainLogRoughAt).</summary>
		public static float FieldAt(float[] field, int width, int height, float cx, float cz)
		{
			float x = cx - 0.5f, z = cz - 0.5f;
			float bx = Mathf.Floor(x), bz = Mathf.Floor(z);
			float fx = x - bx, fz = z - bz;
			int x0 = Mathf.Clamp((int)bx, 0, width - 1), x1 = Mathf.Clamp((int)bx + 1, 0, width - 1);
			int z0 = Mathf.Clamp((int)bz, 0, height - 1), z1 = Mathf.Clamp((int)bz + 1, 0, height - 1);
			float a = Mathf.Lerp(field[z0 * width + x0], field[z0 * width + x1], fx);
			float b = Mathf.Lerp(field[z1 * width + x0], field[z1 * width + x1], fx);
			return Mathf.Lerp(a, b, fz);
		}

		/// <summary>log2, in floats (Mathf has none).</summary>
		public static float Log2(float v) => (float)(Math.Log(v) / Math.Log(2.0));
	}
}
