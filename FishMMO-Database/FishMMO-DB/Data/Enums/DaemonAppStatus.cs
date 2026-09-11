namespace FishMMO.Database.Data.Enums
{
	/// <summary>
	/// What a daemon last reported about one supervised application.
	/// </summary>
	/// <remarks>
	/// Reported, not inferred. The panel must not decide a process is down because a row looks
	/// old — a stale row means the <em>daemon</em> stopped talking, which is a different fault
	/// with a different fix, and conflating them sends an operator to the wrong machine.
	/// </remarks>
	public enum DaemonAppStatus
	{
		/// <summary>The daemon has not determined a state yet.</summary>
		Unknown = 0,

		/// <summary>Running and passing its health checks.</summary>
		Healthy = 1,

		/// <summary>Running, but still inside its initial check delay.</summary>
		Starting = 2,

		/// <summary>Not running.</summary>
		Stopped = 3,

		/// <summary>
		/// Not running and the daemon has given up restarting it.
		/// </summary>
		/// <remarks>
		/// Distinct from <see cref="Stopped"/> on purpose: a process the daemon is still trying
		/// to revive needs patience, and one it has abandoned needs a person.
		/// </remarks>
		Exhausted = 4,

		/// <summary>Deliberately stopped by an operator, and not to be restarted.</summary>
		StoppedByOperator = 5,
	}
}
