using System.Diagnostics;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using FishMMO.Logging;

namespace AppHealthMonitor
{
	/// <summary>
	/// Monitors the health and lifecycle of a single application process.
	/// Provides automatic restart capabilities, resource monitoring, and port health checks.
	/// </summary>
	public class HealthMonitor
	{
		private readonly string appName;
		private readonly string applicationExePath;
		private readonly int monitoredPort;
		private readonly List<PortType> portTypes;
		private readonly string launchArguments;
		private readonly TimeSpan checkInterval;
		private readonly CancellationToken cancellationToken;

		private readonly TimeSpan gracefulShutdownTimeout;
		private readonly int cpuThresholdPercent;
		private readonly long memoryThresholdBytes;

		private readonly TimeSpan initialRestartDelay;
		private readonly TimeSpan maxRestartDelay;
		private readonly int maxRestartAttempts;
		private int currentRestartAttemptCount;
		private TimeSpan currentCalculatedRestartDelay;
		private DateTime lastRestartAttemptTime;

		private readonly int circuitBreakerFailureThreshold;
		private readonly TimeSpan circuitBreakerResetTimeout;
		private int consecutivePortCheckFailures;
		private bool isCircuitOpen;
		private DateTime circuitOpenTimestamp;

		private Process monitoredProcess;
		private DateTime lastCpuCheckTime;
		private TimeSpan lastCpuTotalProcessorTime;

		private readonly string derivedProcessName;
		private readonly Guid monitorInstanceId = Guid.NewGuid();

		private readonly TimeSpan initialHealthCheckDelay = TimeSpan.FromSeconds(30);

		/// <summary>
		/// Gets a value indicating whether this monitor is configured for process-only monitoring without port checks.
		/// </summary>
		private bool IsProcessOnlyMonitoring => this.portTypes.Count == 1 && this.portTypes.Contains(PortType.None);

		/// <summary>
		/// Initializes a new instance of the <see cref="HealthMonitor"/> class.
		/// </summary>
		/// <param name="appName">The friendly name of the application to monitor.</param>
		/// <param name="applicationExePath">The full path to the application executable.</param>
		/// <param name="monitoredPort">The port number to monitor for health checks.</param>
		/// <param name="portTypes">The types of ports to monitor (TCP, UDP, WebSocket, or None).</param>
		/// <param name="launchArguments">Command-line arguments to pass when launching the application.</param>
		/// <param name="checkInterval">The interval between health checks.</param>
		/// <param name="cpuThresholdPercent">The CPU usage threshold percentage (0 to disable).</param>
		/// <param name="memoryThresholdMB">The memory usage threshold in megabytes (0 to disable).</param>
		/// <param name="gracefulShutdownTimeoutSeconds">Timeout in seconds for graceful shutdown before force kill.</param>
		/// <param name="initialRestartDelaySeconds">Initial delay in seconds before first restart attempt.</param>
		/// <param name="maxRestartDelaySeconds">Maximum delay in seconds between restart attempts.</param>
		/// <param name="maxRestartAttempts">Maximum number of restart attempts before giving up.</param>
		/// <param name="circuitBreakerFailureThreshold">Number of consecutive failures before circuit breaker opens.</param>
		/// <param name="circuitBreakerResetTimeoutMinutes">Minutes before circuit breaker attempts to reset.</param>
		/// <param name="cancellationToken">Token to signal cancellation of monitoring operations.</param>
		/// <exception cref="ArgumentNullException">Thrown when applicationExePath is null.</exception>
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
			this.appName = appName;
			this.applicationExePath = Path.GetFullPath(applicationExePath ?? throw new ArgumentNullException(nameof(applicationExePath)));
			this.monitoredPort = monitoredPort;
			this.portTypes = portTypes?.Any() == true ? portTypes : new List<PortType> { PortType.None };
			this.launchArguments = launchArguments ?? string.Empty;
			this.checkInterval = checkInterval;
			this.cancellationToken = cancellationToken;

			this.cpuThresholdPercent = cpuThresholdPercent;
			this.memoryThresholdBytes = (long)memoryThresholdMB * 1024 * 1024;
			this.gracefulShutdownTimeout = TimeSpan.FromSeconds(gracefulShutdownTimeoutSeconds > 0 ? gracefulShutdownTimeoutSeconds : 10);

