using System.Diagnostics;
using System.Runtime.InteropServices;
using FishMMO.Logging;

namespace AppHealthMonitor
{
	/// <summary>
	/// Monitors the health and lifecycle of a single application process.
	/// Provides automatic restart capabilities, resource monitoring, and port health checks.
	/// </summary>
	public sealed class HealthMonitor : IAsyncDisposable
	{
		private readonly IReadOnlyList<IHealthChecker> healthCheckers;
		private readonly CancellationToken cancellationToken;

		private readonly string logSource;
		private readonly string resolvedExePath;
		private readonly string launchArguments;
		private readonly TimeSpan checkInterval;
		private readonly TimeSpan gracefulShutdownTimeout;
		private readonly TimeSpan forceKillTimeout;
		private readonly long memoryThresholdBytes;
		private readonly TimeSpan initialRestartDelay;
		private readonly TimeSpan maxRestartDelay;
		private readonly TimeSpan circuitBreakerResetTimeout;
		private readonly int cpuThresholdPercent;
		private readonly int circuitBreakerFailureThreshold;
		private readonly int monitoredPort;
		private readonly int webSocketCheckTimeoutMs;
		private readonly int portCheckTimeoutMs;
		private readonly int maxRestartAttempts;
		private readonly bool headless;

		private int currentRestartAttemptCount;
		private TimeSpan currentCalculatedRestartDelay;

		private int consecutivePortCheckFailures;
		private bool isCircuitOpen;
		private DateTime circuitOpenTimestamp;

		private Process? monitoredProcess;
		private bool cpuTrackingInitialized;
		private DateTime lastCpuCheckTime;
		private TimeSpan lastCpuTotalProcessorTime;

		private bool maxRestartsReached;
		private int disposed;

		/// <summary>
		/// Reusable array for parallel port check tasks, sized to the number of health checkers.
		/// Avoids allocating a new list on every health check cycle.
		/// </summary>
		private readonly Task<bool>[] portCheckTasks;

		/// <summary>
		/// Number of bytes in one megabyte, used for memory threshold conversions.
		/// </summary>
		private const double BytesPerMB = 1_048_576.0;

		private readonly TimeSpan initialHealthCheckDelay;
		private readonly TimeSpan postLaunchSettleDelay;

		/// <summary>
		/// Gets a value indicating whether this monitor is configured for process-only monitoring without port checks.
		/// </summary>
		private bool IsProcessOnlyMonitoring => healthCheckers.Count == 0;

		/// <summary>
		/// Initializes a new instance of the <see cref="HealthMonitor"/> class.
		/// </summary>
		/// <param name="config">The application configuration. Must have <see cref="AppConfig.TryApplyDefaultsAndValidate"/> called first.</param>
		/// <param name="healthCheckers">The health checkers to use for port monitoring. Empty list for process-only monitoring.</param>
		/// <param name="cancellationToken">Token to signal cancellation of monitoring operations.</param>
		/// <exception cref="ArgumentNullException">Thrown when config is null.</exception>
		public HealthMonitor(
			AppConfig config,
			IReadOnlyList<IHealthChecker> healthCheckers,
			CancellationToken cancellationToken)
		{
			ArgumentNullException.ThrowIfNull(config);

			this.healthCheckers = healthCheckers ?? Array.Empty<IHealthChecker>();
			this.cancellationToken = cancellationToken;

			logSource = config.Name;
			resolvedExePath = Path.GetFullPath(config.ApplicationExePath);
			launchArguments = config.LaunchArguments;

			checkInterval = TimeSpan.FromSeconds(config.CheckIntervalSeconds);
			gracefulShutdownTimeout = TimeSpan.FromSeconds(config.GracefulShutdownTimeoutSeconds);
			forceKillTimeout = TimeSpan.FromSeconds(config.ForceKillTimeoutSeconds);
			initialHealthCheckDelay = TimeSpan.FromSeconds(config.InitialHealthCheckDelaySeconds);
			postLaunchSettleDelay = TimeSpan.FromSeconds(config.PostLaunchSettleDelaySeconds);
			memoryThresholdBytes = (long)config.MemoryThresholdMB * 1024 * 1024;
			initialRestartDelay = TimeSpan.FromSeconds(config.InitialRestartDelaySeconds);
			maxRestartDelay = TimeSpan.FromSeconds(config.MaxRestartDelaySeconds);
			circuitBreakerResetTimeout = TimeSpan.FromMinutes(config.CircuitBreakerResetTimeoutMinutes);
			cpuThresholdPercent = config.CpuThresholdPercent;
			circuitBreakerFailureThreshold = config.CircuitBreakerFailureThreshold;
			monitoredPort = config.MonitoredPort;
			webSocketCheckTimeoutMs = config.WebSocketCheckTimeoutMs;
			portCheckTimeoutMs = config.PortCheckTimeoutMs;
			maxRestartAttempts = config.MaxRestartAttempts;
			headless = config.Headless;

			currentCalculatedRestartDelay = initialRestartDelay;
			portCheckTasks = new Task<bool>[this.healthCheckers.Count];
		}

