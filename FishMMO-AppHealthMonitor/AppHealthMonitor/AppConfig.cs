namespace AppHealthMonitor
{
	/// <summary>
	/// Represents the configuration for a single application to be monitored.
	/// This structure mirrors the expected JSON configuration in appsettings.json.
	/// </summary>
	public class AppConfig
	{
		/// <summary>
		/// Gets or sets the friendly name for the application.
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Gets or sets the full path to the executable.
		/// Supports both Windows and Unix-style paths.
		/// </summary>
		public string ApplicationExePath { get; set; }

		/// <summary>
		/// Gets or sets the optional command-line arguments for launching the application.
		/// </summary>
		public string LaunchArguments { get; set; }

		/// <summary>
		/// Gets or sets the port to monitor. Set to 0 for process-only monitoring.
		/// </summary>
		public int MonitoredPort { get; set; }

		/// <summary>
		/// Gets or sets the types of ports to monitor (e.g., TCP, UDP, WebSocket, None).
		/// </summary>
		public List<PortType> PortTypes { get; set; } = new List<PortType>();

		/// <summary>
		/// Gets or sets how often to perform health checks in seconds.
		/// </summary>
		public int CheckIntervalSeconds { get; set; }

		/// <summary>
		/// Gets or sets the delay before starting this app after the previous one in seconds.
		/// </summary>
		public int LaunchDelaySeconds { get; set; }

		/// <summary>
		/// Gets or sets the CPU usage threshold percentage for restart (0 for no limit).
		/// </summary>
		public int CpuThresholdPercent { get; set; }

		/// <summary>
		/// Gets or sets the memory usage threshold for restart in megabytes (0 for no limit).
		/// </summary>
		public int MemoryThresholdMB { get; set; }

		/// <summary>
		/// Gets or sets the timeout for graceful shutdown in seconds.
		/// </summary>
		public int GracefulShutdownTimeoutSeconds { get; set; }

		/// <summary>
		/// Gets or sets the initial delay for backoff restart in seconds.
		/// </summary>
		public int InitialRestartDelaySeconds { get; set; }

		/// <summary>
		/// Gets or sets the maximum delay for backoff restart in seconds.
		/// </summary>
		public int MaxRestartDelaySeconds { get; set; }

		/// <summary>
		/// Gets or sets the maximum attempts for backoff restart before giving up.
		/// </summary>
		public int MaxRestartAttempts { get; set; }

		/// <summary>
		/// Gets or sets the consecutive failures required to trip the circuit breaker.
		/// </summary>
		public int CircuitBreakerFailureThreshold { get; set; }

		/// <summary>
		/// Gets or sets the time before circuit breaker attempts reset in minutes.
		/// </summary>
		public int CircuitBreakerResetTimeoutMinutes { get; set; }
	}
}