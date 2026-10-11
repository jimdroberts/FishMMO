using System;
using FishNet.Managing.Timing;
using UnityEngine;

namespace FishMMO.Shared.Celestial
{
	/// <summary>
	/// The clock the world's own motion runs on: the sea's waves, the surf on a beach, rain and snow
	/// falling, the trees in the wind, the fog banks, the fish.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One clock, shared by every player.</b> <see cref="Seconds"/> is the world clock — the
	/// server's tick carried to world seconds by <see cref="WorldClock"/> — so two players standing
	/// side by side see the same wave at the same moment, and a rejoin or a hitch puts the sea back
	/// where everyone else has it. Every presenter used to keep a private sum of its own frame times
	/// from zero, so its phase was how long that client had been running (audit 2026-10-06).
	/// </para>
	/// <para>
	/// <b>Smoothed, never backwards.</b> FishNet re-estimates the server's tick about once a second
	/// and sets it outright, a tick or a few either way. Read raw, the waves would hitch forward and
	/// back with it. Instead this runs at real time and leans onto the world clock by at most a
	/// quarter, and only a gap too big to lean across (a join, a scene hop, a long stall) is jumped.
	/// </para>
	/// <para>
	/// <b>Never faster than real time.</b> A preview runs the world clock hundreds of times faster to
	/// watch a day go by, and the sky and the clouds keep up with it. The motion down here does not:
	/// stopped when the clock stops and slowed when it slows (<see cref="Rate"/>), but held to real
	/// time when it races. The sea was let follow the clock (2026-09-25) and it could not be made to
	/// look right: at the sky's hundred and eighty times a four-second wave comes forty times a
	/// second. Once a preview stops racing, the motion rejoins the clock in one step.
	/// </para>
	/// </remarks>
	public static class WorldMotion
	{
		/// <summary>
		/// What a presenter wraps <see cref="Seconds"/> to before a shader's float sees it: whole
		/// seconds to a millisecond, and every decimal period up to it divides it. A shader that wants
		/// a seamless wrap snaps its own periods to whole fractions of this.
		/// </summary>
		public const double ShaderWrapSeconds = 10000.0;

		/// <summary>
		/// The wrap for the sky's slow motions whose shaders scroll noise, which no wrap can make seamless
		/// (the aurora, the rain shafts, the vortices): long, so the one jump comes every eleven hours,
		/// and on every player's screen at once, while a float still holds it to 4 ms.
		/// </summary>
		public const double SkyWrapSeconds = 40000.0;

		/// <summary>A gap between the motion and the world clock wider than this is jumped, not leant across.</summary>
		public const double SnapSeconds = 0.5;
		/// <summary>How hard the motion leans onto the world clock, per second of error.</summary>
		private const double LeanPerSecond = 2.0;
		/// <summary>The most it leans: a quarter faster or slower than real time.</summary>
		private const double MaxLean = 0.25;

		private static float rate = 1f;
		private static double motion = double.NaN;
		private static double lastWorld;
		private static double lastReal = double.NaN;
		private static int frame = -1;
		private static uint seenJumps;

		/// <summary>Seconds of motion per real second, 0 to 1: the sea and its surf, what falls, and the trees.</summary>
		public static float Rate
		{
			get => rate;
			set => rate = Mathf.Clamp01(value);
		}

		/// <summary>
		/// The tick the world clock is read at. Set by whoever owns the connection (the client's
		/// weather); without one the clock is this machine's UTC, which is all an editor has.
		/// </summary>
		public static TimeManager TimeManager { get; set; }

		/// <summary>Follows a world clock running at this many world seconds per real second.</summary>
		public static void FollowClock(double worldSecondsPerSecond)
		{
			Rate = (float)Math.Max(0.0, Math.Min(1.0, worldSecondsPerSecond));
		}

