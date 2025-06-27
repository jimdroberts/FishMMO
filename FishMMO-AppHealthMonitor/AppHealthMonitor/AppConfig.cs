namespace AppHealthMonitor
{
	// Represents the configuration for a single application to be monitored.
	public class AppConfig
	{
		public string Name { get; set; }
		public string ApplicationExePath { get; set; }
		public int MonitoredPort { get; set; }
		// If this list contains ONLY PortType.None, it signifies process-only monitoring.
		public List<PortType> PortTypes { get; set; }
		public string LaunchArguments { get; set; }
		public int CheckIntervalSeconds { get; set; }
		public int LaunchDelaySeconds { get; set; }

		// New properties for robustness
		public int CpuThresholdPercent { get; set; } = 0; // 0 to disable
		public int MemoryThresholdMB { get; set; } = 0; // 0 to disable
		public int GracefulShutdownTimeoutSeconds { get; set; } = 10; // Default 10 seconds

		// Restart Backoff Strategy
		public int InitialRestartDelaySeconds { get; set; } = 5; // Default 5 seconds
		public int MaxRestartDelaySeconds { get; set; } = 60; // Default 60 seconds
		public int MaxRestartAttempts { get; set; } = 5; // Default 5 attempts per failure cycle

		// Circuit Breaker Pattern for Port Checks
		public int CircuitBreakerFailureThreshold { get; set; } = 3; // Default 3 consecutive failures
		public int CircuitBreakerResetTimeoutMinutes { get; set; } = 5; // Default 5 minutes
	}
}