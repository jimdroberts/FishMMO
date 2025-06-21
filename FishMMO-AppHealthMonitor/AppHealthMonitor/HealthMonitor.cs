using System.Diagnostics;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

namespace AppHealthMonitor
{
	public class HealthMonitor
	{
		private readonly string appName;
		private readonly string applicationExePath;
		private readonly string applicationProcessName;
		private readonly int monitoredPort;
		private readonly List<PortType> portTypes;
		private readonly string launchArguments;
		private readonly TimeSpan checkInterval; // Base check interval
		private readonly CancellationToken cancellationToken;

		private Process monitoredProcess;

		private const int MaxHealthCheckRetries = 5;
		private readonly TimeSpan initialHealthCheckDelay = TimeSpan.FromSeconds(30);
		private readonly TimeSpan processCrashRestartDelay = TimeSpan.FromSeconds(10);


		/// <summary>
		/// Initializes a new instance of the HealthMonitor class.
		/// </summary>
		/// <param name="appName">A friendly name for the application being monitored.</param>
		/// <param name="applicationExePath">The full path to the application's executable.</param>
		/// <param name="applicationProcessName">The name of the application's process (e.g., "myapp" for myapp.exe).</param>
		/// <param name="monitoredPort">The TCP/UDP/WebSocket port the application should be listening on.</param>
		/// <param name="portTypes">A list of network port types to monitor (TCP, UDP, WebSocket, or None).</param>
		/// <param name="launchArguments">Optional arguments to pass when launching the application.</param>
		/// <param name="checkInterval">The time interval between health checks.</param>
		/// <param name="cancellationToken">A shared cancellation token to stop monitoring gracefully.</param>
		public HealthMonitor(
			string appName,
			string applicationExePath,
			string applicationProcessName,
			int monitoredPort,
			List<PortType> portTypes,
			string launchArguments,
			TimeSpan checkInterval,
			CancellationToken cancellationToken)
		{
			this.appName = appName ?? throw new ArgumentNullException(nameof(appName));
			this.applicationExePath = applicationExePath ?? throw new ArgumentNullException(nameof(applicationExePath));
			this.applicationProcessName = applicationProcessName ?? throw new ArgumentNullException(nameof(applicationProcessName));
			this.monitoredPort = monitoredPort;
			this.portTypes = portTypes ?? new List<PortType> { PortType.None };
			this.launchArguments = launchArguments ?? string.Empty;
			this.checkInterval = checkInterval;
			this.cancellationToken = cancellationToken;
			this.monitoredProcess = null;
		}

		public async Task StartMonitoring()
		{
			// Initial launch of the application if it's not already running
			if (!IsApplicationProcessRunning())
			{
				Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Application process not found at startup. Attempting initial launch...");
				LaunchApplication();
				// Give it a moment to start up after initial launch
				await Task.Delay(TimeSpan.FromSeconds(5), this.cancellationToken);
			}

			Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Waiting {this.initialHealthCheckDelay.TotalSeconds} seconds before first health check...");
			await Task.Delay(this.initialHealthCheckDelay, this.cancellationToken);

			while (!this.cancellationToken.IsCancellationRequested)
			{
				Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Performing health check...");

				// FIRST CHECK: Is the process running?
				if (!IsApplicationProcessRunning())
				{
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Process is NOT running or has exited. Preparing to restart in {this.processCrashRestartDelay.TotalSeconds} seconds...");
					Console.ResetColor();

					await Task.Delay(this.processCrashRestartDelay, this.cancellationToken); // Wait 10 seconds
					if (this.cancellationToken.IsCancellationRequested) return; // Check for cancellation during delay

					KillApplication(); // Ensure it's fully gone
					LaunchApplication();
					// Give it a moment to start up after crash restart
					await Task.Delay(TimeSpan.FromSeconds(5), this.cancellationToken);
				}
				else // Process IS running, now check ports
				{
					bool portsHealthy = false;
					int retryCount = 0;
					TimeSpan currentRetryDelay = this.checkInterval; // Start with the base interval for the first potential retry

					while (retryCount < MaxHealthCheckRetries)
					{
						portsHealthy = await CheckApplicationPortsResponsiveness(); // Only checks ports

						if (portsHealthy)
						{
							Console.ForegroundColor = ConsoleColor.Green;
							Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Application process is running and ports are healthy.");
							Console.ResetColor();
							break; // Exit retry loop, application is healthy
						}
						else
						{
							retryCount++;
							if (retryCount < MaxHealthCheckRetries)
							{
								Console.ForegroundColor = ConsoleColor.Magenta;
								Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Ports are unresponsive (Attempt {retryCount}/{MaxHealthCheckRetries}). Retrying in {currentRetryDelay.TotalSeconds:F1} seconds...");
								Console.ResetColor();

								try
								{
									await Task.Delay(currentRetryDelay, this.cancellationToken);
								}
								catch (OperationCanceledException)
								{
									Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Monitoring task cancelled during retry delay for ports.");
									return; // Exit StartMonitoring entirely if cancelled
								}

								// Reduce delay by 50% for the next retry
								currentRetryDelay = TimeSpan.FromMilliseconds(currentRetryDelay.TotalMilliseconds * 0.5);
								if (currentRetryDelay < TimeSpan.FromSeconds(1)) // Ensure a minimum delay, e.g., 1 second
								{
									currentRetryDelay = TimeSpan.FromSeconds(1);
								}
							}
						}
					}

					// If after all retries, the ports are still not healthy, then restart
					if (!portsHealthy)
					{
						Console.ForegroundColor = ConsoleColor.Red;
						Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Ports detected as unresponsive after {MaxHealthCheckRetries} retries. Attempting to restart application...");
						Console.ResetColor();

						KillApplication();
						LaunchApplication();
						// Give it a moment to start up after restart
						await Task.Delay(TimeSpan.FromSeconds(5), this.cancellationToken);
					}
				}

				// Wait for the regular check interval before the next main health check cycle
				try
				{
					Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Waiting {this.checkInterval.TotalSeconds} seconds for next main health check cycle...");
					await Task.Delay(this.checkInterval, this.cancellationToken);
				}
				catch (OperationCanceledException)
				{
					Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Monitoring task cancelled.");
					break;
				}
			}
			Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Monitoring stopped.");
		}