		/// <summary>
		/// Seconds of motion per real second now, 0 to 1: the preview's dial (<see cref="Rate"/>) and the
		/// world clock's own pace (an admin's or the bed's hold or slow), whichever is slower. What
		/// <see cref="Seconds"/> advances at, for whatever has to step its own state a frame at a time.
		/// </summary>
		public static float Pace => Math.Min(rate, (float)Math.Min(1.0, ClockRate()));

		/// <summary>How many seconds of motion a stretch of real time is worth (<see cref="Pace"/>).</summary>
		public static float Scale(float realSeconds) => realSeconds * Pace;

		/// <summary>
		/// Seconds of world motion now, since the calendar epoch: the shared world clock in the game,
		/// smoothed (see the remarks). One value for the whole frame, so everything that moves agrees.
		/// </summary>
		public static double Seconds
		{
			get
			{
				double real = Time.realtimeSinceStartupAsDouble;
				// Once a frame in play; in the editor, frames do not always count, so by the clock too.
				if (frame == Time.frameCount && !double.IsNaN(motion) && (Application.isPlaying || real - lastReal < 0.001))
				{
					return motion;
				}
				frame = Time.frameCount;
				double world = WorldSecondsNow();
				double realStep = double.IsNaN(lastReal) ? 0.0 : Math.Max(0.0, real - lastReal);
				// The clock was set to another moment: the motion lands on it, at any pace, as everything else does.
				uint jumps = WorldClock.Shared.Jumps;
				if (jumps != seenJumps)
				{
					seenJumps = jumps;
					motion = double.NaN;
				}
				// Held or slowed by whoever set the world's pace (an admin, the bed), as by a preview's dial.
				motion = double.IsNaN(motion) ? world : Step(motion, lastWorld, world, realStep, Pace);
				lastWorld = world;
				lastReal = real;
				return motion;
			}
		}

		/// <summary><see cref="Seconds"/> wrapped to [0, <paramref name="period"/>) in double, for a float.</summary>
		public static float Wrapped(double period) => (float)Repeat(Seconds, period);

		/// <summary><paramref name="seconds"/> wrapped to [0, <paramref name="period"/>), exactly.</summary>
		public static double Repeat(double seconds, double period)
		{
			if (!(period > 0.0))
			{
				return 0.0;
			}
			double r = seconds - Math.Floor(seconds / period) * period;
			return r >= period ? 0.0 : r;
		}

		/// <summary>
		/// One step of the motion clock: where <paramref name="motion"/> goes when the world clock moved
		/// from <paramref name="lastWorld"/> to <paramref name="world"/> over <paramref name="realSeconds"/>.
		/// Pure, for the tests.
		/// </summary>
		public static double Step(double motion, double lastWorld, double world, double realSeconds, float rate)
		{
			realSeconds = Math.Max(0.0, realSeconds);
			double worldStep = world - lastWorld;
			// A preview racing the clock (a correction of a few ticks is not racing, nor is a stall,
			// whose world step matches its real one).
			bool racing = worldStep > realSeconds * 1.1 + 0.2;
			if (rate < 1f || racing)
			{
				// Slowed, stopped or racing: real time at the dial, and never past the clock.
				return motion + Math.Max(0.0, Math.Min(worldStep, realSeconds * rate));
			}
			// Against where a real-time step would land, or it settles a frame ahead of the clock.
			double error = world - (motion + realSeconds);
			if (Math.Abs(error) > SnapSeconds)
			{
				// A join, a scene hop, a corrected clock, a preview that stopped racing.
				return world;
			}
			double lean = Math.Max(-MaxLean, Math.Min(MaxLean, error * LeanPerSecond));
			return motion + realSeconds * (1.0 + lean);
		}

