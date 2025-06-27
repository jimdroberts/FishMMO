using System.Diagnostics;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Runtime.InteropServices; // Added for OSPlatform detection

namespace AppHealthMonitor
{
	public class HealthMonitor
	{
		private readonly string appName;
		private readonly string applicationExePath;
		private readonly int monitoredPort;
		private readonly List<PortType> portTypes;
		private readonly string launchArguments;
		private readonly TimeSpan checkInterval;
		private readonly CancellationToken cancellationToken;

		// Configuration for graceful shutdown
		private readonly TimeSpan gracefulShutdownTimeout;

		// Configuration for CPU/Memory thresholds
		private readonly int cpuThresholdPercent;
		private readonly long memoryThresholdBytes; // Stored in bytes for comparison

		// Fields for restart backoff
		private readonly TimeSpan initialRestartDelay;
		private readonly TimeSpan maxRestartDelay;
		private readonly int maxRestartAttempts;
		private int _currentRestartAttemptCount;
		private TimeSpan _currentCalculatedRestartDelay;
		private DateTime _lastRestartAttemptTime;

		// Fields for Circuit Breaker
		private readonly int circuitBreakerFailureThreshold;
		private readonly TimeSpan circuitBreakerResetTimeout;
		private int _consecutivePortCheckFailures;
		private bool _isCircuitOpen;
		private DateTime _circuitOpenTimestamp;


		private Process monitoredProcess;
		private DateTime _lastCpuCheckTime;
		private TimeSpan _lastCpuTotalProcessorTime;


		private const int MaxHealthCheckRetries = 5;
		private readonly TimeSpan initialHealthCheckDelay = TimeSpan.FromSeconds(30);
		// private readonly TimeSpan processCrashRestartDelay = TimeSpan.FromSeconds(10); // Replaced by backoff strategy


		// Helper property to determine if only process monitoring is active
		private bool IsProcessOnlyMonitoring => this.portTypes.Count == 1 && this.portTypes.Contains(PortType.None);


