using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// How the steps of a terraced field are faced (<see cref="BiomeFieldCrop.Riser"/>, 2026-10-18, phase I2j-b, decisions 29 and 31).
	/// Stored by number: append only.
	/// </summary>
	public enum BiomeTerraceRiser : byte
	{
		/// <summary>Not terraced (a <see cref="BiomeFieldSurface.Terraced"/> field with none is faced in drystone).</summary>
		None = 0,
		/// <summary>
		/// Drystone: a near-upright wall of field stone faces each step (the Douro's socalcos, the Cévennes' faïsses), laid along it in
		/// the biome's terrace wall style (its first linear style along terrace edges that is a wall, e.g. a named stone such as Nozura),
		/// else plain drystone in its structure palette.
		/// </summary>
		Drystone = 1,
		/// <summary>
		/// Earth bund: a steep earthen step with a low grassed bund along the lip of the bench above it (the Ethiopian highlands' teff
		/// terraces, the rice terraces' bunds). No stone; the bank and the lip keep their turf.
		/// </summary>
		EarthBund = 2,
		/// <summary>A grass bank: a wide turfed slope between benches with no stone at all (the lynchets of a valley's rye terraces).</summary>
		GrassBank = 3,
	}

	/// <summary>
	/// The benches of a terraced field (pure; 2026-10-18, phase I2j-b, decision 29): how high a step stands on a slope, how wide a
	/// bench is, the cut-and-fill profile that turns the hillside into level benches, and where on a bench the crop is sown.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The bench coordinate.</b> A terraced field's ground is smoothed (so its benches follow the hill's broad contours rather than
	/// every hummock) and divided by the step's height: <c>u = (smoothed ground − base) ÷ rise</c>. Each whole number of <c>u</c> is one
	/// bench (its <i>band</i>); the fraction <c>t</c> across it runs from the bench's lip (0, its downhill edge) to the foot of the step
	/// up to the next (1 − <see cref="Share"/>), then up that step (to 1). The benches follow the contours wherever the hill turns.
	/// </para>
	/// <para>
	/// <b>Cut and fill.</b> <see cref="Bench"/> stands each bench level half a step's tread under its back and half over its lip, so as
	/// much ground is dug at the back as is banked at the front: the classic bench terrace. The step itself rises smoothly over its
	/// share of the period (a heightmap two metres apart cannot hold an upright face; a drystone riser's wall stands on it).
	/// </para>
	/// </remarks>
	public static class BiomeTerraces
	{
		/// <summary>The gentlest mean slope a terraced field is stepped on, degrees: gentler, it is sown flat.</summary>
		public const float MinSlopeDegrees = 3f;
		/// <summary>The steepest ground any one sample of a terraced field may stand on, degrees (a flat field's is PointOfInterestFields.MaxSlopeDegrees, 7).</summary>
		public const float MaxSlopeDegrees = 32f;
		/// <summary>The steepest mean slope a terraced field is laid on, degrees.</summary>
		public const float MaxMeanSlopeDegrees = 25f;
		/// <summary>The bench a step is cut to where its riser's heights allow, metres.</summary>
		public const float TreadMetres = 6.5f;
		/// <summary>How far in from a terraced field's edge its benches die back into the hillside, metres.</summary>
		public const float EdgeFadeMetres = 4f;
		/// <summary>How far apart the ground is read for the smoothed ground the benches follow, metres, and the smoothing's reach in those cells (each of two box passes).</summary>
		public const float SmoothCellMetres = 2f;
		public const int SmoothRadiusCells = 3;
		/// <summary>An earth bund's lip: how high it stands over the bench, metres, and how far back from the lip it reaches.</summary>
		public const float LipRise = 0.25f, LipMetres = 1f;

		/// <summary>
		/// The riser a crop's fields are terraced with (pure): a <see cref="BiomeFieldSurface.Terraced"/> field's own, drystone when it
		/// names none; a flat field or a paddy with a riser are terraced with it (terraced paddies); every other surface is never
		/// terraced (<see cref="BiomeTerraceRiser.None"/>).
		/// </summary>
		public static BiomeTerraceRiser RiserOf(BiomeFieldSurface surface, BiomeTerraceRiser riser)
		{
			switch (surface)
			{
				case BiomeFieldSurface.Terraced: return riser == BiomeTerraceRiser.None ? BiomeTerraceRiser.Drystone : riser;
				case BiomeFieldSurface.Flat:
				case BiomeFieldSurface.Paddy:
					return riser;
				default: return BiomeTerraceRiser.None;
			}
		}

		/// <summary>How a terraced field's benches are each sown: flat for <see cref="BiomeFieldSurface.Terraced"/>, else the surface's own (a paddy's bench is flooded).</summary>
		public static BiomeFieldSurface TreadSurface(BiomeFieldSurface surface) => surface == BiomeFieldSurface.Terraced ? BiomeFieldSurface.Flat : surface;

		/// <summary>The heights a riser's step is cut between, metres: drystone 1.2–2.2, an earth bund 0.8–2.2 (a rice terrace's bank), a grass bank 1.0–2.5.</summary>
		public static void RiserRange(BiomeTerraceRiser riser, out float min, out float max)
		{
			switch (riser)
			{
				case BiomeTerraceRiser.EarthBund: min = 0.8f; max = 2.2f; return;
				case BiomeTerraceRiser.GrassBank: min = 1f; max = 2.5f; return;
				default: min = 1.2f; max = 2.2f; return;
			}
		}

		/// <summary>How high a step stands on a slope of <paramref name="slopeDegrees"/>, metres: a <see cref="TreadMetres"/> bench where the riser's range allows.</summary>
		public static float RiserHeight(BiomeTerraceRiser riser, float slopeDegrees)
		{
			RiserRange(riser, out float min, out float max);
			float tan = Mathf.Tan(Mathf.Clamp(slopeDegrees, 0.5f, 60f) * Mathf.Deg2Rad);
			return Mathf.Clamp(TreadMetres * tan, min, max);
		}

		/// <summary>One bench and its step across a slope, metres on the plan: the step's height over the slope's gradient (rise over run).</summary>
		public static float Period(float rise, float gradient) => rise / Mathf.Max(1e-3f, gradient);

		/// <summary>
		/// The share of a bench's period its step takes (pure): a drystone step about 1.6 m across (the heightmap's two-metre samples
		/// make it a ramp the wall stands on), an earth bund's bank 2 m, a grass bank two fifths of the period; never more than 0.6
		/// nor under 0.12.
		/// </summary>
		public static float Share(BiomeTerraceRiser riser, float period)
		{
			float width;
			switch (riser)
			{
				case BiomeTerraceRiser.EarthBund: width = 2f; break;
				case BiomeTerraceRiser.GrassBank: width = 0.4f * period; break;
				default: width = 1.6f; break;
			}
			return Mathf.Clamp(width / Mathf.Max(1e-3f, period), 0.12f, 0.6f);
		}

		/// <summary>The bench a point of bench coordinate <paramref name="u"/> lies on.</summary>
		public static int BandOf(float u) => Mathf.FloorToInt(u);

		/// <summary>How far across its bench a point of bench coordinate <paramref name="u"/> lies, 0 (its lip) … 1 (the top of the step behind it).</summary>
		public static float Phase(float u) => u - Mathf.Floor(u);

		/// <summary>The level bench <paramref name="band"/> is cut to, in steps over the base: half its tread over its lip's (cut and fill balanced).</summary>
		public static float TreadLevel(int band, float share) => band + 0.5f * (1f - share);

		/// <summary>
		/// The terraced ground at bench coordinate <paramref name="u"/>, in steps over the base (pure): level across each bench at
		/// <see cref="TreadLevel"/>, rising smoothly up its step over the last <paramref name="share"/> of the period to the next bench.
		/// Continuous; on average as high as the slope it replaces.
		/// </summary>
		public static float Bench(float u, float share)
		{
			int band = BandOf(u);
			float t = u - band;
			float s = Mathf.Clamp(share, 1e-3f, 1f);
			float rise = t <= 1f - s ? 0f : Smooth((t - (1f - s)) / s);
			return TreadLevel(band, s) + rise;
		}

		/// <summary>An earth bund's lip over its bench at <paramref name="metresIn"/> from the bench's lip, metres (0 for any other riser and past the lip).</summary>
		public static float LipRaise(BiomeTerraceRiser riser, float metresIn)
		{
			if (riser != BiomeTerraceRiser.EarthBund || metresIn < 0f || metresIn >= LipMetres)
			{
				return 0f;
			}
			return LipRise * Mathf.Sin(Mathf.PI * metresIn / LipMetres);
		}

		/// <summary>How far in from its lip a bench is left unsown (an earth bund's lip; a strip at a drystone or grass lip), metres.</summary>
		public static float FrontMargin(BiomeTerraceRiser riser) => riser == BiomeTerraceRiser.EarthBund ? LipMetres : 0.3f;

		/// <summary>How far before the foot of its step a bench is left unsown (a drystone wall's footing), metres.</summary>
		public static float BackMargin(BiomeTerraceRiser riser) => riser == BiomeTerraceRiser.Drystone ? 0.6f : 0.3f;

		/// <summary>
		/// True where a point at phase <paramref name="t"/> of a bench whose period is <paramref name="period"/> metres is sown (pure):
		/// on the bench, its lip's and its step's margins left.
		/// </summary>
		public static bool OnTread(BiomeTerraceRiser riser, float t, float share, float period)
		{
			float front = t * period, back = (1f - share - t) * period;
			return front >= FrontMargin(riser) && back >= BackMargin(riser);
		}

		/// <summary>True for a riser whose bank (and lip) keep the turf that grew there (earth bunds and grass banks); a drystone step is bare stone.</summary>
		public static bool KeepsTurf(BiomeTerraceRiser riser) => riser == BiomeTerraceRiser.EarthBund || riser == BiomeTerraceRiser.GrassBank;

		/// <summary>How much of a terraced field's benches stand at <paramref name="inside"/> metres in from its edge: none at the edge, all of them <see cref="EdgeFadeMetres"/> in.</summary>
		public static float Fade(float inside) => Smooth(inside / EdgeFadeMetres);

		// ── Terraced paddies ──────────────────────────────────────────

		/// <summary>How wide a terraced paddy's bund stands along its bench's lip, metres: wide enough that its water's sheet, drawn a cell larger than its mask, stays under it.</summary>
		public const float PaddyFrontCrest = 1.7f;
		/// <summary>How far before the foot of its step a terraced paddy's floor ends, metres.</summary>
		public const float PaddyBackCrest = 0.5f;
		/// <summary>How far inside a terraced field's full benches a paddy's floor begins at their ends, metres.</summary>
		public const float PaddyEndCrest = 1.7f;
		/// <summary>The inner face of a terraced paddy's bund, from its crest down to its floor, metres.</summary>
		public const float PaddyFace = 0.5f;

		/// <summary>
		/// How far inside a terraced paddy's flooded floor a point lies, metres (pure; negative on its bund): the least of its distance
		/// past the lip's crest, before the back's, and past the ends' (in from the field's edge, past the benches' fade).
		/// </summary>
		public static float PaddyInner(float t, float share, float period, float inside)
		{
			float front = t * period - PaddyFrontCrest;
			float back = (1f - share - t) * period - PaddyBackCrest;
			float ends = inside - EdgeFadeMetres - PaddyEndCrest;
			return Mathf.Min(front, Mathf.Min(back, ends));
		}

		/// <summary>
		/// A terraced paddy's ground on its bench, metres (pure): the floor a paddy's water depth under the water from its inner edge
		/// in, the bund's crest a bund's rise over the water out past its face, its face between.
		/// </summary>
		public static float PaddyTread(float inner, float water)
		{
			float floor = water - BiomeFieldSurfaces.PaddyWaterDepth, crest = water + BiomeFieldSurfaces.PaddyBundRise;
			if (inner >= 0f)
			{
				return floor;
			}
			if (inner <= -PaddyFace)
			{
				return crest;
			}
			return Mathf.Lerp(floor, crest, -inner / PaddyFace);
		}

		private static float Smooth(float t)
		{
			t = Mathf.Clamp01(t);
			return t * t * (3f - 2f * t);
		}
	}
}
