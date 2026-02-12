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
	/// <param name="IsCircuitOpen">Whether the circuit breaker is currently tripped.</param>
	/// <param name="MaxRestartsReached">Whether the monitor has exhausted all restart attempts.</param>
	/// <param name="HasCompletedInitialCheck">Whether the monitor has completed its initial health check delay.</param>
	public sealed record HealthMonitorStatus(
		string Name,
		int? ProcessId,
		bool IsRunning,
		int RestartAttempts,
		int MaxRestartAttempts,
		bool IsCircuitOpen,
		bool MaxRestartsReached,
		bool HasCompletedInitialCheck);
}