		/// <summary>
		/// Initializes a new instance of the HealthMonitor class.
		/// </summary>
		/// <param name="appName">A friendly name for the application being monitored.</param>
		/// <param name="applicationExePath">The full path to the application's executable.</param>
		/// <param name="applicationProcessName">The name of the application's process (e.g., "myapp" for myapp.exe).</param>
		/// <param name="monitoredPort">The TCP/UDP/WebSocket port the application should be listening on. Can be 0 if PortTypes contains only None.</param>
		/// <param name="portTypes">A list of network port types to monitor (TCP, UDP, WebSocket). If this list contains ONLY PortType.None, then only process status will be monitored.</param>
		/// <param name="launchArguments">Optional arguments to pass when launching the application.</param>
		/// <param name="checkInterval">The time interval between health checks.</param>
		/// <param name="cpuThresholdPercent">CPU usage percentage threshold (0-100) before a restart is triggered. 0 to disable.</param>
		/// <param name="memoryThresholdMB">Memory usage threshold in MB before a restart is triggered. 0 to disable.</param>
		/// <param name="gracefulShutdownTimeoutSeconds">Time in seconds to wait for graceful shutdown before force killing.</param>
		/// <param name="initialRestartDelaySeconds">Initial delay in seconds for restart backoff after a failure.</param>
		/// <param name="maxRestartDelaySeconds">Maximum delay in seconds for restart backoff.</param>
		/// <param name="maxRestartAttempts">Maximum number of restart attempts before stopping backoff for this failure cycle.</param>
		/// <param name="circuitBreakerFailureThreshold">Number of consecutive port check failures to open the circuit.</param>
		/// <param name="circuitBreakerResetTimeoutMinutes">Time in minutes before attempting to close the circuit (after it's been open).</param>
		/// <param name="cancellationToken">A shared cancellation token to stop monitoring gracefully.</param>
		public HealthMonitor(
			string appName,
			string applicationExePath,
			int monitoredPort,
			List<PortType> portTypes,
			string launchArguments,
			TimeSpan checkInterval,
			int cpuThresholdPercent,
			int memoryThresholdMB,
			int gracefulShutdownTimeoutSeconds,
			int initialRestartDelaySeconds,
			int maxRestartDelaySeconds,
			int maxRestartAttempts,
			int circuitBreakerFailureThreshold,
			int circuitBreakerResetTimeoutMinutes,
			CancellationToken cancellationToken)
		{
			this.appName = appName ?? throw new ArgumentNullException(nameof(appName));
			this.applicationExePath = Path.GetFullPath(applicationExePath ?? throw new ArgumentNullException(nameof(applicationExePath)));
			this.monitoredPort = monitoredPort;
			this.portTypes = portTypes?.Any() == true ? portTypes : new List<PortType> { PortType.None };
			this.launchArguments = launchArguments ?? string.Empty;
			this.checkInterval = checkInterval;
			this.cancellationToken = cancellationToken;

			// New thresholds
			this.cpuThresholdPercent = cpuThresholdPercent;
			this.memoryThresholdBytes = (long)memoryThresholdMB * 1024 * 1024; // Convert MB to Bytes
			this.gracefulShutdownTimeout = TimeSpan.FromSeconds(gracefulShutdownTimeoutSeconds > 0 ? gracefulShutdownTimeoutSeconds : 10);

			// Backoff strategy
			this.initialRestartDelay = TimeSpan.FromSeconds(initialRestartDelaySeconds > 0 ? initialRestartDelaySeconds : 5);
			this.maxRestartDelay = TimeSpan.FromSeconds(maxRestartDelaySeconds > 0 ? maxRestartDelaySeconds : 60);
			this.maxRestartAttempts = maxRestartAttempts > 0 ? maxRestartAttempts : 5;
			_currentRestartAttemptCount = 0;
			_currentCalculatedRestartDelay = this.initialRestartDelay;
			_lastRestartAttemptTime = DateTime.MinValue; // Initialize to a very old date

			// Circuit Breaker
			this.circuitBreakerFailureThreshold = circuitBreakerFailureThreshold > 0 ? circuitBreakerFailureThreshold : 3;
			this.circuitBreakerResetTimeout = TimeSpan.FromMinutes(circuitBreakerResetTimeoutMinutes > 0 ? circuitBreakerResetTimeoutMinutes : 5);
			_consecutivePortCheckFailures = 0;
			_isCircuitOpen = false;
			_circuitOpenTimestamp = DateTime.MinValue;

			this.monitoredProcess = null;
			_lastCpuCheckTime = DateTime.MinValue;
			_lastCpuTotalProcessorTime = TimeSpan.Zero;
		}

