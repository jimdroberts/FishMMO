using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A site's footprint seen from above (2026-10-18: "Towns, cities and all other points of interest need different carving
	/// besides just circles"): how far its ground reaches from its anchor toward each of a ring of evenly spaced bearings, the
	/// first due north (+z), turning east (+x) as a yaw does. Between two bearings its edge runs straight, so the footprint is a
	/// star-shaped polygon: every point of it is seen from the anchor, and a ray from the anchor leaves it exactly once.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One footprint, read by everything.</b> The pad's levelling, the keep-outs, the ways' owner marks and carve, the
	/// layout's boundary, the walls, the moat, the fields round a town and its region all read the same outline. The site's
	/// radius stays its bounding reach (no point of the outline is farther), so spacing and culling by radius still hold.
	/// </para>
	/// <para>
	/// <b>An empty outline is a circle</b> of the radius it is read with: every site planned before outlines (and every kind
	/// that keeps a disc, a bridge or a mill on its bank) reads exactly as it did.
	/// </para>
	/// </remarks>
	public static class PointOfInterestOutline
	{
		/// <summary>How many bearings a generated outline is drawn at: 11.25° apart.</summary>
		public const int Bearings = 32;

		/// <summary>True when an outline is drawn (else it is a circle).</summary>
		public static bool Drawn(float[] outline) => outline != null && outline.Length >= 3;

		/// <summary>The bearing (radians, 0 … 2π, 0 north, π/2 east) of an offset.</summary>
		public static float BearingOf(float dx, float dz)
		{
			float a = Mathf.Atan2(dx, dz);
			return a < 0f ? a + 2f * Mathf.PI : a;
		}

		/// <summary>The unit direction (x east, y north) of bearing <paramref name="k"/> of <paramref name="count"/>.</summary>
		public static Vector2 Direction(int k, int count)
		{
			float a = k * (2f * Mathf.PI / count);
			return new Vector2(Mathf.Sin(a), Mathf.Cos(a));
		}

		/// <summary>
		/// How far the footprint reaches toward an offset (dx, dz) from its anchor, metres: where the ray that way crosses the
		/// outline's edge between its two nearest bearings; <paramref name="radius"/> for a circle.
		/// </summary>
		public static float Reach(float[] outline, float radius, float dx, float dz)
		{
			if (!Drawn(outline))
			{
				return radius;
			}
			return ReachAt(outline, BearingOf(dx, dz));
		}

		/// <summary>How far a drawn outline reaches toward a bearing (radians), metres.</summary>
		public static float ReachAt(float[] outline, float bearing)
		{
			int n = outline.Length;
			float step = 2f * Mathf.PI / n;
			float f = Mathf.Repeat(bearing, 2f * Mathf.PI) / step;
			int k = Mathf.FloorToInt(f) % n;
			int k1 = (k + 1) % n;
			float ra = Mathf.Max(0.01f, outline[k]), rb = Mathf.Max(0.01f, outline[k1]);
			float a0 = k * step, a1 = (k + 1) * step;
			// The ray along u meets the edge A→B where t·u = A + s·(B − A): t = (A × B) / (u × (B − A)).
			float ax = ra * Mathf.Sin(a0), az = ra * Mathf.Cos(a0), bx = rb * Mathf.Sin(a1), bz = rb * Mathf.Cos(a1);
			float ux = Mathf.Sin(bearing), uz = Mathf.Cos(bearing);
			float ex = bx - ax, ez = bz - az;
			float den = ux * ez - uz * ex;
			if (Mathf.Abs(den) < 1e-6f)
			{
				return Mathf.Lerp(ra, rb, f - Mathf.Floor(f));
			}
			return Mathf.Max(0f, (ax * bz - az * bx) / den);
		}

		/// <summary>How far the footprint reaches toward a bearing in degrees (as a yaw: 0 north, 90 east), metres.</summary>
		public static float ReachToward(float[] outline, float radius, float bearingDegrees)
			=> Drawn(outline) ? ReachAt(outline, bearingDegrees * Mathf.Deg2Rad) : radius;

		/// <summary>Whether an offset (dx, dz) from the anchor lies within the footprint grown by <paramref name="margin"/> metres along its ray.</summary>
		public static bool Contains(float[] outline, float radius, float dx, float dz, float margin = 0f)
		{
			float d2 = dx * dx + dz * dz;
			float most = Max(outline, radius) + margin;
			if (d2 > most * most)
			{
				return false;
			}
			float reach = Reach(outline, radius, dx, dz) + margin;
			return d2 <= reach * reach;
		}

		/// <summary>How far past the footprint's edge an offset lies along its ray from the anchor, metres (negative inside).</summary>
		public static float Beyond(float[] outline, float radius, float dx, float dz)
			=> Mathf.Sqrt(dx * dx + dz * dz) - Reach(outline, radius, dx, dz);

		/// <summary>The outline's farthest reach, metres: its bounding radius; <paramref name="radius"/> for a circle.</summary>
		public static float Max(float[] outline, float radius)
		{
			if (!Drawn(outline))
			{
				return radius;
			}
			float most = 0f;
			foreach (float r in outline)
			{
				most = Mathf.Max(most, r);
			}
			return most;
		}

		/// <summary>The outline's nearest reach, metres; <paramref name="radius"/> for a circle.</summary>
		public static float Min(float[] outline, float radius)
		{
			if (!Drawn(outline))
			{
				return radius;
			}
			float least = float.PositiveInfinity;
			foreach (float r in outline)
			{
				least = Mathf.Min(least, r);
			}
			return least;
		}

		/// <summary>A copy of an outline grown (or, negative, shrunk) by <paramref name="metres"/> along every bearing; empty stays empty.</summary>
		public static float[] Grown(float[] outline, float metres)
		{
			if (!Drawn(outline))
			{
				return System.Array.Empty<float>();
			}
			var grown = new float[outline.Length];
			for (int k = 0; k < outline.Length; k++)
			{
				grown[k] = Mathf.Max(0.01f, outline[k] + metres);
			}
			return grown;
		}

		/// <summary>The area inside an outline, m² (the polygon's): π r² for a circle.</summary>
		public static float Area(float[] outline, float radius)
		{
			if (!Drawn(outline))
			{
				return Mathf.PI * radius * radius;
			}
			int n = outline.Length;
			float sum = 0f, s = Mathf.Sin(2f * Mathf.PI / n);
			for (int k = 0; k < n; k++)
			{
				sum += outline[k] * outline[(k + 1) % n];
			}
			return 0.5f * s * sum;
		}
	}

	/// <summary>
	/// A pad's terraces (2026-10-18: hill towns): its ground levelled not to one height but to two to four, stepped up the
	/// fall line like a hillside's fields, with an earth bank between each pair and a ramp up every bank where a street climbs.
	/// </summary>
	/// <remarks>
	/// <para>
	/// One flat pad on a hillside cut its uphill half into the hill and stood its downhill half on fill, both as deep as the
	/// hill fell across it, and banked out a hundred metres to meet the slope. Terraced, each level is cut and filled only by
	/// the fall across its own band, and the town climbs the hill as old hill towns do.
	/// </para>
	/// <para>
	/// The levels are bands across <see cref="PointOfInterestPad.TerraceAxis"/> (up the hill), split at
	/// <see cref="PointOfInterestPad.TerraceSteps"/> (metres along it from the pad's centre). Each step's bank rises at
	/// <see cref="BankGrade"/> (42°, just under what is walked), except across <see cref="PointOfInterestPad.RampHalfWidth"/>
	/// either side of its ramp (<see cref="PointOfInterestPad.TerraceRamps"/>, metres across the axis), where it is graded at
	/// <see cref="RampGrade"/> for a cart, its sides cut at the bank's grade.
	/// </para>
	/// </remarks>
	public static class PointOfInterestTerraces
	{
		/// <summary>The grade of a bank between two levels (rise over run): about 42°.</summary>
		public const float BankGrade = 0.9f;

		/// <summary>The grade a ramp climbs a bank at: about 11°, a street's.</summary>
		public const float RampGrade = 0.2f;

		/// <summary>True when the pad is terraced.</summary>
		public static bool Terraced(in PointOfInterestPad pad)
			=> pad.TerraceLevels != null && pad.TerraceSteps != null && pad.TerraceLevels.Length >= 2 && pad.TerraceSteps.Length == pad.TerraceLevels.Length - 1;

		/// <summary>The across-axis unit vector (x east, y north): the axis turned a right angle clockwise.</summary>
		public static Vector2 Across(Vector2 axis) => new Vector2(axis.y, -axis.x);

		/// <summary>Half the run of step <paramref name="i"/>'s bank along the axis, metres.</summary>
		public static float BankHalf(in PointOfInterestPad pad, int i)
			=> Mathf.Abs(pad.TerraceLevels[i + 1] - pad.TerraceLevels[i]) * 0.5f / BankGrade;

		/// <summary>Half the run of step <paramref name="i"/>'s ramp along the axis, metres.</summary>
		public static float RampHalf(in PointOfInterestPad pad, int i)
			=> Mathf.Abs(pad.TerraceLevels[i + 1] - pad.TerraceLevels[i]) * 0.5f / RampGrade;

		/// <summary>The terrace (0 lowest) a scene position's band is, by where it lies along the axis.</summary>
		public static int BandAt(in PointOfInterestPad pad, float x, float z)
		{
			if (!Terraced(pad))
			{
				return 0;
			}
			float u = (x - pad.Centre.x) * pad.TerraceAxis.x + (z - pad.Centre.z) * pad.TerraceAxis.y;
			int band = 0;
			while (band < pad.TerraceSteps.Length && u > pad.TerraceSteps[band])
			{
				band++;
			}
			return band;
		}

		/// <summary>
		/// The pad's levelled ground at a scene position: its height, or on a terraced pad its band's level, the bank's slope
		/// across a step and the ramp's grade where the ramp climbs it.
		/// </summary>
		public static float LevelAt(in PointOfInterestPad pad, float x, float z)
		{
			if (!Terraced(pad))
			{
				return pad.Centre.y;
			}
			float dx = x - pad.Centre.x, dz = z - pad.Centre.z;
			float u = dx * pad.TerraceAxis.x + dz * pad.TerraceAxis.y;
			Vector2 across = Across(pad.TerraceAxis);
			float v = dx * across.x + dz * across.y;
			// The nearest step decides: the bands are laid wider than any ramp, so no point feels two.
			int i = 0;
			float nearest = float.PositiveInfinity;
			for (int s = 0; s < pad.TerraceSteps.Length; s++)
			{
				float d = Mathf.Abs(u - pad.TerraceSteps[s]);
				if (d < nearest)
				{
					nearest = d;
					i = s;
				}
			}
			float below = pad.TerraceLevels[i], above = pad.TerraceLevels[i + 1], rise = above - below;
			float step = pad.TerraceSteps[i];
			float bankHalf = Mathf.Max(0.01f, BankHalf(pad, i)), rampHalf = Mathf.Max(0.01f, RampHalf(pad, i));
			float banked = below + rise * Mathf.Clamp01((u - step + bankHalf) / (2f * bankHalf));
			float ramp = pad.TerraceRamps != null && i < pad.TerraceRamps.Length ? pad.TerraceRamps[i] : 0f;
			float off = Mathf.Max(0f, Mathf.Abs(v - ramp) - Mathf.Max(0f, pad.RampHalfWidth));
			if (pad.RampHalfWidth <= 0f)
			{
				return banked;
			}
			float graded = below + rise * Mathf.Clamp01((u - step + rampHalf) / (2f * rampHalf));
			// The ramp's own width at its grade; out from it, its sides cut (or built up) at the bank's grade into the bank.
			return Mathf.Clamp(banked, graded - off * BankGrade, graded + off * BankGrade);
		}

		/// <summary>
		/// Whether a scene position is on a bank or a ramp (not on a level), with <paramref name="margin"/> metres more along
		/// the axis either way: where nothing is built.
		/// </summary>
		public static bool OnSlope(in PointOfInterestPad pad, float x, float z, float margin)
		{
			if (!Terraced(pad))
			{
				return false;
			}
			float dx = x - pad.Centre.x, dz = z - pad.Centre.z;
			float u = dx * pad.TerraceAxis.x + dz * pad.TerraceAxis.y;
			Vector2 across = Across(pad.TerraceAxis);
			float v = dx * across.x + dz * across.y;
			for (int s = 0; s < pad.TerraceSteps.Length; s++)
			{
				float ramp = pad.TerraceRamps != null && s < pad.TerraceRamps.Length ? pad.TerraceRamps[s] : 0f;
				bool onRamp = pad.RampHalfWidth > 0f && Mathf.Abs(v - ramp) <= pad.RampHalfWidth + BankHalf(pad, s) * 2f;
				float half = onRamp ? RampHalf(pad, s) : BankHalf(pad, s);
				if (Mathf.Abs(u - pad.TerraceSteps[s]) <= half + margin)
				{
					return true;
				}
			}
			return false;
		}
	}
}
