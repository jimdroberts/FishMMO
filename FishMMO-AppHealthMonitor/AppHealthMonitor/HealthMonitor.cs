using System.Diagnostics;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Runtime.InteropServices;

namespace AppHealthMonitor
{
	public class HealthMonitor
	{
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

		private bool IsProcessOnlyMonitoring => this.portTypes.Count == 1 && this.portTypes.Contains(PortType.None);

		/// <summary>
		/// Helper method for consistent logging within the HealthMonitor.
		/// </summary>
		/// <param name="message">The message to log.</param>
		/// <param name="color">Optional. The foreground color for the message.</param>
		private void Log(string message, ConsoleColor? color = null)
		{
			if (color.HasValue)
			{
				Console.ForegroundColor = color.Value;
			}
			// Prefix all HealthMonitor logs with its unique instance ID and derived process name
			Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Monitor-{monitorInstanceId.ToString().Substring(0, 8)}] [{this.derivedProcessName}] {message}");
			Console.ResetColor(); // Always reset color after writing
		}

		/// <summary>
		/// Initializes a new instance of the HealthMonitor class.
		/// </summary>
		public HealthMonitor(
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

		public async Task StartMonitoring()
		{
			Log("Starting monitoring loop.");

			if (!IsApplicationProcessRunning())
			{
				Log("Application process not found at startup. Attempting initial launch...");
				LaunchApplication();
				await Task.Delay(TimeSpan.FromSeconds(5), this.cancellationToken);
			}

			Log($"Waiting {this.initialHealthCheckDelay.TotalSeconds} seconds before first full health check...");
			try
			{
				await Task.Delay(this.initialHealthCheckDelay, this.cancellationToken);
			}
			catch (OperationCanceledException)
			{
				Log("Initial delay cancelled. Monitoring stopping.");
				return;
			}

			while (!this.cancellationToken.IsCancellationRequested)
			{
				Log("Performing health check cycle.");

				bool needsRestart = false;

				if (!IsApplicationProcessRunning())
				{
					Log("Process is NOT running or has exited.", ConsoleColor.Red);
					needsRestart = true;
				}
				else
				{
					if ((this.cpuThresholdPercent > 0 || this.memoryThresholdBytes > 0) && !CheckMemoryAndCpuUsage())
					{
						Log("CPU or Memory usage exceeds configured thresholds.", ConsoleColor.Red);
						needsRestart = true;
					}
					else if (!IsProcessOnlyMonitoring)
					{
						if (isCircuitOpen)
						{
							if (DateTime.Now - circuitOpenTimestamp < circuitBreakerResetTimeout)
							{
								Log($"Circuit Breaker is OPEN. Skipping port checks for now. Resets in {Math.Ceiling((circuitBreakerResetTimeout - (DateTime.Now - circuitOpenTimestamp)).TotalSeconds)}s.", ConsoleColor.DarkYellow);
							}
							else
							{
								Log("Circuit Breaker reset timeout reached. Attempting to CLOSE circuit with one port check.", ConsoleColor.Yellow);
								if (await CheckApplicationPortsResponsiveness())
								{
									Log("Circuit Breaker closed successfully. Ports are healthy.", ConsoleColor.Green);
									isCircuitOpen = false;
									consecutivePortCheckFailures = 0;
								}
								else
								{
									Log("Circuit Breaker remains OPEN. Port check failed again.", ConsoleColor.Red);
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
								Log($"Port check failed. Consecutive failures: {consecutivePortCheckFailures}/{circuitBreakerFailureThreshold}.", ConsoleColor.Yellow);

								if (consecutivePortCheckFailures >= circuitBreakerFailureThreshold)
								{
									Log("Circuit Breaker OPEN! Too many consecutive port failures.", ConsoleColor.Red);
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
									Log("Port check successful. Consecutive failures reset.", ConsoleColor.Green);
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
					Log("Application is healthy.", ConsoleColor.Green);
				}

				try
				{
					Log($"Waiting {this.checkInterval.TotalSeconds} seconds for next main health check cycle...");
					await Task.Delay(this.checkInterval, this.cancellationToken);
				}
				catch (OperationCanceledException)
				{
					Log("Monitoring task cancelled. Exiting loop.");
					break;
				}
			}
			Log("Monitoring stopped.");
		}

		private async Task HandleApplicationRestart()
		{
			currentRestartAttemptCount++;
			TimeSpan delayToUse = currentCalculatedRestartDelay;

			Log($"Application unhealthy. Attempting restart (Attempt {currentRestartAttemptCount}/{maxRestartAttempts}).", ConsoleColor.Magenta);
			Log($"Next restart in {delayToUse.TotalSeconds:F1} seconds...", ConsoleColor.Magenta);

			try
			{
				await Task.Delay(delayToUse, this.cancellationToken);
			}
			catch (OperationCanceledException)
			{
				Log("Restart delay cancelled.");
				throw;
			}

			if (currentRestartAttemptCount >= maxRestartAttempts)
			{
				Log($"Max restart attempts ({maxRestartAttempts}) reached. Stopping monitoring for this application.", ConsoleColor.Red);
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

		private bool CheckMemoryAndCpuUsage()
		{
			if (this.monitoredProcess == null || this.monitoredProcess.HasExited)
			{
				Log("Process not available for CPU/Memory check.");
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
						Log($"Memory Usage Alert: {BytesToMB(currentMemory):F2}MB exceeds threshold of {BytesToMB(this.memoryThresholdBytes):F2}MB.", ConsoleColor.Red);
						return false;
					}
				}

				if (this.cpuThresholdPercent > 0)
				{
					if (lastCpuCheckTime == DateTime.MinValue || lastCpuTotalProcessorTime == TimeSpan.Zero)
					{
						lastCpuCheckTime = DateTime.Now;
						lastCpuTotalProcessorTime = this.monitoredProcess.TotalProcessorTime;
						Log("Initializing CPU usage tracking.");
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
							Log($"CPU Usage Alert: {cpuUsage:F2}% exceeds threshold of {this.cpuThresholdPercent}%.", ConsoleColor.Red);
							return false;
						}
					}

					lastCpuCheckTime = currentCheckTime;
					lastCpuTotalProcessorTime = currentTotalProcessorTime;
				}

				Log("CPU/Memory checks passed (if configured).");
				return true;
			}
			catch (InvalidOperationException)
			{
				Log("Process exited during CPU/Memory check.");
				return false;
			}
			catch (Exception ex)
			{
				Log($"Error during CPU/Memory check: {ex.Message}", ConsoleColor.Red);
				return false;
			}
		}

		private double BytesToMB(long bytes)
		{
			return bytes / (1024.0 * 1024.0);
		}

		private async Task<bool> CheckApplicationPortsResponsiveness()
		{
			if (IsProcessOnlyMonitoring)
			{
				Log("Skipping port checks (Process-Only Monitoring).");
				return true;
			}

			if (!this.portTypes.Any(pt => pt != PortType.None) && monitoredPort <= 0)
			{
				Log("No valid port types or port configured. Considering ports healthy by default.");
				return true;
			}


			foreach (var portType in this.portTypes)
			{
				if (portType == PortType.None)
				{
					continue;
				}

				Log($"Port Check: Attempting to connect to port {this.monitoredPort} (Type: {portType})...");
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
						Log($"Port Check: Unsupported PortType '{portType}'. Skipping this check.", ConsoleColor.Yellow);
						currentPortTypeResponsive = false;
						break;
				}

				if (!currentPortTypeResponsive)
				{
					Log($"Port Check: Port {this.monitoredPort} (Type: {portType}) is NOT responsive.", ConsoleColor.Yellow);
					return false;
				}
			}
			Log("All configured ports are responsive.");
			return true;
		}

		private bool IsApplicationProcessRunning()
		{
			if (this.monitoredProcess == null)
			{
				Log("No process currently being monitored (monitoredProcess is null).");
				return false;
			}

			try
			{
				this.monitoredProcess.Refresh();
				if (this.monitoredProcess.HasExited)
				{
					Log($"Monitored process (ID: {this.monitoredProcess.Id}) has exited after refresh.");
					this.monitoredProcess = null;
					return false;
				}
				Log($"Monitored process (ID: {this.monitoredProcess.Id}) is running.");
				return true;
			}
			catch (InvalidOperationException)
			{
				Log($"Monitored process (ID: {this.monitoredProcess?.Id}) seems to have exited unexpectedly (InvalidOperationException).");
				this.monitoredProcess = null;
				return false;
			}
			catch (Exception ex)
			{
				Log($"Error refreshing process state for ID: {this.monitoredProcess?.Id}. Error: {ex.Message}", ConsoleColor.Red);
				this.monitoredProcess = null;
				return false;
			}
		}

		public void KillApplication()
		{
			if (this.monitoredProcess == null)
			{
				Log("KillApplication: No active process reference to kill.");
				return;
			}

			int processId = this.monitoredProcess.Id;
			string processName = this.derivedProcessName;

			try
			{
				this.monitoredProcess.Refresh();
				if (this.monitoredProcess.HasExited)
				{
					Log($"KillApplication: Process ID: {processId} has already exited, no need to kill.");
					this.monitoredProcess = null;
					return;
				}

				Log($"KillApplication: Attempting graceful shutdown for process ID: {processId}...", ConsoleColor.Yellow);

				bool gracefulShutdownAttempted = false;

				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				{
					if (this.monitoredProcess.MainWindowHandle != IntPtr.Zero)
					{
						this.monitoredProcess.CloseMainWindow();
						Log("KillApplication: Sent CloseMainWindow signal (Windows).");
						gracefulShutdownAttempted = true;
					}
					else
					{
						Log("KillApplication: No main window detected for graceful shutdown on Windows via CloseMainWindow. Will proceed to force kill if needed.");
					}
				}
				else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
				{
					Log("KillApplication: Relying on Process.Kill(true) for graceful termination on Unix-like OS (Linux/macOS), which sends SIGTERM to process tree.");
					gracefulShutdownAttempted = true;
				}
				else
				{
					Log("KillApplication: Unsupported OS for specific graceful shutdown methods. Will rely on Process.Kill(true) for all termination attempts.");
				}

				if (gracefulShutdownAttempted)
				{
					Log($"KillApplication: Waiting for process ID: {processId} to exit gracefully ({this.gracefulShutdownTimeout.TotalSeconds}s timeout).");
					if (this.monitoredProcess.WaitForExit((int)this.gracefulShutdownTimeout.TotalMilliseconds))
					{
						Log($"KillApplication: Process ID: {processId} exited gracefully.", ConsoleColor.Green);
						this.monitoredProcess = null;
						return;
					}
					else
					{
						Log($"KillApplication: Process ID: {processId} did not exit gracefully within {this.gracefulShutdownTimeout.TotalSeconds}s. Proceeding with force kill.", ConsoleColor.Yellow);
					}
				}
				else
				{
					Log("KillApplication: No specific graceful shutdown method applied. Proceeding with force kill.");
				}

				Log($"KillApplication: Force killing process ID: {processId} and its children using Process.Kill(true)...", ConsoleColor.Red);
				this.monitoredProcess.Kill(true);

				Log($"KillApplication: Waiting for process ID: {processId} to confirm exit after force kill (5s timeout).");
				this.monitoredProcess.WaitForExit(5000);

				if (this.monitoredProcess.HasExited)
				{
					Log($"KillApplication: Process ID: {processId} and its children killed successfully.", ConsoleColor.Green);
				}
				else
				{
					Log($"KillApplication: Critical Warning: Process ID: {processId} did not exit even after force kill(true) command. It might be stuck!", ConsoleColor.Red);
				}
			}
			catch (InvalidOperationException ex)
			{
				Log($"KillApplication: Process ID: {processId} already exited or invalid handle during kill attempt (InvalidOperationException). Error: {ex.Message}");
			}
			catch (Exception ex)
			{
				Log($"KillApplication: Error during application kill for process ID: {processId}. Error: {ex.Message}", ConsoleColor.Red);
			}
			finally
			{
				this.monitoredProcess = null;
			}
		}

		private void LaunchApplication()
		{
			Log($"Launching application '{this.applicationExePath}' with arguments: '{this.launchArguments}'...", ConsoleColor.Cyan);
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
					Log($"Application launched successfully. Process ID: {this.monitoredProcess.Id}", ConsoleColor.Green);
					lastCpuCheckTime = DateTime.Now;
					lastCpuTotalProcessorTime = TimeSpan.Zero;
				}
				else
				{
					Log($"Warning: Process.Start returned null for '{this.applicationExePath}'. This might indicate a problem.", ConsoleColor.Yellow);
				}
			}
			catch (System.ComponentModel.Win32Exception ex)
			{
				Log($"Error launching application '{this.applicationExePath}'. Check if the path is correct and the executable exists. Error: {ex.Message}", ConsoleColor.Red);
				this.monitoredProcess = null;
			}
			catch (Exception ex)
			{
				Log($"Unexpected error launching application '{this.applicationExePath}'. Error: {ex.Message}", ConsoleColor.Red);
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
							Log($"TCP Port {host}:{port} connection failed (not connected).");
						}
						return client.Connected;
					}
					else
					{
						Log($"TCP Port Check Timeout: {host}:{port} did not respond within {timeoutMilliseconds}ms.");
						return false;
					}
				}
				catch (OperationCanceledException) { throw; }
				catch (SocketException ex)
				{
					Log($"TCP Port Check Error: Could not connect to {host}:{port}. Socket error: {ex.SocketErrorCode} - {ex.Message}");
					return false;
				}
				catch (Exception ex)
				{
					Log($"TCP Port Check Unexpected Error: {ex.Message}");
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
						Log($"UDP Port Send Timeout: {host}:{port} did not complete send within {timeoutMilliseconds}ms.");
						return false;
					}
				}
				catch (OperationCanceledException) { throw; }
				catch (SocketException ex)
				{
					Log($"UDP Port Check Error: Could not send to {host}:{port}. Socket error: {ex.SocketErrorCode} - {ex.Message}");
					return false;
				}
				catch (Exception ex)
				{
					Log($"UDP Port Check Unexpected Error: {ex.Message}");
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
							Log($"WebSocket Port Check: Connection failed. State: {ws.State}.");
							return false;
						}
					}
					else
					{
						Log($"WebSocket Port Check Timeout: {webSocketUri} did not establish connection within {timeoutMilliseconds}ms.");
						return false;
					}
				}
				catch (OperationCanceledException) { throw; }
				catch (WebSocketException ex)
				{
					Log($"WebSocket Port Check Error: {webSocketUri}. WebSocket error: {ex.WebSocketErrorCode} - {ex.Message}");
					return false;
				}
				catch (Exception ex)
				{
					Log($"WebSocket Port Check Unexpected Error: {ex.Message}");
					return false;
				}
			}
		}
	}
}