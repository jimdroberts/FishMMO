#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The bare rock a waterfall wears round itself, for the splat painter: its brink, scoured clean where the water goes
	/// over, and the steep ground beside and below the curtain, kept wet by its spray.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why.</b> The painter turns ground to rock by slope alone, ramping from a biome's cliff angle (about 42°) to 90°, so
	/// a fall's 50–74° walls (<see cref="RiverSettings.FallWallDegrees"/>) came out two thirds grass, and the rounded
	/// brink (<see cref="FallBasin.UnderBrink"/>) grass outright: grass painted down the drop beside the curtain and over
	/// the lip it pours from (2026-10-13). The ground's grass grows from the grass layer's weight, so rock here takes the
	/// grass with it.
	/// </para>
	/// <para>Pure arithmetic: no Unity object is touched, so the painter's worker threads may ask it.</para>
	/// </remarks>
	public sealed class FallRockPaint
	{
		/// <summary>How far upstream of the lip the brink's rock starts, metres: the last of the run, worn smooth.</summary>
		public const float BrinkUpstreamMetres = 1.5f;
		/// <summary>How far out past the water's edge the brink's rock reaches, and fades over, metres.</summary>
		public const float BrinkSideMetres = 1.5f;
		public const float FadeMetres = 1.5f;
		/// <summary>
		/// The slopes over which the ground beside and below the curtain turns to rock, degrees: from a little under a cut
		/// bank's (<see cref="RiverSettings.BankDegrees"/>), so the bank at the face's foot stays ground.
		/// </summary>
		public const float FaceFromDegrees = 40f;
		public const float FaceFullDegrees = 50f;
		/// <summary>How far out past the water's edge, beyond the notch's own blend, the face's rock reaches, metres.</summary>
		public const float FaceSideMetres = 3f;

		private readonly List<Fall> falls = new List<Fall>();

		private struct Fall
		{
			public float LipX, LipZ, DirX, DirZ;
			public float HalfLip, NotchBlend, FootAlong, BrinkReach;
			public float MinX, MaxX, MinZ, MaxZ;
		}

		/// <summary>Every fall basin of the scene's perennial rivers (<see cref="FallBasin.Along"/>).</summary>
		public FallRockPaint(IReadOnlyList<RiverPath> rivers, RiverSettings settings, float spacing)
		{
			if (rivers == null)
			{
				return;
			}
			settings ??= new RiverSettings();
			foreach (RiverPath river in rivers)
			{
				FallBasin[] basins = FallBasin.Along(river, settings, spacing);
				if (basins == null)
				{
					continue;
				}
				for (int v = 0; v < basins.Length; v++)
				{
					FallBasin basin = basins[v];
					if (basin == null || basin.Lip != v)
					{
						continue;
					}
					float footAlong = river.S[basin.Foot] - basin.AlongAtLip;
					float side = basin.HalfLip + Math.Max(0.5f, settings.FallNotchBlendMetres) + FaceSideMetres + FadeMetres;
					float ahead = footAlong + FadeMetres;
					float behind = BrinkUpstreamMetres + FadeMetres;
					// The box round the brink and the face: lip line from behind to ahead, out to the side either way.
					float sideX = -basin.DirZ, sideZ = basin.DirX;
					float x0 = float.PositiveInfinity, x1 = float.NegativeInfinity, z0 = float.PositiveInfinity, z1 = float.NegativeInfinity;
					foreach (float a in new[] { -behind, ahead })
					{
						foreach (float s in new[] { -side, side })
						{
							float x = basin.LipX + basin.DirX * a + sideX * s, z = basin.LipZ + basin.DirZ * a + sideZ * s;
							x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); z0 = Math.Min(z0, z); z1 = Math.Max(z1, z);
						}
					}
					falls.Add(new Fall
					{
						LipX = basin.LipX, LipZ = basin.LipZ, DirX = basin.DirX, DirZ = basin.DirZ,
						HalfLip = basin.HalfLip, NotchBlend = Math.Max(0.5f, settings.FallNotchBlendMetres), FootAlong = footAlong,
						BrinkReach = Math.Min(basin.BrinkReach, footAlong),
						MinX = x0, MaxX = x1, MinZ = z0, MaxZ = z1,
					});
				}
			}
		}

		/// <summary>How many falls it paints.</summary>
		public int Count => falls.Count;

		/// <summary>
		/// The least share of rock at scene (<paramref name="east"/>, <paramref name="north"/>), whose ground slopes
		/// <paramref name="slopeDegrees"/>: 1 on a brink and a steep face by a curtain, 0 away from every fall.
		/// </summary>
		public float RockAt(float east, float north, float slopeDegrees)
		{
			float best = 0f;
			for (int i = 0; i < falls.Count; i++)
			{
				Fall f = falls[i];
				if (east < f.MinX || east > f.MaxX || north < f.MinZ || north > f.MaxZ)
				{
					continue;
				}
				float dx = east - f.LipX, dz = north - f.LipZ;
				float along = dx * f.DirX + dz * f.DirZ;
				float across = Math.Abs(dz * f.DirX - dx * f.DirZ);
				// The brink: the lip's last metres and its rounding over, the width of the water and a little more.
				float brink = Ramp(along + BrinkUpstreamMetres + FadeMetres, FadeMetres)
					* Ramp(f.HalfLip + BrinkSideMetres + FadeMetres - across, FadeMetres)
					* Ramp(f.BrinkReach + FadeMetres - along, FadeMetres);
				// The face: steep ground past the brink beside and below the curtain, out across the notch's blend.
				float face = Ramp(along + FadeMetres, FadeMetres)
					* Ramp(f.FootAlong + FadeMetres - along, FadeMetres)
					* Ramp(f.HalfLip + f.NotchBlend + FaceSideMetres + FadeMetres - across, FadeMetres)
					* Ramp(slopeDegrees - FaceFromDegrees, FaceFullDegrees - FaceFromDegrees);
				best = Math.Max(best, Math.Max(brink, face));
				if (best >= 1f)
				{
					return 1f;
				}
			}
			return best;
		}

		/// <summary>0 at or under 0, easing to 1 by <paramref name="over"/>.</summary>
		private static float Ramp(float value, float over)
		{
			float t = value / Math.Max(1e-3f, over);
			t = t < 0f ? 0f : t > 1f ? 1f : t;
			return t * t * (3f - 2f * t);
		}
	}
}
#endif
