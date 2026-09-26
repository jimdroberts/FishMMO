using System.Diagnostics;

namespace FishMMO.Auth.Core
{
	/// <summary>
	/// A clock for measuring how long something has lasted inside this process.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The authenticator's own copy of the game server's <c>FishMMO.Server.Core.MonotonicClock</c>,
	/// which this library cannot reference. Internal so the two never meet in one namespace scope.
	/// </para>
	/// <para>
	/// <c>DateTime.UtcNow</c> is the wrong clock for a duration: it is the host's wall clock, and
	/// NTP or an operator may step it. Stepped forward, every pending handshake, lockout and rate
	/// window timed against it ages at once; stepped back, nothing ages until the clock catches up
	/// — pending-authentication slots stop being reclaimed and the cap fills. A duration needs a
	/// clock that never goes backwards and never jumps, which is what this is.
	/// </para>
	/// <para>
	/// Seconds from an arbitrary origin: only differences between two readings mean anything, and
	/// nothing outside this process. <see cref="Stopwatch"/> underneath, so any thread may read it.
	/// </para>
	/// </remarks>
	internal static class MonotonicClock
	{
		private static readonly double SecondsPerTick = 1.0 / Stopwatch.Frequency;

		/// <summary>Seconds on this process's monotonic clock.</summary>
		public static double NowSeconds => Stopwatch.GetTimestamp() * SecondsPerTick;
	}
}
