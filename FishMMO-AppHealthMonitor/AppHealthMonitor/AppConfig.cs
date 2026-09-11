using System.Security;

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
		/// Which server tier's pulse to read: <c>login</c>, <c>world</c> or <c>scene</c>.
		/// </summary>
		/// <remarks>
		/// Required when <c>PortTypes</c> includes <c>DatabasePulse</c>, and ignored otherwise.
		/// </remarks>
		public string PulseTier { get; set; } = string.Empty;

		/// <summary>
		/// The name the server registers itself under, from its own <c>ServerName</c> setting.
		/// </summary>
		/// <remarks>
		/// Defaults to <see cref="Name"/>. It is a separate setting because the daemon's name
		/// for a process and the name the server registers under are configured in different
		/// files and need not match — and matching on address or port instead would be
		/// ambiguous, since the server rows are global and two hosts can share a port number.
		/// </remarks>
		public string PulseServerName { get; set; } = string.Empty;

		/// <summary>
		/// How old a pulse may be before the server counts as down. Defaults to 90 seconds.
		/// </summary>
		/// <remarks>
		/// Three missed beats at the usual 30-second interval. Too tight and a momentary
		/// database stall restarts a healthy server; too loose and a dead one lingers. If the
		/// servers' pulse interval changes, this wants changing with it.
		/// </remarks>
		public int PulseStaleSeconds { get; set; }

		/// <summary>
		/// Gets or sets the health checks to run (e.g., TCP, UDP, DatabasePulse).
		/// An empty list indicates process-only monitoring with no port checks.
		/// </summary>
		public List<PortType> PortTypes { get; set; } = [];

		/// <summary>
		/// Gets or sets how often to perform health checks in seconds.
		/// </summary>
		/// <remarks>Minimum enforced value: 5.</remarks>
		public int CheckIntervalSeconds { get; set; }

		/// <summary>
		/// Gets or sets the delay in seconds to wait after launching this application's monitor
		/// before launching the next one. Applied to the current application's config, not the next one.
		/// For example, setting this to 10 on App A means "wait 10s after A before launching B".
		/// Set to 0 for no delay. Not applied to the last application in the list.
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
		/// <remarks>Minimum enforced value: 1.</remarks>
		public int GracefulShutdownTimeoutSeconds { get; set; }

		/// <summary>
		/// Gets or sets the initial delay for backoff restart in seconds.
		/// </summary>
		/// <remarks>Minimum enforced value: 1.</remarks>
		public int InitialRestartDelaySeconds { get; set; }

		/// <summary>
		/// Gets or sets the maximum delay for backoff restart in seconds.
		/// </summary>
		/// <remarks>Minimum enforced value: 1.</remarks>
		public int MaxRestartDelaySeconds { get; set; }

		/// <summary>
		/// Gets or sets the maximum attempts for backoff restart before giving up.
		/// </summary>
		/// <remarks>Minimum enforced value: 1.</remarks>
		public int MaxRestartAttempts { get; set; }

		/// <summary>
		/// Gets or sets the consecutive failures required to trip the circuit breaker.
		/// </summary>
		/// <remarks>Minimum enforced value: 1.</remarks>
		public int CircuitBreakerFailureThreshold { get; set; }

		/// <summary>
		/// Gets or sets the delay in seconds before the first full health check after launch.
		/// Allows the application time to fully initialize before being evaluated.
		/// </summary>
		/// <remarks>Minimum enforced value: 1.</remarks>
		public int InitialHealthCheckDelaySeconds { get; set; }

		/// <summary>
		/// Gets or sets the delay in seconds to wait after launching or restarting the application
		/// before resuming health checks. Allows the process to settle.
		/// </summary>
		/// <remarks>Minimum enforced value: 1.</remarks>
		public int PostLaunchSettleDelaySeconds { get; set; }

		/// <summary>
		/// Gets or sets the timeout in milliseconds for TCP and UDP port health checks.
		/// </summary>
		/// <remarks>Minimum enforced value: 1.</remarks>
		public int PortCheckTimeoutMs { get; set; }


		/// <summary>
		/// Gets or sets the host address used for port health checks.
		/// Change to match the interface your application binds to.
		/// Defaults to "127.0.0.1" if not specified.
		/// </summary>
		public string HealthCheckHost { get; set; } = string.Empty;

		/// <summary>
		/// Gets or sets the number of consecutive CPU/memory check failures tolerated before triggering a restart.
		/// Prevents transient access errors (e.g., brief /proc access denial) from causing unnecessary restarts.
		/// Set to 1 for immediate restart on the first failure.
		/// </summary>
		/// <remarks>Minimum enforced value: 1.</remarks>
		public int ResourceCheckFailureThreshold { get; set; }

		/// <summary>
		/// Gets or sets the timeout in seconds to wait for a force-killed process to exit.
		/// </summary>
		/// <remarks>Minimum enforced value: 1.</remarks>
		public int ForceKillTimeoutSeconds { get; set; }

		/// <summary>
		/// Applies sensible defaults and validates the configuration in a single step.
		/// Ensures the configuration is ready for use without requiring separate calls.
		/// </summary>
		/// <param name="error">When validation fails, contains the error description.</param>
		/// <returns>True if the configuration is valid after applying defaults; otherwise, false.</returns>
		public bool TryApplyDefaultsAndValidate(out string error)
		{
			ApplyDefaults();

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

			string resolvedPath;
			try
			{
				resolvedPath = Path.GetFullPath(ApplicationExePath);
			}
			catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException or SecurityException)
			{
				error = $"ApplicationExePath '{ApplicationExePath}' is not a valid file path for '{Name}': {ex.Message}";
				return false;
			}

			if (!File.Exists(resolvedPath))
			{
				error = $"Executable not found at '{resolvedPath}' for '{Name}'. Verify the file exists and the path is correct.";
				return false;
			}

			// Store the resolved path to avoid redundant re-resolution in HealthMonitor.
			ApplicationExePath = resolvedPath;

			string normalizedHealthCheckHost = NormalizeHealthCheckHost(HealthCheckHost);
			if (string.IsNullOrWhiteSpace(normalizedHealthCheckHost))
			{
				error = $"HealthCheckHost '{HealthCheckHost}' is empty or invalid for '{Name}'.";
				return false;
			}

			// HealthCheckHost is guaranteed non-empty by ApplyDefaults — only validate format.
			if (Uri.CheckHostName(normalizedHealthCheckHost) == UriHostNameType.Unknown)
			{
				error = $"HealthCheckHost '{HealthCheckHost}' is not a valid hostname or IP address for '{Name}'.";
				return false;
			}

			// Store normalized host to keep probe behavior consistent across checkers.
			HealthCheckHost = normalizedHealthCheckHost;

			if (PortTypes != null && PortTypes.Contains(PortType.DatabasePulse))
			{
				if (string.IsNullOrWhiteSpace(PulseServerName))
				{
					PulseServerName = Name;
				}
				if (PulseStaleSeconds <= 0)
				{
					PulseStaleSeconds = 90;
				}

				string tier = (PulseTier ?? string.Empty).Trim().ToLowerInvariant();
				if (tier != "login" && tier != "world" && tier != "scene")
				{
					error = $"'{Name}' uses DatabasePulse but PulseTier is '{PulseTier}'. Set it to login, world or scene.";
					return false;
				}
			}

			if (MonitoredPort < 0 || MonitoredPort > 65535)
			{
				error = $"MonitoredPort must be between 0 and 65535 for '{Name}'. Got: {MonitoredPort}.";
				return false;
			}

			/* Only a check that actually opens a socket needs a port. A DatabasePulse check
			 * reads a row and never touches the network, so demanding a port for it would force
			 * an operator to invent a number — and an invented port is one somebody later
			 * believes is real. */
			bool needsPort = PortTypes.Exists(t => t == PortType.TCP || t == PortType.UDP);

			if (needsPort && MonitoredPort == 0)
			{
				error = $"MonitoredPort must be between 1 and 65535 when '{Name}' has a TCP or UDP check.";
				return false;
			}

			if (MonitoredPort > 0 && PortTypes.Count == 0)
			{
				error = $"MonitoredPort is set to {MonitoredPort} but PortTypes is empty for '{Name}'. Add PortTypes (e.g., TCP, DatabasePulse) to enable health checks, or set MonitoredPort to 0 for process-only monitoring.";
				return false;
			}

			if (MonitoredPort > 0 && !needsPort)
			{
				error = $"MonitoredPort is set to {MonitoredPort} for '{Name}', but none of its checks use a port. " +
					"A DatabasePulse check reads the server's pulse from the database and never connects to it; set MonitoredPort to 0.";
				return false;
			}

			if (CheckIntervalSeconds > 3600)
			{
				error = $"CheckIntervalSeconds ({CheckIntervalSeconds}) exceeds 3600s for '{Name}'. This will cause excessively infrequent health checks.";
				return false;
			}

			if (LaunchDelaySeconds > 3600)
			{
				error = $"LaunchDelaySeconds ({LaunchDelaySeconds}) exceeds 3600s for '{Name}'.";
				return false;
			}

			if (InitialHealthCheckDelaySeconds > 600)
			{
				error = $"InitialHealthCheckDelaySeconds ({InitialHealthCheckDelaySeconds}) exceeds 600s for '{Name}'.";
				return false;
			}

			if (PostLaunchSettleDelaySeconds > 300)
			{
				error = $"PostLaunchSettleDelaySeconds ({PostLaunchSettleDelaySeconds}) exceeds 300s for '{Name}'.";
				return false;
			}

			if (InitialRestartDelaySeconds > 600)
			{
				error = $"InitialRestartDelaySeconds ({InitialRestartDelaySeconds}) exceeds 600s for '{Name}'.";
				return false;
			}

			if (MaxRestartDelaySeconds > 3600)
			{
				error = $"MaxRestartDelaySeconds ({MaxRestartDelaySeconds}) exceeds 3600s for '{Name}'.";
				return false;
			}

			if (MaxRestartDelaySeconds < InitialRestartDelaySeconds)
			{
				error = $"MaxRestartDelaySeconds ({MaxRestartDelaySeconds}) must be >= InitialRestartDelaySeconds ({InitialRestartDelaySeconds}) for '{Name}'.";
				return false;
			}

			if (MaxRestartAttempts > 100)
			{
				error = $"MaxRestartAttempts ({MaxRestartAttempts}) exceeds 100 for '{Name}'.";
				return false;
			}

			if (ResourceCheckFailureThreshold > 100)
			{
				error = $"ResourceCheckFailureThreshold ({ResourceCheckFailureThreshold}) exceeds 100 for '{Name}'.";
				return false;
			}

			if (CircuitBreakerFailureThreshold > 100)
			{
				error = $"CircuitBreakerFailureThreshold ({CircuitBreakerFailureThreshold}) exceeds 100 for '{Name}'.";
				return false;
			}

			if (PortCheckTimeoutMs > 30000)
			{
				error = $"PortCheckTimeoutMs ({PortCheckTimeoutMs}) exceeds 30000ms for '{Name}'. This will cause excessively long health check cycles.";
				return false;
			}

			if (ForceKillTimeoutSeconds > 60)
			{
				error = $"ForceKillTimeoutSeconds ({ForceKillTimeoutSeconds}) exceeds 60s for '{Name}'. This may cause the daemon to hang during shutdown.";
				return false;
			}

			if (GracefulShutdownTimeoutSeconds > 120)
			{
				error = $"GracefulShutdownTimeoutSeconds ({GracefulShutdownTimeoutSeconds}) exceeds 120s for '{Name}'. This may cause excessively slow shutdown cycles.";
				return false;
			}

			if (CpuThresholdPercent < 0 || CpuThresholdPercent > 100)
			{
				error = $"CpuThresholdPercent must be between 0 and 100 for '{Name}'. Got: {CpuThresholdPercent}.";
				return false;
			}

			if (MemoryThresholdMB < 0)
			{
				error = $"MemoryThresholdMB must be >= 0 for '{Name}'. Got: {MemoryThresholdMB}.";
				return false;
			}

			if (MemoryThresholdMB > 1048576)
			{
				error = $"MemoryThresholdMB ({MemoryThresholdMB}) exceeds 1048576 (1TB) for '{Name}'.";
				return false;
			}

			error = string.Empty;
			return true;
		}

		/// <summary>
		/// Applies sensible minimum defaults to all configuration values that are unset or invalid.
		/// </summary>
		private void ApplyDefaults()
		{
			// JSON deserialization may set this to null.
			PortTypes ??= [];
			CheckIntervalSeconds = Math.Max(CheckIntervalSeconds, 5);
			GracefulShutdownTimeoutSeconds = Math.Max(GracefulShutdownTimeoutSeconds, 1);
			InitialRestartDelaySeconds = Math.Max(InitialRestartDelaySeconds, 1);
			MaxRestartDelaySeconds = Math.Max(MaxRestartDelaySeconds, 1);
			MaxRestartAttempts = Math.Max(MaxRestartAttempts, 1);
			CircuitBreakerFailureThreshold = Math.Max(CircuitBreakerFailureThreshold, 1);
			InitialHealthCheckDelaySeconds = Math.Max(InitialHealthCheckDelaySeconds, 1);
			PostLaunchSettleDelaySeconds = Math.Max(PostLaunchSettleDelaySeconds, 1);
			PortCheckTimeoutMs = Math.Max(PortCheckTimeoutMs, 1);
			ForceKillTimeoutSeconds = Math.Max(ForceKillTimeoutSeconds, 1);
			ResourceCheckFailureThreshold = Math.Max(ResourceCheckFailureThreshold, 1);
			LaunchDelaySeconds = Math.Max(LaunchDelaySeconds, 0);

			if (string.IsNullOrWhiteSpace(HealthCheckHost))
			{
				HealthCheckHost = "127.0.0.1";
			}

			// Deduplicate PortTypes preserving insertion order.
			if (PortTypes.Count > 1)
			{
				PortTypes = PortTypes.Distinct().ToList();
			}
		}

		/// <summary>
		/// Normalizes a configured host value for consistent health checker usage.
		/// Accepts bracketed IPv6 input (for example, <c>[::1]</c>) and stores it unbracketed.
		/// </summary>
		/// <param name="host">The raw configured host value.</param>
		/// <returns>The normalized host string, or empty string when input is blank.</returns>
		private static string NormalizeHealthCheckHost(string host)
		{
			if (string.IsNullOrWhiteSpace(host))
			{
				return string.Empty;
			}

			host = host.Trim();
			if (host.StartsWith('[') && host.EndsWith(']') && host.Length > 2)
			{
				host = host[1..^1];
			}

			return host;
		}
	}
}