		/// <summary>
		/// Checks only the responsiveness of the configured ports. Assumes the process is already running.
		/// </summary>
		/// <returns>True if all configured ports are responsive, false otherwise.</returns>
		private async Task<bool> CheckApplicationPortsResponsiveness()
		{
			if (this.monitoredPort <= 0 && this.portTypes.Contains(PortType.None))
			{
				// If no specific port is configured for monitoring, consider ports healthy by default.
				// This covers scenarios where an app doesn't expose a port for health checks.
				return true;
			}

			foreach (var portType in this.portTypes)
			{
				if (portType == PortType.None) continue; // Already handled above or means "no specific type" for port 0

				Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Port Check: Attempting to connect to port {this.monitoredPort} (Type: {portType})...");
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
						Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Port Check: Unsupported PortType '{portType}'. Skipping this check.");
						currentPortTypeResponsive = false; // Treat unsupported as unresponsive for safety
						break;
				}

				if (!currentPortTypeResponsive)
				{
					Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Port Check: Port {this.monitoredPort} (Type: {portType}) is NOT responsive.");
					return false; // If any port check fails, the overall port health is false
				}
			}
			return true; // All configured ports are responsive
		}

		/// <summary>
		/// Determines if the specific process managed by this monitor is currently running.
		/// </summary>
		private bool IsApplicationProcessRunning()
		{
			if (this.monitoredProcess == null)
			{
				Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Process check: monitoredProcess reference is null.");
				return false;
			}

			try
			{
				// Refresh its state; if it throws, the process handle is invalid (i.e., it exited)
				this.monitoredProcess.Refresh();
				if (this.monitoredProcess.HasExited)
				{
					Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Process check: Monitored process (ID: {this.monitoredProcess.Id}) has exited after refresh.");
					// It's crucial to clear the reference if the process has exited so we don't try to use a stale handle.
					this.monitoredProcess = null;
					return false;
				}
				Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Process check: Monitored process (ID: {this.monitoredProcess.Id}, Name: {this.monitoredProcess.ProcessName}) is running.");
				return true;
			}
			catch (InvalidOperationException)
			{
				// The process handle is no longer valid, meaning it exited or was never started properly
				Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Process check: Monitored process seems to have exited unexpectedly (InvalidOperationException).");
				this.monitoredProcess = null; // Clear reference
				return false;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Process check: Error refreshing process state for ID: {this.monitoredProcess?.Id}. Error: {ex.Message}");
				this.monitoredProcess = null; // Assume it's no longer valid
				return false;
			}
		}


		/// <summary>
		/// Kills the specific process instance launched by this monitor.
		/// </summary>
		private void KillApplication()
		{
			if (this.monitoredProcess == null)
			{
				return;
			}

			try
			{
				this.monitoredProcess.Refresh(); // Get latest state
				if (this.monitoredProcess.HasExited)
				{
					Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Process ID: {this.monitoredProcess.Id} (Name: {this.monitoredProcess.ProcessName}) has already exited, no need to kill.");
					this.monitoredProcess = null; // Clear reference if it already exited
					return;
				}

				Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Killing specific process ID: {this.monitoredProcess.Id} (Name: {this.monitoredProcess.ProcessName})...");
				this.monitoredProcess.Kill();
				this.monitoredProcess.WaitForExit(5000); // Wait up to 5 seconds for the process to exit

				if (this.monitoredProcess.HasExited)
				{
					Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Process ID: {this.monitoredProcess.Id} killed successfully.");
				}
				else
				{
					Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Warning: Process ID: {this.monitoredProcess.Id} did not exit after kill command. It might be stuck.");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Error killing process ID: {this.monitoredProcess.Id}. Error: {ex.Message}");
			}
			finally
			{
				this.monitoredProcess = null; // Always clear the reference after attempting to kill, regardless of success
			}
		}

