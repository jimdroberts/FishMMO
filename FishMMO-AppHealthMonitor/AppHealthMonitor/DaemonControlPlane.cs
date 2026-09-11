using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Services;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;

namespace AppHealthMonitor
{
	/// <summary>
	/// Connects this daemon to the database-backed control plane: reports what it supervises, and
	/// executes the commands an operator queued for this host.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The security model, and the reason this class is shaped the way it is.</b> A command row
	/// carries a verb from a closed enumeration and the <em>name</em> of an application this daemon
	/// already supervises. It carries no path, no arguments, no working directory and no shell.
	/// The name is resolved against <see cref="DaemonOrchestrator.ConfiguredApplications"/> — the
	/// list loaded from this host's own <c>appsettings.json</c> — and a name that is not in that
	/// list is refused and reported as a failure. Nothing from the database is ever passed to
	/// <see cref="Process"/>, concatenated into a command line, or used to derive an executable.
	/// </para>
	/// <para>
	/// What that buys: somebody who can write to the database can restart a FishMMO process. They
	/// cannot make a game host run something new. Any change here that lets a row describe what to
	/// run, rather than which already-known thing to act on, turns a database password into remote
	/// code execution.
	/// </para>
	/// <para>
	/// <b>Optional by construction.</b> A deployment with no database credentials gets no control
	/// plane: <see cref="TryCreate"/> returns null, one line is logged, and the daemon supervises
	/// exactly as it always has. A database that disappears mid-run costs a log line and a backoff,
	/// never the supervisor.
	/// </para>
	/// </remarks>
	public sealed class DaemonControlPlane
	{
		/// <summary>Log source for every control-plane message.</summary>
		private const string LogSource = "ControlPlane";

		/// <summary>Default seconds between heartbeat/command polls.</summary>
		private const int DefaultPollSeconds = 10;

		/// <summary>Lower bound for the configured poll interval.</summary>
		private const int MinPollSeconds = 5;

		/// <summary>Upper bound for the configured poll interval.</summary>
		private const int MaxPollSeconds = 300;

		/// <summary>How many commands one poll claims at most.</summary>
		private const int MaxCommandsPerPoll = 10;

		/// <summary>Ceiling for the backoff applied after consecutive database failures.</summary>
		private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(2);

		/// <summary>
		/// Bounded time allowed for writing a command outcome. Deliberately not linked to the daemon
		/// token: an outcome must still be recorded when the daemon is shutting down, because a
		/// command left uncompleted is one an operator watches forever.
		/// </summary>
		private static readonly TimeSpan OutcomeWriteTimeout = TimeSpan.FromSeconds(15);

		/// <summary>The orchestrator that owns the monitors this control plane reports on and acts upon.</summary>
		private readonly DaemonOrchestrator orchestrator;

		/// <summary>The database service. The only thing this class talks to the database through.</summary>
		private readonly IDaemonService daemonService;

		/// <summary>
		/// The applications this daemon supervises, from its own configuration file, keyed by name.
		/// <b>This dictionary is the whole access-control decision</b>: a command naming anything not
		/// in it is refused.
		/// </summary>
		private readonly IReadOnlyDictionary<string, AppConfig> configuredApps;

		/// <summary>The configured applications in their configured order, for reporting.</summary>
		private readonly IReadOnlyList<AppConfig> configuredAppList;

		/// <summary>This machine's name. The only host whose commands this daemon claims.</summary>
		private readonly string hostName;

		/// <summary>
		/// This process's identity, stable for its whole life. Stamped on claimed commands and
		/// required again to report their outcome, so no other instance can report work it did not do.
		/// </summary>
		private readonly string instanceId;

		/// <summary>The daemon build, from the entry assembly.</summary>
		private readonly string daemonVersion;

		/// <summary>What the operating system calls itself.</summary>
		private readonly string osDescription;

		/// <summary>When this daemon process started.</summary>
		private readonly DateTime startedUtc;

		/// <summary>How often to heartbeat and poll.</summary>
		private readonly TimeSpan pollInterval;

		/// <summary>Consecutive database failures, used for backoff and for not flooding the log.</summary>
		private int consecutiveFailures;

