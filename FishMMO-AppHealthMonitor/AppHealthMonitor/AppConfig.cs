namespace AppHealthMonitor
{
	/// <summary>
	/// Represents the configuration for a single application to be monitored.
	/// This structure mirrors the expected JSON configuration in appsettings.json.
	/// </summary>
	public class AppConfig
	{
		public string Name { get; set; } // Friendly name for the application
		public string ApplicationExePath { get; set; } // Full path to the executable
		public string LaunchArguments { get; set; } // Optional arguments for launching
		public int MonitoredPort { get; set; } // Port to monitor, can be 0 if only process monitoring
		public List<PortType> PortTypes { get; set; } = new List<PortType>(); // Types of ports to monitor (e.g., TCP, UDP, WebSocket, None)
		public int CheckIntervalSeconds { get; set; } // How often to perform health checks
		public int LaunchDelaySeconds { get; set; } // Delay before starting this app after the previous one
		public int CpuThresholdPercent { get; set; } // CPU usage threshold for restart (0 for no limit)
		public int MemoryThresholdMB { get; set; } // Memory usage threshold for restart (0 for no limit)
		public int GracefulShutdownTimeoutSeconds { get; set; } // Timeout for graceful shutdown
		public int InitialRestartDelaySeconds { get; set; } // Initial delay for backoff restart
		public int MaxRestartDelaySeconds { get; set; } // Maximum delay for backoff restart
		public int MaxRestartAttempts { get; set; } // Max attempts for backoff restart before giving up
		public int CircuitBreakerFailureThreshold { get; set; } // Consecutive failures to trip circuit breaker
		public int CircuitBreakerResetTimeoutMinutes { get; set; } // Time before circuit breaker attempts reset
	}
}