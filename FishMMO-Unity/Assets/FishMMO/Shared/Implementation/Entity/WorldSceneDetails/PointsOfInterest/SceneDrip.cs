using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>What a drip's drops land on (bits 27–28 of a <see cref="SceneEmitterKind.Drip"/>'s seed). Append only: stored in scene assets.</summary>
	public enum SceneDripLanding : byte
	{
		/// <summary>A puddle of their own water round where they land: they plink, and rings spread over it.</summary>
		Puddle = 0,
		/// <summary>Wet rock, too steep or too open for water to stand: they splat and spatter.</summary>
		Rock = 1,
		/// <summary>The tip of the stalagmite they have built under the stalactite they fall from.</summary>
		Tip = 2,
	}

	/// <summary>
	/// Water dripping from a cave's roof (2026-10-20, caves livened; <see cref="SceneEmitterKind.Drip"/>): how often a drip falls,
	/// how its drops fall, when each lands, and how its seed carries the puddle under it. One definition read by the client's drops
	/// and puddle rings (CaveDrips and its shader, which repeats <see cref="FallenMetres"/>, <see cref="JitterOf"/> and the ring
	/// constants), its sound (CaveDripPresenter) and the cave dressing that bakes the drips.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Shared by every player.</b> Drop <c>k</c> of a drip lands at <c>(k + jitter(k)) · interval + phase</c> seconds of the shared
	/// motion clock (WorldMotion.Seconds), the jitter a hash of the drip and <c>k</c> within ±<see cref="Jitter"/> of the interval:
	/// irregular, as a real drip is, yet the same moment on every screen and in every ear. Nothing is integrated frame to frame;
	/// <see cref="ClockAt"/> reads the clock in double precision, so the drops never drift however long the world has run.
	/// </para>
	/// <para>
	/// <b>The fall.</b> A drop of 4–5 mm falls with the air's drag on it: v(t) = v<sub>t</sub> tanh(g t / v<sub>t</sub>), so
	/// y(t) = (v<sub>t</sub>² / g) ln cosh(g t / v<sub>t</sub>), close to free fall over the first metre or two and at its terminal
	/// <see cref="TerminalSpeed"/> down a high vault's thirty. The cave dressing keeps every drip's interval at least
	/// <see cref="FallSeconds"/> / <see cref="InFlight"/>, so no more than three of its drops are ever in the air at once.
	/// </para>
	/// </remarks>
	public static class SceneDrip
	{
		/// <summary>Gravity, m/s², and a falling drop's terminal speed in still air (a 4–5 mm drop), m/s.</summary>
		public const float Gravity = 9.81f, TerminalSpeed = 8f;

		/// <summary>The shortest interval between two drops of a drip and the step it is stored in, seconds (8 bits: 0.4–13.15 s).</summary>
		public const float LeastInterval = 0.4f, IntervalStep = 0.05f;

		/// <summary>How far a drop's moment may stray from the even beat, as a share of the interval either way.</summary>
		public const float Jitter = 0.2f;

		/// <summary>
		/// The most a drop's fall may last, in intervals: four landings in a row span at least 3 − 2·<see cref="Jitter"/> intervals,
		/// more than this, so at most three drops of a drip are ever in flight at once (the client draws three: the drops due next).
		/// </summary>
		public const float InFlight = 1.8f;

		/// <summary>The highest roof a drop is let fall from, metres, and the lowest (under it a drip is a seep down the rock).</summary>
		public const float MostHeight = 60f, LeastHeight = 0.6f;

		/// <summary>The largest puddle's radius, metres (7 bits of centimetres).</summary>
		public const float MostPuddle = 1.27f;

		/// <summary>A ripple ring's speed over the puddle (capillary-gravity waves' least, ~0.23 m/s), m/s, and how long one shows, s.</summary>
		public const float RingSpeed = 0.24f, RingSeconds = 1.3f;

		/// <summary>
		/// How long a drip's rings show, seconds: <see cref="RingSeconds"/>, or less for a quick drip, so no more than three of its
		/// rings (the last landed, the one before, the one due) are ever on its puddle.
		/// </summary>
		public static float RingLifeOf(uint seed) => Mathf.Min(RingSeconds, 1.75f * IntervalOf(seed));

		/// <summary>How long the spatter of a drop's landing is in the air, seconds.</summary>
		public const float SpatterSeconds = 0.32f;

		/// <summary>The bits of a seed: its own hash (phase and jitter) 0–11, the interval's step 12–19, the puddle's radius in cm 20–26, the landing 27–28.</summary>
		public const int IntervalShift = 12, PuddleShift = 20, LandingShift = 27;
		private const uint HashMask = 0xFFFu, IntervalMask = 0xFFu << IntervalShift, PuddleMask = 0x7Fu << PuddleShift, LandingMask = 0x3u << LandingShift;

		/// <summary>The drop numbers the client hashes a jitter for are taken modulo this (exact in a float).</summary>
		public const long CycleModulus = 65536;

		/// <summary>A drip's seed: <paramref name="hash"/>'s low 12 bits (its phase and jitter), its interval, its puddle and its landing.</summary>
		public static uint Seed(uint hash, float interval, float puddleRadius, SceneDripLanding landing)
		{
			uint step = (uint)Mathf.Clamp(Mathf.RoundToInt((interval - LeastInterval) / IntervalStep), 0, 255);
			uint cm = landing == SceneDripLanding.Puddle ? (uint)Mathf.Clamp(Mathf.RoundToInt(puddleRadius * 100f), 1, 127) : 0u;
			return (hash & HashMask) | (step << IntervalShift) | (cm << PuddleShift) | (((uint)landing & 3u) << LandingShift);
		}

		/// <summary>The seconds between a drip's drops (on the even beat), from its seed.</summary>
		public static float IntervalOf(uint seed) => LeastInterval + ((seed & IntervalMask) >> IntervalShift) * IntervalStep;

		/// <summary>The radius of a drip's puddle, metres; 0 where it has none.</summary>
		public static float PuddleOf(uint seed) => LandingOf(seed) == SceneDripLanding.Puddle ? ((seed & PuddleMask) >> PuddleShift) * 0.01f : 0f;

		/// <summary>What a drip's drops land on (3 reads as rock).</summary>
		public static SceneDripLanding LandingOf(uint seed)
		{
			uint landing = (seed & LandingMask) >> LandingShift;
			return landing > (uint)SceneDripLanding.Tip ? SceneDripLanding.Rock : (SceneDripLanding)landing;
		}

		/// <summary>Where in its beat a drip's drops fall, seconds after a whole interval of the clock.</summary>
		public static float PhaseOf(uint seed) => (seed & HashMask) / 4096f * IntervalOf(seed);

		/// <summary>Metres a drop has fallen <paramref name="seconds"/> after it lets go (with the air's drag).</summary>
		public static float FallenMetres(float seconds)
		{
			float x = Gravity * Mathf.Max(0f, seconds) / TerminalSpeed;
			// ln cosh x, without cosh's overflow: x + ln((1 + e^-2x) / 2).
			return TerminalSpeed * TerminalSpeed / Gravity * (x + (float)Math.Log(0.5 * (1.0 + Math.Exp(-2.0 * x))));
		}

		/// <summary>How fast a drop falls <paramref name="seconds"/> after it lets go, m/s.</summary>
		public static float SpeedAt(float seconds) => TerminalSpeed * (float)Math.Tanh(Gravity * Mathf.Max(0f, seconds) / TerminalSpeed);

		/// <summary>Seconds a drop takes to fall <paramref name="height"/> metres (the inverse of <see cref="FallenMetres"/>).</summary>
		public static float FallSeconds(float height)
		{
			double h = Mathf.Clamp(height, 0f, MostHeight);
			double e = Math.Exp(h * Gravity / (TerminalSpeed * TerminalSpeed));
			return (float)(TerminalSpeed / Gravity * Math.Log(e + Math.Sqrt(e * e - 1.0)));
		}

		/// <summary>The shortest interval a drip falling <paramref name="height"/> metres may have (<see cref="InFlight"/>), seconds.</summary>
		public static float LeastIntervalFor(float height) => Mathf.Max(LeastInterval, FallSeconds(height) / InFlight);

		/// <summary>
		/// A drop's stray from the even beat, as a share of the interval in [−<see cref="Jitter"/>, <see cref="Jitter"/>]: a hash of the
		/// drip's own bits and the drop's number (modulo <see cref="CycleModulus"/>). The shader's FishDripJitter is the same.
		/// </summary>
		public static float JitterOf(uint seed, long cycle)
		{
			uint k = (uint)(((cycle % CycleModulus) + CycleModulus) % CycleModulus);
			uint h = Hash(k, seed & HashMask);
			return ((h >> 8) * (1f / 16777216f) * 2f - 1f) * Jitter;
		}

		/// <summary>A PCG-style mix of two words; the shader's FishDripHash is the same.</summary>
		public static uint Hash(uint a, uint b)
		{
			unchecked
			{
				uint h = a * 747796405u + b * 2891336453u + 0x9E3779B9u;
				h = ((h >> (int)((h >> 28) + 4u)) ^ h) * 277803737u;
				return (h >> 22) ^ h;
			}
		}

		/// <summary>
		/// Where a drip is in its beat at a moment of the shared motion clock: <paramref name="cycle"/> the number of the drop whose even
		/// beat last passed (modulo <see cref="CycleModulus"/>), <paramref name="into"/> the seconds since that beat. Exact in double, so
		/// the clock's size never blurs it.
		/// </summary>
		public static void ClockAt(double seconds, uint seed, out long cycle, out float into)
		{
			double interval = IntervalOf(seed);
			double s = seconds - PhaseOf(seed);
			double k = Math.Floor(s / interval);
			into = (float)(s - k * interval);
			cycle = (long)(k - Math.Floor(k / CycleModulus) * CycleModulus);
		}

		/// <summary>
		/// Seconds from now until drop <paramref name="cycle"/> + <paramref name="offset"/> lands (negative: since it landed), from the
		/// beat <see cref="ClockAt"/> read.
		/// </summary>
		public static float SecondsToLanding(uint seed, long cycle, float into, int offset)
			=> (offset + JitterOf(seed, cycle + offset)) * IntervalOf(seed) - into;
	}
}
