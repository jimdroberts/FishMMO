using System;

namespace FishMMO.Shared.Celestial
{
	/// <summary>"At server tick <see cref="Tick"/>, world time was <see cref="WorldSeconds"/>."</summary>
	[Serializable]
	public struct WorldClockAnchor
	{
		public uint Tick;
		/// <summary>Seconds since the calendar epoch.</summary>
		public double WorldSeconds;
		/// <summary>True once the anchor was checked against the database clock.</summary>
		public bool Verified;
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
	/// Nothing sets the time. There is no API, command or action that moves the clock.
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
			double latest = current.WorldSeconds + (tick - current.Tick) * TickDelta;
			if (!hasPrevious || slewTicks == 0)
			{
				return latest;
			}
			double older = previous.WorldSeconds + (tick - previous.Tick) * TickDelta;
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
				Set(new WorldClockAnchor { Tick = tick, WorldSeconds = referenceWorldSeconds, Verified = verified }, default, false, 0);
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
			WorldClockAnchor from = new WorldClockAnchor { Tick = tick, WorldSeconds = WorldSecondsAt(tick), Verified = current.Verified };
			uint ticks = (uint)Math.Max(1.0, Math.Round(SlewSeconds / Math.Max(1e-6, TickDelta)));
			Set(new WorldClockAnchor { Tick = tick, WorldSeconds = referenceWorldSeconds, Verified = verified || current.Verified }, from, Math.Abs(error) > RepublishThresholdSeconds, ticks);
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
