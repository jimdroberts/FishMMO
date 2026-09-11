using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
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

		/// <summary>
		/// The cycle this monitor belongs to, used to ask for supervision back after the monitoring
		/// loop has ended. Null when the monitor was constructed without a host, in which case a
		/// revival is impossible and operator start/restart on an ended monitor is refused.
		/// </summary>
		private readonly ISupervisionHost? supervisionHost;

		private readonly string logSource;
		private readonly string resolvedExePath;
		private readonly TimeSpan checkInterval;
		private readonly TimeSpan gracefulShutdownTimeout;
		private readonly TimeSpan forceKillTimeout;

		/// <summary>
		/// Maximum total time allowed for the entire kill sequence (graceful + force + safety margin).
		/// Computed per-monitor from configured timeouts to prevent silent truncation.
		/// Prevents the daemon from hanging indefinitely on zombie processes.
		/// </summary>
		private readonly TimeSpan killTimeout;

		private readonly long memoryThresholdBytes;
		private readonly TimeSpan initialRestartDelay;
		private readonly TimeSpan maxRestartDelay;
		private readonly TimeSpan initialHealthCheckDelay;
		private readonly TimeSpan postLaunchSettleDelay;
		private readonly int cpuThresholdPercent;
		private readonly int circuitBreakerFailureThreshold;
		private readonly int monitoredPort;
		private readonly int portCheckTimeoutMs;
		private readonly int maxRestartAttempts;
		private readonly int resourceCheckFailureThreshold;
		private readonly string healthCheckHost;

		/// <summary>
		/// Cached <see cref="ProcessStartInfo"/> built once in the constructor.
		/// Reused for every launch to avoid repeated allocations.
		/// </summary>
		private readonly ProcessStartInfo cachedStartInfo;

		/// <summary>
		/// Whether this monitor is configured for process-only monitoring without port checks.
		/// </summary>
		private readonly bool isProcessOnlyMonitoring;

		/// <summary>
		/// The number of consecutive restart attempts in the current failure cycle.
		/// </summary>
		private int currentRestartAttemptCount;

		/// <summary>
		/// Number of consecutive port check failures in the current monitoring cycle.
		/// Only mutated from the single monitoring loop task. Cross-thread reads via <see cref="GetStatus"/>.
		/// </summary>
		private int consecutivePortCheckFailures;

		/// <summary>
		/// Number of consecutive CPU/memory check failures in the current monitoring cycle.
		/// Only mutated from the single monitoring loop task. Cross-thread reads via <see cref="GetStatus"/>.
		/// </summary>
		private int consecutiveResourceCheckFailures;

		/// <summary>
		/// The currently monitored OS process, or null if none is tracked.
		/// Accessed atomically via <see cref="Volatile"/> and <see cref="Interlocked"/> operations.
		/// </summary>
		private Process? monitoredProcess;

		/// <summary>
		/// Tracks CPU usage measurement state for the monitored process.
		/// </summary>
		private CpuTracker cpuTracker;

		/// <summary>
		/// Whether the monitor has exhausted all configured restart attempts.
		/// </summary>
		private bool maxRestartsReached;

		/// <summary>
		/// Whether the monitor has completed its initial health check delay and begun checking.
		/// False during the startup settle period, preventing premature "HEALTHY" status display.
		/// </summary>
		private bool hasCompletedInitialCheck;

		/// <summary>
		/// Set when an operator deliberately stopped this application through the control plane.
		/// While set, the monitoring loop performs no health checks and no automatic restarts, and
		/// <see cref="LaunchApplicationAsync"/> refuses to start the process. Cleared only by an
		/// operator start or restart. Backed by an int for lock-free access via <see cref="Volatile"/>.
		/// </summary>
		private int operatorStopped;

		/// <summary>
		/// Set once <see cref="StartMonitoringAsync"/> has returned, and cleared again when a
		/// revival starts a fresh loop. A monitor whose loop has ended is supervising nothing, so an
		/// operator start or restart must re-establish supervision before it launches anything —
		/// see <see cref="ReviveByOperatorAsync"/> — and is refused outright when it cannot.
		/// </summary>
		private int monitoringLoopEnded;

		/// <summary>
		/// Claimed for the duration of a revival, so exactly one is ever in flight for this monitor.
		/// </summary>
		/// <remarks>
		/// The lifecycle gate already serializes operator commands, but it is a bounded wait that can
		/// legitimately time out; a revival that went ahead without it could otherwise hand the host a
		/// second monitoring loop for the same application. This claim does not expire.
		/// </remarks>
		private int revivalInProgress;

		/// <summary>
		/// Serializes process lifecycle transitions (kill + launch pairs) between the monitoring
		/// loop's automatic restart path and operator commands arriving from the control plane.
		/// Deliberately NOT taken inside <see cref="KillApplicationAsync"/>, which the orchestrator
		/// calls directly during force-kill and disposal and which must never block on this gate.
		/// </summary>
		private readonly SemaphoreSlim lifecycleGate = new SemaphoreSlim(1, 1);

		/// <summary>
		/// Maximum time an operator command waits for the lifecycle gate before reporting that the
		/// monitor was busy. Bounded so a stuck restart never leaves a command uncompleted.
		/// </summary>
		private static readonly TimeSpan operatorActionTimeout = TimeSpan.FromSeconds(60);

		/// <summary>
		/// Guard flag to prevent double disposal. Set atomically via <see cref="Interlocked.CompareExchange"/>.
		/// </summary>
		private int isDisposed;

		/// <summary>
		/// Gets the display name of the monitored application, used for diagnostics and logging.
		/// </summary>
		public string Name => logSource;

		/// <summary>
		/// Gets whether an operator deliberately stopped this application and it must stay stopped.
		/// </summary>
		public bool IsOperatorStopped => Volatile.Read(ref operatorStopped) != 0;

		/// <summary>
		/// Gets whether the monitoring loop has ended for this monitor (restart attempts exhausted,
		/// initial launch failed, or the cycle was cancelled).
		/// </summary>
		public bool IsMonitoringLoopEnded => Volatile.Read(ref monitoringLoopEnded) != 0;

		/// <summary>
		/// Groups CPU usage tracking state for clarity.
		/// Only accessed from the single monitoring loop task.
		/// </summary>
		private struct CpuTracker
		{
			/// <summary>
			/// Whether the tracker has captured an initial CPU baseline.
			/// </summary>
			public bool Initialized;

			/// <summary>
			/// The monotonic timestamp (from <see cref="Stopwatch.GetTimestamp"/>)
			/// of the last CPU usage measurement.
			/// </summary>
			public long LastCheckTimestamp;

			/// <summary>
			/// The cumulative processor time at the last measurement.
			/// </summary>
			public TimeSpan LastTotalProcessorTime;

			/// <summary>
			/// Resets the tracker so the next check re-initializes the baseline.
			/// After a reset, the first CPU check captures a new baseline and returns healthy,
			/// giving the freshly launched process one free health cycle to settle.
			/// This is an intentional trade-off: accurate delta-based CPU measurement requires
			/// two samples, so the first sample establishes the reference point.
			/// </summary>
			public void Reset() => Initialized = false;
		}

		/// <summary>
		/// Reusable array for parallel port check tasks, sized to the number of health checkers.
		/// Avoids allocating a new list on every health check cycle.
		/// Elements are populated before each WhenAll call and cleared after result inspection.
		/// </summary>
		private readonly Task<bool>[] portCheckTasks;

		/// <summary>
		/// Number of bytes per megabyte, used for memory threshold display conversions.
		/// </summary>
		private const double BytesPerMB = 1_048_576.0;

		/// <summary>
		/// POSIX signal number for SIGTERM (graceful termination request).
		/// </summary>
		private const int SigTerm = 15;

		/// <summary>
		/// Safety margin in seconds added to the combined graceful + force-kill timeout
		/// to account for process.Refresh() calls, logging, and code between kill phases.
		/// </summary>
		private const int KillTimeoutSafetyMarginSeconds = 5;

		/// <summary>
		/// Sends a POSIX signal to a process. Used for graceful SIGTERM shutdown on Linux and macOS.
		/// The method name must match the native function name via <see cref="DllImportAttribute.EntryPoint"/>.
		/// </summary>
		/// <param name="pid">The process ID to signal.</param>
		/// <param name="sig">The signal number to send.</param>
		/// <returns>0 on success; -1 on failure (check errno via <see cref="Marshal.GetLastWin32Error"/>).</returns>
		[DllImport("libc", EntryPoint = "kill", SetLastError = true)]
		[SupportedOSPlatform("linux")]
		[SupportedOSPlatform("macos")]
		private static extern int PosixKill(int pid, int sig);

		/// <summary>
		/// Initializes a new instance of the <see cref="HealthMonitor"/> class.
		/// </summary>
		/// <param name="config">The application configuration. Must have <see cref="AppConfig.TryApplyDefaultsAndValidate"/> called first.</param>
		/// <param name="healthCheckers">The health checkers to use for port monitoring. Empty list for process-only monitoring.</param>
		/// <param name="headless">Whether to launch the process in headless mode (no window, shell execution disabled).</param>
		/// <param name="cancellationToken">Token to signal cancellation of monitoring operations.</param>
		/// <param name="supervisionHost">
		/// The monitoring cycle this monitor runs in, asked for supervision back when an operator
		/// starts or restarts an application whose loop has already ended. Null means no revival is
		/// possible and such a command is refused rather than leaving an unwatched process.
		/// </param>
		/// <exception cref="ArgumentNullException">Thrown when config is null.</exception>
		public HealthMonitor(
			AppConfig config,
			IReadOnlyList<IHealthChecker> healthCheckers,
			bool headless,
			CancellationToken cancellationToken,
			ISupervisionHost? supervisionHost = null)
		{
			ArgumentNullException.ThrowIfNull(config);

			this.healthCheckers = healthCheckers ?? Array.Empty<IHealthChecker>();
			this.cancellationToken = cancellationToken;
			this.supervisionHost = supervisionHost;

			logSource = config.Name;
			resolvedExePath = config.ApplicationExePath;

			checkInterval = TimeSpan.FromSeconds(config.CheckIntervalSeconds);
			gracefulShutdownTimeout = TimeSpan.FromSeconds(config.GracefulShutdownTimeoutSeconds);
			forceKillTimeout = TimeSpan.FromSeconds(config.ForceKillTimeoutSeconds);
			killTimeout = gracefulShutdownTimeout + forceKillTimeout + TimeSpan.FromSeconds(KillTimeoutSafetyMarginSeconds);
			initialHealthCheckDelay = TimeSpan.FromSeconds(config.InitialHealthCheckDelaySeconds);
			postLaunchSettleDelay = TimeSpan.FromSeconds(config.PostLaunchSettleDelaySeconds);
			memoryThresholdBytes = (long)config.MemoryThresholdMB * 1024L * 1024L;
			initialRestartDelay = TimeSpan.FromSeconds(config.InitialRestartDelaySeconds);
			maxRestartDelay = TimeSpan.FromSeconds(config.MaxRestartDelaySeconds);
			cpuThresholdPercent = config.CpuThresholdPercent;
			circuitBreakerFailureThreshold = config.CircuitBreakerFailureThreshold;
			monitoredPort = config.MonitoredPort;
			portCheckTimeoutMs = config.PortCheckTimeoutMs;
			maxRestartAttempts = config.MaxRestartAttempts;
			resourceCheckFailureThreshold = config.ResourceCheckFailureThreshold;
			healthCheckHost = config.HealthCheckHost;

			portCheckTasks = new Task<bool>[this.healthCheckers.Count];
			isProcessOnlyMonitoring = this.healthCheckers.Count == 0;

			cachedStartInfo = new ProcessStartInfo
			{
				FileName = resolvedExePath,
				Arguments = config.LaunchArguments ?? string.Empty,
				UseShellExecute = !headless,
				RedirectStandardOutput = false,
				RedirectStandardError = false,
				CreateNoWindow = headless,
			};
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
					// Do NOT call process.Refresh() here — it is not thread-safe.
					// The monitoring loop refreshes on its own thread; we read cached state only.
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
				catch (NotSupportedException)
				{
					// Process not started by this instance — treat as not running.
				}
				catch (Win32Exception)
				{
					// Permission denied accessing /proc/{pid} on Linux — treat as not running.
				}
			}

			return new HealthMonitorStatus(
				logSource,
				pid,
				running,
				Volatile.Read(ref currentRestartAttemptCount),
				maxRestartAttempts,
				Volatile.Read(ref maxRestartsReached),
				Volatile.Read(ref hasCompletedInitialCheck),
				Volatile.Read(ref consecutivePortCheckFailures),
				Volatile.Read(ref consecutiveResourceCheckFailures),
				Volatile.Read(ref operatorStopped) != 0);
		}

		/// <summary>
		/// Starts the monitoring loop for the application.
		/// Continuously checks application health and performs restarts as needed.
		/// Exits when cancelled, when maximum restart attempts are exhausted, or when the initial launch fails.
		/// </summary>
		/// <returns>A task that represents the asynchronous monitoring operation.</returns>
		public async Task StartMonitoringAsync()
		{
			try
			{
				await MonitorLoopAsync();
			}
			finally
			{
				// Recorded on every exit path — exhausted restarts, failed initial launch or
				// cancellation. Operator start/restart commands consult this so they never launch
				// a process that no supervisor is watching.
				Volatile.Write(ref monitoringLoopEnded, 1);
			}
		}

		/// <summary>
		/// The monitoring loop itself. See <see cref="StartMonitoringAsync"/>.
		/// </summary>
		/// <returns>A task that represents the asynchronous monitoring operation.</returns>
		private async Task MonitorLoopAsync()
		{
			if (cancellationToken.IsCancellationRequested)
			{
				Log.Info(logSource, "Monitoring start skipped because cancellation was already requested.");
				return;
			}

			Log.Info(logSource, "Starting monitoring loop.");

			if (!IsApplicationProcessRunning())
			{
				Log.Info(logSource, "Application process not found at startup. Attempting initial launch.");

				// Serialized against operator commands so an initial launch and a control-plane
				// stop cannot interleave into a running process nobody asked for.
				bool gateTaken = await TryAcquireLifecycleGateAsync(cancellationToken);
				try
				{
					await LaunchApplicationAsync();
				}
				finally
				{
					ReleaseLifecycleGate(gateTaken);
				}

				if (Volatile.Read(ref monitoredProcess) == null)
				{
					Log.Critical(logSource, "Initial launch failed. The executable path may be invalid. Monitoring will not start.");
					Volatile.Write(ref maxRestartsReached, true);
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

			Volatile.Write(ref hasCompletedInitialCheck, true);

			using var periodicTimer = new PeriodicTimer(checkInterval);
			while (!cancellationToken.IsCancellationRequested && !Volatile.Read(ref maxRestartsReached))
			{
				// An operator stop means "stay stopped". Health checks would see a dead process and
				// the supervisor would dutifully restart the very thing that was just stopped, so the
				// whole check-and-restart pass is skipped until an operator starts it again.
				if (Volatile.Read(ref operatorStopped) != 0)
				{
					Log.Debug(logSource, "Stopped by operator. Health checks and automatic restarts are suspended.");

					try
					{
						if (!await periodicTimer.WaitForNextTickAsync(cancellationToken))
						{
							break;
						}
					}
					catch (OperationCanceledException)
					{
						Log.Info(logSource, "Monitoring task cancelled while stopped by operator. Exiting loop.");
						break;
					}
					continue;
				}

				Log.Debug(logSource, "Performing health check cycle.");

				bool needsRestart = false;

				if (!IsApplicationProcessRunning())
				{
					Log.Error(logSource, "Process is NOT running or has exited.");
					needsRestart = true;
				}
				else
				{
					if ((cpuThresholdPercent > 0 || memoryThresholdBytes > 0) && !CheckMemoryAndCpuUsage(out bool isTransientFailure))
					{
						if (isTransientFailure)
						{
							int failures = Interlocked.Increment(ref consecutiveResourceCheckFailures);
							Log.Warning(logSource, $"Transient resource check failure. Consecutive: {failures}/{resourceCheckFailureThreshold}.");
							if (failures >= resourceCheckFailureThreshold)
							{
								Log.Error(logSource, "Resource check failures exceeded threshold. Triggering restart.");
								needsRestart = true;
							}
						}
						else
						{
							Log.Error(logSource, "CPU or Memory usage exceeds configured thresholds.");
							Volatile.Write(ref consecutiveResourceCheckFailures, 0);
							needsRestart = true;
						}
					}
					else
					{
						Volatile.Write(ref consecutiveResourceCheckFailures, 0);
						if (!isProcessOnlyMonitoring)
						{
							try
							{
								needsRestart = await EvaluateCircuitBreakerAndPortHealth();
							}
							catch (OperationCanceledException)
							{
								Log.Info(logSource, "Port health check cancelled. Exiting monitoring loop.");
								break;
							}
						}
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
					Volatile.Write(ref currentRestartAttemptCount, 0);
					Log.Debug(logSource, "Application is healthy.");
				}

				try
				{
					Log.Debug(logSource, $"Waiting for next health check tick ({checkInterval.TotalSeconds}s interval)...");
					if (!await periodicTimer.WaitForNextTickAsync(cancellationToken))
					{
						break;
					}
				}
				catch (OperationCanceledException)
				{
					Log.Info(logSource, "Monitoring task cancelled. Exiting loop.");
					break;
				}
			}

			if (Volatile.Read(ref maxRestartsReached))
			{
				Log.Critical(logSource, $"Monitoring stopped: maximum restart attempts ({maxRestartAttempts}) exhausted.");
			}

			Log.Info(logSource, "Monitoring stopped.");
		}

		/// <summary>
		/// Performs port health checks and tracks consecutive failures against the circuit breaker threshold.
		/// When the threshold is reached, triggers a restart and resets the failure counter.
		/// </summary>
		/// <returns>True if a restart is needed; otherwise, false.</returns>
		private async Task<bool> EvaluateCircuitBreakerAndPortHealth()
		{
			if (!await CheckApplicationPortsResponsiveness())
			{
				int failures = Interlocked.Increment(ref consecutivePortCheckFailures);
				Log.Warning(logSource, $"Port check failed. Consecutive failures: {failures}/{circuitBreakerFailureThreshold}.");

				if (failures >= circuitBreakerFailureThreshold)
				{
					Log.Error(logSource, "Circuit breaker threshold reached. Too many consecutive port failures. Triggering restart.");
					Volatile.Write(ref consecutivePortCheckFailures, 0);
					return true;
				}
				return false;
			}

			if (Volatile.Read(ref consecutivePortCheckFailures) > 0)
			{
				Volatile.Write(ref consecutivePortCheckFailures, 0);
				Log.Info(logSource, "Port check successful. Consecutive failures reset.");
			}
			return false;
		}

		/// <summary>
		/// Handles the restart logic for an unhealthy application.
		/// Implements exponential backoff with random jitter (±20%) to prevent thundering herd
		/// effects when multiple applications restart simultaneously.
		/// Resets consecutive failure counters after restart to give the application a clean evaluation window.
		/// Sets <see cref="maxRestartsReached"/> when the limit is hit.
		/// </summary>
		/// <returns>A task that represents the asynchronous restart operation.</returns>
		private async Task HandleApplicationRestart()
		{
			int attempt = Interlocked.Increment(ref currentRestartAttemptCount);

			if (attempt > maxRestartAttempts)
			{
				Volatile.Write(ref maxRestartsReached, true);
				return;
			}

			// Compute the backoff delay with exponential growth, capped at the configured maximum.
			var backoffDelay = TimeSpan.FromSeconds(
				Math.Min(maxRestartDelay.TotalSeconds, initialRestartDelay.TotalSeconds * Math.Pow(2, attempt - 1))
			);

			TimeSpan delayToUse = ApplyJitter(backoffDelay);

			Log.Warning(logSource, $"Application unhealthy. Attempting restart (Attempt {attempt}/{maxRestartAttempts}).");
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

			// An operator stop can land during the backoff delay. Re-check under the lifecycle gate
			// so the automatic restart never revives an application somebody just stopped.
			bool gateTaken = await TryAcquireLifecycleGateAsync(cancellationToken);
			try
			{
				if (Volatile.Read(ref operatorStopped) != 0)
				{
					Log.Info(logSource, "Automatic restart abandoned: the application was stopped by an operator.");
					return;
				}

				await KillApplicationAsync();
				await LaunchApplicationAsync();
			}
			finally
			{
				ReleaseLifecycleGate(gateTaken);
			}

			// Reset consecutive failure counters so the freshly restarted application gets
			// a clean evaluation window. Exponential backoff and MaxRestartAttempts already
			// prevent rapid-fire restart loops.
			Volatile.Write(ref consecutivePortCheckFailures, 0);
			Volatile.Write(ref consecutiveResourceCheckFailures, 0);

			// Only wait for the settle delay if the launch actually succeeded.
			// If the process failed to start, skip the delay so the next health check cycle
			// detects the failure sooner.
			if (Volatile.Read(ref monitoredProcess) != null)
			{
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
		}

		/// <summary>
		/// Applies ±20% random jitter to a delay to prevent synchronized restart storms.
		/// The jitter factor is 0.8–1.2. The caller is responsible for ensuring the base delay
		/// is positive; a zero base delay produces a zero result.
		/// </summary>
		/// <param name="delay">The base delay to apply jitter to.</param>
		/// <returns>The jittered delay.</returns>
		private static TimeSpan ApplyJitter(TimeSpan delay)
		{
			double jitterFactor = 0.8 + (Random.Shared.NextDouble() * 0.4); // 0.8 to 1.2
			return TimeSpan.FromSeconds(delay.TotalSeconds * jitterFactor);
		}

		/// <summary>
		/// Checks if the monitored application's CPU and memory usage are within configured thresholds.
		/// Distinguishes between genuine threshold violations and transient access errors.
		/// </summary>
		/// <param name="isTransientFailure">
		/// Set to true when the check failed due to a transient error (e.g., process disposed concurrently,
		/// brief /proc access denial) rather than a genuine threshold violation.
		/// </param>
		/// <returns>True if usage is within thresholds or thresholds are disabled; otherwise, false.</returns>
		private bool CheckMemoryAndCpuUsage(out bool isTransientFailure)
		{
			isTransientFailure = false;
			var process = Volatile.Read(ref monitoredProcess);
			if (process == null)
			{
				Log.Debug(logSource, "Process not available for CPU/Memory check.");
				isTransientFailure = true;
				return false;
			}

			try
			{
				// No Refresh() needed — IsApplicationProcessRunning() already refreshed the process state this cycle.
				if (process.HasExited)
				{
					Log.Debug(logSource, "Process not available for CPU/Memory check.");
					isTransientFailure = true;
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
					if (!cpuTracker.Initialized)
					{
						cpuTracker.LastCheckTimestamp = Stopwatch.GetTimestamp();
						cpuTracker.LastTotalProcessorTime = process.TotalProcessorTime;
						cpuTracker.Initialized = true;
						Log.Debug(logSource, "Initializing CPU usage tracking.");
						return true;
					}

					TimeSpan currentTotalProcessorTime = process.TotalProcessorTime;
					long currentCheckTimestamp = Stopwatch.GetTimestamp();

					double cpuTimeUsed = (currentTotalProcessorTime - cpuTracker.LastTotalProcessorTime).TotalMilliseconds;
					double timeElapsed = (currentCheckTimestamp - cpuTracker.LastCheckTimestamp) * 1000.0 / Stopwatch.Frequency;

					// Always update the tracker so subsequent checks use accurate deltas,
					// even when the current reading exceeds the threshold.
					cpuTracker.LastCheckTimestamp = currentCheckTimestamp;
					cpuTracker.LastTotalProcessorTime = currentTotalProcessorTime;

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
				}

				Log.Debug(logSource, "CPU/Memory checks passed (if configured).");
				return true;
			}
			catch (ObjectDisposedException)
			{
				// Process was disposed by a concurrent KillApplicationAsync call.
				Log.Debug(logSource, "Process was disposed during CPU/Memory check (concurrent kill).");
				isTransientFailure = true;
				return false;
			}
			catch (InvalidOperationException ex)
			{
				Log.Error(logSource, "Process exited during CPU/Memory check.", ex);
				isTransientFailure = true;
				return false;
			}
			catch (Exception ex)
			{
				Log.Error(logSource, $"Error during CPU/Memory check: {ex.Message}", ex);
				isTransientFailure = true;
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
			try
			{
				for (int i = 0; i < healthCheckers.Count; i++)
				{
					var checker = healthCheckers[i];
					/* A pulse check is a database round trip, not a socket connect, so it gets a
					 * longer budget: timing it out at the port timeout would report a healthy
					 * server as dead whenever the database was merely busy. */
					int timeout = checker.PortType == PortType.DatabasePulse
						? Math.Max(portCheckTimeoutMs, PulseHealthChecker.DefaultTimeoutMs)
						: portCheckTimeoutMs;
					Log.Debug(logSource, $"Port Check: Checking port {monitoredPort} (Type: {checker.PortType})...");
					portCheckTasks[i] = checker.IsResponsiveAsync(healthCheckHost, monitoredPort, timeout, cancellationToken);
				}

				try
				{
					await Task.WhenAll(portCheckTasks);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					Log.Error(logSource, $"Port check encountered an unexpected error: {ex.Message}", ex);
				}

				bool allResponsive = true;
				for (int i = 0; i < portCheckTasks.Length; i++)
				{
					if (portCheckTasks[i].IsFaulted)
					{
						var taskEx = portCheckTasks[i].Exception;
						if (taskEx != null)
						{
							foreach (var innerEx in taskEx.InnerExceptions)
							{
								Log.Error(logSource, $"Port Check: Port {monitoredPort} (Type: {healthCheckers[i].PortType}) checker faulted: {innerEx.Message}", innerEx);
							}
						}
						allResponsive = false;
					}
					else if (!portCheckTasks[i].IsCompletedSuccessfully || !portCheckTasks[i].Result)
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
			finally
			{
				Array.Clear(portCheckTasks);
			}
		}

		/// <summary>
		/// Atomically releases the monitored process reference and disposes it.
		/// No-ops if a concurrent caller already exchanged the reference.
		/// </summary>
		/// <param name="process">The process reference to release. Must not be null.</param>
		private void TryReleaseProcess(Process process)
		{
			if (Interlocked.CompareExchange(ref monitoredProcess, null, process) == process)
			{
				process.Dispose();
			}
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
					TryReleaseProcess(process);
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
				TryReleaseProcess(process);
				return false;
			}
			catch (Exception ex)
			{
				Log.Error(logSource, $"Error refreshing process state. Error: {ex.Message}", ex);
				TryReleaseProcess(process);
				return false;
			}
		}

		/// <summary>
		/// Attempts to send a platform-appropriate graceful shutdown signal to the process.
		/// On Windows, sends <see cref="Process.CloseMainWindow"/> if a main window exists.
		/// On Linux/macOS, sends SIGTERM via P/Invoke.
		/// </summary>
		/// <param name="process">The process to signal.</param>
		/// <param name="processId">The process ID for logging.</param>
		/// <returns>True if a graceful shutdown signal was sent; false if no signal could be sent.</returns>
		private bool TrySendGracefulShutdownSignal(Process process, int processId)
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				if (process.MainWindowHandle != IntPtr.Zero)
				{
					process.CloseMainWindow();
					Log.Info(logSource, "KillApplication: Sent CloseMainWindow signal (Windows).");
					return true;
				}

				Log.Info(logSource, "KillApplication: No main window detected for graceful shutdown on Windows.");
				return false;
			}

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				try
				{
					int result = PosixKill(processId, SigTerm);
					if (result == 0)
					{
						Log.Info(logSource, $"KillApplication: Sent SIGTERM to process ID: {processId} (Unix).");
						return true;
					}

					Log.Warning(logSource, $"KillApplication: Failed to send SIGTERM to process ID: {processId}. errno: {Marshal.GetLastWin32Error()}");
				}
				catch (Exception ex)
				{
					Log.Warning(logSource, $"KillApplication: Failed to send SIGTERM: {ex.Message}");
				}
			}

			return false;
		}

		/// <summary>
		/// Asynchronously terminates the monitored application process.
		/// Attempts graceful shutdown first (SIGTERM on Linux/macOS, CloseMainWindow on Windows),
		/// then force-kills if the process does not exit within the configured timeout.
		/// Uses a standalone bounded timeout (not linked to the monitoring lifecycle token) to ensure
		/// graceful shutdown is always honored, even during stop/shutdown disposal.
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

			// Use a standalone bounded CTS to prevent hanging indefinitely on zombie processes.
			// This is intentionally NOT linked to cancellationToken — the monitoring lifecycle
			// token is already cancelled during stop/shutdown, which would pre-cancel the CTS
			// and bypass all graceful shutdown waits.
			// The timeout is computed per-monitor from configured graceful + force-kill timeouts
			// plus a safety margin, ensuring user-configured timeouts are fully honored.
			using var killTimeoutCts = new CancellationTokenSource(killTimeout);

			try
			{
				process.Refresh();
				if (process.HasExited)
				{
					Log.Info(logSource, $"KillApplication: Process ID: {process.Id} has already exited.");
					return;
				}

				int processId = process.Id;

				Log.Warning(logSource, $"KillApplication: Attempting graceful shutdown for process ID: {processId}...");

				bool gracefulShutdownAttempted = TrySendGracefulShutdownSignal(process, processId);

				if (gracefulShutdownAttempted)
				{
					Log.Debug(logSource, $"KillApplication: Waiting for process ID: {processId} to exit gracefully ({gracefulShutdownTimeout.TotalSeconds}s timeout).");
					try
					{
						await process.WaitForExitAsync(killTimeoutCts.Token)
							.WaitAsync(gracefulShutdownTimeout, killTimeoutCts.Token);
						Log.Info(logSource, $"KillApplication: Process ID: {processId} exited gracefully.");
						return;
					}
					catch (TimeoutException)
					{
						Log.Warning(logSource, $"KillApplication: Process ID: {processId} did not exit gracefully within {gracefulShutdownTimeout.TotalSeconds}s. Proceeding with force kill.");
					}
					catch (OperationCanceledException)
					{
						Log.Warning(logSource, $"KillApplication: Kill timeout exceeded while waiting for graceful shutdown of process ID: {processId}.");
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
					await process.WaitForExitAsync(killTimeoutCts.Token)
						.WaitAsync(forceKillTimeout, killTimeoutCts.Token);
					Log.Info(logSource, $"KillApplication: Process ID: {processId} and its children killed successfully.");
				}
				catch (TimeoutException)
				{
					Log.Critical(logSource, $"KillApplication: Critical Warning: Process ID: {processId} did not exit even after force kill ({forceKillTimeout.TotalSeconds}s). It might be stuck!");
				}
				catch (OperationCanceledException)
				{
					Log.Critical(logSource, $"KillApplication: Kill timeout exceeded for process ID: {processId}. Abandoning wait.");
				}
			}
			catch (InvalidOperationException ex)
			{
				Log.Error(logSource, $"KillApplication: Process already exited or invalid handle.", ex);
			}
			catch (Exception ex)
			{
				Log.Critical(logSource, $"KillApplication: Error during application kill. Error: {ex.Message}", ex);
			}
			finally
			{
				process.Dispose();
			}
		}

		/// <summary>
		/// Launches the monitored application process using the cached <see cref="ProcessStartInfo"/>.
		/// Awaits termination of any stale process via <see cref="KillApplicationAsync"/> BEFORE starting
		/// the new process to prevent port-bind conflicts from two processes running simultaneously.
		/// </summary>
		/// <returns>A task representing the asynchronous launch operation.</returns>
		private async Task LaunchApplicationAsync()
		{
			// Last line of defence for "stop stays stopped": whatever path reaches here — the
			// initial launch, the automatic restart, a late continuation — refuses while an
			// operator stop is in effect. Only an operator start or restart clears that flag.
			if (Volatile.Read(ref operatorStopped) != 0)
			{
				Log.Info(logSource, "Launch refused: the application is stopped by operator.");
				return;
			}

			if (!File.Exists(resolvedExePath))
			{
				Log.Critical(logSource, $"Executable not found at '{resolvedExePath}'. Cannot launch application.");
				ClearMonitoredProcess();
				return;
			}

			// Kill any stale process and await its exit BEFORE starting the new one to prevent port conflicts.
			await KillApplicationAsync();

			// Reset CPU tracker before the launch attempt so stale data is never used,
			// regardless of whether the launch succeeds or fails.
			cpuTracker.Reset();

			Log.Info(logSource, $"Launching application '{resolvedExePath}' with arguments: '{cachedStartInfo.Arguments}' (Headless: {cachedStartInfo.CreateNoWindow})...");
			try
			{
				var launched = Process.Start(cachedStartInfo);

				if (launched != null)
				{
					var stale = Interlocked.Exchange(ref monitoredProcess, launched);
					stale?.Dispose();
					Log.Info(logSource, $"Application launched successfully. Process ID: {launched.Id}");
				}
				else
				{
					ClearMonitoredProcess();
					Log.Warning(logSource, $"Warning: Process.Start returned null for '{resolvedExePath}'. This might indicate a problem.");
				}
			}
			catch (Win32Exception ex)
			{
				Log.Critical(logSource, $"Error launching application '{resolvedExePath}'. Check if the path is correct and the executable exists. Error: {ex.Message}", ex);
				ClearMonitoredProcess();
			}
			catch (Exception ex)
			{
				Log.Critical(logSource, $"Unexpected error launching application '{resolvedExePath}'. Error: {ex.Message}", ex);
				ClearMonitoredProcess();
			}
		}

		/// <summary>
		/// Acquires the lifecycle gate with a bounded wait so no caller blocks forever.
		/// </summary>
		/// <param name="token">Token cancelled when monitoring or the daemon shuts down.</param>
		/// <returns>True when the gate was taken and must be released; otherwise, false.</returns>
		private async Task<bool> TryAcquireLifecycleGateAsync(CancellationToken token)
		{
			try
			{
				return await lifecycleGate.WaitAsync(operatorActionTimeout, token);
			}
			catch (ObjectDisposedException)
			{
				// The monitor is being disposed; proceed without the gate, the caller's work is
				// about to be cancelled anyway.
				return false;
			}
			catch (OperationCanceledException)
			{
				return false;
			}
		}

		/// <summary>
		/// Releases the lifecycle gate when it was actually taken.
		/// </summary>
		/// <param name="taken">Whether <see cref="TryAcquireLifecycleGateAsync"/> returned true.</param>
		private void ReleaseLifecycleGate(bool taken)
		{
			if (!taken)
			{
				return;
			}

			try
			{
				lifecycleGate.Release();
			}
			catch (ObjectDisposedException)
			{
				// Disposed underneath us during shutdown — nothing left to release.
			}
		}

		/// <summary>
		/// Stops the application on an operator's instruction and leaves it stopped.
		/// </summary>
		/// <remarks>
		/// Sets the operator-stop flag before killing, so the monitoring loop stops health-checking
		/// it and every launch path refuses until an operator starts or restarts it. The flag is the
		/// mechanism: killing alone would simply be seen as an unhealthy process on the next check
		/// and restarted.
		/// </remarks>
		/// <param name="token">Token cancelled when monitoring or the daemon shuts down.</param>
		/// <returns>Whether the stop succeeded, and a sentence describing what happened.</returns>
		public async Task<(bool Succeeded, string Outcome)> StopByOperatorAsync(CancellationToken token)
		{
			bool gateTaken = await TryAcquireLifecycleGateAsync(token);
			try
			{
				// Set first: anything already deciding to relaunch re-reads this under the gate.
				Volatile.Write(ref operatorStopped, 1);

				var process = Volatile.Read(ref monitoredProcess);
				bool wasRunning = process != null;

				await KillApplicationAsync();

				// A later operator start should get a clean evaluation window.
				Volatile.Write(ref currentRestartAttemptCount, 0);
				Volatile.Write(ref consecutivePortCheckFailures, 0);
				Volatile.Write(ref consecutiveResourceCheckFailures, 0);

				string outcome = wasRunning
					? $"Stopped '{logSource}'. It will stay stopped until an operator starts or restarts it."
					: $"'{logSource}' was not running. Marked as stopped by operator so the supervisor will not relaunch it.";
				Log.Warning(logSource, outcome);
				return (true, outcome);
			}
			catch (Exception ex)
			{
				Log.Error(logSource, $"Operator stop failed: {ex.Message}", ex);
				return (false, $"Stop failed on '{logSource}': {ex.Message}");
			}
			finally
			{
				ReleaseLifecycleGate(gateTaken);
			}
		}

		/// <summary>
		/// Starts the application on an operator's instruction, clearing any operator stop.
		/// </summary>
		/// <param name="token">Token cancelled when monitoring or the daemon shuts down.</param>
		/// <returns>Whether the start succeeded, and a sentence describing what happened.</returns>
		public Task<(bool Succeeded, string Outcome)> StartByOperatorAsync(CancellationToken token)
		{
			return StartOrRestartByOperatorAsync(token, forceRestart: false);
		}

		/// <summary>
		/// Stops and starts the application on an operator's instruction, clearing any operator stop.
		/// </summary>
		/// <param name="token">Token cancelled when monitoring or the daemon shuts down.</param>
		/// <returns>Whether the restart succeeded, and a sentence describing what happened.</returns>
		public Task<(bool Succeeded, string Outcome)> RestartByOperatorAsync(CancellationToken token)
		{
			return StartOrRestartByOperatorAsync(token, forceRestart: true);
		}

		/// <summary>
		/// Shared implementation of the operator start and restart verbs.
		/// </summary>
		/// <remarks>
		/// Launches only through <see cref="LaunchApplicationAsync"/>, which uses the
		/// <see cref="ProcessStartInfo"/> built once in the constructor from this monitor's own
		/// <see cref="AppConfig"/>. Nothing about what runs comes from the caller.
		/// </remarks>
		/// <param name="token">Token cancelled when monitoring or the daemon shuts down.</param>
		/// <param name="forceRestart">True to kill a running process first; false to leave it alone.</param>
		/// <returns>Whether it succeeded, and a sentence describing what happened.</returns>
		private async Task<(bool Succeeded, string Outcome)> StartOrRestartByOperatorAsync(CancellationToken token, bool forceRestart)
		{
			string verb = forceRestart ? "Restart" : "Start";

			bool gateTaken = await TryAcquireLifecycleGateAsync(token);
			try
			{
				/* Whether the loop has ended is decided UNDER the gate, not before it. Two commands
				 * can arrive in a single control-plane poll batch: the first revives the monitor and
				 * the second, waiting here, then sees a live loop and takes the ordinary path. Read
				 * outside the gate, both would revive, and the host would be handed two monitoring
				 * loops for one application. */
				if (Volatile.Read(ref monitoringLoopEnded) != 0)
				{
					return await ReviveByOperatorAsync(verb, forceRestart);
				}

				ResetForOperatorStart();

				if (!forceRestart && IsApplicationProcessRunning())
				{
					var running = Volatile.Read(ref monitoredProcess);
					string already = $"'{logSource}' is already running (process id {(running != null ? running.Id.ToString() : "unknown")}). Nothing to start.";
					Log.Info(logSource, already);
					return (true, already);
				}

				if (forceRestart)
				{
					await KillApplicationAsync();
				}

				await LaunchApplicationAsync();

				var launched = Volatile.Read(ref monitoredProcess);
				if (launched == null)
				{
					string failed = $"{verb} failed for '{logSource}': the process did not start. See the daemon log on that host.";
					Log.Error(logSource, failed);
					return (false, failed);
				}

				string succeeded = $"{verb.TrimEnd('e')}ed '{logSource}'. Process id {launched.Id}.";
				Log.Warning(logSource, succeeded);
				return (true, succeeded);
			}
			catch (Exception ex)
			{
				Log.Error(logSource, $"Operator {verb.ToLowerInvariant()} failed: {ex.Message}", ex);
				return (false, $"{verb} failed on '{logSource}': {ex.Message}");
			}
			finally
			{
				ReleaseLifecycleGate(gateTaken);
			}
		}

		/// <summary>
		/// Clears the state that would otherwise keep an application down, so an operator start gets
		/// a clean evaluation window.
		/// </summary>
		/// <remarks>
		/// Clearing <see cref="operatorStopped"/> is what re-enables launching and health checks, and
		/// clearing <see cref="maxRestartsReached"/> both re-arms the backoff and is what makes the
		/// application report its real state again instead of staying <c>EXHAUSTED</c>. Callers must
		/// hold the lifecycle gate.
		/// </remarks>
		private void ResetForOperatorStart()
		{
			Volatile.Write(ref operatorStopped, 0);
			Volatile.Write(ref currentRestartAttemptCount, 0);
			Volatile.Write(ref consecutivePortCheckFailures, 0);
			Volatile.Write(ref consecutiveResourceCheckFailures, 0);

			// An operator asking for this application back is an explicit decision to give the
			// backoff another run, so an exhausted monitor is re-armed.
			Volatile.Write(ref maxRestartsReached, false);
		}

		/// <summary>
		/// Revives supervision for this one application after its monitoring loop has ended, then
		/// starts it. Called from <see cref="StartOrRestartByOperatorAsync"/> under the lifecycle gate.
		/// </summary>
		/// <remarks>
		/// <para>
		/// An exhausted application is precisely the one an operator wants back, so refusing here —
		/// as this used to — left them with no recourse but restarting the daemon, which takes down
		/// every other application on the host. The refusal itself was sound though: launching from a
		/// monitor whose loop has ended leaves a process nothing watches. So the launch is ordered
		/// around re-establishing supervision instead of replacing it:
		/// </para>
		/// <list type="number">
		/// <item>reserve a place in the running cycle, which cannot conclude while the reservation is held;</item>
		/// <item>launch — and if the launch fails, nothing was started and the reservation is dropped;</item>
		/// <item>hand a fresh monitoring loop back to the cycle before reporting success;</item>
		/// <item>release the reservation.</item>
		/// </list>
		/// <para>
		/// At no point is a process running with nothing watching it: before the launch there is no
		/// process, and from the launch onwards the cycle is pinned open and ends by killing whatever
		/// this monitor holds. If no reservation can be made — no cycle, a cycle that has already
		/// concluded, or a daemon shutting down — nothing is launched and the operator is told why.
		/// </para>
		/// <para>
		/// Only this application is touched. The reservation is a task in the cycle's own list; no
		/// other monitor is signalled, restarted or even consulted.
		/// </para>
		/// </remarks>
		/// <param name="verb">The operator's verb, for the message they read back.</param>
		/// <param name="forceRestart">True to relaunch even if a process somehow survived the loop.</param>
		/// <returns>Whether it succeeded, and a sentence describing what happened.</returns>
		private async Task<(bool Succeeded, string Outcome)> ReviveByOperatorAsync(string verb, bool forceRestart)
		{
			// Not gate-dependent: one revival at a time for this monitor, full stop.
			if (Interlocked.CompareExchange(ref revivalInProgress, 1, 0) != 0)
			{
				string busy = $"{verb} refused: supervision for '{logSource}' is already being resumed by another command. Nothing was done.";
				Log.Warning(logSource, busy);
				return (false, busy);
			}

			try
			{
				return await ReviveUnderClaimAsync(verb, forceRestart);
			}
			finally
			{
				Volatile.Write(ref revivalInProgress, 0);
			}
		}

		/// <summary>
		/// Performs the revival itself, with the single-revival claim already held.
		/// See <see cref="ReviveByOperatorAsync"/>.
		/// </summary>
		/// <param name="verb">The operator's verb, for the message they read back.</param>
		/// <param name="forceRestart">True to relaunch even if a process somehow survived the loop.</param>
		/// <returns>Whether it succeeded, and a sentence describing what happened.</returns>
		private async Task<(bool Succeeded, string Outcome)> ReviveUnderClaimAsync(string verb, bool forceRestart)
		{
			var host = supervisionHost;
			if (host == null)
			{
				string noHost = $"{verb} refused: the supervisor for '{logSource}' is no longer running and there is no monitoring cycle to resume it in. Restart monitoring on that host.";
				Log.Warning(logSource, noHost);
				return (false, noHost);
			}

			var reservation = host.TryReserveRevival(logSource, out string refusal);
			if (reservation == null)
			{
				string refused = $"{verb} refused: {refusal} Nothing was launched.";
				Log.Warning(logSource, refused);
				return (false, refused);
			}

			try
			{
				// Re-checked after the reservation: a cycle cancelled in between must not be handed a
				// monitoring loop it will never run, and a process must not be launched into one.
				if (!reservation.IsSupervisionAvailable)
				{
					string gone = $"{verb} refused: the monitoring cycle for '{logSource}' is shutting down, so supervision could not be resumed. Nothing was launched.";
					Log.Warning(logSource, gone);
					return (false, gone);
				}

				Log.Warning(logSource, $"Supervision for '{logSource}' had ended. Reviving it on operator {verb.ToLowerInvariant()}.");

				ResetForOperatorStart();

				bool alreadyRunning = !forceRestart && IsApplicationProcessRunning();
				if (!alreadyRunning)
				{
					// LaunchApplicationAsync kills any stale process and awaits its exit first, so a
					// forced restart needs nothing extra here.
					await LaunchApplicationAsync();
				}

				var launched = Volatile.Read(ref monitoredProcess);
				if (launched == null)
				{
					/* Nothing started, so nothing was revived — and the application must go back to
					 * reporting what it is. ResetForOperatorStart cleared maxRestartsReached to
					 * re-arm the backoff for a loop that is now never going to run; leaving it clear
					 * would downgrade a loudly EXHAUSTED application to a quiet DOWN, which is the
					 * one state an operator must not be told about an application nothing is
					 * watching. */
					Volatile.Write(ref maxRestartsReached, true);

					string failed = $"{verb} failed for '{logSource}': the process did not start, so supervision was not resumed and nothing is running. It is still exhausted. See the daemon log on that host.";
					Log.Error(logSource, failed);
					return (false, failed);
				}

				// Read before the loop starts: once it is running it owns this reference and may
				// dispose it the moment the process exits.
				int processId = launched.Id;

				/* The loop is started and handed back BEFORE success is reported and before the
				 * reservation is released, which is what closes the window the old refusal was
				 * protecting. Clearing the flag first means the fresh loop's own exit re-sets it,
				 * so an application that exhausts again can be revived again. */
				Volatile.Write(ref monitoringLoopEnded, 0);
				reservation.Resume(StartMonitoringAsync());

				string revived = $"{verb.TrimEnd('e')}ed '{logSource}' and resumed supervision of it. Process id {processId}.";
				Log.Warning(logSource, revived);
				return (true, revived);
			}
			finally
			{
				reservation.Dispose();
			}
		}

		/// <summary>
		/// Atomically clears the monitored process reference and disposes the stale process.
		/// Used on error paths in <see cref="LaunchApplicationAsync"/> to avoid duplicated cleanup logic.
		/// </summary>
		private void ClearMonitoredProcess()
		{
			var stale = Interlocked.Exchange(ref monitoredProcess, null);
			stale?.Dispose();
		}

		/// <summary>
		/// Disposes of the monitor by killing any active process.
		/// Health checkers are NOT disposed here — they are shared across monitoring cycles
		/// and owned by <see cref="DaemonOrchestrator"/>.
		/// </summary>
		/// <returns>A <see cref="ValueTask"/> representing the asynchronous dispose operation.</returns>
		public async ValueTask DisposeAsync()
		{
			if (Interlocked.CompareExchange(ref isDisposed, 1, 0) != 0)
			{
				return;
			}
			GC.SuppressFinalize(this);
			Log.Info(logSource, "Disposing health monitor.");
			await KillApplicationAsync();
			lifecycleGate.Dispose();
		}
	}
}