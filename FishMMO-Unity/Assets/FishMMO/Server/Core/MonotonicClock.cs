using System.Diagnostics;

namespace FishMMO.Server.Core
{
	/// <summary>
	/// A clock for measuring how long something has lasted inside this process.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>DateTime.UtcNow</c> is the wrong clock for a duration. It is the host's wall clock, and NTP
	/// or an operator is free to step it: forward, and everything timed against it ages at once
	/// (every instance within the step of its lifetime cap closes on the next pulse, with its
	/// players inside); backward, and nothing ages until the clock catches up again. A duration
	/// only needs a clock that never goes backwards and never jumps, which is what this is.
	/// </para>
	/// <para>
	/// The value is seconds from an arbitrary origin, so it means nothing on its own and nothing
	/// outside this process. Only differences between two readings are meaningful. It is
	/// <see cref="Stopwatch"/> underneath, so it is safe to read from any thread, which
	/// <c>UnityEngine.Time</c> is not.
	/// </para>
	/// <para>
	/// An instant that has to mean the same thing in another process, or survive a restart, is not
	/// a job for this clock. Take it from the database, which every process shares.
	/// </para>
	/// </remarks>
	public static class MonotonicClock
	{
		private static readonly double SecondsPerTick = 1.0 / Stopwatch.Frequency;

		/// <summary>Seconds on this process's monotonic clock.</summary>
		public static double NowSeconds => Stopwatch.GetTimestamp() * SecondsPerTick;

		/// <summary>
		/// The same reading in <see cref="System.TimeSpan"/> ticks (100 ns), for a store that keeps
		/// its instants as <c>long</c>s. It is not a <see cref="System.DateTime"/>'s ticks, and is
		/// never compared with one.
		/// </summary>
		public static long NowTicks => (long)(NowSeconds * System.TimeSpan.TicksPerSecond);
	}
}
