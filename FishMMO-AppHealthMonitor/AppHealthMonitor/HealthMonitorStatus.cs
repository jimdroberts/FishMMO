namespace AppHealthMonitor
{
	/// <summary>
	/// Immutable snapshot of a <see cref="HealthMonitor"/>'s current state for diagnostics.
	/// </summary>
	/// <param name="Name">The friendly name of the monitored application.</param>
	/// <param name="ProcessId">The OS process ID, or null if no process is currently tracked.</param>
	/// <param name="IsRunning">Whether the monitored process is currently alive.</param>
	/// <param name="RestartAttempts">The number of restart attempts in the current failure cycle.</param>
	/// <param name="MaxRestartAttempts">The configured maximum restart attempts before giving up.</param>
	/// <param name="MaxRestartsReached">Whether the monitor has exhausted all restart attempts.</param>
	/// <param name="HasCompletedInitialCheck">Whether the monitor has completed its initial health check delay.</param>
	/// <param name="ConsecutivePortFailures">The number of consecutive port check failures in the current cycle.</param>
	/// <param name="ConsecutiveResourceFailures">The number of consecutive CPU/memory check failures in the current cycle.</param>
	public sealed record HealthMonitorStatus(
		string Name,
		int? ProcessId,
		bool IsRunning,
		int RestartAttempts,
		int MaxRestartAttempts,
		bool MaxRestartsReached,
		bool HasCompletedInitialCheck,
		int ConsecutivePortFailures,
		int ConsecutiveResourceFailures)
	{
		/// <summary>
		/// Gets a human-readable state label derived from the current monitor status.
		/// Returns "EXHAUSTED" when all restart attempts are used, "DOWN" when the process is not running,
		/// "STARTING" while the process is running but still in initial delay, and "HEALTHY" otherwise.
		/// </summary>
		public string StateLabel =>
			MaxRestartsReached ? "EXHAUSTED" :
			!IsRunning ? "DOWN" :
			!HasCompletedInitialCheck ? "STARTING" :
			"HEALTHY";
	}
}