		/// <summary>
		/// A displayed copy of a clock-like <paramref name="target"/> (seconds) that runs at real time,
		/// leans onto the target, holds when it stops or steps back, and jumps only a gap wider than
		/// <see cref="SnapSeconds"/> — so a preview racing it is followed exactly. For things that are
		/// the weather rather than its motion (where the storm cells are), which a preview may race.
		/// </summary>
		public static double Follow(double shown, double lastTarget, double target, double realSeconds)
		{
			realSeconds = Math.Max(0.0, realSeconds);
			if (double.IsNaN(shown) || double.IsInfinity(shown) || Math.Abs(target - shown) > SnapSeconds)
			{
				return target;
			}
			if (target <= lastTarget)
			{
				return shown;
			}
			// Against where a real-time step would land, or it settles a frame ahead of the target.
			double error = target - (shown + realSeconds);
			double lean = Math.Max(-MaxLean, Math.Min(MaxLean, error * LeanPerSecond));
			return shown + realSeconds * (1.0 + lean);
		}

		/// <summary>
		/// Which window of <paramref name="windowSeconds"/> the motion clock is in, and how far into it
		/// (seconds, 0 to the window). Every player changes window at the same moment.
		/// </summary>
		/// <remarks>
		/// How a motion whose speed changes is kept seamless and shared: a phase written as
		/// speed × clock jumps whenever the speed does, by the change times the whole clock. Instead the
		/// speed is held for the window (<see cref="HeldValue"/>) and snapped so the window holds a whole
		/// number of the motion's cycles, and the phase is that speed times the time into the window:
		/// at each window's end it has come round to where the next one starts.
		/// </remarks>
		public static double Window(double windowSeconds, out long index)
		{
			double seconds = Seconds;
			double whole = Math.Floor(seconds / windowSeconds);
			index = (long)whole;
			return Math.Max(0.0, Math.Min(windowSeconds, seconds - whole * windowSeconds));
		}

		/// <summary>
		/// A value held still for each window of the motion clock (<see cref="Window"/>): taken when the
		/// window begins, or when this client first sees the window (a join part-way through).
		/// </summary>
		public struct HeldValue
		{
			private long index;
			private bool has;
			private float value;

			/// <summary>The value for window <paramref name="windowIndex"/>: <paramref name="current"/> if it has just begun.</summary>
			public float Hold(float current, long windowIndex)
			{
				if (!has || windowIndex != index)
				{
					has = true;
					index = windowIndex;
					value = current;
				}
				return value;
			}
		}

		/// <summary>
		/// <paramref name="cyclesPerSecond"/> snapped to a whole number of cycles in
		/// <paramref name="windowSeconds"/> (never negative), so a phase it drives meets itself at the
		/// window's end.
		/// </summary>
		public static float WholeCycles(float cyclesPerSecond, double windowSeconds)
		{
			double cycles = Math.Max(0.0, Math.Round(cyclesPerSecond * windowSeconds));
			return (float)(cycles / windowSeconds);
		}

		/// <summary>The pace the world clock runs at now: 1 the world's own, 0 held (an admin's or the bed's).</summary>
		private static double ClockRate()
		{
			if (FishMMO.Shared.WorldDayNightCycle.PreviewHours.HasValue)
			{
				return 1.0;
			}
			WorldClock clock = WorldClock.Shared;
			return clock.HasAnchor && (TimeManager != null || LocalWorldClock.Active != null) ? clock.Rate : 1.0;
		}

		/// <summary>The world clock now, in seconds: a preview's when one holds the sky, else the shared one.</summary>
		private static double WorldSecondsNow()
		{
			if (FishMMO.Shared.WorldDayNightCycle.PreviewHours.HasValue && FishMMO.Shared.WorldDayNightCycle.SkyClockHours.HasValue)
			{
				return FishMMO.Shared.WorldDayNightCycle.SkyClockHours.Value * 3600.0;
			}
			return WorldTime.CurrentHours(TimeManager) * 3600.0;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetOnPlay()
		{
			rate = 1f;
			motion = double.NaN;
			lastReal = double.NaN;
			lastWorld = 0.0;
			frame = -1;
			TimeManager = null;
		}
	}
}
