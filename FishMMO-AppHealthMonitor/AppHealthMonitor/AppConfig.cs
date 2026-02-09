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
		public string Name { get; set; } = string.Empty;

		/// <summary>
		/// Gets or sets the full path to the executable.
		/// Supports both Windows and Unix-style paths.
		/// </summary>
		public string ApplicationExePath { get; set; } = string.Empty;

		/// <summary>
		/// Gets or sets the optional command-line arguments for launching the application.
		/// </summary>
		public string LaunchArguments { get; set; } = string.Empty;

		/// <summary>
		/// Gets or sets the port to monitor. Set to 0 for process-only monitoring.
		/// </summary>
		public int MonitoredPort { get; set; }

		/// <summary>
		/// Gets or sets the types of ports to monitor (e.g., TCP, UDP, WebSocket).
		/// An empty list indicates process-only monitoring with no port checks.
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
		/// Note: CPU usage is calculated using <see cref="System.Environment.ProcessorCount"/>,
		/// which returns logical cores (including hyperthreaded cores on Intel/AMD CPUs).
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

		/// <summary>
		/// Gets or sets the delay in seconds before the first full health check after launch.
		/// Allows the application time to fully initialize before being evaluated.
		/// </summary>
		public int InitialHealthCheckDelaySeconds { get; set; }

		/// <summary>
		/// Gets or sets the delay in seconds to wait after launching or restarting the application
		/// before resuming health checks. Allows the process to settle.
		/// </summary>
		public int PostLaunchSettleDelaySeconds { get; set; }

		/// <summary>
		/// Gets or sets the timeout in milliseconds for TCP and UDP port health checks.
		/// </summary>
		public int PortCheckTimeoutMs { get; set; }

		/// <summary>
		/// Gets or sets the timeout in milliseconds for WebSocket port health checks.
		/// WebSocket connections typically require more time due to the upgrade handshake.
		/// </summary>
		public int WebSocketCheckTimeoutMs { get; set; }

		/// <summary>
		/// Gets or sets the timeout in seconds to wait for a force-killed process to exit.
		/// </summary>
		public int ForceKillTimeoutSeconds { get; set; }

		/// <summary>
		/// Gets or sets whether the application should be launched in headless mode.
		/// When true, the process is launched with no visible window and shell execution disabled.
		/// Recommended for production daemon deployments.
		/// </summary>
		public bool Headless { get; set; }

		/// <summary>
		/// Applies sensible defaults and validates the configuration in a single step.
		/// Ensures the configuration is ready for use without requiring separate calls.
		/// </summary>
		/// <param name="error">When validation fails, contains the error description.</param>
		/// <returns>True if the configuration is valid after applying defaults; otherwise, false.</returns>
		public bool TryApplyDefaultsAndValidate(out string error)
		{
			PortTypes ??= new List<PortType>();

			if (CheckIntervalSeconds <= 0)
			{
				CheckIntervalSeconds = 10;
			}

			if (GracefulShutdownTimeoutSeconds <= 0)
			{
				GracefulShutdownTimeoutSeconds = 10;
			}

			if (InitialRestartDelaySeconds <= 0)
			{
				InitialRestartDelaySeconds = 5;
			}

			if (MaxRestartDelaySeconds <= 0)
			{
				MaxRestartDelaySeconds = 60;
			}

			if (MaxRestartAttempts <= 0)
			{
				MaxRestartAttempts = 5;
			}

			if (CircuitBreakerFailureThreshold <= 0)
			{
				CircuitBreakerFailureThreshold = 3;
			}

			if (CircuitBreakerResetTimeoutMinutes <= 0)
			{
				CircuitBreakerResetTimeoutMinutes = 5;
			}

			if (InitialHealthCheckDelaySeconds <= 0)
			{
				InitialHealthCheckDelaySeconds = 30;
			}

			if (PostLaunchSettleDelaySeconds <= 0)
			{
				PostLaunchSettleDelaySeconds = 5;
			}

			if (PortCheckTimeoutMs <= 0)
			{
				PortCheckTimeoutMs = 2000;
			}

			if (WebSocketCheckTimeoutMs <= 0)
			{
				WebSocketCheckTimeoutMs = 5000;
			}

			if (ForceKillTimeoutSeconds <= 0)
			{
				ForceKillTimeoutSeconds = 5;
			}

			if (string.IsNullOrWhiteSpace(Name))
			{
				error = "Application Name is required.";
				return false;
			}

			if (string.IsNullOrWhiteSpace(ApplicationExePath))
			{
				error = $"ApplicationExePath is required for '{Name}'.";
				return false;
			}

			if (PortTypes.Count > 0 && (MonitoredPort < 1 || MonitoredPort > 65535))
			{
				error = $"MonitoredPort must be between 1 and 65535 when PortTypes are configured for '{Name}'.";
				return false;
			}

			if (MaxRestartDelaySeconds < InitialRestartDelaySeconds)
			{
				error = $"MaxRestartDelaySeconds ({MaxRestartDelaySeconds}) must be >= InitialRestartDelaySeconds ({InitialRestartDelaySeconds}) for '{Name}'.";
				return false;
			}

			if (CpuThresholdPercent < 0 || CpuThresholdPercent > 100)
			{
				error = $"CpuThresholdPercent must be between 0 and 100 for '{Name}'. Got: {CpuThresholdPercent}.";
				return false;
			}

			error = string.Empty;
			return true;
		}
	}
}