		/// <summary>
		/// Initializes a new instance of the <see cref="DaemonControlPlane"/> class.
		/// Use <see cref="TryCreate"/> rather than calling this directly.
		/// </summary>
		/// <param name="orchestrator">The orchestrator owning the monitors.</param>
		/// <param name="daemonService">The database service to report through.</param>
		/// <param name="pollInterval">How often to heartbeat and poll for commands.</param>
		private DaemonControlPlane(DaemonOrchestrator orchestrator, IDaemonService daemonService, TimeSpan pollInterval)
		{
			this.orchestrator = orchestrator;
			this.daemonService = daemonService;
			this.pollInterval = pollInterval;

			configuredAppList = orchestrator.ConfiguredApplications;

			var byName = new Dictionary<string, AppConfig>(configuredAppList.Count, StringComparer.OrdinalIgnoreCase);
			foreach (var config in configuredAppList)
			{
				// Duplicate names are rejected at orchestrator construction, so this cannot collide.
				byName[config.Name] = config;
			}
			configuredApps = byName;

			hostName = Environment.MachineName;
			instanceId = Clamp($"{hostName}:{Environment.ProcessId}:{Guid.NewGuid():N}", 128);
			daemonVersion = Clamp(ResolveDaemonVersion(), 64);
			osDescription = Clamp(RuntimeInformation.OSDescription, 256);
			startedUtc = ResolveProcessStartUtc();
		}

		/// <summary>
		/// Builds a control plane when this deployment has a database, and returns null when it does not.
		/// </summary>
		/// <remarks>
		/// Credentials are resolved the way every other FishMMO process resolves them — the
		/// <c>FISHMMO_DB_*</c> environment variables, falling back to <c>/etc/fishmmo/db-secrets.env</c>
		/// (<c>%ProgramData%\FishMMO\db-secrets.env</c> on Windows) — never from the daemon's own
		/// configuration file. No credentials means no control plane, which is a supported deployment
		/// and not an error. <c>ControlPlane:Enabled=false</c> turns it off even where credentials exist.
		/// </remarks>
		/// <param name="orchestrator">The orchestrator owning the monitors.</param>
		/// <param name="configuration">The daemon's configuration, or null.</param>
		/// <returns>A control plane, or null when it is disabled or could not be constructed.</returns>
		/// <summary>
		/// Builds a read service for pulse health checks, or null when there is no database.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Separate from <see cref="TryCreate"/> because the two are independent: a deployment
		/// can want pulse health checks without the command queue, and the health checkers are
		/// built when the monitors are, before the control plane starts.
		/// </para>
		/// <para>
		/// Returns null rather than throwing when no credentials are configured. A daemon with
		/// no database still supervises by process; refusing to start would turn a
		/// configuration gap into an outage on a machine that was otherwise fine.
		/// </para>
		/// </remarks>
		public static IServerBoardService? TryCreateBoardService(IConfiguration? configuration)
		{
			try
			{
				string? username = DatabaseSecrets.TryResolveUsername();
				string? password = DatabaseSecrets.TryResolvePassword();
				if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
				{
					return null;
				}

				var dbConfiguration = new NpgsqlDbConfiguration(configuration ?? new ConfigurationBuilder().Build());
				return new ServerBoardService(new NpgsqlDbContextFactory(dbConfiguration));
			}
			catch (Exception ex)
			{
				Log.Warning(LogSource,
					$"Pulse health checks are unavailable: the database could not be reached ({ex.Message}). " +
					"Applications configured for a DatabasePulse check will be supervised by process only.");
				return null;
			}
		}

