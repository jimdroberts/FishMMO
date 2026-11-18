#if UNITY_EDITOR
using System;
using System.Threading.Tasks;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>How glaciers carve.</summary>
	public sealed class GlacialSettings
	{
		/// <summary>Passes. Each routes the ice afresh over the ground the last one carved.</summary>
		public int Passes = 30;

		/// <summary>
		/// The ice flux, in m² of fully nourished snowfield, at which a glacier is <see cref="HalfWidthMetres"/>
		/// wide and cuts <see cref="CutMetres"/> a pass.
		/// </summary>
		public float ReferenceFlux = 100_000f;

		/// <summary>The least flux that counts as a glacier rather than a snowfield.</summary>
		public float MinimumFlux = 5_000f;

		/// <summary>Half a glacier's width at the reference flux, in metres; it grows with flux to the 0.4, as valley glaciers do.</summary>
		public float HalfWidthMetres = 60f;

		/// <summary>The widest half-width, in metres.</summary>
		public float MaxHalfWidthMetres = 400f;

		/// <summary>Metres the bed under a glacier's centre line is lowered per pass at the reference flux on a 20% bed in middling rock.</summary>
		public float CutMetres = 0.25f;

		/// <summary>How deep the ice stands over its bed at the reference flux, in metres; it grows with flux to the 0.3.</summary>
		public float ThicknessMetres = 40f;

		/// <summary>
		/// The trough's cross-section exponent: the ground is cut towards bed + thickness × (distance ÷ half
		/// width)^this. Real glacial troughs fit 1.5–2.5; 2 is the parabola.
		/// </summary>
		public float TroughExponent = 2f;

		/// <summary>The most a glacier lowers its centre line in one pass, in metres.</summary>
		public float MaxCutMetres = 1.5f;

		/// <summary>
		/// Share of a wall's height above the trough cut away per pass. The walls relax towards the
		/// trough while the centre line deepens only at the cutting rate, so the floor widens: cut by
		/// the same amount everywhere, a V only sank.
		/// </summary>
		public float WallRelaxation = 0.35f;

		/// <summary>
		/// The slope a glacier still cuts on, added to the bed's own: ice flows on its own surface's
		/// slope, not the bed's, so it carves flat and even uphill stretches — the overdeepened basins
		/// that hold lakes after the ice has gone.
		/// </summary>
		public float SurfaceSlope = 0.1f;
	}

	/// <summary>
	/// Glaciers wearing the ground: U-shaped troughs, overdeepened basins, cirques at their heads and
	/// knife-edge ridges between.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Where.</b> A glacier is fed above the snowline, where more snow falls than melts, and wastes
	/// below it. Most glacial landscapes on Earth were carved at the last glacial maximum, about six
	/// degrees colder, and outlived the ice; the ground's own climate says how glacial each point is
	/// (<see cref="IErosionGround.IceBalance"/>), so cold uplands get troughs whether or not they hold
	/// ice today, and a world with no snowfall gets none.
	/// </para>
	/// <para>
	/// <b>How.</b> The ice is routed down the valleys the rivers made, as glaciers do, gathering
	/// everything fed above the snowline and losing it below until it runs out — so a glacier's
	/// reach is its own mass balance. Where it flows it cuts in proportion to the square root of its
	/// flux, across its whole width, in a flat-floored profile with steep sides: a V-valley becomes a
	/// U. Ice flows on its surface's slope, not its bed's, so unlike a river it cuts flat and even
	/// uphill stretches, and leaves basins below a lip — the tarns and finger lakes of glaciated
	/// country. Neighbouring troughs widening into each other leave arêtes; their heads, cut back
	/// into the snowfield, cirques.
	/// </para>
	/// <para>
	/// Runs after the rivers (glaciers occupy river valleys) on the landscape model's grid.
	/// Sequential routing, parallel carving into a max-combined map: deterministic.
	/// </para>
	/// </remarks>
	public static class GlacialErosion
	{
		/// <summary>Carves <paramref name="height"/> in place. Returns the share of cells a glacier flowed over in the last pass.</summary>
		/// <param name="iceBalance">Per cell and altitude, how much ice is fed (positive, above the snowline) or melted (negative), relative to a well-fed snowfield (1).</param>
		/// <param name="hardness">The rock's hardness at a cell and altitude, 0 … 1.</param>
		/// <param name="outlet">Per cell, true where ice and water leave: the edge, the sea, sinks.</param>
		public static float Run(float[] height, int width, int depth, float cellMetres, Func<int, float, float> iceBalance,
			Func<int, float, float> hardness, bool[] outlet, GlacialSettings settings)
		{
			int count = width * depth;
			float cellArea = cellMetres * cellMetres;
			var filled = new float[count];
			var receiver = new int[count];
			var distance = new float[count];
			var order = new int[count];
			var flux = new float[count];
			var cut = new float[count];
			float diagonal = cellMetres * 1.41421356f;
			float iced = 0f;

			// Anything glacial at all? A warm world skips the routing entirely.
			bool any = false;
			for (int n = 0; n < count && !any; n += 7)
			{
				any = iceBalance(n, height[n]) > 0f;
			}
			if (!any)
			{
				return 0f;
			}

			for (int pass = 0; pass < settings.Passes; pass++)
			{
				int ordered = LandscapeEvolution.Route(height, width, depth, cellMetres, diagonal, outlet, filled, receiver, distance, order);

				// The ice: fed above the snowline, melted below, carried down the valleys from the heads.
				for (int n = 0; n < count; n++)
				{
					flux[n] = 0f;
				}
				for (int t = ordered - 1; t >= 0; t--)
				{
					int n = order[t];
					flux[n] = Math.Max(0f, flux[n] + iceBalance(n, height[n]) * cellArea);
					int r = receiver[n];
					if (r >= 0)
					{
						flux[r] += flux[n];
					}
				}

				// The cut, across each glacier's width, combined by the deepest at every cell.
				Array.Clear(cut, 0, count);
				int glaciated = 0;
				for (int t = 0; t < ordered; t++)
				{
					int n = order[t];
					int r = receiver[n];
					if (r < 0 || flux[n] < settings.MinimumFlux)
					{
						continue;
					}
					glaciated++;
					float share = flux[n] / settings.ReferenceFlux;
					float slope = Math.Max(0f, height[n] - height[r]) / distance[n] + settings.SurfaceSlope;
					float softness = 1f - hardness(n, height[n]);
					float rock = 0.15f + 0.85f * softness * softness / 0.25f;
					float depthCut = Math.Min(settings.MaxCutMetres, settings.CutMetres * MathF.Sqrt(share) * (slope / 0.2f) * rock);
					float half = Math.Clamp(settings.HalfWidthMetres * MathF.Pow(share, 0.4f), cellMetres, settings.MaxHalfWidthMetres);
					/* The ice fills the valley to its surface and grinds every wall it touches below it, so
					 * the ground is cut towards a trough: the bed at the centre line, rising as a parabola to
					 * the ice surface at the glacier's edge. A V's walls stand above that curve and are cut
					 * back — the floor widens — where lowering the whole width evenly would only have sunk
					 * the V. */
					float bed = height[n] - depthCut;
					float thickness = settings.ThicknessMetres * MathF.Pow(share, 0.3f);
					int reach = (int)MathF.Ceiling(half / cellMetres);
					int cx = n % width, cz = n / width;
					for (int dz = -reach; dz <= reach; dz++)
					{
						int z = cz + dz;
						if (z < 0 || z >= depth)
						{
							continue;
						}
						for (int dx = -reach; dx <= reach; dx++)
						{
							int x = cx + dx;
							if (x < 0 || x >= width)
							{
								continue;
							}
							float d = MathF.Sqrt(dx * dx + dz * dz) * cellMetres / half;
							if (d >= 1f)
							{
								continue;
							}
							int m = z * width + x;
							float trough = bed + thickness * MathF.Pow(d, settings.TroughExponent);
							float above = height[m] - trough;
							float here = m == n ? depthCut : above * settings.WallRelaxation;
							if (here > cut[m])
							{
								cut[m] = here;
							}
						}
					}
				}
				Parallel.For(0, depth, z =>
				{
					for (int x = 0; x < width; x++)
					{
						int m = z * width + x;
						if (!outlet[m])
						{
							height[m] -= cut[m];
						}
					}
				});
				iced = glaciated / (float)count;
			}
			return iced;
		}
	}
}
#endif