			this.initialRestartDelay = TimeSpan.FromSeconds(initialRestartDelaySeconds > 0 ? initialRestartDelaySeconds : 5);
			this.maxRestartDelay = TimeSpan.FromSeconds(maxRestartDelaySeconds > 0 ? maxRestartDelaySeconds : 60);
			this.maxRestartAttempts = maxRestartAttempts > 0 ? maxRestartAttempts : 5;
			currentRestartAttemptCount = 0;
			currentCalculatedRestartDelay = this.initialRestartDelay;
			lastRestartAttemptTime = DateTime.MinValue;

			this.circuitBreakerFailureThreshold = circuitBreakerFailureThreshold > 0 ? circuitBreakerFailureThreshold : 3;
			this.circuitBreakerResetTimeout = TimeSpan.FromMinutes(circuitBreakerResetTimeoutMinutes > 0 ? circuitBreakerResetTimeoutMinutes : 5);
			consecutivePortCheckFailures = 0;
			isCircuitOpen = false;
			circuitOpenTimestamp = DateTime.MinValue;

			this.monitoredProcess = null;
			lastCpuCheckTime = DateTime.MinValue;
			lastCpuTotalProcessorTime = TimeSpan.Zero;

			this.derivedProcessName = Path.GetFileNameWithoutExtension(this.applicationExePath);
		}

		/// <summary>
		/// Gets the log source identifier for this monitor instance.
		/// </summary>
		/// <returns>The application name used as the log source.</returns>
		private string GetLogSource() => $"{appName}";

		/// <summary>
		/// Starts the monitoring loop for the application.
		/// Continuously checks application health and performs restarts as needed.
		/// </summary>
		/// <returns>A task that represents the asynchronous monitoring operation.</returns>
		public async Task StartMonitoring()
		{
			Log.Info(GetLogSource(), "Starting monitoring loop.");

			if (!IsApplicationProcessRunning())
			{
				Log.Info(GetLogSource(), "Application process not found at startup. Attempting initial launch.");
				LaunchApplication();
				await Task.Delay(TimeSpan.FromSeconds(5), this.cancellationToken);
			}

			Log.Info(GetLogSource(), $"Waiting {this.initialHealthCheckDelay.TotalSeconds} seconds before first full health check...");
			try
			{
				await Task.Delay(this.initialHealthCheckDelay, this.cancellationToken);
			}
			catch (OperationCanceledException)
			{
				Log.Info(GetLogSource(), "Initial delay cancelled. Monitoring stopping.");
				return;
			}

			while (!this.cancellationToken.IsCancellationRequested)
			{
				Log.Debug(GetLogSource(), "Performing health check cycle.");

				bool needsRestart = false;

				if (!IsApplicationProcessRunning())
				{
					Log.Error(GetLogSource(), "Process is NOT running or has exited.");
					needsRestart = true;
				}
				else
				{
					if ((this.cpuThresholdPercent > 0 || this.memoryThresholdBytes > 0) && !CheckMemoryAndCpuUsage())
					{
						Log.Error(GetLogSource(), "CPU or Memory usage exceeds configured thresholds.");
						needsRestart = true;
					}
					else if (!IsProcessOnlyMonitoring)
					{
						if (isCircuitOpen)
						{
							if (DateTime.Now - circuitOpenTimestamp < circuitBreakerResetTimeout)
							{
								Log.Warning(GetLogSource(), $"Circuit Breaker is OPEN. Skipping port checks for now. Resets in {Math.Ceiling((circuitBreakerResetTimeout - (DateTime.Now - circuitOpenTimestamp)).TotalSeconds)}s.");
							}
							else
							{
								Log.Warning(GetLogSource(), "Circuit Breaker reset timeout reached. Attempting to CLOSE circuit with one port check.");
								if (await CheckApplicationPortsResponsiveness())
								{
									Log.Info(GetLogSource(), "Circuit Breaker closed successfully. Ports are healthy.");
									isCircuitOpen = false;
									consecutivePortCheckFailures = 0;
								}
								else
								{
									Log.Error(GetLogSource(), "Circuit Breaker remains OPEN. Port check failed again.");
									circuitOpenTimestamp = DateTime.Now;
									needsRestart = true;
								}
							}
						}
						else
						{
							if (!await CheckApplicationPortsResponsiveness())
							{
								consecutivePortCheckFailures++;
								Log.Warning(GetLogSource(), $"Port check failed. Consecutive failures: {consecutivePortCheckFailures}/{circuitBreakerFailureThreshold}.");

								if (consecutivePortCheckFailures >= circuitBreakerFailureThreshold)
								{
									Log.Error(GetLogSource(), "Circuit Breaker OPEN! Too many consecutive port failures.");
									isCircuitOpen = true;
									circuitOpenTimestamp = DateTime.Now;
									needsRestart = true;
								}
								else
								{
									needsRestart = true;
								}
							}
							else
							{
								if (consecutivePortCheckFailures > 0)
								{
									Log.Info(GetLogSource(), "Port check successful. Consecutive failures reset.");
								}
								consecutivePortCheckFailures = 0;
							}
						}
					}
				}

				if (needsRestart)
				{
					await HandleApplicationRestart();
				}
				else
				{
					currentRestartAttemptCount = 0;
					currentCalculatedRestartDelay = this.initialRestartDelay;
					isCircuitOpen = false;
					consecutivePortCheckFailures = 0;
					Log.Info(GetLogSource(), "Application is healthy.");
				}

				try
				{
					Log.Debug(GetLogSource(), $"Waiting {this.checkInterval.TotalSeconds} seconds for next main health check cycle...");
					await Task.Delay(this.checkInterval, this.cancellationToken);
				}
				catch (OperationCanceledException)
				{
					Log.Info(GetLogSource(), "Monitoring task cancelled. Exiting loop.");
					break;
				}
			}
			Log.Info(GetLogSource(), "Monitoring stopped.");
		}

