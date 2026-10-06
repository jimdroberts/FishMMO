using System;

namespace FishMMO.Shared.Celestial
{
	/// <summary>
	/// "At server tick <see cref="Tick"/>, world time was <see cref="WorldSeconds"/>, and it runs at
	/// <see cref="Rate"/> world seconds to each second of ticks."
	/// </summary>
	[Serializable]
	public struct WorldClockAnchor
	{
		public uint Tick;
		/// <summary>Seconds since the calendar epoch.</summary>
		public double WorldSeconds;
		/// <summary>True once the anchor was checked against the database clock.</summary>
		public bool Verified;
		/// <summary>
		/// World seconds per second of ticks: 1 the world's own pace, 0 held still, more raced. Set by an
		/// admin (the world clock control). Every anchor states it: a default anchor (no anchor) has 0.
		/// </summary>
		public double Rate;
	}

	/// <summary>
	/// World time, carried forward by FishNet's synchronised tick and anchored to one reference clock.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every scene server measures its offset from the database server's clock and publishes an
	/// anchor. Clients never read their own wall clock: they take the anchor and advance it with
	/// the server tick FishNet already keeps in step, so every client in a scene agrees to within
	/// a tick or two, and every scene server agrees with every other.
	/// </para>
	/// <para>
	/// A correction is never a jump. A new anchor replaces the old one over <c>SlewTicks</c>, and
	/// both are shipped, so the blend is a pure function of the tick: a client that joins mid-slew
	/// computes the same value as one that was there when it began.
	/// </para>
	/// <para>
	/// <b>The tick drives it; the anchor says what time it is.</b> FishNet's tick never changes pace;
	/// an admin who sets the time, holds it or races it (the world clock control, adopted by every
	/// scene server) publishes a new anchor at the tick it happens on — the time it has reached and
	/// the new pace — so nothing drifts out of line and every client computes the same moment. Such a
	/// change is instant (<see cref="Override"/>); a correction of the clock's own drift is eased.
	/// </para>
	/// </remarks>
	public sealed class WorldClock
	{
		/// <summary>The process-wide clock. The server publishes into it; the client adopts into it.</summary>
		public static readonly WorldClock Shared = new WorldClock();

		/// <summary>An error below this is left alone. A quarter second moves a 6-hour sun by 0.004°.</summary>
		public const double RepublishThresholdSeconds = 0.25;
		/// <summary>An error above this is worth a warning: something is wrong with a host clock.</summary>
		public const double WarnThresholdSeconds = 5.0;
		/// <summary>How long a correction is eased in over.</summary>
		public const double SlewSeconds = 10.0;

		private WorldClockAnchor current;
		private WorldClockAnchor previous;
		private uint slewTicks;
		private bool hasPrevious;

		public bool HasAnchor { get; private set; }
		public WorldClockAnchor Current => current;
		public WorldClockAnchor Previous => previous;
		public uint SlewTicks => slewTicks;
		public bool HasPrevious => hasPrevious;
		/// <summary>Seconds per tick. Set from the TimeManager before use.</summary>
		public double TickDelta { get; set; } = 1.0 / 30.0;
		/// <summary>The most recent measured error, in seconds (reference − clock). Diagnostics only.</summary>
		public double LastMeasuredError { get; private set; }

		/// <summary>Raised whenever a new anchor is published or adopted.</summary>
		public event Action<WorldClock> OnAnchorChanged;

		/// <summary>World seconds at a (possibly fractional) server tick.</summary>
		public double WorldSecondsAt(double tick)
		{
			if (!HasAnchor)
			{
				return 0;
			}
			double latest = current.WorldSeconds + (tick - current.Tick) * TickDelta * current.Rate;
			if (!hasPrevious || slewTicks == 0)
			{
				return latest;
			}
			double older = previous.WorldSeconds + (tick - previous.Tick) * TickDelta * previous.Rate;
			double f = (tick - current.Tick) / slewTicks;
			if (f >= 1.0)
			{
				return latest;
			}
			if (f <= 0.0)
			{
				return older;
			}
			f = f * f * (3.0 - 2.0 * f);
			return older + (latest - older) * f;
		}

		/// <summary>Real hours since the epoch at a tick.</summary>
		public double WorldHoursAt(double tick) => WorldSecondsAt(tick) / 3600.0;

		/// <summary>World seconds per second of ticks now: 1 the world's own pace, 0 held, more raced. 1 without an anchor.</summary>
		public double Rate => HasAnchor ? current.Rate : 1.0;

