#if UNITY_EDITOR
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A whole scene's ground as one stitched grid of altitudes, in scene metres above the body's sea
	/// level, before any of it is written to a terrain.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One grid, not one heightmap per tile.</b> Neighbouring tiles share their edge row and
	/// column: sample 512 of one tile is sample 0 of the next. Sampled tile by tile, that sharing
	/// only held because both tiles asked the same pure function. A pass that reads its
	/// neighbourhood — erosion moving material downhill, a river tracing its course, a road
	/// levelling its bed — would see a different neighbourhood on each side of a seam and open a
	/// step along it. Here a seam is just a row of the grid, and tiles are cut from it afterwards.
	/// </para>
	/// <para>
	/// <b>The range is measured, not bounded.</b> A terrain stores its heights as fractions of one
	/// range, so a height outside the range is clamped flat. The generator used to bound that range
	/// in advance and shrink it once the tiles were written, which cost a second 16-bit
	/// requantisation, and which no pass that cuts below the planet's ground could have lived with.
	/// With every height in hand first, the range is simply the lowest and highest of them.
	/// </para>
	/// <para>
	/// Positions are those of <see cref="SceneGeneration.AltitudeMetres"/>: metres along the scene's
	/// +X (east at a heading of 0) and +Z from its centre. Rows run south to north and columns west to
	/// east, so <c>Metres[z * Width + x]</c> is the sample at (<see cref="EastOf"/>(x), <see cref="NorthOf"/>(z)).
	/// </para>
	/// </remarks>
	public sealed class SceneHeightField
	{
		/// <summary>The tile grid this field is cut into.</summary>
		public readonly TerrainTilePlan Plan;

		/// <summary>Samples west to east: every tile's columns, edges shared.</summary>
		public readonly int Width;

		/// <summary>Samples south to north: every tile's rows, edges shared.</summary>
		public readonly int Depth;

		/// <summary>Metres between neighbouring samples, the same both ways.</summary>
		public readonly float Spacing;

		/// <summary>Altitude of each sample, in scene metres above the body's sea level, row by row from the south.</summary>
		public readonly float[] Metres;

		/// <summary>The scene's own ground, asked for anything past the grid's edge.</summary>
		private readonly SceneAltitude beyond;

		private SceneHeightField(TerrainTilePlan plan, SceneAltitude beyond)
		{
			Plan = plan;
			int perTile = Mathf.Max(1, plan.Resolution - 1);
			Width = plan.CountX * perTile + 1;
			Depth = plan.CountZ * perTile + 1;
			Spacing = plan.MetresPerSample;
			Metres = new float[Width * Depth];
			this.beyond = beyond;
		}

		/// <summary>
		/// Samples the scene's ground at every heightmap sample of every tile.
		/// </summary>
		/// <remarks>
		/// Row by row on every core: each sample is a pure function of its position
		/// (<see cref="SceneAltitude"/>), so the grid is the same however the rows are shared out. A
		/// 6.5 km scene is about 7.9 million samples.
		/// </remarks>
		public static SceneHeightField Sample(SceneGenerationRequest request, TerrainTilePlan plan)
		{
			var altitude = new SceneAltitude(request);
			var field = new SceneHeightField(plan, altitude);
			int width = field.Width;
			float[] metres = field.Metres;
			Parallel.For(0, field.Depth, z =>
			{
				float north = field.NorthOf(z);
				int row = z * width;
				for (int x = 0; x < width; x++)
				{
					metres[row + x] = altitude.At(field.EastOf(x), north);
				}
			});
			return field;
		}

		/// <summary>Scene metres east of the centre of column <paramref name="x"/>.</summary>
		/// <remarks>
		/// From the column's index across the whole grid, in double precision, so a column shared by
		/// two tiles is one position, not two that round differently.
		/// </remarks>
		public float EastOf(int x) => (float)(x * (double)Plan.TileMetres / Mathf.Max(1, Plan.Resolution - 1) - Plan.WidthMetres * 0.5);

		/// <summary>Scene metres north of the centre of row <paramref name="z"/>.</summary>
		public float NorthOf(int z) => (float)(z * (double)Plan.TileMetres / Mathf.Max(1, Plan.Resolution - 1) - Plan.DepthMetres * 0.5);

		/// <summary>The lowest and highest sample, in scene metres above the body's sea level.</summary>
		public void Range(out float lowest, out float highest)
		{
			lowest = float.MaxValue;
			highest = float.MinValue;
			foreach (float metres in Metres)
			{
				if (metres < lowest)
				{
					lowest = metres;
				}
				if (metres > highest)
				{
					highest = metres;
				}
			}
			if (Metres.Length == 0)
			{
				lowest = highest = 0f;
			}
		}

		/// <summary>True when a scene position lies on the grid, edges included.</summary>
		public bool Contains(float eastMetres, float northMetres)
		{
			float halfWidth = Plan.WidthMetres * 0.5f;
			float halfDepth = Plan.DepthMetres * 0.5f;
			return eastMetres >= -halfWidth && eastMetres <= halfWidth && northMetres >= -halfDepth && northMetres <= halfDepth;
		}

		/// <summary>
		/// The ground at a scene position: from the grid on the scene, from the planet past its edge.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Everything that meets the terrain reads the ground through this</b> — the backdrop where
		/// it joins the scene's edge, and the biome field the horizon is coloured from — so once a
		/// pass reshapes the grid they meet the reshaped ground, not the planet's.
		/// </para>
		/// <para>
		/// Between samples it is bilinear: the same surface a terrain draws through its own samples,
		/// to well within what its level of detail moves it by.
		/// </para>
		/// </remarks>
		public float MetresAt(float eastMetres, float northMetres)
		{
			if (!Contains(eastMetres, northMetres))
			{
				return beyond.At(eastMetres, northMetres);
			}
			float gx = (eastMetres + Plan.WidthMetres * 0.5f) / Spacing;
			float gz = (northMetres + Plan.DepthMetres * 0.5f) / Spacing;
			int x0 = Mathf.Clamp((int)gx, 0, Width - 1);
			int z0 = Mathf.Clamp((int)gz, 0, Depth - 1);
			int x1 = Math.Min(x0 + 1, Width - 1);
			int z1 = Math.Min(z0 + 1, Depth - 1);
			float tx = Mathf.Clamp01(gx - x0);
			float tz = Mathf.Clamp01(gz - z0);
			float south = Mathf.Lerp(Metres[z0 * Width + x0], Metres[z0 * Width + x1], tx);
			float north = Mathf.Lerp(Metres[z1 * Width + x0], Metres[z1 * Width + x1], tx);
			return Mathf.Lerp(south, north, tz);
		}

		/// <summary>
		/// One tile's heightmap, as the fractions of <paramref name="reliefMetres"/> above
		/// <paramref name="floorMetres"/> a <see cref="TerrainData"/> stores, indexed [z, x] as Unity
		/// indexes it.
		/// </summary>
		public float[,] TileHeights(int tileX, int tileZ, float floorMetres, float reliefMetres)
		{
			int resolution = Plan.Resolution;
			int perTile = Mathf.Max(1, resolution - 1);
			var heights = new float[resolution, resolution];
			float scale = reliefMetres > 0f ? 1f / reliefMetres : 0f;
			for (int z = 0; z < resolution; z++)
			{
				int row = (tileZ * perTile + z) * Width + tileX * perTile;
				for (int x = 0; x < resolution; x++)
				{
					heights[z, x] = Mathf.Clamp01((Metres[row + x] - floorMetres) * scale);
				}
			}
			return heights;
		}
	}
}
#endif
