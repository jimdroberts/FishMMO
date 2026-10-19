#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Terraced fields (2026-10-18, phase I2j-b, decisions 29 and 31): a hillside field cut into level benches along its contours, its
	/// steps faced in drystone (a wall laid along each), as an earth bund with a grassed lip, or as a grass bank, its crop sown on
	/// the benches; a terraced paddy's every bench dug level, bunded and flooded; and the old, given-up flights round a hamlet.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The plan</b> (<see cref="Plan"/>, pure): the field's ground read on a lattice <see cref="BiomeTerraces.SmoothCellMetres"/>
	/// apart and smoothed (two box passes), so the benches follow the hill's broad contours, not each hummock; its mean slope sets
	/// how high a step stands (<see cref="BiomeTerraces.RiserHeight"/>); the bench coordinate <c>u</c> (smoothed ground over the
	/// step's height from the ground at the field's middle) places every point on a bench and across it. The ground the field leaves
	/// is <see cref="BiomeTerraces.Bench"/> of it, dying back into the hillside over <see cref="BiomeTerraces.EdgeFadeMetres"/> at
	/// the field's edge.
	/// </para>
	/// <para>
	/// <b>Carved and put back</b> as ridge and furrow and paddies are (PointOfInterestFieldCarve.CarveTo: recorded, restored before
	/// anything reads the ground again), so every cut, repaint and Regenerate POIs lays the same benches from the same ground.
	/// </para>
	/// <para>
	/// <b>Water</b> (terraced paddies): one lake of the scene's hydrology a connected stretch of a bench's floor, on a one-metre mask
	/// fixed to the scene's metres, its id in the paddies' range (<see cref="LakeId"/>), so the place stage's clearing and the cut's
	/// own water handle it as a paddy's. Each bench's bund is wide enough that the water's sheet, drawn a cell larger than its mask,
	/// stays under ground (tested).
	/// </para>
	/// </remarks>
	public static class PointOfInterestFieldTerraces
	{
		/// <summary>The kinds of lake a field adds besides a paddy's, in bits 24–26 of a paddy's id (a paddy's own are 0 there; a channel's set bit 27).</summary>
		public const int TreadLakeKind = 1, RillLakeKind = 2;

		/// <summary>
		/// A lake id for a field's terraced paddy bench or marcita rill (pure): the paddies' base, the kind, the site's low 12 bits, the
		/// field's index and the piece. Inside the paddies' range (<see cref="PointOfInterestPaddies.IsPaddy"/>), never a paddy's or a channel's.
		/// </summary>
		public static int LakeId(int kind, int site, int field, int piece)
			=> PointOfInterestPaddies.LakeIdBase | ((kind & 0x7) << 24) | ((site & 0xFFF) << 12) | ((field & 0xF) << 8) | (piece & 0xFF);

		/// <summary>The cell a terraced paddy's water is masked on, metres.</summary>
		public const float WaterCellMetres = 1f;

		/// <summary>The lattice a field's bench lines (its walls' and its orchard rows') are traced on, metres.</summary>
		public const float LineCellMetres = 1.5f;

		/// <summary>How far in from a field's edge its steps' walls are laid, metres: where its benches stand at least most of their height.</summary>
		public const float WallInsideMetres = 0.6f * BiomeTerraces.EdgeFadeMetres;

		/// <summary>One terraced field's benches (pure; see the class remarks).</summary>
		public sealed class Plan
		{
			public Vector2 Centre;
			public float Yaw;
			public Vector2 Size;
			public BiomeTerraceRiser Riser;
			/// <summary>Each bench a paddy (dug, bunded and flooded).</summary>
			public bool Paddy;
			/// <summary>False where the ground is too gentle to step (or has none): the field is then laid flat.</summary>
			public bool Terraced;
			/// <summary>The smoothed ground at the field's middle (u = 0), metres.</summary>
			public float Base;
			/// <summary>A step's height, metres.</summary>
			public float Rise;
			/// <summary>The share of a bench's period its step takes.</summary>
			public float Share;
			/// <summary>The field's mean slope, degrees, and its mean gradient (rise over run).</summary>
			public float Slope, Gradient;
			/// <summary>The smoothed ground's lattice: its first point (world xz), spacing, size and values (row by row).</summary>
			public Vector2 Origin;
			public float Cell;
			public int Width, Height;
			public float[] Smooth;

			/// <summary>The smoothed ground at a point, metres (bilinear on the lattice, held to its edge).</summary>
			public float SmoothAt(float x, float z)
			{
				float fx = Mathf.Clamp((x - Origin.x) / Cell, 0f, Width - 1.001f), fz = Mathf.Clamp((z - Origin.y) / Cell, 0f, Height - 1.001f);
				int i = (int)fx, j = (int)fz;
				float tx = fx - i, tz = fz - j;
				float a = Smooth[j * Width + i], b = Smooth[j * Width + i + 1], c = Smooth[(j + 1) * Width + i], d = Smooth[(j + 1) * Width + i + 1];
				return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
			}

			/// <summary>The smoothed ground's gradient at a point (rise over run, its size).</summary>
			public float GradientAt(float x, float z)
			{
				float h = 0.5f * Cell;
				float gx = (SmoothAt(x + h, z) - SmoothAt(x - h, z)) / (2f * h), gz = (SmoothAt(x, z + h) - SmoothAt(x, z - h)) / (2f * h);
				return Mathf.Sqrt(gx * gx + gz * gz);
			}

			/// <summary>The bench coordinate at a point (see BiomeTerraces).</summary>
			public float U(float x, float z) => (SmoothAt(x, z) - Base) / Rise;

			/// <summary>A bench and its step across the slope at a point, metres on the plan.</summary>
			public float PeriodAt(float x, float z) => BiomeTerraces.Period(Rise, GradientAt(x, z));

			/// <summary>How far in from the field's edge a point lies, metres (negative outside).</summary>
			public float Inside(float x, float z) => PointOfInterestPaddies.Inside(Centre, Yaw, Size, x, z);

			/// <summary>The level bench <paramref name="band"/> is cut to, metres (a terraced paddy's water there).</summary>
			public float Level(int band) => Base + Rise * BiomeTerraces.TreadLevel(band, Share);

			/// <summary>
			/// The ground the benches leave at a point before their fade at the field's edge, metres: the bench profile (with an earth
			/// bund's lip), or on a terraced paddy its bench's floor and bund, its step raised a bund's height so it meets both.
			/// </summary>
			public float Target(float x, float z)
			{
				float u = U(x, z);
				int band = BiomeTerraces.BandOf(u);
				float t = u - band;
				float period = PeriodAt(x, z);
				float stair = Base + Rise * BiomeTerraces.Bench(u, Share);
				if (!Paddy)
				{
					return stair + BiomeTerraces.LipRaise(Riser, t * period);
				}
				if (t > 1f - Share)
				{
					return stair + BiomeFieldSurfaces.PaddyBundRise;
				}
				return BiomeTerraces.PaddyTread(BiomeTerraces.PaddyInner(t, Share, period, Inside(x, z)), Level(band));
			}

			/// <summary>How far the benches move the ground at a point standing at <paramref name="ground"/>, metres, signed (0 outside the field and where it is not terraced).</summary>
			public float Offset(float x, float z, float ground)
			{
				if (!Terraced)
				{
					return 0f;
				}
				float inside = Inside(x, z);
				if (inside <= 0f)
				{
					return 0f;
				}
				return (Target(x, z) - ground) * BiomeTerraces.Fade(inside);
			}

			/// <summary>True where the crop is sown at a point: on a bench clear of its margins (a terraced paddy: on its floor), <paramref name="headland"/> in from the edge.</summary>
			public bool Sown(float x, float z, float headland)
			{
				float inside = Inside(x, z);
				if (inside < headland)
				{
					return false;
				}
				if (!Terraced)
				{
					return true;
				}
				float u = U(x, z);
				float t = BiomeTerraces.Phase(u);
				float period = PeriodAt(x, z);
				if (Paddy)
				{
					return t <= 1f - Share && BiomeTerraces.PaddyInner(t, Share, period, inside) >= 0f;
				}
				return BiomeTerraces.OnTread(Riser, t, Share, period);
			}

			/// <summary>How far in from its bench's lip a point lies, metres (a terraced row's place across its bench).</summary>
			public float AcrossBench(float x, float z) => BiomeTerraces.Phase(U(x, z)) * PeriodAt(x, z);

			/// <summary>The world rectangle the benches can touch: the field's bounds with a sample's room.</summary>
			public Rect Area()
			{
				float reach = 0.5f * Size.magnitude + 1f;
				return Rect.MinMaxRect(Centre.x - reach, Centre.y - reach, Centre.x + reach, Centre.y + reach);
			}

			/// <summary>The lowest and highest bench the field holds.</summary>
			public void Bands(out int low, out int high)
			{
				low = int.MaxValue;
				high = int.MinValue;
				for (int j = 0; j < Height; j++)
				{
					for (int i = 0; i < Width; i++)
					{
						float x = Origin.x + i * Cell, z = Origin.y + j * Cell;
						if (Inside(x, z) < 0f)
						{
							continue;
						}
						int band = BiomeTerraces.BandOf((Smooth[j * Width + i] - Base) / Rise);
						low = Math.Min(low, band);
						high = Math.Max(high, band);
					}
				}
				if (low > high)
				{
					low = high = 0;
				}
			}
		}

		/// <summary>
		/// The benches of a field (pure): its ground (<paramref name="ground"/>, metres at world x, z) read and smoothed, its slope, step
		/// and share. Not <see cref="Plan.Terraced"/> (laid flat) where its riser is none, its mean slope under
		/// <see cref="BiomeTerraces.MinSlopeDegrees"/> or any of its ground missing.
		/// </summary>
		public static Plan Make(Vector2 centre, float yaw, Vector2 size, BiomeTerraceRiser riser, bool paddy, Func<float, float, float> ground)
		{
			float cell = BiomeTerraces.SmoothCellMetres;
			int r = BiomeTerraces.SmoothRadiusCells;
			float reach = 0.5f * size.magnitude + (2 * r + 2) * cell;
			// On a lattice fixed to the scene's metres, so two fields read their shared ground alike.
			float x0 = Mathf.Floor((centre.x - reach) / cell) * cell, z0 = Mathf.Floor((centre.y - reach) / cell) * cell;
			int w = Mathf.CeilToInt((centre.x + reach - x0) / cell) + 1, h = Mathf.CeilToInt((centre.y + reach - z0) / cell) + 1;
			var plan = new Plan { Centre = centre, Yaw = yaw, Size = size, Riser = riser, Paddy = paddy, Origin = new Vector2(x0, z0), Cell = cell, Width = w, Height = h };
			var raw = new float[w * h];
			bool whole = ground != null && riser != BiomeTerraceRiser.None;
			for (int j = 0; j < h && whole; j++)
			{
				for (int i = 0; i < w; i++)
				{
					float g = ground(x0 + i * cell, z0 + j * cell);
					if (float.IsNaN(g) || float.IsInfinity(g))
					{
						whole = false;
						break;
					}
					raw[j * w + i] = g;
				}
			}
			plan.Smooth = whole ? BoxBlur(BoxBlur(raw, w, h, r), w, h, r) : raw;
			plan.Rise = 1f;
			if (!whole)
			{
				return plan;
			}
			// The mean slope over the field's own ground.
			plan.Base = plan.SmoothAt(centre.x, centre.y);
			double sum = 0.0;
			int count = 0;
			for (int j = 0; j < h; j++)
			{
				for (int i = 0; i < w; i++)
				{
					float x = x0 + i * cell, z = z0 + j * cell;
					if (PointOfInterestPaddies.Inside(centre, yaw, size, x, z) < 0f)
					{
						continue;
					}
					sum += plan.GradientAt(x, z);
					count++;
				}
			}
			plan.Gradient = count > 0 ? (float)(sum / count) : 0f;
			plan.Slope = Mathf.Atan(plan.Gradient) * Mathf.Rad2Deg;
			plan.Rise = BiomeTerraces.RiserHeight(riser, plan.Slope);
			plan.Share = BiomeTerraces.Share(riser, BiomeTerraces.Period(plan.Rise, plan.Gradient));
			plan.Terraced = plan.Slope >= BiomeTerraces.MinSlopeDegrees;
			return plan;
		}

		/// <summary>One separable box pass of radius <paramref name="r"/> cells over a lattice (pure; held to its edges).</summary>
		public static float[] BoxBlur(float[] values, int w, int h, int r)
		{
			var across = new float[values.Length];
			var result = new float[values.Length];
			for (int j = 0; j < h; j++)
			{
				for (int i = 0; i < w; i++)
				{
					float s = 0f;
					for (int k = -r; k <= r; k++)
					{
						s += values[j * w + Mathf.Clamp(i + k, 0, w - 1)];
					}
					across[j * w + i] = s / (2 * r + 1);
				}
			}
			for (int j = 0; j < h; j++)
			{
				for (int i = 0; i < w; i++)
				{
					float s = 0f;
					for (int k = -r; k <= r; k++)
					{
						s += across[Mathf.Clamp(j + k, 0, h - 1) * w + i];
					}
					result[j * w + i] = s / (2 * r + 1);
				}
			}
			return result;
		}

		// ── Lines along the benches ───────────────────────────────────

		/// <summary>
		/// The lines along every bench of a field at a phase across it (pure): the level line where u = band + <paramref name="phase"/>,
		/// for every band the field holds, kept only where it lies at least <paramref name="inside"/> metres in from the field's edge,
		/// each in runs of at least two metres. Each line comes with its band.
		/// </summary>
		public static List<(int Band, List<Vector2> Line)> BenchLines(Plan plan, float phase, float inside)
		{
			var lines = new List<(int, List<Vector2>)>();
			if (plan == null || !plan.Terraced)
			{
				return lines;
			}
			plan.Bands(out int low, out int high);
			Rect area = plan.Area();
			for (int band = low; band <= high; band++)
			{
				float level = plan.Base + plan.Rise * (band + phase);
				foreach (List<Vector2> contour in PointOfInterestLinearFeatures.Contours((x, z) => plan.SmoothAt(x, z) - level, area, LineCellMetres))
				{
					var run = new List<Vector2>();
					void Flush()
					{
						if (run.Count >= 2 && PointOfInterestLinearFeatures.Length(run) >= 2f)
						{
							lines.Add((band, run));
						}
						run = new List<Vector2>();
					}
					foreach (Vector2 p in contour)
					{
						if (plan.Inside(p.x, p.y) >= inside)
						{
							run.Add(p);
						}
						else
						{
							Flush();
						}
					}
					Flush();
				}
			}
			return lines;
		}

		/// <summary>The middle of every step of a field (its walls' line), with the bench below it (pure).</summary>
		public static List<(int Band, List<Vector2> Line)> RiserLines(Plan plan) => BenchLines(plan, 1f - 0.5f * (plan != null ? plan.Share : 0f), WallInsideMetres);

		/// <summary>The lip of every bench (where an earth bund's or grass bank's own style is laid), with the bench below it (pure).</summary>
		public static List<(int Band, List<Vector2> Line)> LipLines(Plan plan) => BenchLines(plan, 1f - 1e-3f, WallInsideMetres);

		/// <summary>The middle of every bench (an orchard's rows), <paramref name="inside"/> metres in from the field's edge (pure).</summary>
		public static List<(int Band, List<Vector2> Line)> TreadLines(Plan plan, float inside) => BenchLines(plan, 0.5f * (1f - (plan != null ? plan.Share : 0f)), inside);

		// ── Terraced paddies' water ───────────────────────────────────

		/// <summary>
		/// A terraced paddy's water (pure): every <see cref="WaterCellMetres"/> cell, on a lattice fixed to the scene's metres, whose middle
		/// lies on a bench's floor at least half a metre in from its bund (<see cref="BiomeTerraces.PaddyInner"/>), one lake a connected
		/// stretch of one bench's cells at that bench's level. Ids by <paramref name="id"/> (piece number); at most 255 lakes.
		/// </summary>
		public static List<SceneHydrology.Lake> TreadLakes(Plan plan, Func<int, int> id)
		{
			var lakes = new List<SceneHydrology.Lake>();
			if (plan == null || !plan.Terraced || !plan.Paddy || id == null)
			{
				return lakes;
			}
			float cell = WaterCellMetres;
			Rect area = plan.Area();
			float x0 = Mathf.Floor(area.xMin / cell) * cell, z0 = Mathf.Floor(area.yMin / cell) * cell;
			int w = Mathf.CeilToInt((area.xMax - x0) / cell), h = Mathf.CeilToInt((area.yMax - z0) / cell);
			var band = new int[w * h];
			for (int j = 0; j < h; j++)
			{
				for (int i = 0; i < w; i++)
				{
					float x = x0 + (i + 0.5f) * cell, z = z0 + (j + 0.5f) * cell;
					band[j * w + i] = int.MinValue;
					float inside = plan.Inside(x, z);
					if (inside <= 0f)
					{
						continue;
					}
					float u = plan.U(x, z);
					int k = BiomeTerraces.BandOf(u);
					float t = u - k;
					if (t <= 1f - plan.Share && BiomeTerraces.PaddyInner(t, plan.Share, plan.PeriodAt(x, z), inside) >= 0.5f)
					{
						band[j * w + i] = k;
					}
				}
			}
			// Connected stretches, in lattice order (the same ground gives the same lakes).
			var seen = new bool[w * h];
			var stack = new Stack<int>();
			for (int start = 0; start < band.Length && lakes.Count < 255; start++)
			{
				if (seen[start] || band[start] == int.MinValue)
				{
					continue;
				}
				int k = band[start];
				var cells = new List<int>();
				stack.Push(start);
				seen[start] = true;
				while (stack.Count > 0)
				{
					int c = stack.Pop();
					cells.Add(c);
					int ci = c % w, cj = c / w;
					foreach ((int di, int dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
					{
						int ni = ci + di, nj = cj + dj;
						if (ni < 0 || nj < 0 || ni >= w || nj >= h)
						{
							continue;
						}
						int n = nj * w + ni;
						if (!seen[n] && band[n] == k)
						{
							seen[n] = true;
							stack.Push(n);
						}
					}
				}
				cells.Sort();
				int minI = int.MaxValue, minJ = int.MaxValue, maxI = int.MinValue, maxJ = int.MinValue;
				foreach (int c in cells)
				{
					minI = Math.Min(minI, c % w);
					maxI = Math.Max(maxI, c % w);
					minJ = Math.Min(minJ, c / w);
					maxJ = Math.Max(maxJ, c / w);
				}
				int lw = maxI - minI + 1, lh = maxJ - minJ + 1;
				var bits = new byte[(lw * lh + 7) / 8];
				foreach (int c in cells)
				{
					int bit = (c / w - minJ) * lw + (c % w - minI);
					bits[bit >> 3] |= (byte)(1 << (bit & 7));
				}
				float ox = x0 + minI * cell, oz = z0 + minJ * cell;
				int first = cells[0];
				float level = plan.Level(k);
				lakes.Add(new SceneHydrology.Lake
				{
					Id = id(lakes.Count),
					PlanetLake = -1,
					Level = level,
					SpillLevel = level,
					Seeds = new[] { new Vector2(x0 + (first % w + 0.5f) * cell, z0 + (first / w + 0.5f) * cell) },
					Bounds = new Rect(ox, oz, lw * cell, lh * cell),
					Mask = bits,
					MaskOrigin = new Vector2(ox, oz),
					MaskCell = cell,
					MaskWidth = lw,
					MaskHeight = lh,
				});
			}
			return lakes;
		}

		// ── The scene ─────────────────────────────────────────────────

		/// <summary>
		/// The wall style a biome faces its drystone steps with: its first linear style along terrace edges that is a wall (a named stone
		/// such as Nozura in its own material), else plain drystone in its structure palette.
		/// </summary>
		public static BiomeLinearStyle WallStyle(BiomeTemplate biome)
		{
			IReadOnlyList<BiomeLinearStyle> styles = biome != null ? BiomeTemplateArt.LinearFeatures(biome) : null;
			if (styles != null)
			{
				foreach (BiomeLinearStyle style in styles)
				{
					if (style.Where == BiomeLinearPlace.TerraceEdges && style.Kind == BiomeLinearKind.Wall && BiomeLinearFeatures.IsLaid(style))
					{
						return style;
					}
				}
			}
			return new BiomeLinearStyle(BiomeLinearForm.Drystone, BiomeLinearPlace.TerraceEdges);
		}

		/// <summary>The style a biome lays along its earth-bund and grass-bank lips (its first terrace-edge style that is no wall nor channel: a tea hedge, a row of trees), or none.</summary>
		public static bool LipStyle(BiomeTemplate biome, out BiomeLinearStyle lip)
		{
			lip = default;
			IReadOnlyList<BiomeLinearStyle> styles = biome != null ? BiomeTemplateArt.LinearFeatures(biome) : null;
			if (styles == null)
			{
				return false;
			}
			foreach (BiomeLinearStyle style in styles)
			{
				if (style.Where == BiomeLinearPlace.TerraceEdges && style.Kind != BiomeLinearKind.Wall && style.Kind != BiomeLinearKind.Channel && BiomeLinearFeatures.IsLaid(style))
				{
					lip = style;
					return true;
				}
			}
			return false;
		}

		/// <summary>Carves a terraced field's benches into every tile it crosses (recorded for the restore); returns the samples moved.</summary>
		public static int Carve(PointOfInterestSiteContext context, Plan plan)
		{
			if (plan == null || !plan.Terraced)
			{
				return 0;
			}
			return PointOfInterestFieldCarve.CarveTo(context, plan.Area(), plan.Offset);
		}

		/// <summary>
		/// Faces a terraced field's steps: a drystone riser's wall along the middle of each step, seated on the bench below and standing a
		/// little over the bench above, in the biome's wall style (tumbled by <paramref name="breaks"/>); an earth bund's or grass bank's
		/// lip lined with the biome's own lip style where it names one. Seeded by <paramref name="seed"/>. Returns the props laid.
		/// </summary>
		public static int FaceSteps(PointOfInterestSiteContext context, BiomeTemplate biome, Plan plan, int seed, float breaks = 0f)
		{
			if (plan == null || !plan.Terraced)
			{
				return 0;
			}
			var own = (plan.Centre, plan.Yaw, plan.Size);
			int laid = 0, line = 0;
			if (plan.Riser == BiomeTerraceRiser.Drystone)
			{
				BiomeLinearStyle wall = WallStyle(biome);
				wall.Height = plan.Rise + 0.2f;
				wall.GateWidth = 0f;
				wall.Breaks = Mathf.Max(wall.Breaks, breaks);
				foreach ((int band, List<Vector2> riser) in RiserLines(plan))
				{
					float below = plan.Level(band) - 0.05f;
					laid += PointOfInterestLinearFeatures.LayAlong(context, biome, wall, riser, unchecked(seed + 7919 * ++line), own,
						p => Mathf.Min(below, context.GroundAt != null ? context.GroundAt(p.x, p.y) : below));
				}
				return laid;
			}
			if (LipStyle(biome, out BiomeLinearStyle lip))
			{
				lip.Breaks = Mathf.Max(lip.Breaks, breaks);
				foreach ((int _, List<Vector2> edge) in LipLines(plan))
				{
					laid += PointOfInterestLinearFeatures.LayAlong(context, biome, lip, edge, unchecked(seed + 7919 * ++line), own);
				}
			}
			return laid;
		}
	}
}
#endif
