using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// How a crop's field is laid out on the ground (<see cref="BiomeFieldCrop.Surface"/>, 2026-10-16, phase I2e). Values are
	/// stored by number: append only.
	/// </summary>
	public enum BiomeFieldSurface : byte
	{
		/// <summary>Sown edge to edge on the level, as every field was until 2026-10-16.</summary>
		Flat = 0,
		/// <summary>
		/// Lazy beds: narrow raised ridges about 1.2 m wide, dug up from the turf and heaped with the spoil of the trenches
		/// between them (0.6 m), each ridge sown and the trenches left bare to drain: how wet, thin-soiled ground (bog,
		/// moorland, the western isles) was cropped with oats, kale and potatoes.
		/// </summary>
		LazyBed = 1,
		/// <summary>
		/// Ridge and furrow: the open field's strips, broad low ridges about 6 m across ploughed up toward their crowns, with a
		/// metre-wide furrow between each, sown on the ridges: the medieval common field of the lowlands.
		/// </summary>
		RidgeAndFurrow = 2,
	}

	/// <summary>
	/// The ridges of a field's surface (pure): where across the sown band each ridge and furrow lies, how high the ground
	/// stands there, and how much of a stretch of it is sown. The band is the field's sown width (inside its headland) across
	/// the ridges, which run the length of the field; it holds a whole number of ridges, each with half a furrow either side,
	/// the nominal width stretched to fit, so a field's two edges lie in furrows against its headland.
	/// </summary>
	/// <remarks>
	/// The cut lays a field's ridges as rows of its crop with the furrows bare between them (PointOfInterestFields); the height
	/// is the profile's, for anything that shapes or shades the ground by it.
	/// </remarks>
	public static class BiomeFieldSurfaces
	{
		/// <summary>One surface's nominal rows: a ridge's width, a furrow's, and how high a ridge's crown stands over its furrows (metres).</summary>
		public readonly struct Rows
		{
			public readonly float Ridge;
			public readonly float Furrow;
			public readonly float Height;

			public Rows(float ridge, float furrow, float height)
			{
				Ridge = ridge;
				Furrow = furrow;
				Height = height;
			}

			/// <summary>A ridge and a furrow, metres; 0 for a flat field.</summary>
			public float Period => Ridge + Furrow;
		}

		/// <summary>The nominal rows of a surface; all zero for <see cref="BiomeFieldSurface.Flat"/> (and an unknown value).</summary>
		public static Rows Nominal(BiomeFieldSurface surface)
		{
			switch (surface)
			{
				case BiomeFieldSurface.LazyBed: return new Rows(1.2f, 0.6f, 0.35f);
				case BiomeFieldSurface.RidgeAndFurrow: return new Rows(6f, 1f, 0.5f);
				default: return default;
			}
		}

		/// <summary>How far a lazy bed's shoulders round off into its trenches, metres.</summary>
		public const float LazyBedShoulder = 0.2f;

		/// <summary>True for a surface laid in ridges.</summary>
		public static bool Ridged(BiomeFieldSurface surface) => Nominal(surface).Period > 0f;

		/// <summary>How many ridges a band <paramref name="band"/> metres across holds (at least one); 0 for a flat field.</summary>
		public static int RidgeCount(BiomeFieldSurface surface, float band)
		{
			float period = Nominal(surface).Period;
			if (!(period > 0f) || !(band > 0f))
			{
				return 0;
			}
			return Mathf.Max(1, Mathf.RoundToInt(band / period));
		}

		/// <summary>A ridge and a furrow as laid across a band (the nominal period stretched to a whole number of ridges), metres; 0 for a flat field.</summary>
		public static float Period(BiomeFieldSurface surface, float band)
		{
			int count = RidgeCount(surface, band);
			return count > 0 ? band / count : 0f;
		}

		/// <summary>How far <paramref name="x"/> (metres across the band from its edge) lies from the centre of its ridge, metres, and that ridge's half width as laid.</summary>
		private static float FromCrown(BiomeFieldSurface surface, float band, float x, out float halfRidge)
		{
			Rows rows = Nominal(surface);
			float period = Period(surface, band);
			halfRidge = 0.5f * period * rows.Ridge / rows.Period;
			float t = x / period;
			t -= Mathf.Floor(t);
			return Mathf.Abs(t - 0.5f) * period;
		}

		/// <summary>True where <paramref name="x"/> metres across the band lies on a ridge (always, on a flat field; never outside the band).</summary>
		public static bool OnRidge(BiomeFieldSurface surface, float band, float x)
		{
			if (!Ridged(surface))
			{
				return true;
			}
			if (x < 0f || x > band || !(band > 0f))
			{
				return false;
			}
			return FromCrown(surface, band, x, out float halfRidge) < halfRidge;
		}

		/// <summary>
		/// How high the ground stands at <paramref name="x"/> metres across the band over the bottom of its furrows, metres: 0
		/// in a furrow (and everywhere on a flat field); a lazy bed's flat top with its shoulders rounded off over
		/// <see cref="LazyBedShoulder"/>; ridge and furrow's broad crown, highest on the ridge's middle line.
		/// </summary>
		public static float Height(BiomeFieldSurface surface, float band, float x)
		{
			if (!Ridged(surface) || x < 0f || x > band || !(band > 0f))
			{
				return 0f;
			}
			float d = FromCrown(surface, band, x, out float halfRidge);
			if (d >= halfRidge)
			{
				return 0f;
			}
			float height = Nominal(surface).Height;
			if (surface == BiomeFieldSurface.LazyBed)
			{
				float t = Mathf.Clamp01((halfRidge - d) / LazyBedShoulder);
				return height * t * t * (3f - 2f * t);
			}
			return height * Mathf.Cos(0.5f * Mathf.PI * d / halfRidge);
		}

		/// <summary>How much of the stretch from <paramref name="x0"/> to <paramref name="x1"/> across the band is sown (on a ridge), 0 … 1 (1 on a flat field).</summary>
		public static float SownShare(BiomeFieldSurface surface, float band, float x0, float x1)
		{
			if (!Ridged(surface))
			{
				return 1f;
			}
			const int samples = 8;
			int sown = 0;
			for (int i = 0; i < samples; i++)
			{
				float x = Mathf.Lerp(x0, x1, (i + 0.5f) / samples);
				sown += OnRidge(surface, band, x) ? 1 : 0;
			}
			return sown / (float)samples;
		}

		/// <summary>
		/// The furrows between a band's ridges: each one's centre, metres across the band from its edge (the half furrows at
		/// the band's two edges, against the headland, are not listed), and through <paramref name="width"/> how wide each is
		/// as laid. None for a flat field or a band of one ridge.
		/// </summary>
		public static List<float> Furrows(BiomeFieldSurface surface, float band, out float width)
		{
			var centres = new List<float>();
			width = 0f;
			int count = RidgeCount(surface, band);
			if (count <= 0)
			{
				return centres;
			}
			Rows rows = Nominal(surface);
			float period = band / count;
			width = period * rows.Furrow / rows.Period;
			for (int k = 1; k < count; k++)
			{
				centres.Add(k * period);
			}
			return centres;
		}
	}
}