		/// <summary>
		/// Handles the restart logic for an unhealthy application.
		/// Implements exponential backoff and respects maximum restart attempt limits.
		/// </summary>
		/// <returns>A task that represents the asynchronous restart operation.</returns>
		/// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
		private async Task HandleApplicationRestart()
		{
			currentRestartAttemptCount++;
			TimeSpan delayToUse = currentCalculatedRestartDelay;

			Log.Warning(GetLogSource(), $"Application unhealthy. Attempting restart (Attempt {currentRestartAttemptCount}/{maxRestartAttempts}).");
			Log.Warning(GetLogSource(), $"Next restart in {delayToUse.TotalSeconds:F1} seconds...");

			try
			{
				await Task.Delay(delayToUse, this.cancellationToken);
			}
			catch (OperationCanceledException)
			{
				Log.Info(GetLogSource(), "Restart delay cancelled.");
				throw;
			}

			if (currentRestartAttemptCount >= maxRestartAttempts)
			{
				Log.Critical(GetLogSource(), $"Max restart attempts ({maxRestartAttempts}) reached. Stopping monitoring for this application.");
				this.cancellationToken.ThrowIfCancellationRequested();
				return;
			}

			KillApplication();
			LaunchApplication();
			lastRestartAttemptTime = DateTime.Now;

			currentCalculatedRestartDelay = TimeSpan.FromSeconds(
				Math.Min(maxRestartDelay.TotalSeconds, initialRestartDelay.TotalSeconds * Math.Pow(2, currentRestartAttemptCount - 1))
			);

			await Task.Delay(TimeSpan.FromSeconds(5), this.cancellationToken);
		}

		/// <summary>
		/// Checks if the monitored application's CPU and memory usage are within configured thresholds.
		/// </summary>
		/// <returns>True if usage is within thresholds or thresholds are disabled; otherwise, false.</returns>
		private bool CheckMemoryAndCpuUsage()
		{
			if (this.monitoredProcess == null || this.monitoredProcess.HasExited)
			{
				Log.Debug(GetLogSource(), "Process not available for CPU/Memory check.");
				return false;
			}

			try
			{
				this.monitoredProcess.Refresh();

				if (this.memoryThresholdBytes > 0)
				{
					long currentMemory = this.monitoredProcess.WorkingSet64;
					if (currentMemory > this.memoryThresholdBytes)
					{
						Log.Warning(GetLogSource(), $"Memory Usage Alert: {BytesToMB(currentMemory):F2}MB exceeds threshold of {BytesToMB(this.memoryThresholdBytes):F2}MB.");
						return false;
					}
				}

				if (this.cpuThresholdPercent > 0)
				{
					if (lastCpuCheckTime == DateTime.MinValue || lastCpuTotalProcessorTime == TimeSpan.Zero)
					{
						lastCpuCheckTime = DateTime.Now;
						lastCpuTotalProcessorTime = this.monitoredProcess.TotalProcessorTime;
						Log.Debug(GetLogSource(), "Initializing CPU usage tracking.");
						return true;
					}

					TimeSpan currentTotalProcessorTime = this.monitoredProcess.TotalProcessorTime;
					DateTime currentCheckTime = DateTime.Now;

					double cpuTimeUsed = (currentTotalProcessorTime - lastCpuTotalProcessorTime).TotalMilliseconds;
					double timeElapsed = (currentCheckTime - lastCpuCheckTime).TotalMilliseconds;

					if (timeElapsed > 0)
					{
						double cpuUsage = (cpuTimeUsed / (timeElapsed * Environment.ProcessorCount)) * 100;

						if (cpuUsage > this.cpuThresholdPercent)
						{
							Log.Warning(GetLogSource(), $"CPU Usage Alert: {cpuUsage:F2}% exceeds threshold of {this.cpuThresholdPercent}%.");
							return false;
						}
					}

					lastCpuCheckTime = currentCheckTime;
					lastCpuTotalProcessorTime = currentTotalProcessorTime;
				}

				Log.Debug(GetLogSource(), "CPU/Memory checks passed (if configured).");
				return true;
			}
			catch (InvalidOperationException ex)
			{
				Log.Error(GetLogSource(), "Process exited during CPU/Memory check.", ex);
				return false;
			}
			catch (Exception ex)
			{
				Log.Error(GetLogSource(), $"Error during CPU/Memory check: {ex.Message}", ex);
				return false;
			}
		}