		public static DaemonControlPlane? TryCreate(DaemonOrchestrator orchestrator, IConfiguration? configuration)
		{
			ArgumentNullException.ThrowIfNull(orchestrator);

			bool? explicitlyEnabled = null;
			int pollSeconds = DefaultPollSeconds;
			try
			{
				explicitlyEnabled = configuration?.GetValue<bool?>("ControlPlane:Enabled");
				pollSeconds = configuration?.GetValue<int?>("ControlPlane:PollSeconds") ?? DefaultPollSeconds;
			}
			catch (Exception ex)
			{
				Log.Warning(LogSource, $"Could not read the 'ControlPlane' configuration section: {ex.Message}. Using defaults.");
			}

			if (explicitlyEnabled == false)
			{
				Log.Info(LogSource, "Control plane disabled by configuration ('ControlPlane:Enabled' is false). Supervising normally.");
				return null;
			}

			string? username;
			string? password;
			try
			{
				username = DatabaseSecrets.TryResolveUsername();
				password = DatabaseSecrets.TryResolvePassword();
			}
			catch (Exception ex)
			{
				Log.Warning(LogSource, $"Control plane disabled: database credentials could not be read ({ex.Message}). Supervising normally.");
				return null;
			}

			if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
			{
				string where = $"{DatabaseSecrets.UsernameEnvVar}/{DatabaseSecrets.PasswordEnvVar} or {DatabaseSecrets.DefaultSecretsFilePath}";
				if (explicitlyEnabled == true)
				{
					Log.Warning(LogSource, $"'ControlPlane:Enabled' is true but no database credentials were found ({where}). Control plane disabled; supervising normally.");
				}
				else
				{
					Log.Info(LogSource, $"No database credentials found ({where}). Control plane disabled; supervising normally.");
				}
				return null;
			}

			if (pollSeconds < MinPollSeconds || pollSeconds > MaxPollSeconds)
			{
				Log.Warning(LogSource, $"'ControlPlane:PollSeconds' ({pollSeconds}) is outside {MinPollSeconds}-{MaxPollSeconds}. Using {DefaultPollSeconds}s.");
				pollSeconds = DefaultPollSeconds;
			}

			try
			{
				// The daemon's appsettings.json carries no credentials and normally no 'Npgsql'
				// section at all; NpgsqlDbConfiguration falls back to its defaults and lets the
				// FISHMMO_DB_* values (env or secrets file) supply host, port and database.
				var dbConfiguration = new NpgsqlDbConfiguration(configuration ?? new ConfigurationBuilder().Build());
				var factory = new NpgsqlDbContextFactory(dbConfiguration);
				var service = new DaemonService(factory);

				return new DaemonControlPlane(orchestrator, service, TimeSpan.FromSeconds(pollSeconds));
			}
			catch (Exception ex)
			{
				// A misconfigured database must never stop a host from supervising its game servers.
				Log.Error(LogSource, $"Control plane could not be started: {ex.Message}. Supervising normally without it.", ex);
				return null;
			}
		}

		/// <summary>
		/// Runs the heartbeat/command loop until the daemon shuts down.
		/// </summary>
		/// <param name="token">The daemon-wide shutdown token.</param>
		/// <returns>A task that completes when the loop stops.</returns>
		public async Task RunAsync(CancellationToken token)
		{
			Log.Info(LogSource, $"Control plane enabled. Host '{hostName}', instance '{instanceId}', reporting {configuredAppList.Count} application(s) every {pollInterval.TotalSeconds:F0}s.");

			while (!token.IsCancellationRequested)
			{
				bool healthy;
				try
				{
					healthy = await HeartbeatAsync(token);

					// Commands are only polled when the heartbeat got through: if the database is
					// unreachable there is nothing to claim, and trying twice doubles the noise.
					if (healthy)
					{
						healthy = await PollCommandsAsync(token);
					}
				}
				catch (OperationCanceledException) when (token.IsCancellationRequested)
				{
					break;
				}
				catch (Exception ex)
				{
					healthy = false;
					RecordFailure($"Control plane cycle failed: {ex.Message}", ex);
				}

				TimeSpan delay = pollInterval;
				if (healthy)
				{
					RecordSuccess();
				}
				else
				{
					delay = BackoffDelay();
				}

				try
				{
					await Task.Delay(delay, token);
				}
				catch (OperationCanceledException)
				{
					break;
				}
			}

			Log.Info(LogSource, "Control plane loop stopped.");
		}

		/// <summary>
		/// Reports this host and everything it supervises.
		/// </summary>
		/// <param name="token">The daemon-wide shutdown token.</param>
		/// <returns>True when the database accepted the heartbeat; otherwise, false.</returns>
		private async Task<bool> HeartbeatAsync(CancellationToken token)
		{
			var apps = BuildReports();

			var result = await daemonService.HeartbeatAsync(
				hostName,
				daemonVersion,
				osDescription,
				Environment.ProcessorCount,
				startedUtc,
				apps,
				token);

			if (!result.IsSuccess)
			{
				RecordFailure($"Heartbeat rejected: [{result.ErrorCode}] {result.ErrorMessage}");
				return false;
			}

			Log.Debug(LogSource, $"Heartbeat sent for {apps.Count} application(s).");
			return true;
		}

