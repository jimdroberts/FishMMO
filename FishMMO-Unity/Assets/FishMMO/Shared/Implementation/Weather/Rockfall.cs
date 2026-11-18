using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// FALLING STONE (2026-10-25, phase I2u; decisions 79, 95, 97): stones falling down the scene's stored chutes, every fall a pure
	/// function of the shared clock. Each chute's clock is cut into <see cref="SlotSeconds"/> slots; a slot holds a fall when its hash
	/// draws under the chute's chance (one fall per 20–60 min, its own, times the weather's <see cref="Multiplier"/>: ×3 in the two
	/// hours after the air at the scene crosses 0 °C upward, ×2 in rain and the hour after it, ×2 near a walking rock), at a hashed
	/// moment, with 1–12 stones whose paths (<see cref="StoneOf"/>, <see cref="RunOf"/>) are hashed from the fall. Presentation only:
	/// nothing on the wire, no damage; a later hazard must read this same timing. FishRockfall.hlsl mirrors the stone, the run and the
	/// hop constant for constant.
	/// </summary>
	/// <remarks>
	/// Unlike the vents (wrapped float time), the falls are found on the CPU in double on the unwrapped clock, so they never repeat and
	/// are continuous across every wrap; the GPU is handed each fall's age (seconds, small).
	/// </remarks>
	public static class Rockfall
	{
		/// <summary>A slot of a chute's clock, seconds (divides the 10 000 s wrap).</summary>
		public const double SlotSeconds = 125.0;

		/// <summary>A chute's own mean time between falls in calm, dry weather, seconds: hashed between these (decision 79: 20–60 min).</summary>
		public const float LeastPeriod = 1200f, MostPeriod = 3600f;

		/// <summary>The weather's boosts (decision 79): freeze-thaw, rain.</summary>
		public const float ThawBoost = 3f, RainBoost = 2f;

		/// <summary>The weather's knots, seconds; how long a thaw boosts after its knot; how much rain (precipitation × its rain share) counts.</summary>
		public const double KnotSeconds = 3600.0, ThawSeconds = 7200.0;
		public const float RainCounts = 0.1f;

		/// <summary>The stones a fall sends, fewest and most.</summary>
		public const int LeastStones = 1, MostStones = 12;

		/// <summary>The most seconds after the first a stone of a fall sets off.</summary>
		public const float StaggerSeconds = 4f;

		/// <summary>How long a stone lies where it stopped, then how long it takes to sink out of sight, seconds.</summary>
		public const float RestSeconds = 600f, SinkSeconds = 60f;

		/// <summary>A stone's radius, m, smallest and largest, on a scree chute and on a sciara.</summary>
		public const float ScreeLeast = 0.12f, ScreeMost = 0.6f, SciaraLeast = 0.2f, SciaraMost = 0.8f;

		/// <summary>A stone's top speed, m/s; its gain and loss of speed as shares of gravity.</summary>
		public const float LeastPeak = 6f, MostPeak = 15f, LeastAccel = 0.25f, MostAccel = 0.45f, LeastDecel = 0.6f, MostDecel = 1.0f;

		/// <summary>Where a stone stops, as a share of the chute: the smallest stop highest (fall sorting: the coarse run on to the toe).</summary>
		public const float LeastStop = 0.62f, MostStop = 1f;

		/// <summary>A stone's hop: metres of travel per hop, and its height, m (per unit of size rank + 0.5).</summary>
		public const float LeastHop = 3f, MostHop = 9f, LeastHopHeight = 0.15f, MostHopHeight = 1.2f;

		/// <summary>How far to either side of the line a stone falls, as a share of the chute's half width; its wobble's wavelength, m.</summary>
		public const float ScreeAcross = 0.5f, SciaraAcross = 0.7f, LeastWobble = 12f, MostWobble = 40f;

		/// <summary>A fresh scar's fade: a lasting part over days and a bright part over hours (seconds), and their shares.</summary>
		public const double ScarDaysSeconds = 2.0 * 86400.0, ScarHoursSeconds = 3.0 * 3600.0;
		public const float ScarLasting = 0.6f;

		/// <summary>How far back the last fall is looked for (scar freshness), seconds.</summary>
		public const double ScarLookBackSeconds = 2.0 * 86400.0;

		/// <summary>Salts (FishRockfall.hlsl mirrors them).</summary>
		public const uint PeriodSalt = 0x0C47E5u, StartSalt = 0x51A27u, EventSalt = 0xE7E47u, CountSalt = 0x5707Eu,
			SizeSalt = 0x51E0u, DelaySalt = 0xDE1Au, AcrossSalt = 0xAC05u, PeakSalt = 0x9EA6u, AccelSalt = 0xACCEu, DecelSalt = 0xDECEu,
			StopSalt = 0x5709u, HopSalt = 0x4099u, HopHeightSalt = 0x40E1u, WobbleSalt = 0x30B1u, PhaseSalt = 0x9A5Eu;

		/// <summary>The weather at the scene on one knot of the world clock: the air (°C) and the rain (0..1).</summary>
		public struct Knot
		{
			public float Celsius;
			public float Rain;
		}

		/// <summary>
		/// The weather's boost at a moment (pure in the knots, <paramref name="knotAt"/> by index = floor(seconds / <see cref="KnotSeconds"/>)):
		/// ×<see cref="ThawBoost"/> when the air crossed 0 °C upward at a knot within the last <see cref="ThawSeconds"/> (the morning thaw), and
		/// ×<see cref="RainBoost"/> when rain fell at this knot or the one before (in rain and the hour after it). 1 otherwise.
		/// </summary>
		public static float Multiplier(double seconds, Func<long, Knot> knotAt)
		{
			if (knotAt == null || double.IsNaN(seconds) || double.IsInfinity(seconds))
			{
				return 1f;
			}
			long k = (long)Math.Floor(seconds / KnotSeconds);
			float m = 1f;
			Knot now = knotAt(k), before = knotAt(k - 1);
			int window = (int)Math.Ceiling(ThawSeconds / KnotSeconds);
			Knot later = now;
			for (int j = 0; j < window; j++)
			{
				Knot earlier = j == 0 ? before : knotAt(k - j - 1);
				if (earlier.Celsius < 0f && later.Celsius >= 0f)
				{
					m *= ThawBoost;
					break;
				}
				later = earlier;
			}
			if (now.Rain >= RainCounts || before.Rain >= RainCounts)
			{
				m *= RainBoost;
			}
			return m;
		}

		/// <summary>A chute's own mean time between falls in calm, dry weather, seconds.</summary>
		public static float PeriodOf(uint chuteSeed) => Mathf.Lerp(LeastPeriod, MostPeriod, U(chuteSeed, 0, PeriodSalt));

		/// <summary>
		/// Whether a slot of a chute's clock holds a fall (its hash under SlotSeconds / period × boost × the weather's multiplier at the
		/// slot's start, at most 1), with the fall's start (anywhere in the slot), seed and stones.
		/// </summary>
		public static bool Falls(long slot, uint chuteSeed, float boost, float multiplier, out double start, out uint seed, out int stones)
		{
			uint h = TurbidityEpisodes.Mix((uint)slot ^ (uint)(slot >> 32) * 0x27D4EB2Fu, chuteSeed);
			double chance = Math.Min(1.0, SlotSeconds / PeriodOf(chuteSeed) * Math.Max(0f, boost) * Math.Max(0f, multiplier));
			start = slot * SlotSeconds + TurbidityEpisodes.Unit(TurbidityEpisodes.Mix(h, StartSalt)) * SlotSeconds;
			seed = TurbidityEpisodes.Mix(h, EventSalt) & 0xFFFFFFu;
			stones = LeastStones + (int)(TurbidityEpisodes.Unit(TurbidityEpisodes.Mix(h, CountSalt)) * (MostStones - LeastStones + 1));
			return TurbidityEpisodes.Unit(h) < chance;
		}

		/// <summary>One fall: when it started (world seconds), its seed (24 bits: a float carries it whole), its stones.</summary>
		public struct Fall
		{
			public double Start;
			public uint Seed;
			public int Stones;
		}

		/// <summary>
		/// How long a fall on a chute this long shows, seconds: an upper bound on its slowest stone's run (stagger, the slowest gain, the
		/// whole chute at the least top speed), then its rest and its sink.
		/// </summary>
		public static double LifetimeOf(float length, float gravity = 9.81f)
		{
			float g = Mathf.Max(0.5f, gravity);
			double run = StaggerSeconds + LeastPeak / (LeastAccel * g) + Math.Max(0f, length) / LeastPeak + LeastPeak / (LeastDecel * g);
			return run + RestSeconds + SinkSeconds;
		}

		/// <summary>
		/// The falls of a chute still showing at a moment (started in (seconds − lifetime, seconds]), oldest first; the weather's multiplier
		/// read at each slot's start (<paramref name="multiplierAt"/>, world seconds; null: 1). Pure in the clock.
		/// </summary>
		public static void FallsAt(double seconds, uint chuteSeed, float boost, double lifetime, Func<double, float> multiplierAt, List<Fall> into)
		{
			long last = (long)Math.Floor(seconds / SlotSeconds);
			long first = (long)Math.Floor((seconds - lifetime) / SlotSeconds);
			for (long slot = first; slot <= last; slot++)
			{
				float m = multiplierAt != null ? multiplierAt(slot * SlotSeconds) : 1f;
				if (Falls(slot, chuteSeed, boost, m, out double start, out uint seed, out int stones) && start <= seconds && start > seconds - lifetime)
				{
					into.Add(new Fall { Start = start, Seed = seed, Stones = stones });
				}
			}
		}

		/// <summary>The start of a chute's last fall at or before a moment, looking back at most <paramref name="lookBack"/> seconds; NaN for none.</summary>
		public static double LastFall(double seconds, uint chuteSeed, float boost, Func<double, float> multiplierAt, double lookBack = ScarLookBackSeconds)
		{
			long last = (long)Math.Floor(seconds / SlotSeconds);
			long first = (long)Math.Floor((seconds - lookBack) / SlotSeconds);
			for (long slot = last; slot >= first; slot--)
			{
				float m = multiplierAt != null ? multiplierAt(slot * SlotSeconds) : 1f;
				if (Falls(slot, chuteSeed, boost, m, out double start, out _, out _) && start <= seconds)
				{
					return start;
				}
			}
			return double.NaN;
		}

		/// <summary>How fresh a chute's scar is this long after its last fall (NaN: none in the look-back): 1 just after, fading over days.</summary>
		public static float ScarFreshness(double sinceFall)
		{
			if (double.IsNaN(sinceFall) || sinceFall < 0.0)
			{
				return double.IsNaN(sinceFall) ? 0f : 1f;
			}
			return (float)(ScarLasting * Math.Exp(-sinceFall / ScarDaysSeconds) + (1.0 - ScarLasting) * Math.Exp(-sinceFall / ScarHoursSeconds));
		}

		// ── A stone ──────────────────────────────────────────────

		/// <summary>One stone of a fall, hashed from the fall's seed and its index (FishRockfall.hlsl RockStoneOf).</summary>
		public struct Stone
		{
			/// <summary>Its radius, m, and where that falls between the kind's smallest and largest (0..1).</summary>
			public float Size, Rank;
			/// <summary>Seconds after the fall's start it sets off.</summary>
			public float Delay;
			/// <summary>Its line's offset to the right of the chute's, m.</summary>
			public float Across;
			public float Peak, Accel, Decel;
			/// <summary>Where it stops, metres down the chute.</summary>
			public float Stop;
			public float HopLength, HopHeight;
			public float Wobble, WobbleLength, Phase;
		}

		/// <summary>A hashed unit draw for one stone of a fall (index <paramref name="i"/>) and one property (its salt).</summary>
		public static float U(uint seed, int i, uint salt)
			=> (float)TurbidityEpisodes.Unit(TurbidityEpisodes.Mix((uint)i * 0x9E3779B1u ^ salt, seed));

		/// <summary>Stone <paramref name="i"/> of a fall down a chute of this kind, length and half width, under this gravity (m/s²).</summary>
		public static Stone StoneOf(uint fallSeed, int i, RockChuteKind kind, float length, float half, float gravity = 9.81f)
		{
			bool sciara = kind == RockChuteKind.Sciara;
			float g = Mathf.Max(0.5f, gravity);
			float rank = U(fallSeed, i, SizeSalt);
			rank *= rank;
			var s = new Stone
			{
				Rank = rank,
				Size = sciara ? Mathf.Lerp(SciaraLeast, SciaraMost, rank) : Mathf.Lerp(ScreeLeast, ScreeMost, rank),
				Delay = i == 0 ? 0f : U(fallSeed, i, DelaySalt) * StaggerSeconds,
				Across = (U(fallSeed, i, AcrossSalt) * 2f - 1f) * (sciara ? SciaraAcross : ScreeAcross) * half,
				Peak = Mathf.Lerp(LeastPeak, MostPeak, U(fallSeed, i, PeakSalt)),
				Accel = g * Mathf.Lerp(LeastAccel, MostAccel, U(fallSeed, i, AccelSalt)),
				Decel = g * Mathf.Lerp(LeastDecel, MostDecel, U(fallSeed, i, DecelSalt)),
				Stop = Mathf.Max(0f, length) * Mathf.Lerp(LeastStop, MostStop, 0.6f * rank + 0.4f * U(fallSeed, i, StopSalt)),
				HopLength = Mathf.Lerp(LeastHop, MostHop, U(fallSeed, i, HopSalt)),
				HopHeight = Mathf.Lerp(LeastHopHeight, MostHopHeight, U(fallSeed, i, HopHeightSalt)) * (0.5f + rank),
				WobbleLength = Mathf.Lerp(LeastWobble, MostWobble, U(fallSeed, i, WobbleSalt)),
				Phase = U(fallSeed, i, PhaseSalt) * 6.2831853f,
			};
			s.Wobble = 0.25f * (sciara ? SciaraAcross : ScreeAcross) * half * U(fallSeed, i, WobbleSalt ^ 0x77u);
			return s;
		}

		/// <summary>A run down a chute: speeding up, at its top speed, slowing to its stop (all from 0 at along 0).</summary>
		public struct Run
		{
			public float Peak, Accel, Decel, Stop;
			/// <summary>Seconds speeding up, at its top speed, and slowing; metres covered speeding up and by the end of its top speed.</summary>
			public float T1, T2, T3, S1, S2;
			public float Total => T1 + T2 + T3;
		}

		/// <summary>
		/// A run that speeds up at <paramref name="accel"/> to <paramref name="peak"/>, holds it, and slows at <paramref name="decel"/> to
		/// stop exactly at <paramref name="stop"/> m; a short run never reaches its top speed (it peaks lower). Pure (RockRunOf).
		/// </summary>
		public static Run RunOf(float stop, float peak, float accel, float decel)
		{
			var r = new Run { Stop = Mathf.Max(0f, stop), Accel = Mathf.Max(1e-3f, accel), Decel = Mathf.Max(1e-3f, decel), Peak = Mathf.Max(1e-3f, peak) };
			float s1 = r.Peak * r.Peak / (2f * r.Accel), sd = r.Peak * r.Peak / (2f * r.Decel);
			if (s1 + sd > r.Stop)
			{
				r.Peak = Mathf.Sqrt(Mathf.Max(1e-6f, 2f * r.Stop * r.Accel * r.Decel / (r.Accel + r.Decel)));
				s1 = r.Peak * r.Peak / (2f * r.Accel);
				sd = r.Peak * r.Peak / (2f * r.Decel);
			}
			r.T1 = r.Peak / r.Accel;
			r.S1 = s1;
			r.S2 = Mathf.Max(s1, r.Stop - sd);
			r.T2 = (r.S2 - s1) / r.Peak;
			r.T3 = r.Peak / r.Decel;
			return r;
		}

		/// <summary>Metres covered <paramref name="t"/> seconds into a run (0 before it, its stop after it). Continuous.</summary>
		public static float AlongAt(in Run r, float t)
		{
			if (t <= 0f)
			{
				return 0f;
			}
			if (t < r.T1)
			{
				return 0.5f * r.Accel * t * t;
			}
			if (t < r.T1 + r.T2)
			{
				return r.S1 + r.Peak * (t - r.T1);
			}
			float u = Mathf.Min(t - r.T1 - r.T2, r.T3);
			return Mathf.Min(r.Stop, r.S2 + r.Peak * u - 0.5f * r.Decel * u * u);
		}

		/// <summary>Its speed <paramref name="t"/> seconds into a run, m/s.</summary>
		public static float SpeedAt(in Run r, float t)
		{
			if (t <= 0f || t >= r.Total)
			{
				return 0f;
			}
			if (t < r.T1)
			{
				return r.Accel * t;
			}
			if (t < r.T1 + r.T2)
			{
				return r.Peak;
			}
			return Mathf.Max(0f, r.Peak - r.Decel * (t - r.T1 - r.T2));
		}

		/// <summary>How high a stone is off its line, m: its hops, as high as its speed allows, each one <see cref="Stone.HopLength"/> of travel.</summary>
		public static float HopAt(in Stone s, float along, float speed, float peak)
			=> s.HopHeight * Mathf.Clamp01(speed / Mathf.Max(1e-3f, peak)) * Mathf.Abs(Mathf.Sin(Mathf.PI * along / Mathf.Max(0.1f, s.HopLength)));

		/// <summary>Its sideways offset from the chute's line, m, <paramref name="along"/> metres down: its own line plus a slow wobble.</summary>
		public static float AcrossAt(in Stone s, float along) => s.Across + s.Wobble * Mathf.Sin(along / Mathf.Max(1f, s.WobbleLength) * 6.2831853f + s.Phase);

		/// <summary>
		/// Where a stone of a fall is (along the chute, m; off its line, m; height above it, m) and how much of it shows (1, then 0 once
		/// sunk) <paramref name="age"/> seconds after the fall started. Pure: FishRockfall.hlsl RockStoneState.
		/// </summary>
		public static Vector4 StateAt(in Stone s, float age)
		{
			Run run = RunOf(s.Stop, s.Peak, s.Accel, s.Decel);
			float t = age - s.Delay;
			float along = AlongAt(run, t);
			float speed = SpeedAt(run, t);
			float rest = t - run.Total;
			float shown = t <= 0f ? 0f : 1f - Mathf.Clamp01((rest - RestSeconds) / SinkSeconds);
			return new Vector4(along, AcrossAt(s, along), HopAt(s, along, speed, run.Peak), shown);
		}
	}

	/// <summary>
	/// Strombolian BOMBS (2026-10-25, phase I2u; decision 95): each burst of a Strombolian vent (VentBursts, the very burst
	/// <c>_FishVentBurst</c> follows) throws 20–80 glowing bombs (times its strength) on ballistic arcs 50–200 m long; they land, those on
	/// the sciara rolling on down its stored chute on <see cref="Rockfall"/>'s run, and cool over a minute. Pure in the wrapped motion
	/// clock and the ground (landing found once a burst on the CPU; flight and roll on the GPU).
	/// </summary>
	public static class StromboliBombs
	{
		public const int LeastBombs = 20, MostBombs = 80;
		/// <summary>A bomb's reach on level ground, m, and its launch elevation, degrees.</summary>
		public const float LeastRange = 50f, MostRange = 200f, LeastElevation = 50f, MostElevation = 80f;
		/// <summary>A bomb's radius, m (10–60 cm across).</summary>
		public const float LeastSize = 0.05f, MostSize = 0.3f;
		/// <summary>How long a bomb glows (its glow falls to nothing over this, seconds), lies, and sinks.</summary>
		public const float CoolSeconds = 60f, RestSeconds = 240f, SinkSeconds = 30f;
		/// <summary>The share of the burst's seconds bombs leave over.</summary>
		public const float LaunchShare = 0.8f;

		public const uint CountSalt = 0xB0CBu, AzimuthSalt = 0xA21Bu, ElevationSalt = 0xE1EBu, RangeSalt = 0x4A6Eu, LaunchSalt = 0x1A0Cu,
			SizeSalt = 0x5B1Eu, RollSalt = 0x4011u;

		/// <summary>
		/// The burst of a vent whose slot holds a moment of the wrapped motion clock (<paramref name="wrapped"/>, 0 … 10 000 s), as
		/// EpisodeSlots.At draws it with VentBursts' constants: its start (wrapped seconds), strength and slot; false when the slot is
		/// skipped. <paramref name="slotOffset"/> −1 asks the slot before (the wrap's last slot, its start 10 000 s earlier, before slot 0).
		/// </summary>
		public static bool BurstOf(float wrapped, uint seed, int slotOffset, out float start, out float strength, out int slot)
		{
			float length = VentBursts.PeriodOf(seed);
			float on = Math.Min(VentBursts.BurstSeconds, length);
			int slots = Mathf.Max(1, Mathf.RoundToInt(10000f / length));
			int k = (int)Math.Floor(wrapped / length) + slotOffset;
			float shift = 0f;
			if (k < 0)
			{
				k += slots;
				shift = -10000f;
			}
			slot = k;
			uint h = TurbidityEpisodes.Mix((uint)k, seed);
			start = k * length + (float)TurbidityEpisodes.Unit(h) * (length - on) + shift;
			strength = 0f;
			if (VentBursts.Skip > 0f && TurbidityEpisodes.Unit(TurbidityEpisodes.Mix(h, seed ^ EpisodeSlots.SkipSalt)) < VentBursts.Skip)
			{
				return false;
			}
			strength = VentBursts.Weakest + (1f - VentBursts.Weakest) * (float)TurbidityEpisodes.Unit(TurbidityEpisodes.Mix(h, seed ^ EpisodeSlots.StrengthSalt));
			return true;
		}

		private static float U(uint seed, int slot, int i, uint salt)
			=> (float)TurbidityEpisodes.Unit(TurbidityEpisodes.Mix((uint)i * 0x9E3779B1u ^ salt, TurbidityEpisodes.Mix((uint)slot, seed)));

		/// <summary>How many bombs a burst throws: 20–80 by its hash, times its strength (at least one).</summary>
		public static int CountOf(uint seed, int slot, float strength)
			=> Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(LeastBombs, MostBombs, U(seed, slot, 0, CountSalt)) * Mathf.Clamp01(strength)));

		/// <summary>Bomb <paramref name="i"/>'s launch: its velocity (m/s) for its reach under this gravity, seconds after the burst's start, radius.</summary>
		public static Vector3 Launch(uint seed, int slot, int i, float gravity, out float delay, out float size)
		{
			float g = Mathf.Max(0.5f, gravity);
			float azimuth = U(seed, slot, i, AzimuthSalt) * 6.2831853f;
			float elevation = Mathf.Lerp(LeastElevation, MostElevation, U(seed, slot, i, ElevationSalt)) * Mathf.Deg2Rad;
			float range = Mathf.Lerp(LeastRange, MostRange, U(seed, slot, i, RangeSalt));
			float speed = Mathf.Sqrt(range * g / Mathf.Max(0.05f, Mathf.Sin(2f * elevation)));
			delay = U(seed, slot, i, LaunchSalt) * VentBursts.BurstSeconds * LaunchShare;
			float r = U(seed, slot, i, SizeSalt);
			size = Mathf.Lerp(LeastSize, MostSize, r * r);
			float flat = speed * Mathf.Cos(elevation);
			return new Vector3(Mathf.Cos(azimuth) * flat, speed * Mathf.Sin(elevation), Mathf.Sin(azimuth) * flat);
		}

		/// <summary>A bomb's own hash for its roll down a sciara (Rockfall.StoneOf's seed).</summary>
		public static uint RollSeed(uint seed, int slot, int i) => TurbidityEpisodes.Mix((uint)i * 0x9E3779B1u ^ RollSalt, TurbidityEpisodes.Mix((uint)slot, seed)) & 0xFFFFFFu;

		/// <summary>The step a bomb's arc is walked down in looking for the ground, seconds, and how often the crossing is halved.</summary>
		public const float LandingStep = 0.25f;
		public const int LandingSteps = 400, LandingHalvings = 20;

		/// <summary>
		/// Where and when a bomb thrown from <paramref name="from"/> at <paramref name="velocity"/> meets the ground (pure in the ground):
		/// its arc walked down from its top in <see cref="LandingStep"/> s steps to the first point under the ground, the crossing then
		/// halved down. Seconds of flight; <paramref name="at"/> the point. No ground under it (NaN) counts as the vent's own level.
		/// </summary>
		public static float Landing(Vector3 from, Vector3 velocity, float gravity, Func<float, float, float> groundAt, out Vector3 at)
		{
			float g = Mathf.Max(0.5f, gravity);
			float level = groundAt != null ? groundAt(from.x, from.z) : float.NaN;
			float fallback = float.IsNaN(level) ? from.y : level;
			float Above(float t)
			{
				float y = from.y + velocity.y * t - 0.5f * g * t * t;
				float ground = groundAt != null ? groundAt(from.x + velocity.x * t, from.z + velocity.z * t) : float.NaN;
				return y - (float.IsNaN(ground) ? fallback : ground);
			}
			float lo = Mathf.Max(0f, velocity.y / g), hi = lo;
			bool crossed = false;
			for (int k = 0; k < LandingSteps; k++)
			{
				hi = lo + LandingStep;
				if (Above(hi) <= 0f)
				{
					crossed = true;
					break;
				}
				lo = hi;
			}
			if (crossed)
			{
				for (int k = 0; k < LandingHalvings; k++)
				{
					float mid = 0.5f * (lo + hi);
					if (Above(mid) > 0f)
					{
						lo = mid;
					}
					else
					{
						hi = mid;
					}
				}
			}
			float t = Mathf.Max(0.05f, hi);
			at = new Vector3(from.x + velocity.x * t, from.y + velocity.y * t - 0.5f * g * t * t, from.z + velocity.z * t);
			return t;
		}

		/// <summary>How brightly a bomb glows this long after it left the vent (0..1): white hot, cooling to nothing over <see cref="CoolSeconds"/>.</summary>
		public static float GlowAt(float sinceLaunch)
		{
			if (sinceLaunch < 0f)
			{
				return 0f;
			}
			float c = Mathf.Clamp01(1f - sinceLaunch / CoolSeconds);
			return c * c;
		}
	}

	/// <summary>
	/// BREATHING TALUS (2026-10-25, phase I2u; decision 79 (b)): a talus cone's air runs with the season, from the shared clock. In the
	/// warm half of its thermal year cold air drains out of its toe (ice in the block gaps, rime on the stones, a cold sinking mist) —
	/// but only where its winter freezes; in the cold half warm air rises out of its head (faint steam) where the air is cold enough to
	/// show it. Pure.
	/// </summary>
	public static class TalusBreath
	{
		/// <summary>The air at the head colder than this shows the steam fully, warmer than <see cref="SteamWarmest"/> not at all, °C.</summary>
		public const float SteamColdest = -6f, SteamWarmest = 2f;
		/// <summary>The winter's coldest point at the cone must reach below this for ice to hold through the summer, °C (full at <see cref="IceColdest"/>).</summary>
		public const float IceWarmest = 0f, IceColdest = -8f;

		/// <summary>
		/// The cone's summer ice (x, 0..1) and winter steam (y, 0..1) at a point of its thermal year (<paramref name="phase01"/>: 0 its
		/// coldest point, 0.5 its warmest), its coldest air (°C) and its air now (°C), both at the cone's height.
		/// </summary>
		public static Vector2 Of(float phase01, float coldestCelsius, float celsiusNow)
		{
			float warm = -Mathf.Cos(phase01 * 6.2831853f);
			float holds = Smooth((IceWarmest - coldestCelsius) / (IceWarmest - IceColdest));
			float shows = Smooth((SteamWarmest - celsiusNow) / (SteamWarmest - SteamColdest));
			return new Vector2(Mathf.Max(0f, warm) * holds, Mathf.Max(0f, -warm) * shows);
		}

		private static float Smooth(float t)
		{
			t = Mathf.Clamp01(t);
			return t * t * (3f - 2f * t);
		}
	}
}