		/// <summary>
		/// Returns a snapshot of the current monitor status for diagnostics.
		/// </summary>
		/// <returns>A <see cref="HealthMonitorStatus"/> representing the current state.</returns>
		public HealthMonitorStatus GetStatus()
		{
			var process = Volatile.Read(ref monitoredProcess);
			int? pid = null;
			bool running = false;

			if (process != null)
			{
				try
				{
					process.Refresh();
					if (!process.HasExited)
					{
						pid = process.Id;
						running = true;
					}
				}
				catch (ObjectDisposedException)
				{
					// Process disposed concurrently — treat as not running.
				}
				catch (InvalidOperationException)
				{
					// Process exited concurrently — treat as not running.
				}
			}

			return new HealthMonitorStatus(
				logSource,
				pid,
				running,
				currentRestartAttemptCount,
				maxRestartAttempts,
				isCircuitOpen,
				maxRestartsReached);
		}

		/// <summary>
		/// Starts the monitoring loop for the application.
		/// Continuously checks application health and performs restarts as needed.
		/// Exits when cancelled or when maximum restart attempts are exhausted.
		/// </summary>
		/// <returns>A task that represents the asynchronous monitoring operation.</returns>
		public async Task StartMonitoring()
		{
			Log.Info(logSource, "Starting monitoring loop.");

			if (!IsApplicationProcessRunning())
			{
				Log.Info(logSource, "Application process not found at startup. Attempting initial launch.");
				LaunchApplication();

				if (Volatile.Read(ref monitoredProcess) == null)
				{
					Log.Critical(logSource, "Initial launch failed. The executable path may be invalid. Monitoring will not start.");
					maxRestartsReached = true;
					return;
				}

				try
				{
					await Task.Delay(postLaunchSettleDelay, cancellationToken);
				}
				catch (OperationCanceledException)
				{
					Log.Info(logSource, "Post-launch settle delay cancelled. Monitoring stopping.");
					return;
				}
			}

			Log.Info(logSource, $"Waiting {initialHealthCheckDelay.TotalSeconds} seconds before first full health check...");
			try
			{
				await Task.Delay(initialHealthCheckDelay, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				Log.Info(logSource, "Initial delay cancelled. Monitoring stopping.");
				return;
			}

			while (!cancellationToken.IsCancellationRequested && !maxRestartsReached)
			{
				Log.Debug(logSource, "Performing health check cycle.");

				bool needsRestart = false;

				if (!IsApplicationProcessRunning())
				{
					Log.Error(logSource, "Process is NOT running or has exited.");
					needsRestart = true;
				}
				else
				{
					if ((cpuThresholdPercent > 0 || memoryThresholdBytes > 0) && !CheckMemoryAndCpuUsage())
					{
						Log.Error(logSource, "CPU or Memory usage exceeds configured thresholds.");
						needsRestart = true;
					}
					else if (!IsProcessOnlyMonitoring)
					{
						needsRestart = await EvaluateCircuitBreakerAndPortHealth();
					}
				}

				if (needsRestart)
				{
					try
					{
						await HandleApplicationRestart();
					}
					catch (OperationCanceledException)
					{
						Log.Info(logSource, "Restart cancelled. Exiting monitoring loop.");
						break;
					}
				}
				else
				{
					currentRestartAttemptCount = 0;
					currentCalculatedRestartDelay = initialRestartDelay;
					Log.Info(logSource, "Application is healthy.");
				}

				try
				{
					Log.Debug(logSource, $"Waiting {checkInterval.TotalSeconds} seconds for next health check cycle...");
					await Task.Delay(checkInterval, cancellationToken);
				}
				catch (OperationCanceledException)
				{
					Log.Info(logSource, "Monitoring task cancelled. Exiting loop.");
					break;
				}
			}

			if (maxRestartsReached)
			{
				Log.Critical(logSource, $"Monitoring stopped: maximum restart attempts ({maxRestartAttempts}) exhausted.");
			}

			Log.Info(logSource, "Monitoring stopped.");
		}

		/// <summary>
		/// Evaluates the circuit breaker state and performs port health checks.
		/// </summary>
		/// <returns>True if a restart is needed; otherwise, false.</returns>
		private async Task<bool> EvaluateCircuitBreakerAndPortHealth()
		{
			if (isCircuitOpen)
			{
				var elapsed = DateTime.UtcNow - circuitOpenTimestamp;
				if (elapsed < circuitBreakerResetTimeout)
				{
					var remaining = Math.Ceiling((circuitBreakerResetTimeout - elapsed).TotalSeconds);
					Log.Warning(logSource, $"Circuit Breaker is OPEN. Skipping port checks. Resets in {remaining}s.");
					return false;
				}

				Log.Warning(logSource, "Circuit Breaker reset timeout reached. Attempting to CLOSE circuit with one port check.");
				if (await CheckApplicationPortsResponsiveness())
				{
					Log.Info(logSource, "Circuit Breaker closed successfully. Ports are healthy.");
					isCircuitOpen = false;
					consecutivePortCheckFailures = 0;
					return false;
				}

				Log.Error(logSource, "Circuit Breaker remains OPEN. Port check failed again.");
				circuitOpenTimestamp = DateTime.UtcNow;
				return true;
			}

			if (!await CheckApplicationPortsResponsiveness())
			{
				consecutivePortCheckFailures++;
				Log.Warning(logSource, $"Port check failed. Consecutive failures: {consecutivePortCheckFailures}/{circuitBreakerFailureThreshold}.");

				if (consecutivePortCheckFailures >= circuitBreakerFailureThreshold)
				{
					Log.Error(logSource, "Circuit Breaker OPEN! Too many consecutive port failures.");
					isCircuitOpen = true;
					circuitOpenTimestamp = DateTime.UtcNow;
				}
				return true;
			}

			if (consecutivePortCheckFailures > 0)
			{
				consecutivePortCheckFailures = 0;
				Log.Info(logSource, "Port check successful. Consecutive failures reset.");
			}
			return false;
		}

		/// <summary>
		/// Handles the restart logic for an unhealthy application.
		/// Implements exponential backoff and sets <see cref="maxRestartsReached"/> when the limit is hit.
		/// </summary>
		/// <returns>A task that represents the asynchronous restart operation.</returns>
		private async Task HandleApplicationRestart()
		{
			currentRestartAttemptCount++;

			if (currentRestartAttemptCount > maxRestartAttempts)
			{
				maxRestartsReached = true;
				return;
			}

			TimeSpan delayToUse = currentCalculatedRestartDelay;

			Log.Warning(logSource, $"Application unhealthy. Attempting restart (Attempt {currentRestartAttemptCount}/{maxRestartAttempts}).");
			Log.Warning(logSource, $"Next restart in {delayToUse.TotalSeconds:F1} seconds...");

			try
			{
				await Task.Delay(delayToUse, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				Log.Info(logSource, "Restart delay cancelled.");
				throw;
			}

			await KillApplicationAsync();
			LaunchApplication();

			currentCalculatedRestartDelay = TimeSpan.FromSeconds(
				Math.Min(maxRestartDelay.TotalSeconds, initialRestartDelay.TotalSeconds * Math.Pow(2, currentRestartAttemptCount - 1))
			);

			try
			{
				await Task.Delay(postLaunchSettleDelay, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				Log.Info(logSource, "Post-launch settle delay cancelled.");
				throw;
			}
		}

		/// <summary>
		/// Checks if the monitored application's CPU and memory usage are within configured thresholds.
		/// </summary>
		/// <returns>True if usage is within thresholds or thresholds are disabled; otherwise, false.</returns>
		private bool CheckMemoryAndCpuUsage()
		{
			var process = Volatile.Read(ref monitoredProcess);
			if (process == null)
			{
				Log.Debug(logSource, "Process not available for CPU/Memory check.");
				return false;
			}

			try
			{
				process.Refresh();
				if (process.HasExited)
				{
					Log.Debug(logSource, "Process not available for CPU/Memory check.");
					return false;
				}

				if (memoryThresholdBytes > 0)
				{
					long currentMemory = process.WorkingSet64;
					if (currentMemory > memoryThresholdBytes)
					{
						Log.Warning(logSource, $"Memory Usage Alert: {currentMemory / BytesPerMB:F2}MB exceeds threshold of {memoryThresholdBytes / BytesPerMB:F2}MB.");
						return false;
					}
				}

				if (cpuThresholdPercent > 0)
				{
					if (!cpuTrackingInitialized)
					{
						lastCpuCheckTime = DateTime.UtcNow;
						lastCpuTotalProcessorTime = process.TotalProcessorTime;
						cpuTrackingInitialized = true;
						Log.Debug(logSource, "Initializing CPU usage tracking.");
						return true;
					}

					TimeSpan currentTotalProcessorTime = process.TotalProcessorTime;
					DateTime currentCheckTime = DateTime.UtcNow;

					double cpuTimeUsed = (currentTotalProcessorTime - lastCpuTotalProcessorTime).TotalMilliseconds;
					double timeElapsed = (currentCheckTime - lastCpuCheckTime).TotalMilliseconds;

					if (timeElapsed > 0)
					{
						// Environment.ProcessorCount returns logical core count (includes hyperthreaded cores).
						// On HT-enabled systems, reported CPU% may appear ~50% of actual per-physical-core usage.
						double cpuUsage = (cpuTimeUsed / (timeElapsed * Environment.ProcessorCount)) * 100;

						if (cpuUsage > cpuThresholdPercent)
						{
							Log.Warning(logSource, $"CPU Usage Alert: {cpuUsage:F2}% exceeds threshold of {cpuThresholdPercent}%.");
							return false;
						}
					}

					lastCpuCheckTime = currentCheckTime;
					lastCpuTotalProcessorTime = currentTotalProcessorTime;
				}

				Log.Debug(logSource, "CPU/Memory checks passed (if configured).");
				return true;
			}
			catch (ObjectDisposedException)
			{
				// Process was disposed by a concurrent KillApplicationAsync call.
				// Treat as intentionally gone — do not flag as unhealthy.
				Log.Debug(logSource, "Process was disposed during CPU/Memory check (concurrent kill).");
				return true;
			}
			catch (InvalidOperationException ex)
			{
				Log.Error(logSource, "Process exited during CPU/Memory check.", ex);
				return false;
			}
			catch (Exception ex)
			{
				Log.Error(logSource, $"Error during CPU/Memory check: {ex.Message}", ex);
				return false;
			}
		}

		/// <summary>
		/// Checks if all configured application ports are responsive.
		/// Runs all health checks in parallel for faster evaluation.
		/// </summary>
		/// <returns>True if all configured ports are responsive; otherwise, false.</returns>
		private async Task<bool> CheckApplicationPortsResponsiveness()
		{
			if (IsProcessOnlyMonitoring)
			{
				Log.Debug(logSource, "Skipping port checks (Process-Only Monitoring).");
				return true;
			}

			for (int i = 0; i < healthCheckers.Count; i++)
			{
				var checker = healthCheckers[i];
				int timeout = checker.PortType == PortType.WebSocket ? webSocketCheckTimeoutMs : portCheckTimeoutMs;
				Log.Debug(logSource, $"Port Check: Checking port {monitoredPort} (Type: {checker.PortType})...");
				portCheckTasks[i] = checker.IsResponsiveAsync("127.0.0.1", monitoredPort, timeout, cancellationToken);
			}

			bool[] results = await Task.WhenAll(portCheckTasks);

			bool allResponsive = true;
			for (int i = 0; i < results.Length; i++)
			{
				if (!results[i])
				{
					Log.Warning(logSource, $"Port Check: Port {monitoredPort} (Type: {healthCheckers[i].PortType}) is NOT responsive.");
					allResponsive = false;
				}
			}

			if (allResponsive)
			{
				Log.Debug(logSource, "All configured ports are responsive.");
			}
			return allResponsive;
		}

		/// <summary>
		/// Checks if the monitored application process is currently running.
		/// </summary>
		/// <returns>True if the process is running; otherwise, false.</returns>
		private bool IsApplicationProcessRunning()
		{
			var process = Volatile.Read(ref monitoredProcess);
			if (process == null)
			{
				Log.Debug(logSource, "No process currently being monitored (monitoredProcess is null).");
				return false;
			}

			try
			{
				process.Refresh();
				if (process.HasExited)
				{
					Log.Info(logSource, $"Monitored process (ID: {process.Id}) has exited after refresh.");
					if (Interlocked.CompareExchange(ref monitoredProcess, null, process) == process)
					{
						process.Dispose();
					}
					return false;
				}
				Log.Debug(logSource, $"Monitored process (ID: {process.Id}) is running.");
				return true;
			}
			catch (ObjectDisposedException)
			{
				// Process was disposed by a concurrent KillApplicationAsync call.
				Log.Debug(logSource, "Process was disposed during running check (concurrent kill).");
				return false;
			}
			catch (InvalidOperationException ex)
			{
				Log.Error(logSource, "Monitored process seems to have exited unexpectedly (InvalidOperationException).", ex);
				if (Interlocked.CompareExchange(ref monitoredProcess, null, process) == process)
				{
					process.Dispose();
				}
				return false;
			}
			catch (Exception ex)
			{
				Log.Error(logSource, $"Error refreshing process state. Error: {ex.Message}", ex);
				if (Interlocked.CompareExchange(ref monitoredProcess, null, process) == process)
				{
					process.Dispose();
				}
				return false;
			}
		}

		/// <summary>
		/// Asynchronously terminates the monitored application process.
		/// Attempts graceful shutdown first (SIGTERM on Linux/macOS, CloseMainWindow on Windows),
		/// then force-kills if the process does not exit within the configured timeout.
		/// </summary>
		/// <returns>A task representing the asynchronous kill operation.</returns>
		public async Task KillApplicationAsync()
		{
			var process = Interlocked.Exchange(ref monitoredProcess, null);
			if (process == null)
			{
				Log.Debug(logSource, "KillApplication: No active process reference to kill.");
				return;
			}

			int processId = process.Id;

			try
			{
				process.Refresh();
				if (process.HasExited)
				{
					Log.Info(logSource, $"KillApplication: Process ID: {processId} has already exited.");
					return;
				}

				Log.Warning(logSource, $"KillApplication: Attempting graceful shutdown for process ID: {processId}...");

				bool gracefulShutdownAttempted = false;

				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				{
					if (process.MainWindowHandle != IntPtr.Zero)
					{
						process.CloseMainWindow();
						Log.Info(logSource, "KillApplication: Sent CloseMainWindow signal (Windows).");
						gracefulShutdownAttempted = true;
					}
					else
					{
						Log.Info(logSource, "KillApplication: No main window detected for graceful shutdown on Windows.");
					}
				}
				else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
				{
					try
					{
						// Kill(false) sends SIGTERM on .NET 8+ Linux/macOS via the internal process handle,
						// which is immune to PID recycling unlike shelling out to 'kill -15'.
						process.Kill(false);
						Log.Info(logSource, $"KillApplication: Sent SIGTERM to process ID: {processId} (Unix).");
						gracefulShutdownAttempted = true;
					}
					catch (Exception ex)
					{
						Log.Warning(logSource, $"KillApplication: Failed to send SIGTERM: {ex.Message}");
					}
				}

				if (gracefulShutdownAttempted)
				{
					Log.Debug(logSource, $"KillApplication: Waiting for process ID: {processId} to exit gracefully ({gracefulShutdownTimeout.TotalSeconds}s timeout).");
					try
					{
						await process.WaitForExitAsync(CancellationToken.None)
							.WaitAsync(gracefulShutdownTimeout, CancellationToken.None);
						Log.Info(logSource, $"KillApplication: Process ID: {processId} exited gracefully.");
						return;
					}
					catch (TimeoutException)
					{
						Log.Warning(logSource, $"KillApplication: Process ID: {processId} did not exit gracefully within {gracefulShutdownTimeout.TotalSeconds}s. Proceeding with force kill.");
					}
				}

				try
				{
					process.Refresh();
					if (process.HasExited)
					{
						Log.Info(logSource, $"KillApplication: Process ID: {processId} exited during graceful shutdown attempt.");
						return;
					}
				}
				catch (InvalidOperationException)
				{
					Log.Info(logSource, $"KillApplication: Process ID: {processId} exited during graceful shutdown attempt.");
					return;
				}

				Log.Error(logSource, $"KillApplication: Force killing process ID: {processId} and its children...");
				process.Kill(true);

				try
				{
					await process.WaitForExitAsync(CancellationToken.None)
						.WaitAsync(forceKillTimeout, CancellationToken.None);
					Log.Info(logSource, $"KillApplication: Process ID: {processId} and its children killed successfully.");
				}
				catch (TimeoutException)
				{
					Log.Critical(logSource, $"KillApplication: Critical Warning: Process ID: {processId} did not exit even after force kill ({forceKillTimeout.TotalSeconds}s). It might be stuck!");
				}
			}
			catch (InvalidOperationException ex)
			{
				Log.Error(logSource, $"KillApplication: Process ID: {processId} already exited or invalid handle.", ex);
			}
			catch (Exception ex)
			{
				Log.Critical(logSource, $"KillApplication: Error during application kill for process ID: {processId}. Error: {ex.Message}", ex);
			}
			finally
			{
				process.Dispose();
			}
		}

		/// <summary>
		/// Launches the monitored application process with configured arguments.
		/// </summary>
		private void LaunchApplication()
		{
			Log.Info(logSource, $"Launching application '{resolvedExePath}' with arguments: '{launchArguments}' (Headless: {headless})...");
			try
			{
				var old = Interlocked.Exchange(ref monitoredProcess, null);
				old?.Dispose();

				var startInfo = new ProcessStartInfo
				{
					FileName = resolvedExePath,
					Arguments = launchArguments,
					UseShellExecute = !headless,
					RedirectStandardOutput = false,
					RedirectStandardError = false,
					CreateNoWindow = headless,
				};

				var launched = Process.Start(startInfo);
				Volatile.Write(ref monitoredProcess, launched);
				if (launched != null)
				{
					Log.Info(logSource, $"Application launched successfully. Process ID: {launched.Id}");
					cpuTrackingInitialized = false;
				}
				else
				{
					Log.Warning(logSource, $"Warning: Process.Start returned null for '{resolvedExePath}'. This might indicate a problem.");
				}
			}
			catch (System.ComponentModel.Win32Exception ex)
			{
				Log.Critical(logSource, $"Error launching application '{resolvedExePath}'. Check if the path is correct and the executable exists. Error: {ex.Message}", ex);
				Volatile.Write(ref monitoredProcess, null);
			}
			catch (Exception ex)
			{
				Log.Critical(logSource, $"Unexpected error launching application '{resolvedExePath}'. Error: {ex.Message}", ex);
				Volatile.Write(ref monitoredProcess, null);
			}
		}

		/// <summary>
		/// Disposes of the monitor by killing any active process.
		/// </summary>
		/// <returns>A <see cref="ValueTask"/> representing the asynchronous dispose operation.</returns>
		public async ValueTask DisposeAsync()
		{
			if (Interlocked.CompareExchange(ref disposed, 1, 0) != 0)
			{
				return;
			}
			await KillApplicationAsync();
		}
	}
}