using FishMMO.Shared.Weather;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A sea hall's vents as every client draws them (2026-10-26, phase I2n-b, decision 83): each smoker's plume and its strength on
	/// the plume tide, the smoke ceiling it spreads into under the water's surface (the Great Smoker's under the ice roof as well), the
	/// flange mirrors on the chimneys, the carbon-dioxide beads off the white smokers, and the brinicles growing from the dome's roof
	/// and snapping. Pure functions of the shared motion clock (WorldMotion.Seconds) and the hall's stored spots; FishSeaHallVents.shader
	/// mirrors every one number for number (SeaHallVents draws them). PRESENTATION ONLY: nothing on the wire, nothing hurts; any later
	/// hazard must evaluate these same functions on the server.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Strength (decision 83).</b> The plume tide of decision 53 (SceneGlowSky.PlumeTide, 0.3 … 1 over the host moon's orbit)
	/// remapped to 0.75 … 1, times a flicker: one "on" span a slot of its own length (400–1250 s, each dividing the 10 000 s wrap), over
	/// 85–100 % of the slot with 30 s ramps (EpisodeSlots). Smokers barely falter; the hall's Great Smoker never falls under 0.85.
	/// </para>
	/// <para>
	/// <b>Nothing scrubs.</b> A puff's or a bead's place is its own phase on a period that is a whole number of cycles in its wrap, at
	/// a speed of its own (never the strength's), so a plume thickens and thins without a puff jumping; a brinicle's length is a
	/// sawtooth on a 6–18 h period, each a divisor of the 36 h brinicle wrap.
	/// </para>
	/// </remarks>
	public static class SceneSeaHallVents
	{
		/// <summary>The plumes', the ceiling's and the beads' wrap (WorldMotion.ShaderWrapSeconds), and the brinicles' (36 h, seconds).</summary>
		public const float Wrap = 10000f, BrinicleWrap = 129600f;
		/// <summary>The flicker slots a smoker draws its length from (each divides <see cref="Wrap"/>; the draft's 300–1800 s).</summary>
		public static readonly float[] FlickerSlots = { 400f, 500f, 625f, 1000f, 1250f };
		/// <summary>A slot's least and most "on" share, its ramps (s), the tide's remap, the Great Smoker's floor (decision 83).</summary>
		public const float FlickerLeast = 0.85f, FlickerMost = 1f, FlickerRamp = 30f, TideLow = 0.75f, GreatFloor = 0.85f;
		/// <summary>SceneGlowSky.PlumeTide's own range (SceneGlowModes.PlumeTideOf: 0.3 … 1).</summary>
		public const float SkyTideLow = 0.3f;
		/// <summary>A plume's rise, metres a second at the least and the most (the draft's 0.3–1 m/s).</summary>
		public const float RiseSlowest = 0.3f, RiseFastest = 1f;
		/// <summary>The brinicles' periods, hours (each divides the 36 h wrap; the draft's 6–18 h), and their longest and shortest, metres.</summary>
		public static readonly float[] BriniclePeriodsHours = { 6f, 9f, 12f, 18f };
		public const float BrinicleShortest = 2f, BrinicleLongest = 12f;
		/// <summary>How long a snapped brinicle's ice puff shows, seconds.</summary>
		public const float SnapSeconds = 4f;
		/// <summary>The smoke ceiling's current: whole turns round the hall in a wrap (a pure-clock drift, ~0.6 mrad/s).</summary>
		public const int CeilingTurns = 1;
		/// <summary>The most smokers drawn in a hall, puffs a plume, puffs a ceiling, flanges a chimney, beads a white smoker, brinicles a grove, groves a hall.</summary>
		public const int MaxSmokers = 24, PlumePuffs = 48, CeilingPuffs = 16, Flanges = 4, Beads = 24, GrovePerHall = 2, BriniclesPerGrove = 40;
		/// <summary>The kinds as the shader takes them (Smoker spot flags).</summary>
		public const int Black = 0, White = 1, Beehive = 2, Great = 3;

		// ── Hashes (the shader's, bit for bit: SceneCaveWater's) ─────

		public static uint Mix(uint x) => SceneCaveWater.Mix(x);
		public static uint Hash(uint seed, uint a, uint b) => SceneCaveWater.Hash(seed, a, b);
		public static float Unit(uint h) => SceneCaveWater.Unit(h);

		/// <summary>A smoker's seed from its orifice (to the centimetre), as every client loads it.</summary>
		public static uint SeedOf(Vector3 orifice) => SceneCaveWater.SeedOf(orifice);

		/// <summary>The kind a Smoker spot's flags name (<see cref="Black"/> … <see cref="Great"/>).</summary>
		public static int KindOf(SceneCaveLifeFlags flags)
			=> (flags & SceneCaveLifeFlags.Great) != 0 ? Great : (flags & SceneCaveLifeFlags.White) != 0 ? White : (flags & SceneCaveLifeFlags.Beehive) != 0 ? Beehive : Black;

		// ── Strength ─────────────────────────────────────────────────

		/// <summary>The sky's plume tide (0.3 … 1) remapped to the smokers' 0.75 … 1.</summary>
		public static float Tide(float skyTide) => TideLow + (1f - TideLow) * Mathf.Clamp01((skyTide - SkyTideLow) / (1f - SkyTideLow));

		/// <summary>A smoker's flicker now (0..1): one "on" span a slot of its own length over 85–100 % of it, 30 s ramps.</summary>
		public static float Flicker(float wrappedSeconds, uint seed)
		{
			uint h = Mix(seed ^ 0x5E0A11u);
			float length = FlickerSlots[(int)(h % (uint)FlickerSlots.Length)];
			float share = FlickerLeast + (FlickerMost - FlickerLeast) * Unit(Mix(h ^ 0x77u));
			// A slot of all "on" is an episode as long as the slot: its ramps still fall at its ends, so cap the share under one.
			return EpisodeSlots.At(wrappedSeconds, length, Mathf.Min(share, 0.995f) * length, FlickerRamp, h);
		}

		/// <summary>A smoker's plume strength now (decision 83): the remapped tide times its flicker; the Great Smoker never under 0.85.</summary>
		public static float Strength(float skyTide, float wrappedSeconds, uint seed, bool great)
		{
			float s = Tide(skyTide) * Flicker(wrappedSeconds, seed);
			return great ? Mathf.Max(GreatFloor, s) : s;
		}

		// ── Plumes ───────────────────────────────────────────────────

		/// <summary>A plume's rise speed, m/s (its own, by seed: never the strength's, so nothing scrubs).</summary>
		public static float RiseSpeed(uint seed) => RiseSlowest + (RiseFastest - RiseSlowest) * Unit(Hash(seed, 3u, 1u));

		/// <summary>
		/// A period near <paramref name="seconds"/> that is a whole number of cycles in <paramref name="wrap"/> (at least one): what every
		/// phase here runs on, so nothing jumps where the clock wraps.
		/// </summary>
		public static float WholePeriod(float seconds, float wrap) => wrap / Mathf.Max(1f, Mathf.Round(wrap / Mathf.Max(0.01f, seconds)));

		/// <summary>A plume puff's share of its climb now, 0 at the orifice … 1 at its ceiling.</summary>
		public static float PuffShare(float wrappedSeconds, uint seed, int puff, float riseHeight)
		{
			float period = WholePeriod(Mathf.Max(0.5f, riseHeight) / RiseSpeed(seed), Wrap);
			return Fraction(wrappedSeconds / period + Unit(Hash(seed, (uint)puff, 4u)));
		}

		/// <summary>A plume's half-width at a height over its orifice: entrainment widening it (a buoyant plume's ~0.12 a metre).</summary>
		public static float PlumeWidth(float orificeRadius, float above) => orificeRadius + 0.12f * Mathf.Max(0f, above);

		// ── The smoke ceiling ────────────────────────────────────────

		/// <summary>The ceiling's turn round the hall now, radians: <see cref="CeilingTurns"/> whole turns a wrap.</summary>
		public static float CeilingTurn(float wrappedSeconds) => 2f * Mathf.PI * CeilingTurns * Fraction(wrappedSeconds / Wrap);

		/// <summary>A ceiling puff's spread share now (0 where the plume meets the ceiling … 1 at its reach), on its own whole period.</summary>
		public static float CeilingShare(float wrappedSeconds, uint seed, int puff)
		{
			float period = WholePeriod(120f + 120f * Unit(Hash(seed, (uint)puff, 9u)), Wrap);
			return Fraction(wrappedSeconds / period + Unit(Hash(seed, (uint)puff, 10u)));
		}

		// ── Flanges, beads ───────────────────────────────────────────

		/// <summary>A flange's height share up its chimney (0.35 … 0.8, one band each) and its turn about it (degrees).</summary>
		public static void Flange(uint seed, int flange, float yaw, out float share, out float turn)
		{
			share = 0.35f + 0.15f * flange + 0.05f * Unit(Hash(seed, (uint)flange, 12u));
			turn = yaw + 90f * flange + 40f * Unit(Hash(seed, (uint)flange, 13u));
		}

		/// <summary>A flange mirror's shimmer at a moment (0.6 … 1), pulsing with the plume's strength.</summary>
		public static float MirrorShimmer(float wrappedSeconds, uint seed, int flange, float strength)
		{
			float phase = Unit(Hash(seed, (uint)flange, 14u));
			float wave = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * Fraction(wrappedSeconds / 2.5f + phase));
			return (0.6f + 0.4f * wave) * Mathf.Clamp01(strength);
		}

		/// <summary>A white smoker's bead: its climb over the orifice now, metres, up to its 4–10 m before it dissolves.</summary>
		public static float BeadRise(float wrappedSeconds, uint seed, int bead, out float height)
		{
			height = 4f + 6f * Unit(Hash(seed, 21u, 0u));
			float speed = 0.22f + 0.08f * Unit(Hash(seed, (uint)bead, 22u));
			float period = WholePeriod(height / speed, Wrap);
			return height * Fraction(wrappedSeconds / period + Unit(Hash(seed, (uint)bead, 23u)));
		}

		// ── Brinicles ────────────────────────────────────────────────

		/// <summary>A brinicle's period, seconds (6, 9, 12 or 18 h by its seed: each divides <see cref="BrinicleWrap"/>).</summary>
		public static float BriniclePeriod(uint seed) => 3600f * BriniclePeriodsHours[(int)(Hash(seed, 31u, 0u) % (uint)BriniclePeriodsHours.Length)];

		/// <summary>
		/// A brinicle's length now (decision 83: L(t) = Lmax · frac(t / Tb + h), growing, snapping with a puff of ice and growing again):
		/// <paramref name="brinicleSeconds"/> is the motion clock wrapped at <see cref="BrinicleWrap"/> (taken in double where it is wrapped);
		/// <paramref name="sinceSnap"/> the seconds since it last snapped.
		/// </summary>
		public static float BrinicleLength(float brinicleSeconds, uint seed, float longest, out float sinceSnap)
		{
			float period = BriniclePeriod(seed);
			float phase = Fraction(brinicleSeconds / period + Unit(Hash(seed, 32u, 0u)));
			sinceSnap = phase * period;
			return longest * phase;
		}

		/// <summary>A brinicle's longest, metres: 2–12 m by its seed, and never into the water under it (half a metre clear).</summary>
		public static float BrinicleLongestOver(uint seed, float roofOverWater)
			=> Mathf.Min(BrinicleShortest + (BrinicleLongest - BrinicleShortest) * Unit(Hash(seed, 33u, 0u)), Mathf.Max(0f, roofOverWater - 0.5f));

		/// <summary>The motion clock wrapped for the brinicles (double, then a float: 36 h holds to a hundredth of a second).</summary>
		public static float BrinicleSeconds(double motionSeconds) => (float)Celestial.WorldMotion.Repeat(motionSeconds, BrinicleWrap);

		/// <summary>A grove's middle as a share of the hall's waterline radius (over the deepest water: within 0.55 of it), and its turn.</summary>
		public static void Grove(uint hallSeed, int grove, out float share, out float turn)
		{
			share = 0.55f * Mathf.Sqrt(Unit(Hash(hallSeed, (uint)grove, 41u)));
			turn = 2f * Mathf.PI * Unit(Hash(hallSeed, (uint)grove, 42u));
		}

		/// <summary>The fraction part, as the shader's frac (exact for the small magnitudes here).</summary>
		public static float Fraction(float v) => v - Mathf.Floor(v);
	}
}