		/// <summary>
		/// Builds one report per configured application from the live monitor statuses.
		/// </summary>
		/// <remarks>
		/// Driven by the configuration rather than by the live monitor list, so the reported set is
		/// exactly what this daemon supervises. That is what makes the database's picture of this
		/// host authoritative, and it is why an application removed from the configuration stops
		/// being a target an operator can aim a command at.
		/// </remarks>
		/// <returns>The reports for this cycle.</returns>
		private IReadOnlyList<DaemonAppReport> BuildReports()
		{
			var statuses = orchestrator.GetActiveMonitorStatuses();
			var byName = new Dictionary<string, HealthMonitorStatus>(statuses.Count, StringComparer.OrdinalIgnoreCase);
			foreach (var status in statuses)
			{
				byName[status.Name] = status;
			}

			var reports = new List<DaemonAppReport>(configuredAppList.Count);
			foreach (var config in configuredAppList)
			{
				byName.TryGetValue(config.Name, out var status);

				reports.Add(new DaemonAppReport
				{
					Name = config.Name,
					Status = MapStatus(status),
					ProcessID = status?.ProcessId,
					RestartAttempts = status?.RestartAttempts ?? 0,
					// From the configuration, not from a monitor that may not exist this cycle.
					MaxRestartAttempts = config.MaxRestartAttempts,
					MonitoredPort = config.MonitoredPort,
				});
			}
			return reports;
		}

		/// <summary>
		/// Maps a monitor snapshot onto what the database understands.
		/// </summary>
		/// <remarks>
		/// <see cref="HealthMonitorStatus.StateLabel"/> cannot know about an operator stop — it only
		/// sees a process that is not running, which is indistinguishable from a crash. The monitor
		/// tracks that intent itself (<see cref="HealthMonitorStatus.IsOperatorStopped"/>) and it is
		/// checked first, so a deliberate stop is never reported as a fault.
		/// <para>
		/// No monitor at all maps to <see cref="DaemonAppStatus.Unknown"/> rather than
		/// <see cref="DaemonAppStatus.Stopped"/>: between monitoring cycles the daemon is not
		/// watching that application and genuinely has not determined a state, and claiming it is
		/// down would be an inference rather than an observation.
		/// </para>
		/// </remarks>
		/// <param name="status">The monitor snapshot, or null when no monitor exists this cycle.</param>
		/// <returns>The status to report.</returns>
		private static DaemonAppStatus MapStatus(HealthMonitorStatus? status)
		{
			if (status == null)
			{
				return DaemonAppStatus.Unknown;
			}

			if (status.IsOperatorStopped)
			{
				return DaemonAppStatus.StoppedByOperator;
			}

			return status.StateLabel switch
			{
				"EXHAUSTED" => DaemonAppStatus.Exhausted,
				"DOWN" => DaemonAppStatus.Stopped,
				"STARTING" => DaemonAppStatus.Starting,
				"HEALTHY" => DaemonAppStatus.Healthy,
				_ => DaemonAppStatus.Unknown,
			};
		}

		/// <summary>
		/// Claims this host's waiting commands, executes them and reports every outcome.
		/// </summary>
		/// <param name="token">The daemon-wide shutdown token.</param>
		/// <returns>True when the database was reachable; otherwise, false.</returns>
		private async Task<bool> PollCommandsAsync(CancellationToken token)
		{
			// hostName is this machine's own name. A daemon never asks for, and never receives,
			// another host's work.
			var claim = await daemonService.ClaimCommandsAsync(hostName, instanceId, MaxCommandsPerPoll, token);

			if (!claim.IsSuccess)
			{
				RecordFailure($"Command claim rejected: [{claim.ErrorCode}] {claim.ErrorMessage}");
				return false;
			}

			var commands = claim.Data;
			if (commands == null || commands.Count == 0)
			{
				return true;
			}

			Log.Info(LogSource, $"Claimed {commands.Count} command(s).");

			foreach (var command in commands)
			{
				bool succeeded;
				string outcome;

				try
				{
					(succeeded, outcome) = await ExecuteCommandAsync(command, token);
				}
				catch (Exception ex)
				{
					// Even an unexpected failure is reported. A claimed command with no outcome is
					// one an operator watches forever.
					succeeded = false;
					outcome = $"Command failed with an unexpected error: {ex.Message}";
					Log.Error(LogSource, $"Command {command.ID} threw: {ex.Message}", ex);
				}

				await ReportOutcomeAsync(command, succeeded, outcome);
			}

			return true;
		}

