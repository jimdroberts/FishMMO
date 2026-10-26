using System;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// The burn mosaic (2026-10-25, phase I2t, decisions 75, 82 and 84): where and when a biome's ground burns, a pure function of the
	/// shared world clock, the world's season and the scene, so every player sees the same fire line cross the same hill at the same
	/// moment and a late joiner finds the same black ground. The C# twin of FishBurn.hlsl, constant for constant: the client draws it
	/// on the GPU (the terrain's burnt-veld tint and flame line, the blade grass cut to stubble, the green flush, the fire lilies);
	/// this side is what tests pin, and what any later gameplay hazard must call so the server agrees with what is drawn.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The year.</b> A scene's thermal year runs from its coldest point (<see cref="BurnClock"/>; the climate table's coldest phase
	/// at the scene's latitude, as <see cref="SceneClimate.FoliageState.Phase01"/> reads it), so "the late dry season" is a share of
	/// THAT year whatever the calendar says, on any body. Stage lengths are in year-days (a 365th of the year).
	/// </para>
	/// <para>
	/// <b>The cells.</b> The scene is tiled into <see cref="CellMetres"/> cells, each with a jittered ignition point whose Voronoi
	/// region is the patch that fire burns, so neighbouring burns meet on irregular lines and the cells run 200–600 m across. Each
	/// year re-hashes them: a burn chance (<see cref="ChanceOf"/>), an ignition moment inside the regime's window, a spread rate
	/// (<see cref="MinSpeed"/>–<see cref="MaxSpeed"/>). The front reaches a point at <c>ignition + (r − e·along) / v</c>: r the
	/// distance from the ignition point (raggedly noised), along its share downwind of the scene's prevailing wind, e
	/// <see cref="Eccentricity"/>. Its level sets are ellipses with the ignition at a focus, the head running downwind at v/(1−e)
	/// and the back fire upwind at v/(1+e), as a grass fire does.
	/// </para>
	/// <para>
	/// <b>Continuous across the year's wrap.</b> Every age is measured on the absolute clock: a point reads this year's fire once
	/// it has passed and last year's until then, so nothing jumps when the year turns. (A fire of the year before last drops out
	/// at the turn, but no stage lasts that long: the last, <see cref="GreenDays"/>, ends well inside a year.)
	/// </para>
	/// </remarks>
	public static class BurnMosaic
	{
		/// <summary>The burn cells' grid, m (each fire's patch is the Voronoi region of its jittered ignition point).</summary>
		public const float CellMetres = 400f;
		/// <summary>How far inside its grid cell an ignition point may sit, as a share of the cell (0.15–0.85).</summary>
		public const float Jitter = 0.7f;
		/// <summary>The front's ellipse: head speed v/(1−e) downwind, back speed v/(1+e) upwind.</summary>
		public const float Eccentricity = 0.6f;
		/// <summary>The flank spread, m/s (0.5–2 m a minute).</summary>
		public const float MinSpeed = 0.5f / 60f, MaxSpeed = 2f / 60f;
		/// <summary>How ragged the front runs (± half this share of the distance), over patches about <see cref="RaggedMetres"/> across.</summary>
		public const float Ragged = 0.3f, RaggedMetres = 35f;

		/// <summary>The flame band behind the front, seconds of world time (a thin line a few metres deep).</summary>
		public const float FlameSeconds = 240f;
		/// <summary>The embers after it, seconds.</summary>
		public const float EmberSeconds = 1800f;
		/// <summary>Year-days of black stubble before the green comes.</summary>
		public const float CharDays = 3f;
		/// <summary>Year-days the green flush takes to regrow the blades from their bases.</summary>
		public const float FlushDays = 21f;
		/// <summary>Year-days until the ground has lost its burnt-veld tint.</summary>
		public const float ScarDays = 45f;
		/// <summary>Year-days until the flush's emerald has faded back into the season's own colour (the scar gone).</summary>
		public const float GreenDays = 75f;
		/// <summary>The fire lilies' bloom, year-days after the front (1–4 weeks).</summary>
		public const float LilyFromDays = 7f, LilyToDays = 28f;
		/// <summary>A blade burnt to stubble keeps this share of its height.</summary>
		public const float Stubble = 0.1f;
		/// <summary>The year-days in a year (stage lengths are shares of the year).</summary>
		public const float YearDays = 365f;

		/// <summary>The annual veld burn: share of cells that burn each year and its window in the thermal year (late dry season).</summary>
		public const float AnnualChance = 0.6f, AnnualFrom = 0.10f, AnnualTo = 0.30f;
		/// <summary>The occasional steppe burn: about one cell in five a year, in spring.</summary>
		public const float OccasionalChance = 0.2f, OccasionalFrom = 0.18f, OccasionalTo = 0.30f;

		/// <summary>The brimstone seeps: a grid, the share of its cells with one, their radius range, m.</summary>
		public const float SeepMetres = 20f, SeepChance = 0.18f, SeepMinRadius = 1.5f, SeepMaxRadius = 4f;
		/// <summary>The wrap of the motion clock the flames' flicker reads (whole cycles in it, so the wrap never shows).</summary>
		public const float FlickerWrapSeconds = 10000f;
		/// <summary>The flicker's two rates, whole cycles per <see cref="FlickerWrapSeconds"/> (3.7 Hz and 1.3 Hz).</summary>
		public const float FlickerCyclesA = 37000f, FlickerCyclesB = 13000f;

		/// <summary>Never burnt (no fire has passed this point in this year or the last).</summary>
		public const float Never = -1f;

		// Salts (FishBurn.hlsl).
		public const uint SaltCell = 0xB5E1A7C3u, SaltRagged = 0x2F9D3E61u, SaltSeep = 0x7C1D9E35u;

		/// <summary>A scene's thermal year at a moment: which year (from the coldest point), how far into it, how long it is.</summary>
		public struct BurnClock
		{
			/// <summary>The year's number (the GPU hashes it mod 65 536).</summary>
			public int Year;
			/// <summary>Seconds since the year's coldest point.</summary>
			public float Seconds;
			/// <summary>The year's length, seconds.</summary>
			public float YearSeconds;
		}

		/// <summary>
		/// The thermal year at <paramref name="worldSeconds"/> on a body whose year is <paramref name="yearSeconds"/> long and whose
		/// coldest point at the scene falls at <paramref name="coldest01"/> of the calendar year. Pure, double precision.
		/// </summary>
		public static BurnClock ClockAt(double worldSeconds, double yearSeconds, double coldest01)
		{
			if (!(yearSeconds > 0.0))
			{
				return new BurnClock { Year = 0, Seconds = 0f, YearSeconds = 0f };
			}
			double u = worldSeconds / yearSeconds - (coldest01 - Math.Floor(coldest01));
			double year = Math.Floor(u);
			return new BurnClock { Year = (int)year, Seconds = (float)((u - year) * yearSeconds), YearSeconds = (float)yearSeconds };
		}

		/// <summary>The share of cells that burn a year under a regime (0 for none and Brimstone, which never spreads).</summary>
		public static float ChanceOf(BiomeBurnRegime regime)
			=> regime == BiomeBurnRegime.Annual ? AnnualChance : regime == BiomeBurnRegime.Occasional ? OccasionalChance : 0f;

		/// <summary>A regime's ignition window, shares of the thermal year.</summary>
		public static void WindowOf(BiomeBurnRegime regime, out float from, out float to)
		{
			from = regime == BiomeBurnRegime.Occasional ? OccasionalFrom : AnnualFrom;
			to = regime == BiomeBurnRegime.Occasional ? OccasionalTo : AnnualTo;
		}

		/// <summary>A scene's seed: a hash of its name, 24 bits (exact in a float on the GPU).</summary>
		public static uint SeedOf(string scene)
		{
			uint h = 2166136261u;
			if (!string.IsNullOrEmpty(scene))
			{
				foreach (char c in scene)
				{
					h = unchecked((h ^ c) * 16777619u);
				}
			}
			return Hash(h) & 0xFFFFFFu;
		}

		/// <summary>One cell's fire in one year.</summary>
		public struct Fire
		{
			/// <summary>Whether it burns this year.</summary>
			public bool Burns;
			/// <summary>The ignition point, world xz (its Voronoi region is the patch).</summary>
			public float X, Z;
			/// <summary>When it is lit, seconds into its thermal year.</summary>
			public float Lit;
			/// <summary>Its flank spread, m/s.</summary>
			public float Speed;
		}

		/// <summary>The fire of grid cell (<paramref name="cx"/>, <paramref name="cz"/>) in <paramref name="year"/>: deterministic in its arguments.</summary>
		public static Fire FireOf(int cx, int cz, int year, uint seed, BiomeBurnRegime regime, float yearSeconds)
		{
			uint h = CellHash(cx, cz, YearSalt(year, seed));
			WindowOf(regime, out float from, out float to);
			return new Fire
			{
				Burns = Unit(Hash(h ^ 1u)) < ChanceOf(regime),
				X = (cx + 0.5f * (1f - Jitter) + Jitter * Unit(Hash(h ^ 4u))) * CellMetres,
				Z = (cz + 0.5f * (1f - Jitter) + Jitter * Unit(Hash(h ^ 5u))) * CellMetres,
				Lit = (from + (to - from) * Unit(Hash(h ^ 2u))) * yearSeconds,
				Speed = MinSpeed + (MaxSpeed - MinSpeed) * Unit(Hash(h ^ 3u)),
			};
		}

		/// <summary>The year's salt for the cells: the year mod 65 536 and the scene (FishBurn.hlsl FishBurnYearSalt).</summary>
		public static uint YearSalt(int year, uint seed) => unchecked(((uint)year & 0xFFFFu) * 0x9E3779B9u ^ seed * 0x85EBCA6Bu ^ SaltCell);

		/// <summary>
		/// Seconds since the front of <paramref name="year"/>'s fire reached (x, z), measured at <paramref name="now"/> seconds into
		/// that year (negative: not yet), or <see cref="float.NegativeInfinity"/> when its cell does not burn that year.
		/// </summary>
		public static float ArrivalAge(float x, float z, int year, float now, float windX, float windZ, uint seed, BiomeBurnRegime regime, float yearSeconds)
		{
			int gx = (int)Math.Floor(x / CellMetres), gz = (int)Math.Floor(z / CellMetres);
			float best = float.PositiveInfinity;
			Fire owner = default;
			for (int j = -1; j <= 1; j++)
			{
				for (int i = -1; i <= 1; i++)
				{
					Fire f = FireOf(gx + i, gz + j, year, seed, regime, yearSeconds);
					float dx = x - f.X, dz = z - f.Z;
					float d2 = dx * dx + dz * dz;
					if (d2 < best)
					{
						best = d2;
						owner = f;
					}
				}
			}
			if (!owner.Burns)
			{
				return float.NegativeInfinity;
			}
			float ox = x - owner.X, oz = z - owner.Z;
			float r = (float)Math.Sqrt(ox * ox + oz * oz);
			r *= 1f + Ragged * (Noise(x / RaggedMetres, z / RaggedMetres, SaltRagged ^ seed) - 0.5f);
			float along = ox * windX + oz * windZ;
			float arrival = owner.Lit + (r - Eccentricity * along) / owner.Speed;
			return now - arrival;
		}

		/// <summary>
		/// Seconds since a fire last passed (x, z) at <paramref name="clock"/>: this year's once its front has gone by, else last
		/// year's; <see cref="Never"/> when neither has. Pure in its arguments, continuous across the year's turn.
		/// </summary>
		public static float AgeAt(float x, float z, in BurnClock clock, float windX, float windZ, uint seed, BiomeBurnRegime regime)
		{
			if (!BiomeBurnRegimes.IsVeld(regime) || !(clock.YearSeconds > 0f))
			{
				return Never;
			}
			Wind(ref windX, ref windZ);
			float age = ArrivalAge(x, z, clock.Year, clock.Seconds, windX, windZ, seed, regime, clock.YearSeconds);
			if (age >= 0f)
			{
				return age;
			}
			float last = ArrivalAge(x, z, clock.Year - 1, clock.Seconds + clock.YearSeconds, windX, windZ, seed, regime, clock.YearSeconds);
			return last >= 0f ? last : Never;
		}

		/// <summary>
		/// The longest a front can take to cross its patch, seconds: a point is never further from its nearest ignition point than the
		/// cell's diagonal, the ragged noise stretches that by at most <see cref="Ragged"/>/2, and the back fire is the slowest way.
		/// </summary>
		public const float MaxSpreadSeconds = 1.5f * CellMetres * (1f + 0.5f * Ragged) * (1f + Eccentricity) / MinSpeed;

		/// <summary>
		/// Which years can show anything at <paramref name="clock"/>: bit 1 this year (its window has opened and its last scar not yet
		/// faded), bit 2 last year. The GPU skips the others (_FishBurnClock.w): what it draws is exactly <see cref="StageAt"/>.
		/// </summary>
		public static int LiveYears(in BurnClock clock, BiomeBurnRegime regime)
		{
			if (!BiomeBurnRegimes.IsVeld(regime) || !(clock.YearSeconds > 0f))
			{
				return 0;
			}
			WindowOf(regime, out float from, out float to);
			float y = clock.YearSeconds;
			float end = to * y + MaxSpreadSeconds + GreenDays * (y / YearDays);
			int live = 0;
			if (clock.Seconds >= from * y && clock.Seconds <= end)
			{
				live |= 1;
			}
			if (clock.Seconds + y <= end)
			{
				live |= 2;
			}
			return live;
		}

		/// <summary>
		/// The stage at (x, z) as the GPU draws it (FishBurnVeldAt): only the <see cref="LiveYears"/> are read. Equal to
		/// <see cref="StageOf"/> of <see cref="AgeAt"/> everywhere (pinned by the tests).
		/// </summary>
		public static BurnStage StageAt(float x, float z, in BurnClock clock, float windX, float windZ, uint seed, BiomeBurnRegime regime)
		{
			int live = LiveYears(in clock, regime);
			Wind(ref windX, ref windZ);
			float age = Never;
			if ((live & 1) != 0)
			{
				float now = ArrivalAge(x, z, clock.Year, clock.Seconds, windX, windZ, seed, regime, clock.YearSeconds);
				age = now >= 0f ? now : Never;
			}
			if (age < 0f && (live & 2) != 0)
			{
				float last = ArrivalAge(x, z, clock.Year - 1, clock.Seconds + clock.YearSeconds, windX, windZ, seed, regime, clock.YearSeconds);
				age = last >= 0f ? last : Never;
			}
			return StageOf(age, clock.YearSeconds);
		}

		/// <summary>What a burn of a given age shows (all 0 / <see cref="Regrow"/> 1 for untouched ground).</summary>
		public struct BurnStage
		{
			/// <summary>The flame line, 1 at the front fading through its band.</summary>
			public float Flame;
			/// <summary>The embers behind it.</summary>
			public float Ember;
			/// <summary>The ground's burnt-veld tint.</summary>
			public float Scar;
			/// <summary>The blades' height against their own: <see cref="Stubble"/> just burnt, regrowing from the base through the flush.</summary>
			public float Regrow;
			/// <summary>The blades' colour along charcoal (1) → emerald (<see cref="EmeraldTone"/>) → their own (0).</summary>
			public float Tone;
			/// <summary>The share of after-burn flowers (fire lilies) standing.</summary>
			public float Lily;
		}

		/// <summary>The point of <see cref="BurnStage.Tone"/> that is the flush's full emerald (exact in the GPU's three bits: 3/7).</summary>
		public const float EmeraldTone = 3f / 7f;

		/// <summary>The stage of a burn <paramref name="ageSeconds"/> old in a year <paramref name="yearSeconds"/> long.</summary>
		public static BurnStage StageOf(float ageSeconds, float yearSeconds)
		{
			var s = new BurnStage { Regrow = 1f };
			if (!(ageSeconds >= 0f) || !(yearSeconds > 0f))
			{
				return s;
			}
			float days = ageSeconds / (yearSeconds / YearDays);
			s.Flame = ageSeconds < FlameSeconds ? 1f - ageSeconds / FlameSeconds : 0f;
			s.Ember = ageSeconds < EmberSeconds ? 1f - ageSeconds / EmberSeconds : 0f;
			float flush = Smooth(CharDays, CharDays + FlushDays, days);
			s.Scar = 1f - Smooth(CharDays, ScarDays, days);
			s.Regrow = Stubble + (1f - Stubble) * flush;
			s.Tone = days < CharDays + FlushDays ? 1f + (EmeraldTone - 1f) * flush : EmeraldTone * (1f - Smooth(CharDays + FlushDays, GreenDays, days));
			s.Lily = Smooth(LilyFromDays, LilyFromDays + 2f, days) * (1f - Smooth(LilyToDays - 4f, LilyToDays, days));
			return s;
		}

		/// <summary>How dark it is for the blue fire: 1 from dusk to dawn, 0 by day, by the sun's height (FishBurn.hlsl FishBurnNight).</summary>
		public static float NightOf(float sunHeight) => 1f - Smooth(-0.08f, 0.08f, sunHeight);

		/// <summary>The flames' flicker at a hashed phase, on the motion clock wrapped at <see cref="FlickerWrapSeconds"/> (whole cycles: no jump at the wrap).</summary>
		public static float Flicker(float motionSeconds, float phase)
		{
			const float tau = 6.2831853f;
			float t = motionSeconds / FlickerWrapSeconds;
			float a = (float)Math.Sin(tau * (FlickerCyclesA * t + phase));
			float b = (float)Math.Sin(tau * (FlickerCyclesB * t + 0.37f + 1.7f * phase));
			return 0.65f + 0.35f * (0.5f * a + 0.5f * b);
		}

		/// <summary>
		/// The blue fire of a brimstone seep at (x, z): its flames' strength 0..1 (night × the seep's falloff × flicker). The vents'
		/// own lips are added by the client from the scene's sites.
		/// </summary>
		public static float SeepAt(float x, float z, float motionSeconds, float night, uint seed)
		{
			if (!(night > 0f))
			{
				return 0f;
			}
			int gx = (int)Math.Floor(x / SeepMetres), gz = (int)Math.Floor(z / SeepMetres);
			uint h = CellHash(gx, gz, SaltSeep ^ seed);
			if (Unit(Hash(h ^ 1u)) >= SeepChance)
			{
				return 0f;
			}
			float cx = (gx + 0.25f + 0.5f * Unit(Hash(h ^ 2u))) * SeepMetres, cz = (gz + 0.25f + 0.5f * Unit(Hash(h ^ 3u))) * SeepMetres;
			float radius = SeepMinRadius + (SeepMaxRadius - SeepMinRadius) * Unit(Hash(h ^ 4u));
			float dx = x - cx, dz = z - cz;
			float q = 1f - (dx * dx + dz * dz) / (radius * radius);
			if (q <= 0f)
			{
				return 0f;
			}
			return night * q * q * Flicker(motionSeconds, Unit(Hash(h ^ 5u)));
		}

		// ── The GPU's arithmetic (FishFlora.hlsl's hashes) ──

		/// <summary>FishFloraHash.</summary>
		public static uint Hash(uint x)
		{
			unchecked
			{
				x ^= x >> 16; x *= 0x7feb352du;
				x ^= x >> 15; x *= 0x846ca68bu;
				x ^= x >> 16;
				return x;
			}
		}

		/// <summary>FishFloraUnit's arithmetic on a hash: its top 24 bits as [0, 1).</summary>
		public static float Unit(uint h) => (h >> 8) * (1f / 16777216f);

		/// <summary>FishFloraCellHash.</summary>
		public static uint CellHash(int cx, int cz, uint salt) => Hash(unchecked((uint)cx * 0x8da6b343u ^ (uint)cz * 0xd8163841u ^ salt * 0xcb1ab31fu));

		/// <summary>FishFloraNoise: smooth value noise in [0, 1].</summary>
		public static float Noise(float px, float pz, uint salt)
		{
			float fx0 = (float)Math.Floor(px), fz0 = (float)Math.Floor(pz);
			int ix = (int)fx0, iz = (int)fz0;
			float fx = px - fx0, fz = pz - fz0;
			float ux = fx * fx * (3f - 2f * fx), uz = fz * fz * (3f - 2f * fz);
			float a = Unit(CellHash(ix, iz, salt)), b = Unit(CellHash(ix + 1, iz, salt));
			float c = Unit(CellHash(ix, iz + 1, salt)), d = Unit(CellHash(ix + 1, iz + 1, salt));
			float ab = a + (b - a) * ux, cd = c + (d - c) * ux;
			return ab + (cd - ab) * uz;
		}

		/// <summary>HLSL smoothstep.</summary>
		public static float Smooth(float from, float to, float x)
		{
			float t = to == from ? (x >= to ? 1f : 0f) : (x - from) / (to - from);
			t = t < 0f ? 0f : t > 1f ? 1f : t;
			return t * t * (3f - 2f * t);
		}

		/// <summary>The downwind way as a unit vector (east when calm).</summary>
		public static void Wind(ref float x, ref float z)
		{
			float l = (float)Math.Sqrt(x * x + z * z);
			if (l < 1e-6f)
			{
				x = 1f;
				z = 0f;
				return;
			}
			x /= l;
			z /= l;
		}
	}
}