		/// <summary>
		/// Converts bytes to megabytes.
		/// </summary>
		/// <param name="bytes">The number of bytes to convert.</param>
		/// <returns>The equivalent value in megabytes.</returns>
		private double BytesToMB(long bytes)
		{
			return bytes / (1024.0 * 1024.0);
		}

		/// <summary>
		/// Checks if the configured application ports are responsive.
		/// Supports TCP, UDP, and WebSocket port checking.
		/// </summary>
		/// <returns>True if all configured ports are responsive; otherwise, false.</returns>
		private async Task<bool> CheckApplicationPortsResponsiveness()
		{
			if (IsProcessOnlyMonitoring)
			{
				Log.Debug(GetLogSource(), "Skipping port checks (Process-Only Monitoring).");
				return true;
			}

			if (!this.portTypes.Any(pt => pt != PortType.None) && monitoredPort <= 0)
			{
				Log.Warning(GetLogSource(), "No valid port types or port configured. Considering ports healthy by default.");
				return true;
			}


			foreach (var portType in this.portTypes)
			{
				if (portType == PortType.None)
				{
					continue;
				}

				Log.Debug(GetLogSource(), $"Port Check: Attempting to connect to port {this.monitoredPort} (Type: {portType})...");
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
						Log.Warning(GetLogSource(), $"Port Check: Unsupported PortType '{portType}'. Skipping this check.");
						currentPortTypeResponsive = false;
						break;
				}

				if (!currentPortTypeResponsive)
				{
					Log.Warning(GetLogSource(), $"Port Check: Port {this.monitoredPort} (Type: {portType}) is NOT responsive.");
					return false;
				}
			}
			Log.Debug(GetLogSource(), "All configured ports are responsive.");
			return true;
		}

		/// <summary>
		/// Checks if the monitored application process is currently running.
		/// </summary>
		/// <returns>True if the process is running; otherwise, false.</returns>
		private bool IsApplicationProcessRunning()
		{
			if (this.monitoredProcess == null)
			{
				Log.Debug(GetLogSource(), "No process currently being monitored (monitoredProcess is null).");
				return false;
			}

			try
			{
				this.monitoredProcess.Refresh();
				if (this.monitoredProcess.HasExited)
				{
					Log.Info(GetLogSource(), $"Monitored process (ID: {this.monitoredProcess.Id}) has exited after refresh.");
					this.monitoredProcess = null;
					return false;
				}
				Log.Debug(GetLogSource(), $"Monitored process (ID: {this.monitoredProcess.Id}) is running.");
				return true;
			}
			catch (InvalidOperationException ex)
			{
				Log.Error(GetLogSource(), $"Monitored process (ID: {this.monitoredProcess?.Id}) seems to have exited unexpectedly (InvalidOperationException).", ex);
				this.monitoredProcess = null;
				return false;
			}
			catch (Exception ex)
			{
				Log.Error(GetLogSource(), $"Error refreshing process state for ID: {this.monitoredProcess?.Id}. Error: {ex.Message}", ex);
				this.monitoredProcess = null;
				return false;
			}
		}

		/// <summary>
		/// Terminates the monitored application process.
		/// Attempts graceful shutdown first, then force-kills if necessary.
		/// Platform-specific behavior: Uses CloseMainWindow on Windows, SIGTERM on Unix-like systems.
		/// </summary>
		public void KillApplication()
		{
			if (this.monitoredProcess == null)
			{
				Log.Debug(GetLogSource(), "KillApplication: No active process reference to kill.");
				return;
			}

			int processId = this.monitoredProcess.Id;
			string processName = this.derivedProcessName;

			try
			{
				this.monitoredProcess.Refresh();
				if (this.monitoredProcess.HasExited)
				{
					Log.Info(GetLogSource(), $"KillApplication: Process ID: {processId} has already exited, no need to kill.");
					this.monitoredProcess = null;
					return;
				}

				Log.Warning(GetLogSource(), $"KillApplication: Attempting graceful shutdown for process ID: {processId}...");

				bool gracefulShutdownAttempted = false;

				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				{
					if (this.monitoredProcess.MainWindowHandle != IntPtr.Zero)
					{
						this.monitoredProcess.CloseMainWindow();
						Log.Info(GetLogSource(), "KillApplication: Sent CloseMainWindow signal (Windows).");
						gracefulShutdownAttempted = true;
					}
					else
					{
						Log.Info(GetLogSource(), "KillApplication: No main window detected for graceful shutdown on Windows via CloseMainWindow. Will proceed to force kill if needed.");
					}
				}
				else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
				{
					Log.Info(GetLogSource(), "KillApplication: Relying on Process.Kill(true) for graceful termination on Unix-like OS (Linux/macOS), which sends SIGTERM to process tree.");
					gracefulShutdownAttempted = true;
				}
				else
				{
					Log.Warning(GetLogSource(), "KillApplication: Unsupported OS for specific graceful shutdown methods. Will rely on Process.Kill(true) for all termination attempts.");
				}

				if (gracefulShutdownAttempted)
				{
					Log.Debug(GetLogSource(), $"KillApplication: Waiting for process ID: {processId} to exit gracefully ({this.gracefulShutdownTimeout.TotalSeconds}s timeout).");
					if (this.monitoredProcess.WaitForExit((int)this.gracefulShutdownTimeout.TotalMilliseconds))
					{
						Log.Info(GetLogSource(), $"KillApplication: Process ID: {processId} exited gracefully.");
						this.monitoredProcess = null;
						return;
					}
					else
					{
						Log.Warning(GetLogSource(), $"KillApplication: Process ID: {processId} did not exit gracefully within {this.gracefulShutdownTimeout.TotalSeconds}s. Proceeding with force kill.");
					}
				}
				else
				{
					Log.Info(GetLogSource(), "KillApplication: No specific graceful shutdown method applied. Proceeding with force kill.");
				}

				Log.Error(GetLogSource(), $"KillApplication: Force killing process ID: {processId} and its children using Process.Kill(true)...");
				this.monitoredProcess.Kill(true);

				Log.Debug(GetLogSource(), $"KillApplication: Waiting for process ID: {processId} to confirm exit after force kill (5s timeout).");
				this.monitoredProcess.WaitForExit(5000);

				if (this.monitoredProcess.HasExited)
				{
					Log.Info(GetLogSource(), $"KillApplication: Process ID: {processId} and its children killed successfully.");
				}
				else
				{
					Log.Critical(GetLogSource(), $"KillApplication: Critical Warning: Process ID: {processId} did not exit even after force kill(true) command. It might be stuck!");
				}
			}
			catch (InvalidOperationException ex)
			{
				Log.Error(GetLogSource(), $"KillApplication: Process ID: {processId} already exited or invalid handle during kill attempt (InvalidOperationException).", ex);
			}
			catch (Exception ex)
			{
				Log.Critical(GetLogSource(), $"KillApplication: Error during application kill for process ID: {processId}. Error: {ex.Message}", ex);
			}
			finally
			{
				this.monitoredProcess = null;
			}
		}

		/// <summary>
		/// Launches the monitored application process with configured arguments.
		/// </summary>
		private void LaunchApplication()
		{
			Log.Info(GetLogSource(), $"Launching application '{this.applicationExePath}' with arguments: '{this.launchArguments}'...");
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
					Log.Info(GetLogSource(), $"Application launched successfully. Process ID: {this.monitoredProcess.Id}");
					lastCpuCheckTime = DateTime.Now;
					lastCpuTotalProcessorTime = TimeSpan.Zero;
				}
				else
				{
					Log.Warning(GetLogSource(), $"Warning: Process.Start returned null for '{this.applicationExePath}'. This might indicate a problem.");
				}
			}
			catch (System.ComponentModel.Win32Exception ex)
			{
				Log.Critical(GetLogSource(), $"Error launching application '{this.applicationExePath}'. Check if the path is correct and the executable exists. Error: {ex.Message}", ex);
				this.monitoredProcess = null;
			}
			catch (Exception ex)
			{
				Log.Critical(GetLogSource(), $"Unexpected error launching application '{this.applicationExePath}'. Error: {ex.Message}", ex);
				this.monitoredProcess = null;
			}
		}

		/// <summary>
		/// Checks if a TCP port is responsive by attempting to establish a connection.
		/// </summary>
		/// <param name="host">The hostname or IP address to connect to.</param>
		/// <param name="port">The port number to check.</param>
		/// <param name="timeoutMilliseconds">The connection timeout in milliseconds.</param>
		/// <returns>True if the port is responsive; otherwise, false.</returns>
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
							Log.Debug(GetLogSource(), $"TCP Port {host}:{port} connection failed (not connected).");
						}
						return client.Connected;
					}
					else
					{
						Log.Warning(GetLogSource(), $"TCP Port Check Timeout: {host}:{port} did not respond within {timeoutMilliseconds}ms.");
						return false;
					}
				}
				catch (OperationCanceledException) { throw; }
				catch (SocketException ex)
				{
					Log.Warning(GetLogSource(), $"TCP Port Check Error: Could not connect to {host}:{port}. Socket error: {ex.SocketErrorCode} - {ex.Message}");
					return false;
				}
				catch (Exception ex)
				{
					Log.Error(GetLogSource(), $"TCP Port Check Unexpected Error: {ex.Message}", ex);
					return false;
				}
			}
		}

		/// <summary>
		/// Checks if a UDP port is responsive by attempting to send a test datagram.
		/// </summary>
		/// <param name="host">The hostname or IP address to send to.</param>
		/// <param name="port">The port number to check.</param>
		/// <param name="timeoutMilliseconds">The send timeout in milliseconds.</param>
		/// <returns>True if the datagram was sent successfully; otherwise, false.</returns>
		private async Task<bool> IsUdpPortResponsive(string host, int port, int timeoutMilliseconds)
		{
			using (UdpClient udpClient = new UdpClient())
			{
				try
				{
					byte[] datagram = Encoding.UTF8.GetBytes("healthcheckping");
					var sendTask = udpClient.SendAsync(datagram, datagram.Length, host, port);

					if (await Task.WhenAny(sendTask, Task.Delay(timeoutMilliseconds, this.cancellationToken)) == sendTask)
					{
						this.cancellationToken.ThrowIfCancellationRequested();
						return true;
					}
					else
					{
						Log.Warning(GetLogSource(), $"UDP Port Send Timeout: {host}:{port} did not complete send within {timeoutMilliseconds}ms.");
						return false;
					}
				}
				catch (OperationCanceledException) { throw; }
				catch (SocketException ex)
				{
					Log.Warning(GetLogSource(), $"UDP Port Check Error: Could not send to {host}:{port}. Socket error: {ex.SocketErrorCode} - {ex.Message}");
					return false;
				}
				catch (Exception ex)
				{
					Log.Error(GetLogSource(), $"UDP Port Check Unexpected Error: {ex.Message}", ex);
					return false;
				}
			}
		}

		/// <summary>
		/// Checks if a WebSocket endpoint is responsive by attempting to establish a connection.
		/// </summary>
		/// <param name="webSocketUri">The WebSocket URI to connect to.</param>
		/// <param name="timeoutMilliseconds">The connection timeout in milliseconds.</param>
		/// <returns>True if the WebSocket connection was established; otherwise, false.</returns>
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
							Log.Warning(GetLogSource(), $"WebSocket Port Check: Connection failed. State: {ws.State}.");
							return false;
						}
					}
					else
					{
						Log.Warning(GetLogSource(), $"WebSocket Port Check Timeout: {webSocketUri} did not establish connection within {timeoutMilliseconds}ms.");
						return false;
					}
				}
				catch (OperationCanceledException) { throw; }
				catch (WebSocketException ex)
				{
					Log.Warning(GetLogSource(), $"WebSocket Port Check Error: {webSocketUri}. WebSocket error: {ex.WebSocketErrorCode} - {ex.Message}");
					return false;
				}
				catch (Exception ex)
				{
					Log.Error(GetLogSource(), $"WebSocket Port Check Unexpected Error: {ex.Message}", ex);
					return false;
				}
			}
		}
	}
}