		/// <summary>
		/// Executes one claimed command.
		/// </summary>
		/// <remarks>
		/// <b>This method is where the security model is enforced.</b> In order: the command must
		/// name this host; it must not have expired; its application name must resolve against this
		/// daemon's own loaded configuration; its verb must be one of the closed enumeration; and the
		/// named application must have a live monitor. Only then is a method called on that monitor.
		/// Every refusal returns false with a sentence saying why, which is written back as a failed
		/// outcome. No value from <paramref name="command"/> reaches a process, a path, an argument
		/// or a shell.
		/// </remarks>
		/// <param name="command">The claimed command.</param>
		/// <param name="token">The daemon-wide shutdown token.</param>
		/// <returns>Whether it succeeded, and the sentence to record.</returns>
		private async Task<(bool Succeeded, string Outcome)> ExecuteCommandAsync(DaemonCommandData command, CancellationToken token)
		{
			Log.Warning(LogSource,
				$"Command {command.ID}: {command.Verb} '{command.AppName}' on '{command.HostName}' requested by '{command.RequestedBy}' — {command.Reason}");

			// 1. This host's work only. The claim already filtered on host name; this refuses a row
			//    that somehow arrived for another machine rather than acting on it.
			if (!string.Equals(command.HostName?.Trim(), hostName, StringComparison.OrdinalIgnoreCase))
			{
				return Refuse(command, $"Refused: the command names host '{command.HostName}', but this daemon is '{hostName}'. Nothing was done.");
			}

			// 2. Expiry. ClaimCommandsAsync never returns an expired command; this covers the time
			//    spent between the claim and this line, because a restart nobody is waiting for any
			//    more is worse than one that never happened.
			if (command.ExpiresUtc <= DateTime.UtcNow)
			{
				return Refuse(command, $"Refused: the command expired at {command.ExpiresUtc:u} before it could be executed. Nothing was done.");
			}

			// 3. The name must be one this daemon already supervises, from its own configuration
			//    file on this host. This is the check that keeps a database write from choosing what
			//    runs here: an unknown name buys a refusal, never a launch.
			string requestedName = command.AppName?.Trim() ?? string.Empty;
			if (requestedName.Length == 0 || !configuredApps.TryGetValue(requestedName, out var config))
			{
				return Refuse(command, $"Refused: no application named '{command.AppName}' is configured on host '{hostName}'. Nothing was launched.");
			}

			// 4. The verb must be one of the closed enumeration. An out-of-range integer in the
			//    column is refused rather than falling through to a default action.
			if (!Enum.IsDefined(typeof(DaemonCommandVerb), command.Verb))
			{
				return Refuse(command, $"Refused: '{(int)command.Verb}' is not a known command verb. Nothing was done.");
			}

			// 5. The application must be supervised right now. Launching outside a monitoring cycle
			//    would leave a process nothing is watching.
			if (!orchestrator.TryGetActiveMonitor(config.Name, out var monitor) || monitor == null)
			{
				return Refuse(command, $"Refused: '{config.Name}' is not being supervised at the moment (monitoring is not active on host '{hostName}'). Nothing was done.");
			}

			// The verb selects a method that already exists on the monitor. What the monitor launches
			// comes from the AppConfig it was constructed with — never from this command.
			var result = command.Verb switch
			{
				DaemonCommandVerb.Start => await monitor.StartByOperatorAsync(token),
				DaemonCommandVerb.Stop => await monitor.StopByOperatorAsync(token),
				DaemonCommandVerb.Restart => await monitor.RestartByOperatorAsync(token),
				_ => (false, $"Refused: '{command.Verb}' is not a supported command verb. Nothing was done."),
			};

			if (result.Item1)
			{
				Log.Info(LogSource, $"Command {command.ID} succeeded: {result.Item2}");
			}
			else
			{
				Log.Error(LogSource, $"Command {command.ID} failed: {result.Item2}");
			}

			return result;
		}

		/// <summary>
		/// Logs a refusal and shapes it as a failed outcome.
		/// </summary>
		/// <param name="command">The refused command.</param>
		/// <param name="reason">Why it was refused, in a sentence an operator can act on.</param>
		/// <returns>A failed result carrying the reason.</returns>
		private static (bool Succeeded, string Outcome) Refuse(DaemonCommandData command, string reason)
		{
			Log.Error(LogSource, $"Command {command.ID} refused: {reason}");
			return (false, reason);
		}