		public async Task StartMonitoring()
		{
			Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Starting monitoring loop.");

			// Initial launch of the application if it's not already running
			if (!IsApplicationProcessRunning())
			{
				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Application process not found at startup. Attempting initial launch...");
				LaunchApplication();
				// Give it a moment to start up after initial launch
				await Task.Delay(TimeSpan.FromSeconds(5), this.cancellationToken);
			}

			Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Waiting {this.initialHealthCheckDelay.TotalSeconds} seconds before first full health check...");
			try
			{
				await Task.Delay(this.initialHealthCheckDelay, this.cancellationToken);
			}
			catch (OperationCanceledException)
			{
				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Initial delay cancelled. Monitoring stopping.");
				return;
			}

			while (!this.cancellationToken.IsCancellationRequested)
			{
				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Performing health check cycle.");

				bool needsRestart = false;

				// 1. Check if the process is running
				if (!IsApplicationProcessRunning())
				{
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Process is NOT running or has exited.");
					Console.ResetColor();
					needsRestart = true;
				}
				else // Process IS running, proceed with other checks
				{
					// 2. Check CPU and Memory usage if thresholds are set
					if ((this.cpuThresholdPercent > 0 || this.memoryThresholdBytes > 0) && !CheckMemoryAndCpuUsage())
					{
						Console.ForegroundColor = ConsoleColor.Red;
						Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] CPU or Memory usage exceeds configured thresholds.");
						Console.ResetColor();
						needsRestart = true;
					}
					// 3. Perform port checks if not in process-only mode and not already flagged for restart
					else if (!IsProcessOnlyMonitoring)
					{
						// Check Circuit Breaker state before attempting port checks
						if (_isCircuitOpen)
						{
							if (DateTime.Now - _circuitOpenTimestamp < circuitBreakerResetTimeout)
							{
								Console.ForegroundColor = ConsoleColor.DarkYellow;
								Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Circuit Breaker is OPEN. Skipping port checks for now. Resets in {Math.Ceiling((circuitBreakerResetTimeout - (DateTime.Now - _circuitOpenTimestamp)).TotalSeconds)}s.");
								Console.ResetColor();
								// Consider this a 'healthy' check for the purposes of the main loop, but don't reset restart attempt count
								// We are actively avoiding port checks due to previous failures.
							}
							else
							{
								Console.ForegroundColor = ConsoleColor.Yellow;
								Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Circuit Breaker reset timeout reached. Attempting to CLOSE circuit with one port check.");
								Console.ResetColor();
								// Attempt a single health check to see if the service has recovered
								if (await CheckApplicationPortsResponsiveness())
								{
									Console.ForegroundColor = ConsoleColor.Green;
									Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Circuit Breaker closed successfully. Ports are healthy.");
									Console.ResetColor();
									_isCircuitOpen = false;
									_consecutivePortCheckFailures = 0; // Reset failures on recovery
								}
								else
								{
									Console.ForegroundColor = ConsoleColor.Red;
									Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Circuit Breaker remains OPEN. Port check failed again.");
									Console.ResetColor();
									_circuitOpenTimestamp = DateTime.Now; // Reset open timestamp
									needsRestart = true; // Still unhealthy
								}
							}
						}
						else // Circuit is CLOSED, perform regular port checks
						{
							if (!await CheckApplicationPortsResponsiveness())
							{
								_consecutivePortCheckFailures++;
								Console.ForegroundColor = ConsoleColor.Yellow;
								Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Port check failed. Consecutive failures: {_consecutivePortCheckFailures}/{circuitBreakerFailureThreshold}.");
								Console.ResetColor();

								if (_consecutivePortCheckFailures >= circuitBreakerFailureThreshold)
								{
									Console.ForegroundColor = ConsoleColor.Red;
									Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Circuit Breaker OPEN! Too many consecutive port failures.");
									Console.ResetColor();
									_isCircuitOpen = true;
									_circuitOpenTimestamp = DateTime.Now;
									needsRestart = true;
								}
								else
								{
									// Not enough failures to open circuit, but port is unhealthy.
									needsRestart = true;
								}
							}
							else
							{
								// Port checks successful, reset consecutive failures
								if (_consecutivePortCheckFailures > 0)
								{
									Console.ForegroundColor = ConsoleColor.Green;
									Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Port check successful. Consecutive failures reset.");
									Console.ResetColor();
								}
								_consecutivePortCheckFailures = 0;
							}
						}
					}
				}

				// Handle restart if needed
				if (needsRestart)
				{
					await HandleApplicationRestart();
				}
				else
				{
					// If all checks pass, reset restart attempt counter and delay for the next failure cycle
					_currentRestartAttemptCount = 0;
					_currentCalculatedRestartDelay = this.initialRestartDelay;
					_isCircuitOpen = false; // Close circuit if application recovered
					_consecutivePortCheckFailures = 0;
					Console.ForegroundColor = ConsoleColor.Green;
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Application is healthy.");
					Console.ResetColor();
				}

				// Wait for the regular check interval before the next main health check cycle
				try
				{
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Waiting {this.checkInterval.TotalSeconds} seconds for next main health check cycle...");
					await Task.Delay(this.checkInterval, this.cancellationToken);
				}
				catch (OperationCanceledException)
				{
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Monitoring task cancelled.");
					break;
				}
			}
			Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Monitoring stopped.");
		}

		/// <summary>
		/// Handles the restart logic including backoff.
		/// </summary>
		private async Task HandleApplicationRestart()
		{
			_currentRestartAttemptCount++;
			TimeSpan delayToUse = _currentCalculatedRestartDelay;

			Console.ForegroundColor = ConsoleColor.Magenta;
			Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Application unhealthy. Attempting restart (Attempt {_currentRestartAttemptCount}/{maxRestartAttempts}).");
			Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Next restart in {delayToUse.TotalSeconds:F1} seconds...");
			Console.ResetColor();

			try
			{
				await Task.Delay(delayToUse, this.cancellationToken);
			}
			catch (OperationCanceledException)
			{
				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Restart delay cancelled.");
				throw; // Re-throw to propagate cancellation up to StartMonitoring
			}

			if (_currentRestartAttemptCount >= maxRestartAttempts)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Max restart attempts ({maxRestartAttempts}) reached. Stopping monitoring for this application.");
				Console.ResetColor();
				// Optionally: log critical error, send final alert, then exit this monitor task
				this.cancellationToken.ThrowIfCancellationRequested(); // Force cancellation for this specific monitor
				return;
			}