		/// <summary>
		/// The (fractional) tick at which the clock reads <paramref name="worldSeconds"/>, by the current
		/// anchor; while the clock is held, the anchor's own tick (every tick reads the same moment).
		/// </summary>
		public double TickAt(double worldSeconds)
		{
			if (!HasAnchor || current.Rate <= 0.0 || TickDelta <= 0.0)
			{
				return current.Tick;
			}
			return current.Tick + (worldSeconds - current.WorldSeconds) / (TickDelta * current.Rate);
		}

		/// <summary>
		/// Sets the clock outright: at <paramref name="tick"/> the world reads <paramref name="worldSeconds"/>
		/// and runs at <paramref name="rate"/> from there. Instant — no easing — because it is a decision
		/// (an admin's, a test bed's), not a correction: the new time is exact from that tick on.
		/// </summary>
		public void Override(uint tick, double worldSeconds, double rate, bool verified)
		{
			LastMeasuredError = 0;
			Set(new WorldClockAnchor { Tick = tick, WorldSeconds = worldSeconds, Verified = verified, Rate = Math.Max(0.0, rate) }, default, false, 0);
		}

		/// <summary>
		/// Offers a fresh reading of the reference clock. Publishes (and returns true) only when there
		/// is no anchor yet, the error exceeds <see cref="RepublishThresholdSeconds"/>, or the reading
		/// verifies an unverified anchor.
		/// </summary>
		public bool Propose(uint tick, double referenceWorldSeconds, bool verified)
		{
			if (!HasAnchor)
			{
				LastMeasuredError = 0;
				Set(new WorldClockAnchor { Tick = tick, WorldSeconds = referenceWorldSeconds, Verified = verified, Rate = 1.0 }, default, false, 0);
				return true;
			}
			double error = referenceWorldSeconds - WorldSecondsAt(tick);
			LastMeasuredError = error;
			bool upgrade = verified && !current.Verified;
			if (Math.Abs(error) <= RepublishThresholdSeconds && !upgrade)
			{
				return false;
			}
			// Freeze the blend in progress so the new slew starts from what is shown right now.
			WorldClockAnchor from = new WorldClockAnchor { Tick = tick, WorldSeconds = WorldSecondsAt(tick), Verified = current.Verified, Rate = current.Rate };
			uint ticks = (uint)Math.Max(1.0, Math.Round(SlewSeconds / Math.Max(1e-6, TickDelta)));
			// A correction keeps the pace the clock runs at; only a decision (Override) changes it.
			Set(new WorldClockAnchor { Tick = tick, WorldSeconds = referenceWorldSeconds, Verified = verified || current.Verified, Rate = current.Rate }, from, Math.Abs(error) > RepublishThresholdSeconds, ticks);
			return true;
		}

		/// <summary>Adopts an anchor published by the server.</summary>
		public void Adopt(WorldClockAnchor anchor, WorldClockAnchor previousAnchor, bool hasPreviousAnchor, uint slew)
		{
			Set(anchor, previousAnchor, hasPreviousAnchor, slew);
		}

		/// <summary>Forgets everything. Used on disconnect and by tests.</summary>
		public void Reset()
		{
			current = default;
			previous = default;
			hasPrevious = false;
			slewTicks = 0;
			HasAnchor = false;
			LastMeasuredError = 0;
		}

		private void Set(WorldClockAnchor anchor, WorldClockAnchor from, bool blend, uint ticks)
		{
			previous = from;
			hasPrevious = blend;
			slewTicks = blend ? ticks : 0;
			current = anchor;
			HasAnchor = true;
			OnAnchorChanged?.Invoke(this);
		}

		/// <summary>Seconds since the epoch for a UTC instant.</summary>
		public static double WorldSecondsFromUtc(DateTime utc, long epochUnixSeconds)
		{
			DateTime epoch = DateTimeOffset.FromUnixTimeSeconds(epochUnixSeconds).UtcDateTime;
			return (utc.ToUniversalTime() - epoch).TotalSeconds;
		}

		/// <summary>Seconds since the epoch for Unix milliseconds.</summary>
		public static double WorldSecondsFromUnixMilliseconds(long unixMilliseconds, long epochUnixSeconds)
		{
			return (unixMilliseconds - epochUnixSeconds * 1000L) / 1000.0;
		}
	}
}
