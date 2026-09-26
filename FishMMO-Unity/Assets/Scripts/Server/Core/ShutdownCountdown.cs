using System;
using FishMMO.Database.Data;

namespace FishMMO.Server.Core
{
	/// <summary>
	/// One server's countdown to an operator-scheduled shutdown, run on this process's
	/// <see cref="MonotonicClock"/> from the time remaining the database measured.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The deadline lives on the server's database row as an absolute UTC instant. Comparing that
	/// instant with <c>DateTime.UtcNow</c>, as the world and scene servers used to, compared the
	/// scheduler's clock with this host's: a host a minute fast stopped a minute early, a clock
	/// stepped forward by NTP stopped the server at once, and the world server and the scene
	/// servers clearing its players each counted down to a different moment. Every read of the row
	/// now carries the seconds left as the database measured them in the reading statement, and
	/// this class anchors that on the monotonic clock at the moment the reply arrived.
	/// </para>
	/// <para>
	/// <b>Each reading can only place the deadline late, never early.</b> The database measured
	/// the time left before the reply travelled back and was handed to the caller, so a reading's
	/// anchor, <c>arrival + remaining</c>, is late by that round trip. While the scheduled instant
	/// is unchanged, the earliest anchor seen is therefore the best estimate, and a later reading
	/// only ever tightens it. A different instant is a new schedule and starts over.
	/// </para>
	/// <para>
	/// A pure object with no Unity or network dependency, so the rule is pinned by tests. Not
	/// thread-safe: each server adopts readings on its main thread.
	/// </para>
	/// </remarks>
	public sealed class ShutdownCountdown
	{
		/// <summary>
		/// The scheduled instant as the row holds it, or <c>null</c> when nothing is scheduled.
		/// The schedule's identity: a changed value is a new countdown.
		/// </summary>
		public DateTime? ScheduledAtUtc { get; private set; }

		/// <summary>
		/// The deadline on <see cref="MonotonicClock"/>. Positive infinity when nothing is
		/// scheduled, or when a schedule arrived without a measurement.
		/// </summary>
		public double Deadline { get; private set; } = double.PositiveInfinity;

		/// <summary>Whether a shutdown is scheduled.</summary>
		public bool IsScheduled => ScheduledAtUtc.HasValue;

		/// <summary>
		/// Adopts one reading of the row.
		/// </summary>
		/// <param name="scheduledAtUtc">The row's scheduled instant, or null when none is scheduled.</param>
		/// <param name="secondsRemainingAtRead">
		/// Seconds left before it as the database measured them in the reading statement; zero or
		/// negative once it has passed. A schedule with no measurement never falls due from this
		/// reading: stopping a server on a value nobody measured is the wrong way to fail.
		/// </param>
		/// <param name="readAt">The <see cref="MonotonicClock"/> reading taken when the reply arrived.</param>
		/// <returns>True when the schedule changed: set, moved to another instant, or cancelled.</returns>
		public bool Adopt(DateTime? scheduledAtUtc, double? secondsRemainingAtRead, double readAt)
		{
			if (!scheduledAtUtc.HasValue)
			{
				bool wasScheduled = ScheduledAtUtc.HasValue;
				Clear();
				return wasScheduled;
			}

			double anchored = secondsRemainingAtRead.HasValue && !double.IsNaN(secondsRemainingAtRead.Value)
				? readAt + secondsRemainingAtRead.Value
				: double.PositiveInfinity;

			if (ScheduledAtUtc != scheduledAtUtc)
			{
				ScheduledAtUtc = scheduledAtUtc;
				Deadline = anchored;
				return true;
			}

			// The same schedule: keep the tightest anchor. See the class remarks.
			if (anchored < Deadline)
			{
				Deadline = anchored;
			}
			return false;
		}

		/// <summary>Adopts one reading of the row. See <see cref="Adopt(DateTime?, double?, double)"/>.</summary>
		/// <returns>True when the schedule changed.</returns>
		public bool Adopt(ServerControlReading reading)
		{
			return reading != null &&
				Adopt(reading.State.ShutdownAtUtc, reading.State.ShutdownInSeconds, reading.ReadAt);
		}

		/// <summary>Seconds left at <paramref name="now"/>, never negative; infinity when none is scheduled.</summary>
		/// <param name="now">A <see cref="MonotonicClock"/> reading.</param>
		public double SecondsRemaining(double now)
		{
			double remaining = Deadline - now;
			return remaining > 0.0 ? remaining : 0.0;
		}

		/// <summary>Whether the scheduled shutdown has fallen due at <paramref name="now"/>.</summary>
		/// <param name="now">A <see cref="MonotonicClock"/> reading.</param>
		public bool IsDue(double now) => IsScheduled && now >= Deadline;

		/// <summary>Forgets any schedule.</summary>
		public void Clear()
		{
			ScheduledAtUtc = null;
			Deadline = double.PositiveInfinity;
		}
	}

	/// <summary>
	/// One read of a server row's control state, stamped with when its reply arrived.
	/// </summary>
	/// <remarks>
	/// The arrival time is what anchors <see cref="ServerControlState.ShutdownInSeconds"/> on this
	/// process's clock, so it is taken by the worker that awaited the read, the moment the reply
	/// is in hand, rather than when the main thread gets round to adopting it a pulse later. A
	/// class so the pulse worker can publish it to the main thread in one atomic reference store.
	/// </remarks>
	public sealed class ServerControlReading
	{
		/// <summary>The control state as read.</summary>
		public readonly ServerControlState State;

		/// <summary>The <see cref="MonotonicClock"/> reading taken when the reply arrived.</summary>
		public readonly double ReadAt;

		/// <summary>Creates a reading.</summary>
		public ServerControlReading(ServerControlState state, double readAt)
		{
			State = state;
			ReadAt = readAt;
		}

		/// <summary>A reading whose reply has just arrived.</summary>
		public static ServerControlReading ArrivedNow(ServerControlState state)
		{
			return new ServerControlReading(state, MonotonicClock.NowSeconds);
		}
	}
}