		/// <summary>
		/// Launches the application executable and stores its process reference.
		/// </summary>
		private void LaunchApplication()
		{
			Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Launching application '{this.applicationExePath}' with arguments: '{this.launchArguments}'...");
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
					Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Application launched successfully. Process ID: {this.monitoredProcess.Id}");
				}
				else
				{
					Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Warning: Process.Start returned null for '{this.applicationExePath}'. This might indicate a problem.");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Error launching application '{this.applicationExePath}'. Error: {ex.Message}");
				this.monitoredProcess = null; // Ensure process reference is cleared on launch failure
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
						return client.Connected;
					}
					else
					{
						Console.WriteLine($"[{DateTime.Now}] [{this.appName}] TCP Port Check Timeout: {host}:{port} did not respond within {timeoutMilliseconds}ms.");
						return false;
					}
				}
				catch (OperationCanceledException) { throw; }
				catch (SocketException ex)
				{
					Console.WriteLine($"[{DateTime.Now}] [{this.appName}] TCP Port Check Error: Could not connect to {host}:{port}. Socket error: {ex.SocketErrorCode} - {ex.Message}");
					return false;
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[{DateTime.Now}] [{this.appName}] TCP Port Check Unexpected Error: {ex.Message}");
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
					// For UDP, "responsive" often just means we can send a datagram without an immediate error.
					// True responsiveness (e.g., getting a reply) would require the target app to send one back.
					// This simple check confirms if the OS allows sending to that port.
					byte[] datagram = Encoding.UTF8.GetBytes("health_check_ping");
					var sendTask = udpClient.SendAsync(datagram, datagram.Length, host, port);

					// We just care if the send operation completes or times out
					if (await Task.WhenAny(sendTask, Task.Delay(timeoutMilliseconds, this.cancellationToken)) == sendTask)
					{
						this.cancellationToken.ThrowIfCancellationRequested();
						return true;
					}
					else
					{
						Console.WriteLine($"[{DateTime.Now}] [{this.appName}] UDP Port Send Timeout: {host}:{port} did not complete send within {timeoutMilliseconds}ms.");
						return false;
					}
				}
				catch (OperationCanceledException) { throw; }
				catch (SocketException ex)
				{
					Console.WriteLine($"[{DateTime.Now}] [{this.appName}] UDP Port Check Error: Could not send to {host}:{port}. Socket error: {ex.SocketErrorCode} - {ex.Message}");
					return false;
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[{DateTime.Now}] [{this.appName}] UDP Port Check Unexpected Error: {ex.Message}");
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
					// Set a reasonable connection timeout for the WebSocket
					ws.Options.KeepAliveInterval = TimeSpan.Zero; // Don't send keep-alives during connect attempt
					var connectTask = ws.ConnectAsync(new Uri(webSocketUri), this.cancellationToken);

					if (await Task.WhenAny(connectTask, Task.Delay(timeoutMilliseconds, this.cancellationToken)) == connectTask)
					{
						this.cancellationToken.ThrowIfCancellationRequested();
						if (ws.State == WebSocketState.Open)
						{
							// Close the connection immediately after successful establishment
							await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Health check complete", CancellationToken.None); // Use CancellationToken.None for graceful close
							return true;
						}
						else
						{
							Console.WriteLine($"[{DateTime.Now}] [{this.appName}] WebSocket Port Check: Connection failed. State: {ws.State}");
							return false;
						}
					}
					else
					{
						Console.WriteLine($"[{DateTime.Now}] [{this.appName}] WebSocket Port Check Timeout: {webSocketUri} did not establish connection within {timeoutMilliseconds}ms.");
						return false;
					}
				}
				catch (OperationCanceledException) { throw; }
				catch (WebSocketException ex)
				{
					Console.WriteLine($"[{DateTime.Now}] [{this.appName}] WebSocket Port Check Error: {webSocketUri}. WebSocket error: {ex.WebSocketErrorCode} - {ex.Message}");
					return false;
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[{DateTime.Now}] [{this.appName}] WebSocket Port Check Unexpected Error: {ex.Message}");
					return false;
				}
			}
		}
	}
}