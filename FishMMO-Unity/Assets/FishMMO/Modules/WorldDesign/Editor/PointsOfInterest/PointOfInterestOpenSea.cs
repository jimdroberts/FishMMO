#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The open sea of a scene (2026-10-20: "Docks, shipyards, etc … land on lakes and inlets instead of open ocean. Ships are
	/// only on seas and the FFT ocean."): the ground under sea level that the ocean past the scene's edge reaches through water
	/// a ship could pass, as against a basin under sea level shut in by land or shallows (a lagoon in the drawn ocean, calm as a
	/// lake) or an inlet's narrows.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Two floods.</b> From the cells on the scene's border, through every cell at least <see cref="PassageDepth"/> under sea
	/// level (a ship's way in); then from those over the shallows they touch (under sea level, too shallow to pass), never on
	/// into deeper water past them: a basin behind a sand bar is a lagoon. What neither flood reaches is not the sea's, however
	/// low it lies.
	/// </para>
	/// <para>
	/// <b>Open water.</b> <see cref="OpenShare"/>: how much of the square about a place is open sea. A harbour wants a fair share
	/// of it (<see cref="HarbourShare"/>); a narrow inlet's banks see little.
	/// </para>
	/// </remarks>
	public static class PointOfInterestOpenSea
	{
		/// <summary>The depth under sea level a way in from the open sea needs, metres: a cog's draft and some.</summary>
		public const float PassageDepth = 2f;

		/// <summary>The half side of the square <see cref="OpenShare"/> is read over for a harbour, metres.</summary>
		public const float HarbourReach = 480f;

		/// <summary>The share of open sea a harbour's square needs: a straight coast's is about half, a narrow inlet's a sixth.</summary>
		public const float HarbourShare = 0.25f;

		/// <summary>The map's cell for a site builder's queries, metres.</summary>
		public const float MapCell = 16f;

		/// <summary>
		/// Which cells of a grid (row-major, <paramref name="width"/> × <paramref name="depth"/>, heights in scene metres) are
		/// the open sea's: the two floods of the remarks. Four-connected, so a diagonal touch of land is no way through.
		/// </summary>
		public static bool[] Flood(int width, int depth, float[] heights, float sea)
		{
			int n = width * depth;
			var open = new bool[n];
			if (width <= 0 || depth <= 0 || heights == null || heights.Length < n || float.IsNegativeInfinity(sea) || float.IsNaN(sea))
			{
				return open;
			}
			float passage = sea - PassageDepth;
			var queue = new Queue<int>();
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					int i = z * width + x;
					if ((x == 0 || z == 0 || x == width - 1 || z == depth - 1) && heights[i] < passage)
					{
						open[i] = true;
						queue.Enqueue(i);
					}
				}
			}
			Spread(width, depth, queue, open, i => heights[i] < passage);
			// The shallows of the same water: the cells under sea level but too shallow to pass that the passable sea touches. Never
			// on into deeper water past them: a basin behind a sand bar is not the sea's.
			for (int i = 0; i < n; i++)
			{
				if (open[i])
				{
					queue.Enqueue(i);
				}
			}
			Spread(width, depth, queue, open, i => heights[i] < sea && heights[i] >= passage);
			return open;
		}

		private static void Spread(int width, int depth, Queue<int> queue, bool[] open, Func<int, bool> passes)
		{
			while (queue.Count > 0)
			{
				int i = queue.Dequeue();
				int cx = i % width, cz = i / width;
				for (int k = 0; k < 4; k++)
				{
					int nx = cx + (k == 0 ? 1 : k == 1 ? -1 : 0), nz = cz + (k == 2 ? 1 : k == 3 ? -1 : 0);
					if (nx < 0 || nz < 0 || nx >= width || nz >= depth)
					{
						continue;
					}
					int j = nz * width + nx;
					if (!open[j] && passes(j))
					{
						open[j] = true;
						queue.Enqueue(j);
					}
				}
			}
		}

		/// <summary>A summed-area table of a mask (one wider and deeper than it, a zero row and column first).</summary>
		public static int[] Summed(int width, int depth, bool[] mask)
		{
			var sum = new int[(width + 1) * (depth + 1)];
			for (int z = 0; z < depth; z++)
			{
				int row = 0;
				for (int x = 0; x < width; x++)
				{
					row += mask[z * width + x] ? 1 : 0;
					sum[(z + 1) * (width + 1) + x + 1] = sum[z * (width + 1) + x + 1] + row;
				}
			}
			return sum;
		}

		/// <summary>
		/// The share of the cells within <paramref name="span"/> cells of (<paramref name="x"/>, <paramref name="z"/>) the mask
		/// holds, over the whole square: cells past the grid's edge count as the edge's (the open sea runs on past the scene).
		/// </summary>
		public static float OpenShare(int width, int depth, bool[] mask, int[] summed, int x, int z, int span)
		{
			int x0 = Math.Max(0, x - span), z0 = Math.Max(0, z - span), x1 = Math.Min(width - 1, x + span), z1 = Math.Min(depth - 1, z + span);
			if (x1 < x0 || z1 < z0)
			{
				return 0f;
			}
			int w1 = width + 1;
			int inside = summed[(z1 + 1) * w1 + x1 + 1] - summed[z0 * w1 + x1 + 1] - summed[(z1 + 1) * w1 + x0] + summed[z0 * w1 + x0];
			int cells = (x1 - x0 + 1) * (z1 - z0 + 1);
			// Past the edge: as open as the border cells beside it.
			int side = 2 * span + 1;
			int outside = side * side - cells;
			if (outside > 0)
			{
				int border = 0, borderOpen = 0;
				for (int bz = z0; bz <= z1; bz++)
				{
					for (int bx = x0; bx <= x1; bx++)
					{
						if (bx == 0 || bz == 0 || bx == width - 1 || bz == depth - 1)
						{
							border++;
							borderOpen += mask[bz * width + bx] ? 1 : 0;
						}
					}
				}
				float edgeShare = border > 0 ? borderOpen / (float)border : 0f;
				return (inside + edgeShare * outside) / (side * side);
			}
			return inside / (float)cells;
		}

		/// <summary>A scene's open sea on a grid, for site builders.</summary>
		public sealed class Map
		{
			private readonly int width, depth;
			private readonly float x0, z0, cell;
			private readonly bool[] open;
			private readonly int[] summed;

			/// <summary>A map of a scene <paramref name="widthMetres"/> × <paramref name="depthMetres"/> centred on the origin.</summary>
			public Map(float widthMetres, float depthMetres, float cellMetres, Func<float, float, float> ground, float sea)
			{
				cell = Mathf.Max(1f, cellMetres);
				width = Mathf.Max(1, Mathf.FloorToInt(widthMetres / cell));
				depth = Mathf.Max(1, Mathf.FloorToInt(depthMetres / cell));
				x0 = -width * cell * 0.5f;
				z0 = -depth * cell * 0.5f;
				var heights = new float[width * depth];
				for (int z = 0; z < depth; z++)
				{
					for (int x = 0; x < width; x++)
					{
						heights[z * width + x] = ground(x0 + (x + 0.5f) * cell, z0 + (z + 0.5f) * cell);
					}
				}
				open = Flood(width, depth, heights, sea);
				summed = Summed(width, depth, open);
			}

			/// <summary>Whether a point is over the open sea (false off the map, and wherever the ground stands over sea level).</summary>
			public bool IsOpen(float x, float z)
			{
				int gx = Mathf.FloorToInt((x - x0) / cell), gz = Mathf.FloorToInt((z - z0) / cell);
				return gx >= 0 && gz >= 0 && gx < width && gz < depth && open[gz * width + gx];
			}

			/// <summary>The share of open sea within <paramref name="reach"/> metres (a square) of a point.</summary>
			public float ShareAt(float x, float z, float reach)
			{
				int gx = Mathf.Clamp(Mathf.FloorToInt((x - x0) / cell), 0, width - 1), gz = Mathf.Clamp(Mathf.FloorToInt((z - z0) / cell), 0, depth - 1);
				return OpenShare(width, depth, open, summed, gx, gz, Mathf.Max(0, Mathf.RoundToInt(reach / cell)));
			}
		}

		/// <summary>
		/// The open sea of a site's scene as its ground stands now; null where the scene has no sea of water or its size is not
		/// known (the caller then takes every sea cell for open). Built afresh each call: a site builder asks once.
		/// </summary>
		public static Map For(PointOfInterestSiteContext context)
		{
			if (context == null || context.GroundAt == null || context.Tiles.WidthMetres <= 0f || context.Tiles.DepthMetres <= 0f)
			{
				return null;
			}
			float sea = PropsFeature.SeaLevelOf(context);
			if (float.IsNegativeInfinity(sea))
			{
				return null;
			}
			return new Map(context.Tiles.WidthMetres, context.Tiles.DepthMetres, MapCell, context.GroundAt, sea);
		}
	}
}
#endif
