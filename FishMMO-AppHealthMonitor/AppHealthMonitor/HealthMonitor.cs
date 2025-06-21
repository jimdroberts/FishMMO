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
		private readonly TimeSpan checkInterval;
		private readonly CancellationToken cancellationToken;

		// NEW: Field to hold the specific process instance launched by this monitor
		private Process monitoredProcess;

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
			this.monitoredProcess = null; // Initialize to null
		}

		public async Task StartMonitoring()
		{
			// Initial launch of the application if it's not already running
			// or if it crashed before the daemon started monitoring.
			if (!IsApplicationProcessRunning())
			{
				Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Application process not found at startup. Attempting initial launch...");
				LaunchApplication();
				// Give it a moment to start up before the first health check
				await Task.Delay(TimeSpan.FromSeconds(5), this.cancellationToken); // Initial startup delay
			}


			while (!this.cancellationToken.IsCancellationRequested)
			{
				Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Performing health check...");
				bool isHealthy = await CheckApplicationHealth();

				if (!isHealthy)
				{
					Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Application detected as unhealthy. Attempting to restart...");
					KillApplication(); // Only kills the specific process this monitor manages
					LaunchApplication(); // Relaunches the specific process
				}
				else
				{
					Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Application is healthy.");
				}

				try
				{
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
		/// Checks the health of the application by verifying its specific process and all configured port types.
		/// </summary>
		/// <returns>True if the application is considered healthy, false otherwise.</returns>
		private async Task<bool> CheckApplicationHealth()
		{
			// 1. Check if the specific process launched by THIS monitor is running
			if (!IsApplicationProcessRunning())
			{
				Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Health Check: Monitored process is not running or has exited.");
				return false; // Process not running or exited
			}

			// If we reached here, monitoredProcess is not null and is still running.
			// Refresh its state to ensure accurate HasExited.
			try
			{
				this.monitoredProcess.Refresh();
				if (this.monitoredProcess.HasExited)
				{
					Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Health Check: Monitored process (ID: {this.monitoredProcess.Id}) has exited after refresh.");
					return false;
				}
				Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Health Check: Monitored process (ID: {this.monitoredProcess.Id}, Name: {this.monitoredProcess.ProcessName}) is running.");
			}
			catch (InvalidOperationException) // Process might have exited just before Refresh()
			{
				Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Health Check: Monitored process seems to have exited unexpectedly during refresh.");
				return false;
			}


			// 2. Check if the specified ports are responsive based on ALL configured PortTypes
			bool allPortsResponsive = true;
			if (this.monitoredPort > 0)
			{
				foreach (var portType in this.portTypes)
				{
					Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Health Check: Attempting to connect to port {this.monitoredPort} (Type: {portType})...");
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
						case PortType.None:
							Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Health Check: PortType is None. Skipping specific port check for this type.");
							currentPortTypeResponsive = true;
							break;
						default:
							Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Health Check: Unsupported PortType '{portType}'. Skipping this check.");
							currentPortTypeResponsive = false;
							break;
					}

					if (!currentPortTypeResponsive)
					{
						Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Health Check: Port {this.monitoredPort} (Type: {portType}) is NOT responsive.");
						allPortsResponsive = false; // Mark as unhealthy if any port check fails
						break; // No need to check other ports if one already failed
					}
					Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Health Check: Port {this.monitoredPort} (Type: {portType}) is responsive.");
				}
			}
			else
			{
				Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Health Check: No port specified for monitoring (MonitoredPort is 0 or less). Skipping all port checks.");
			}

			return allPortsResponsive; // Return true only if process is running and all ports are healthy
		}

		/// <summary>
		/// Determines if the specific process managed by this monitor is currently running.
		/// </summary>
		private bool IsApplicationProcessRunning()
		{
			if (this.monitoredProcess == null)
			{
				return false; // Process not launched yet by this monitor
			}

			try
			{
				// Try to refresh its state; if it throws, the process handle is invalid (i.e., it exited)
				this.monitoredProcess.Refresh();
				return !this.monitoredProcess.HasExited;
			}
			catch (InvalidOperationException)
			{
				// The process has exited. Clear the reference.
				this.monitoredProcess = null;
				return false;
			}
			catch (Exception ex)
			{
				// Other errors refreshing process state
				Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Error refreshing process state for ID: {this.monitoredProcess?.Id}. Error: {ex.Message}");
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
				Console.WriteLine($"[{DateTime.Now}] [{this.appName}] No specific process to kill (not launched by this monitor or already exited).");
				return;
			}

			// Refresh to get the latest status before attempting to kill
			this.monitoredProcess.Refresh();
			if (this.monitoredProcess.HasExited)
			{
				Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Process ID: {this.monitoredProcess.Id} (Name: {this.monitoredProcess.ProcessName}) has already exited.");
				this.monitoredProcess = null; // Clear reference
				return;
			}

			try
			{
				Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Killing specific process ID: {this.monitoredProcess.Id} (Name: {this.monitoredProcess.ProcessName})...");
				this.monitoredProcess.Kill();
				this.monitoredProcess.WaitForExit(5000); // Wait up to 5 seconds for the process to exit

				if (this.monitoredProcess.HasExited)
				{
					Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Process ID: {this.monitoredProcess.Id} killed successfully.");
				}
				else
				{
					Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Warning: Process ID: {this.monitoredProcess.Id} did not exit after kill command.");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Error killing process ID: {this.monitoredProcess.Id}. Error: {ex.Message}");
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
			Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Launching application '{this.applicationExePath}' with arguments: '{this.launchArguments}'...");
			try
			{
				ProcessStartInfo startInfo = new ProcessStartInfo
				{
					FileName = this.applicationExePath,
					Arguments = this.launchArguments,
					UseShellExecute = true // Set to true to launch as a separate process, allowing it to run independently
				};

				this.monitoredProcess = Process.Start(startInfo); // Store the launched process
				if (this.monitoredProcess != null)
				{
					Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Application launched successfully. Process ID: {this.monitoredProcess.Id}");
				}
				else
				{
					Console.WriteLine($"[{DateTime.Now}] [{this.appName}] Warning: Process.Start returned null for '{this.applicationExePath}'.");
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
					byte[] datagram = Encoding.UTF8.GetBytes("healththis.check");
					var sendTask = udpClient.SendAsync(datagram, datagram.Length, host, port);

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
					var connectTask = ws.ConnectAsync(new Uri(webSocketUri), this.cancellationToken);
					if (await Task.WhenAny(connectTask, Task.Delay(timeoutMilliseconds, this.cancellationToken)) == connectTask)
					{
						this.cancellationToken.ThrowIfCancellationRequested();
						if (ws.State == WebSocketState.Open)
						{
							await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Health check", this.cancellationToken);
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