			KillApplication();
			LaunchApplication();
			_lastRestartAttemptTime = DateTime.Now;

			// Exponential backoff calculation:
			// current_delay = min(max_delay, initial_delay * 2^attempt_count)
			_currentCalculatedRestartDelay = TimeSpan.FromSeconds(
				Math.Min(maxRestartDelay.TotalSeconds, initialRestartDelay.TotalSeconds * Math.Pow(2, _currentRestartAttemptCount - 1))
			);

			// Give it a moment to start up after restart
			await Task.Delay(TimeSpan.FromSeconds(5), this.cancellationToken);
		}

		/// <summary>
		/// Checks the current CPU and Memory usage of the monitored process against configured thresholds.
		/// </summary>
		/// <returns>True if CPU and Memory are within thresholds, false otherwise.</returns>
		private bool CheckMemoryAndCpuUsage()
		{
			if (this.monitoredProcess == null || this.monitoredProcess.HasExited)
			{
				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Process not available for CPU/Memory check.");
				return false;
			}

			try
			{
				this.monitoredProcess.Refresh();

				// Memory Check (Working Set)
				if (this.memoryThresholdBytes > 0)
				{
					long currentMemory = this.monitoredProcess.WorkingSet64; // In bytes
					if (currentMemory > this.memoryThresholdBytes)
					{
						Console.ForegroundColor = ConsoleColor.Red;
						Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Memory Usage Alert: {BytesToMB(currentMemory):F2}MB exceeds threshold of {BytesToMB(this.memoryThresholdBytes):F2}MB.");
						Console.ResetColor();
						return false;
					}
				}

				// CPU Check (requires tracking over time)
				if (this.cpuThresholdPercent > 0)
				{
					if (_lastCpuCheckTime == DateTime.MinValue || _lastCpuTotalProcessorTime == TimeSpan.Zero)
					{
						// First check, initialize values
						_lastCpuCheckTime = DateTime.Now;
						_lastCpuTotalProcessorTime = this.monitoredProcess.TotalProcessorTime;
						Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Initializing CPU usage tracking.");
						return true; // Consider healthy for the first check
					}

					TimeSpan currentTotalProcessorTime = this.monitoredProcess.TotalProcessorTime;
					DateTime currentCheckTime = DateTime.Now;

					double cpuTimeUsed = (currentTotalProcessorTime - _lastCpuTotalProcessorTime).TotalMilliseconds;
					double timeElapsed = (currentCheckTime - _lastCpuCheckTime).TotalMilliseconds;

					if (timeElapsed > 0)
					{
						// Calculate CPU usage percentage (cores * 100)
						// Environment.ProcessorCount gives logical core count
						double cpuUsage = (cpuTimeUsed / (timeElapsed * Environment.ProcessorCount)) * 100;

						if (cpuUsage > this.cpuThresholdPercent)
						{
							Console.ForegroundColor = ConsoleColor.Red;
							Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] CPU Usage Alert: {cpuUsage:F2}% exceeds threshold of {this.cpuThresholdPercent}%.");
							Console.ResetColor();
							return false;
						}
					}

					// Update for next check
					_lastCpuCheckTime = currentCheckTime;
					_lastCpuTotalProcessorTime = currentTotalProcessorTime;
				}

				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] CPU/Memory checks passed (if configured).");
				return true;
			}
			catch (InvalidOperationException)
			{
				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Process exited during CPU/Memory check.");
				return false;
			}
			catch (Exception ex)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Error during CPU/Memory check: {ex.Message}");
				Console.ResetColor();
				return false;
			}
		}

		private double BytesToMB(long bytes)
		{
			return bytes / (1024.0 * 1024.0);
		}

		/// <summary>
		/// Checks only the responsiveness of the configured ports. Assumes the process is already running.
		/// </summary>
		/// <returns>True if all configured ports are responsive, false otherwise.</returns>
		private async Task<bool> CheckApplicationPortsResponsiveness()
		{
			// If configured for process-only monitoring via PortType.None, no port checks are needed.
			if (IsProcessOnlyMonitoring)
			{
				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Skipping port checks (Process-Only Monitoring).");
				return true;
			}

			// If PortTypes is empty or only contains 'None', and we're not in full process-only mode,
			// this implies no specific port monitoring is needed, but process is still considered for health.
			// This case should ideally be caught by IsProcessOnlyMonitoring, but as a fallback:
			if (!this.portTypes.Any(pt => pt != PortType.None) && monitoredPort <= 0)
			{
				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] No valid port types or port configured. Considering ports healthy by default.");
				return true;
			}


			foreach (var portType in this.portTypes)
			{
				if (portType == PortType.None)
				{
					continue; // Skip PortType.None in the iteration
				}

				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Port Check: Attempting to connect to port {this.monitoredPort} (Type: {portType})...");
				bool currentPortTypeResponsive = false;

				switch (portType)
				{
					case PortType.TCP:
						currentPortTypeResponsive = await IsTcpPortResponsive("127.0.0.1", this.monitoredPort, 2000);
						break;
					case PortType.UDP:
						currentPortTypeResponsive = await IsUdpPortResponsive("127.0.0.1", this.monitoredPort, 2000);
						break;
					case PortType.WebSocket:
						currentPortTypeResponsive = await IsWebSocketPortResponsive($"ws://127.0.0.1:{this.monitoredPort}", 5000);
						break;
					default:
						Console.ForegroundColor = ConsoleColor.Yellow;
						Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Port Check: Unsupported PortType '{portType}'. Skipping this check.");
						Console.ResetColor();
						currentPortTypeResponsive = false; // Treat unsupported as unresponsive for safety
						break;
				}

				if (!currentPortTypeResponsive)
				{
					Console.ForegroundColor = ConsoleColor.Yellow;
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Port Check: Port {this.monitoredPort} (Type: {portType}) is NOT responsive.");
					Console.ResetColor();
					return false; // If any port check fails, the overall port health is false
				}
			}
			Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] All configured ports are responsive.");
			return true; // All configured ports are responsive
		}

		/// <summary>
		/// Determines if the specific process managed by this monitor is currently running.
		/// </summary>
		private bool IsApplicationProcessRunning()
		{
			if (this.monitoredProcess == null)
			{
				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Process check: monitoredProcess reference is null.");
				return false;
			}

			try
			{
				this.monitoredProcess.Refresh();
				if (this.monitoredProcess.HasExited)
				{
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Process check: Monitored process (ID: {this.monitoredProcess.Id}) has exited.");
					this.monitoredProcess = null;
					return false;
				}
				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Process check: Monitored process (ID: {this.monitoredProcess.Id}, Name: {this.monitoredProcess.ProcessName}) is running.");
				return true;
			}
			catch (InvalidOperationException)
			{
				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Process check: Monitored process seems to have exited (InvalidOperationException).");
				this.monitoredProcess = null;
				return false;
			}
			catch (Exception ex)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Process check: Error refreshing process state for ID: {this.monitoredProcess?.Id}. Error: {ex.Message}");
				Console.ResetColor();
				this.monitoredProcess = null;
				return false;
			}
		}

		/// <summary>
		/// Attempts to gracefully shut down the application process, then kills it if it doesn't exit.
		/// Handles OS-specific graceful shutdown.
		/// </summary>
		private void KillApplication()
		{
			if (this.monitoredProcess == null)
			{
				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] No active process reference to kill.");
				return;
			}

			try
			{
				this.monitoredProcess.Refresh();
				if (this.monitoredProcess.HasExited)
				{
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Process ID: {this.monitoredProcess.Id} (Name: {this.monitoredProcess.ProcessName}) has already exited, no need to kill.");
					this.monitoredProcess = null;
					return;
				}

				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Attempting graceful shutdown for process ID: {this.monitoredProcess.Id} (Name: {this.monitoredProcess.ProcessName})...");
				Console.ResetColor();

				bool gracefulShutdownAttempted = false;

				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				{
					// Windows: Try to close main window for GUI apps
					if (this.monitoredProcess.MainWindowHandle != IntPtr.Zero)
					{
						this.monitoredProcess.CloseMainWindow();
						Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Sent CloseMainWindow signal (Windows).");
						gracefulShutdownAttempted = true;
					}
					else
					{
						Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] No main window detected for graceful shutdown on Windows via CloseMainWindow. Falling back to Process.Kill(true).");
					}
				}
				// For .NET 6+, Process.Kill(true) sends SIGTERM to process tree on Unix-like systems (Linux, macOS).
				// This is the preferred method over explicitly calling 'kill' command.
				else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
				{
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Relying on Process.Kill(true) for graceful termination on Unix-like OS (Linux/macOS), which sends SIGTERM to process tree.");
					// On Unix-like systems, Process.Kill(true) *is* the graceful termination attempt via SIGTERM.
					// We set gracefulShutdownAttempted to true here because we are attempting the .NET-native graceful kill.
					gracefulShutdownAttempted = true;
				}
				else
				{
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Unsupported OS for specific graceful shutdown methods. Will rely on Process.Kill(true) for all termination attempts.");
				}

				// If a graceful shutdown attempt was initiated (or implicitly handled by Kill(true) on Unix-like)
				if (gracefulShutdownAttempted)
				{
					if (this.monitoredProcess.WaitForExit((int)this.gracefulShutdownTimeout.TotalMilliseconds))
					{
						Console.ForegroundColor = ConsoleColor.Green;
						Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Process ID: {this.monitoredProcess.Id} exited gracefully.");
						Console.ResetColor();
						this.monitoredProcess = null;
						return;
					}
					else
					{
						Console.ForegroundColor = ConsoleColor.Yellow;
						Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Process ID: {this.monitoredProcess.Id} did not exit gracefully within {this.gracefulShutdownTimeout.TotalSeconds}s. Proceeding with force kill (Process.Kill(true)).");
						Console.ResetColor();
					}
				}
				else
				{
					// This else block handles cases where graceful shutdown wasn't applicable or was explicitly skipped.
					// It will still proceed to the forceful Kill(true).
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] No graceful shutdown signal could be sent. Proceeding with force kill (Process.Kill(true)).");
				}

				// If not exited gracefully (or if graceful shutdown wasn't attempted/failed), force kill with process tree
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Force killing process ID: {this.monitoredProcess.Id} (Name: {this.monitoredProcess.ProcessName}) and its children using Process.Kill(true)...");
				Console.ResetColor();
				this.monitoredProcess.Kill(true); // Kills the process and its descendants
				this.monitoredProcess.WaitForExit(5000); // Wait up to 5 seconds for the process to exit after kill

				if (this.monitoredProcess.HasExited)
				{
					Console.ForegroundColor = ConsoleColor.Green;
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Process ID: {this.monitoredProcess.Id} and its children killed successfully.");
					Console.ResetColor();
				}
				else
				{
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Critical Warning: Process ID: {this.monitoredProcess.Id} did not exit even after force kill(true) command. It might be stuck!");
					Console.ResetColor();
				}
			}
			catch (Exception ex)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Error during application kill for process ID: {this.monitoredProcess?.Id}. Error: {ex.Message}");
				Console.ResetColor();
			}
			finally
			{
				this.monitoredProcess = null; // Always clear the reference after attempting to kill
			}
		}

		/// <summary>
		/// Launches the application executable and stores its process reference.
		/// </summary>
		private void LaunchApplication()
		{
			Console.ForegroundColor = ConsoleColor.Cyan;
			Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Launching application '{this.applicationExePath}' with arguments: '{this.launchArguments}'...");
			Console.ResetColor();
			try
			{
				ProcessStartInfo startInfo = new ProcessStartInfo
				{
					FileName = this.applicationExePath,
					Arguments = this.launchArguments,
					UseShellExecute = true,
					RedirectStandardOutput = false,
					RedirectStandardError = false,
					CreateNoWindow = false,
				};

				this.monitoredProcess = Process.Start(startInfo);
				if (this.monitoredProcess != null)
				{
					Console.ForegroundColor = ConsoleColor.Green;
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Application launched successfully. Process ID: {this.monitoredProcess.Id}");
					Console.ResetColor();
					// Reset CPU tracking for the newly launched process
					_lastCpuCheckTime = DateTime.Now;
					_lastCpuTotalProcessorTime = TimeSpan.Zero;
				}
				else
				{
					Console.ForegroundColor = ConsoleColor.Yellow;
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Warning: Process.Start returned null for '{this.applicationExePath}'. This might indicate a problem.");
					Console.ResetColor();
				}
			}
			catch (System.ComponentModel.Win32Exception ex)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Error launching application '{this.applicationExePath}'. Check if the path is correct and the executable exists. Error: {ex.Message}");
				Console.ResetColor();
				this.monitoredProcess = null;
			}
			catch (Exception ex)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] Unexpected error launching application '{this.applicationExePath}'. Error: {ex.Message}");
				Console.ResetColor();
				this.monitoredProcess = null;
			}
		}

		private async Task<bool> IsTcpPortResponsive(string host, int port, int timeoutMilliseconds)
		{
			using (TcpClient client = new TcpClient())
			{
				try
				{
					var connectTask = client.ConnectAsync(host, port);
					if (await Task.WhenAny(connectTask, Task.Delay(timeoutMilliseconds, this.cancellationToken)) == connectTask)
					{
						this.cancellationToken.ThrowIfCancellationRequested();
						if (!client.Connected)
						{
							Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] TCP Port {host}:{port} connection failed (not connected).");
						}
						return client.Connected;
					}
					else
					{
						Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] TCP Port Check Timeout: {host}:{port} did not respond within {timeoutMilliseconds}ms.");
						return false;
					}
				}
				catch (OperationCanceledException) { throw; }
				catch (SocketException ex)
				{
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] TCP Port Check Error: Could not connect to {host}:{port}. Socket error: {ex.SocketErrorCode} - {ex.Message}");
					return false;
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] TCP Port Check Unexpected Error: {ex.Message}");
					return false;
				}
			}
		}

		private async Task<bool> IsUdpPortResponsive(string host, int port, int timeoutMilliseconds)
		{
			using (UdpClient udpClient = new UdpClient())
			{
				try
				{
					byte[] datagram = Encoding.UTF8.GetBytes("health_check_ping");
					var sendTask = udpClient.SendAsync(datagram, datagram.Length, host, port);

					if (await Task.WhenAny(sendTask, Task.Delay(timeoutMilliseconds, this.cancellationToken)) == sendTask)
					{
						this.cancellationToken.ThrowIfCancellationRequested();
						return true;
					}
					else
					{
						Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] UDP Port Send Timeout: {host}:{port} did not complete send within {timeoutMilliseconds}ms.");
						return false;
					}
				}
				catch (OperationCanceledException) { throw; }
				catch (SocketException ex)
				{
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] UDP Port Check Error: Could not send to {host}:{port}. Socket error: {ex.SocketErrorCode} - {ex.Message}");
					return false;
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] UDP Port Check Unexpected Error: {ex.Message}");
					return false;
				}
			}
		}

		private async Task<bool> IsWebSocketPortResponsive(string webSocketUri, int timeoutMilliseconds)
		{
			using (ClientWebSocket ws = new ClientWebSocket())
			{
				try
				{
					ws.Options.KeepAliveInterval = TimeSpan.Zero;
					var connectTask = ws.ConnectAsync(new Uri(webSocketUri), this.cancellationToken);

					if (await Task.WhenAny(connectTask, Task.Delay(timeoutMilliseconds, this.cancellationToken)) == connectTask)
					{
						this.cancellationToken.ThrowIfCancellationRequested();
						if (ws.State == WebSocketState.Open)
						{
							await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Health check complete", CancellationToken.None);
							return true;
						}
						else
						{
							Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] WebSocket Port Check: Connection failed. State: {ws.State}");
							return false;
						}
					}
					else
					{
						Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] WebSocket Port Check Timeout: {webSocketUri} did not establish connection within {timeoutMilliseconds}ms.");
						return false;
					}
				}
				catch (OperationCanceledException) { throw; }
				catch (WebSocketException ex)
				{
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] WebSocket Port Check Error: {webSocketUri}. WebSocket error: {ex.WebSocketErrorCode} - {ex.Message}");
					return false;
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{this.appName}] WebSocket Port Check Unexpected Error: {ex.Message}");
					return false;
				}
			}
		}
	}
}