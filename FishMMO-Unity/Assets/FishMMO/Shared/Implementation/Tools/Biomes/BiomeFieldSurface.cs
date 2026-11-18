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
		/// <summary>
		/// A paddy (2026-10-18, phase I2i): a level field flooded a hand's depth deep inside a low earth bund, the crop (rice) standing
		/// in the water. The cut digs the floor level and raises the bund (<see cref="BiomeFieldSurfaces.PaddyGround"/>, carved and put
		/// back like ridge and furrow) and lays the water as a small lake of the scene's hydrology.
		/// </summary>
		Paddy = 3,
		/// <summary>
		/// Terraced (2026-10-18, phase I2j-b, decision 29): a hillside cut into level benches along its contours, each bench (tread)
		/// sown flat, each step (riser) faced as the crop's <see cref="BiomeFieldCrop.Riser"/> says (drystone when it says none). The cut
		/// carves the benches (BiomeTerraces, PointOfInterestFieldTerraces) and takes them out again as ridge and furrow's; a terraced
		/// field is laid on slopes a flat field is not (<see cref="BiomeTerraces.MaxSlopeDegrees"/>), and lies flat where its ground is
		/// too gentle to step (<see cref="BiomeTerraces.MinSlopeDegrees"/>). Flat or Paddy with a riser are terraced too (terraced paddies).
		/// </summary>
		Terraced = 4,
		/// <summary>
		/// Corn hills (2026-10-18, I2j-b): the Three Sisters' planting, maize, beans and squash sown in low mounds a metre across on a
		/// 1.3 m grid (<see cref="CornHillSpacing"/>) over tilled earth. Painted: far finer than the heightmap.
		/// </summary>
		CornHills = 5,
		/// <summary>
		/// Baulatura (2026-10-18, I2j-b): the Po plain's wide cambered wheat beds, 24 m across between 1.5 m ditches, 0.6 m high at the
		/// crown, a row of trained trees (the biome's piantata) along each ditch bank. Carved, as ridge and furrow.
		/// </summary>
		Baulatura = 6,
		/// <summary>
		/// Marcita (2026-10-18, I2j-b): a water meadow of 8 m cambered beds with a feeder rill of standing water dug along each crown,
		/// kept green through winter. The beds are carved as ridge and furrow's; the rills are carved and flooded as a standing ditch's
		/// pounds (PointOfInterestLinearFeatures.Pounds).
		/// </summary>
		Marcita = 7,
		/// <summary>
		/// Raised beds (2026-10-18, I2j-b): a kitchen garden's or herber's wattle-edged beds 1.2 m by 6 m with 0.5 m paths between them
		/// both ways; such a field is a garden (<see cref="RaisedBedGarden"/> at most). Painted, the edging laid as low wattle.
		/// </summary>
		RaisedBeds = 8,
	}

	/// <summary>
	/// The ridges of a field's surface (pure): where across the sown band each ridge and furrow lies, how high the ground
	/// stands there, and how much of a stretch of it is sown. The band is the field's sown width (inside its headland) across
	/// the ridges, which run the length of the field; it holds a whole number of ridges, each with half a furrow either side,
	/// the nominal width stretched to fit, so a field's two edges lie in furrows against its headland.
	/// </summary>
	/// <remarks>
	/// The cut lays a field's ridges as rows of its crop with the furrows bare between them (PointOfInterestFields), and since
	/// 2026-10-18 (phase I2h) also carves ridge and furrow into the terrain by <see cref="CarvedHeight"/> (<see cref="Carved"/>;
	/// PointOfInterestFieldCarve): the furrows' crisp lines stay painted, the broad swell of the ridges is in the ground.
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
				case BiomeFieldSurface.Baulatura: return new Rows(24f, 1.5f, 0.6f);
				case BiomeFieldSurface.Marcita: return new Rows(8f, 1f, 0.3f);
				// The beds lie across the field (their paths painted as furrows); their 6 m lengths are broken by cross paths (OnBedAlong).
				case BiomeFieldSurface.RaisedBeds: return new Rows(1.2f, 0.5f, 0f);
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
		/// True for a surface the cut also carves into the terrain (2026-10-18, phase I2h): ridge and furrow, whose ridges, about
		/// seven metres from furrow to furrow, the heightmap's samples (about two metres apart) can hold. Lazy beds stay painted
		/// only: under two samples to a ridge, carved they would alias into a ripple that wanders across the field. Baulatura's 25 m
		/// beds and a marcita's 9 m ones are carved the same way (2026-10-18, I2j-b); corn hills and raised beds are far too fine.
		/// </summary>
		public static bool Carved(BiomeFieldSurface surface) => surface == BiomeFieldSurface.RidgeAndFurrow || surface == BiomeFieldSurface.Baulatura
			|| surface == BiomeFieldSurface.Marcita;

		/// <summary>How far in from either end of the sown band a carved ridge takes to rise from nothing to its full height, metres: it dies into the headland where the plough turned.</summary>
		public const float RidgeEndMetres = 3f;

		/// <summary>
		/// How high the carved ground stands at <paramref name="x"/> metres across the band over the bottom of its furrows, metres:
		/// <see cref="Height"/> averaged over <paramref name="footprint"/> metres across the ridges, the ground one heightmap sample
		/// stands for (0: the profile at the point). Read at points, the samples of a field laid at a slant to the heightmap's rows
		/// caught crowns and furrows unevenly, and its ridges wobbled in height along their length. 0 for a surface not carved.
		/// </summary>
		public static float CarvedHeight(BiomeFieldSurface surface, float band, float x, float footprint)
		{
			if (!Carved(surface) || !(band > 0f))
			{
				return 0f;
			}
			const int samples = 8;
			float half = 0.5f * Mathf.Max(0f, footprint);
			float sum = 0f;
			for (int i = 0; i < samples; i++)
			{
				sum += Height(surface, band, x - half + 2f * half * (i + 0.5f) / samples);
			}
			return sum / samples;
		}

		/// <summary>How much of a carved ridge's height stands <paramref name="fromEnd"/> metres in from the nearer end of the sown band: none at the end (and past it), all of it <see cref="RidgeEndMetres"/> in, smoothly between.</summary>
		public static float RidgeEnd(float fromEnd)
		{
			float t = Mathf.Clamp01(fromEnd / RidgeEndMetres);
			return t * t * (3f - 2f * t);
		}

		// ── Paddies (2026-10-18, I2i) ─────────────────────────────────

		/// <summary>True for a surface flooded behind a bund (<see cref="BiomeFieldSurface.Paddy"/>).</summary>
		public static bool Flooded(BiomeFieldSurface surface) => surface == BiomeFieldSurface.Paddy;

		/// <summary>How deep a paddy's water stands over its floor, metres.</summary>
		public const float PaddyWaterDepth = 0.12f;
		/// <summary>How high a paddy's bund stands over its water, metres.</summary>
		public const float PaddyBundRise = 0.25f;
		/// <summary>The bund's flat crest, metres across, from the field's edge inward.</summary>
		public const float PaddyBundCrest = 0.8f;
		/// <summary>The bund's inner face down to the floor, metres across.</summary>
		public const float PaddyInnerSlope = 0.5f;
		/// <summary>The bund's outer face down to the ground round it, metres across.</summary>
		public const float PaddyOuterSlope = 1f;
		/// <summary>
		/// How far out past the field's edge the ground must stand over the water, metres: the lake's mask is drawn a 4 m cell larger
		/// than it is (InlandWaterRenderer), and a mask cell reaches past the floor it covers, so the water's sheet runs up to about
		/// this far under the bund and the ground beyond it, where it must stay hidden.
		/// </summary>
		public const float PaddySlackMetres = 8f;
		/// <summary>How far under the lowest ground round it a paddy's water is kept, metres.</summary>
		public const float PaddyFreeboard = 0.05f;
		/// <summary>The most a paddy's floor is dug below, and filled above, the ground it is laid on, metres: past either it is sown flat.</summary>
		public const float PaddyMaxCut = 1f, PaddyMaxFill = 0.6f;

		/// <summary>How far in from the field's edge its flooded floor begins, metres (the bund's crest and inner face).</summary>
		public static float PaddyFloorFrom => PaddyBundCrest + PaddyInnerSlope;

		/// <summary>
		/// A paddy's water level (pure): <see cref="PaddyFreeboard"/> under the lowest of <paramref name="ring"/> (the ground round the
		/// field out to <see cref="PaddySlackMetres"/>, and its bund line), so no ground the water's sheet can reach stands under it;
		/// false when the floor <see cref="PaddyWaterDepth"/> under that would cut more than <see cref="PaddyMaxCut"/> into
		/// <paramref name="interior"/> (the field's own ground) or fill more than <see cref="PaddyMaxFill"/> over it.
		/// </summary>
		public static bool PaddyLevel(IEnumerable<float> interior, IEnumerable<float> ring, out float water)
		{
			water = float.NaN;
			float lowRing = float.PositiveInfinity, low = float.PositiveInfinity, high = float.NegativeInfinity;
			foreach (float g in ring)
			{
				if (float.IsNaN(g) || float.IsInfinity(g))
				{
					return false;
				}
				lowRing = Mathf.Min(lowRing, g);
			}
			foreach (float g in interior)
			{
				if (float.IsNaN(g) || float.IsInfinity(g))
				{
					return false;
				}
				low = Mathf.Min(low, g);
				high = Mathf.Max(high, g);
			}
			if (float.IsInfinity(lowRing) || float.IsInfinity(low))
			{
				return false;
			}
			float level = lowRing - PaddyFreeboard, floor = level - PaddyWaterDepth;
			if (high - floor > PaddyMaxCut || floor - low > PaddyMaxFill)
			{
				return false;
			}
			water = level;
			return true;
		}

		/// <summary>
		/// The ground a paddy leaves at a point (pure), metres: <paramref name="inside"/> is how far in from the field's edge the point
		/// lies (negative outside), <paramref name="ground"/> the ground there as it was, <paramref name="water"/> the paddy's level.
		/// The floor <see cref="PaddyWaterDepth"/> under the water from <see cref="PaddyFloorFrom"/> in; the bund's crest
		/// <see cref="PaddyBundRise"/> over the water along the edge (never lowering ground that stands higher); its inner face down to
		/// the floor; its outer face down to the ground over <see cref="PaddyOuterSlope"/> (raised only); the ground beyond as it was.
		/// </summary>
		public static float PaddyGround(float inside, float ground, float water)
		{
			float crest = water + PaddyBundRise, floor = water - PaddyWaterDepth;
			if (inside >= PaddyFloorFrom)
			{
				return floor;
			}
			if (inside > PaddyBundCrest)
			{
				return Mathf.Lerp(Mathf.Max(crest, ground), floor, (inside - PaddyBundCrest) / PaddyInnerSlope);
			}
			if (inside >= 0f)
			{
				return Mathf.Max(ground, crest);
			}
			if (inside > -PaddyOuterSlope)
			{
				return Mathf.Max(ground, Mathf.Lerp(crest, ground, -inside / PaddyOuterSlope));
			}
			return ground;
		}

		// ── Corn hills and raised beds (2026-10-18, I2j-b) ────────────

		/// <summary>How far apart corn hills stand, metres, both ways.</summary>
		public const float CornHillSpacing = 1.3f;
		/// <summary>A corn hill's radius, metres: the mound the maize, beans and squash are sown in.</summary>
		public const float CornHillRadius = 0.5f;
		/// <summary>A raised bed's length, metres, and the cross path between one bed and the next along its row.</summary>
		public const float BedLength = 6f, BedCrossPath = 0.5f;
		/// <summary>The largest garden of raised beds, metres (long side, short side): a field drawn larger is laid at this about its middle.</summary>
		public static readonly Vector2 RaisedBedGarden = new Vector2(24f, 16f);

		/// <summary>
		/// How much of a texel <paramref name="texel"/> metres square centred at (<paramref name="u"/>, <paramref name="v"/>) metres
		/// from a field's middle along its own axes lies on a corn hill (pure): 0 … 1, on a lattice <see cref="CornHillSpacing"/>
		/// apart with a hill on the field's middle.
		/// </summary>
		public static float HillShare(float u, float v, float texel)
		{
			const int samples = 4;
			int on = 0;
			for (int j = 0; j < samples; j++)
			{
				for (int i = 0; i < samples; i++)
				{
					float x = u + texel * ((i + 0.5f) / samples - 0.5f), z = v + texel * ((j + 0.5f) / samples - 0.5f);
					float dx = x - Mathf.Round(x / CornHillSpacing) * CornHillSpacing, dz = z - Mathf.Round(z / CornHillSpacing) * CornHillSpacing;
					on += dx * dx + dz * dz <= CornHillRadius * CornHillRadius ? 1 : 0;
				}
			}
			return on / (float)(samples * samples);
		}

		/// <summary>
		/// How much of the stretch from <paramref name="along0"/> to <paramref name="along1"/> metres along a field's sown length (from
		/// its sown end) lies on a bed rather than a cross path (pure): raised beds break every <see cref="BedLength"/> for a
		/// <see cref="BedCrossPath"/>; every other surface runs unbroken (1).
		/// </summary>
		public static float BedShareAlong(BiomeFieldSurface surface, float along0, float along1)
		{
			if (surface != BiomeFieldSurface.RaisedBeds)
			{
				return 1f;
			}
			const int samples = 8;
			const float period = BedLength + BedCrossPath;
			int on = 0;
			for (int i = 0; i < samples; i++)
			{
				float a = Mathf.Lerp(along0, along1, (i + 0.5f) / samples);
				float t = a - Mathf.Floor(a / period) * period;
				on += a >= 0f && t < BedLength ? 1 : 0;
			}
			return on / (float)samples;
		}

		/// <summary>The cross paths of a garden of raised beds <paramref name="length"/> metres long: each one's middle, metres from its sown end (none for any other surface).</summary>
		public static List<float> CrossPaths(BiomeFieldSurface surface, float length)
		{
			var middles = new List<float>();
			if (surface != BiomeFieldSurface.RaisedBeds)
			{
				return middles;
			}
			const float period = BedLength + BedCrossPath;
			for (float a = BedLength + 0.5f * BedCrossPath; a < length - 1f; a += period)
			{
				middles.Add(a);
			}
			return middles;
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