		/// <summary>
		/// Writes the outcome of a claimed command back to the database.
		/// </summary>
		/// <remarks>
		/// Uses its own bounded timeout rather than the daemon shutdown token: a command claimed just
		/// before shutdown must still be completed, or it stays claimed-but-unfinished and an operator
		/// is left watching a row that will never change.
		/// </remarks>
		/// <param name="command">The command being reported on.</param>
		/// <param name="succeeded">Whether it succeeded.</param>
		/// <param name="outcome">What happened, in words.</param>
		/// <returns>A task that completes when the write has been attempted.</returns>
		private async Task ReportOutcomeAsync(DaemonCommandData command, bool succeeded, string outcome)
		{
			try
			{
				using var timeoutCts = new CancellationTokenSource(OutcomeWriteTimeout);

				var result = await daemonService.CompleteCommandAsync(command.ID, instanceId, succeeded, outcome, timeoutCts.Token);
				if (!result.IsSuccess)
				{
					Log.Error(LogSource, $"Could not record the outcome of command {command.ID}: [{result.ErrorCode}] {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				Log.Error(LogSource, $"Could not record the outcome of command {command.ID}: {ex.Message}", ex);
			}
		}

		/// <summary>
		/// Records a database failure, backing off and keeping the log honest but quiet.
		/// </summary>
		/// <param name="message">What failed.</param>
		/// <param name="exception">The exception, when there was one.</param>
		private void RecordFailure(string message, Exception? exception = null)
		{
			int failures = Interlocked.Increment(ref consecutiveFailures);

			if (failures == 1)
			{
				string first = $"{message} Supervision continues; the control plane will retry with backoff.";
				if (exception != null)
				{
					Log.Error(LogSource, first, exception);
				}
				else
				{
					Log.Error(LogSource, first);
				}
			}
			else if (failures % 10 == 0)
			{
				Log.Warning(LogSource, $"{message} (failure {failures} in a row; still supervising normally.)");
			}
			else
			{
				Log.Debug(LogSource, $"{message} (failure {failures} in a row.)");
			}
		}

		/// <summary>
		/// Clears the failure streak, noting recovery when there was one.
		/// </summary>
		private void RecordSuccess()
		{
			int failures = Interlocked.Exchange(ref consecutiveFailures, 0);
			if (failures > 0)
			{
				Log.Info(LogSource, $"Control plane reconnected after {failures} failed attempt(s).");
			}
		}

		/// <summary>
		/// Computes the backoff after consecutive failures: the poll interval doubled per failure,
		/// capped at <see cref="MaxBackoff"/>.
		/// </summary>
		/// <returns>How long to wait before the next attempt.</returns>
		private TimeSpan BackoffDelay()
		{
			int failures = Math.Min(Volatile.Read(ref consecutiveFailures), 10);
			double seconds = pollInterval.TotalSeconds * Math.Pow(2, Math.Max(0, failures - 1));
			return TimeSpan.FromSeconds(Math.Min(seconds, MaxBackoff.TotalSeconds));
		}

		/// <summary>
		/// Resolves the daemon build string from the entry assembly.
		/// </summary>
		/// <returns>A version string, or "unknown".</returns>
		private static string ResolveDaemonVersion()
		{
			try
			{
				var assembly = Assembly.GetEntryAssembly() ?? typeof(DaemonControlPlane).Assembly;

				string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
				if (!string.IsNullOrWhiteSpace(informational))
				{
					return informational;
				}

				return assembly.GetName().Version?.ToString() ?? "unknown";
			}
			catch (Exception)
			{
				return "unknown";
			}
		}

		/// <summary>
		/// Resolves this process's start time in UTC, falling back to now when the platform refuses.
		/// </summary>
		/// <returns>The process start time in UTC.</returns>
		private static DateTime ResolveProcessStartUtc()
		{
			try
			{
				using var process = Process.GetCurrentProcess();
				return process.StartTime.ToUniversalTime();
			}
			catch (Exception)
			{
				return DateTime.UtcNow;
			}
		}

		/// <summary>
		/// Trims a value to the column width the database allows.
		/// </summary>
		/// <param name="value">The value to clamp.</param>
		/// <param name="maxLength">The maximum length.</param>
		/// <returns>The clamped value.</returns>
		private static string Clamp(string? value, int maxLength)
		{
			if (string.IsNullOrEmpty(value))
			{
				return string.Empty;
			}
			return value.Length <= maxLength ? value : value.Substring(0, maxLength);
		}
	}
}
