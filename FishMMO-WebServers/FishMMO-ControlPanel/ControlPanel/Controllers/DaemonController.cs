using FishMMO.ControlPanel.Auth;
using FishMMO.ControlPanel.Services;
using FishMMO.Database;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Npgsql.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FishMMO.ControlPanel.Controllers
{
	/// <summary>
	/// The daemon control plane: what each host is supervising, and start, stop and restart.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>This is the most dangerous surface in the panel</b>, well past banning an account: it
	/// is the only one that can cause a process to run on another machine. Everything about it
	/// is shaped by containing that.
	/// </para>
	/// <para>
	/// A command carries a verb from a closed enumeration and the <em>name</em> of an
	/// application some daemon already reported supervising. It carries no path, no arguments
	/// and no shell. The daemon then looks that name up in its own configuration file on disk
	/// and refuses anything it does not recognise. So the worst an attacker with this endpoint
	/// — or with the database password — can do is restart a FishMMO process that was already
	/// configured to run. They cannot make a game host run something new.
	/// </para>
	/// <para>
	/// <b>Queued, not executed.</b> The panel writes a row; the daemon collects it on its next
	/// poll. Every acknowledgement here says so, for the same reason the server board does: an
	/// operator who believes a process has restarted, when the row has not been collected, will
	/// press the button again.
	/// </para>
	/// <para>
	/// Operator plus a step-up, and every command is audited. A queued command that nobody can
	/// attribute is the one thing worse than not having the capability at all.
	/// </para>
	/// </remarks>
	[ApiController]
	[Route("api/daemon")]
	public sealed class DaemonController : ControllerBase
	{
		/// <summary>
		/// How long without a heartbeat before a daemon is shown as stale.
		/// </summary>
		/// <remarks>
		/// Shorter than the server board's threshold because the daemon's own beat is faster,
		/// and because a silent daemon has a sharper consequence: nothing on that machine is
		/// being supervised and no command queued for it will ever be collected.
		/// </remarks>
		private const int StaleAfterSeconds = 45;

		/// <summary>
		/// How long a queued command stays executable.
		/// </summary>
		/// <remarks>
		/// Five minutes. A restart carried out long after it was asked for — once the operator
		/// has moved on and players are back on the server — is worse than one that never
		/// happened, so an uncollected command is abandoned rather than run late.
		/// </remarks>
		private static readonly TimeSpan CommandLifetime = TimeSpan.FromMinutes(5);

		private readonly IDaemonService daemon;
		private readonly AuditScope audit;
		private readonly ILogger<DaemonController> log;

		public DaemonController(IDaemonService daemon, AuditScope audit, ILogger<DaemonController> log)
		{
			this.daemon = daemon;
			this.audit = audit;
			this.log = log;
		}

		/// <summary>Every daemon host and what it supervises.</summary>
		[HttpGet("hosts")]
		[Authorize(Policy = PanelPolicies.Operator)]
		public async Task<IActionResult> Hosts()
		{
			var result = await daemon.FetchHostsAsync(HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "The daemon hosts could not be read." });
			}

			DateTime now = DateTime.UtcNow;
			return Ok(new
			{
				staleAfterSeconds = StaleAfterSeconds,
				hosts = result.Data.Select(h => new
				{
					id = h.ID,
					hostName = h.HostName,
					daemonVersion = h.DaemonVersion,
					osDescription = h.OSDescription,
					processorCount = h.ProcessorCount,
					startedUtc = h.StartedUtc,
					lastHeartbeatUtc = h.LastHeartbeatUtc,
					heartbeatAgeSeconds = Math.Round((now - h.LastHeartbeatUtc).TotalSeconds, 1),
					stale = (now - h.LastHeartbeatUtc).TotalSeconds > StaleAfterSeconds,
					apps = h.Apps.Select(a => new
					{
						id = a.ID,
						name = a.Name,
						status = (int)a.Status,
						statusName = a.Status.ToString(),
						processId = a.ProcessID,
						restartAttempts = a.RestartAttempts,
						maxRestartAttempts = a.MaxRestartAttempts,
						monitoredPort = a.MonitoredPort,
						lastReportedUtc = a.LastReportedUtc,
					}),
				}),
			});
		}

		/// <summary>The command history, newest first.</summary>
		[HttpGet("commands")]
		[Authorize(Policy = PanelPolicies.Operator)]
		public async Task<IActionResult> Commands(
			[FromQuery] string hostName,
			[FromQuery] int page = 1,
			[FromQuery] int pageSize = 50)
		{
			var result = await daemon.FetchCommandsAsync(hostName, page, pageSize, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "The command log could not be read." });
			}

			var data = result.Data;
			return Ok(new
			{
				items = data.Items.Select(c => new
				{
					id = c.ID,
					hostName = c.HostName,
					appName = c.AppName,
					verb = (int)c.Verb,
					verbName = c.Verb.ToString(),
					requestedBy = c.RequestedBy,
					requestedUtc = c.RequestedUtc,
					reason = c.Reason,
					expiresUtc = c.ExpiresUtc,
					claimedUtc = c.ClaimedUtc,
					claimedBy = c.ClaimedBy,
					completedUtc = c.CompletedUtc,
					succeeded = c.Succeeded,
					outcome = c.Outcome,
				}),
				page = data.Page,
				pageSize = data.PageSize,
				totalCount = data.TotalCount,
			});
		}

		/// <summary>
		/// The supervision history: what the daemons observed changing, newest first.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>The sibling of the command log, and not the same thing.</b> That route lists what
		/// operators asked this panel for. This lists what the supervisors saw happen by
		/// themselves — a process that died overnight, the restarts that followed, and whether it
		/// came back — which nobody asked for and which nothing else records. The row in
		/// <c>daemon_apps</c> behind the hosts page is overwritten on every heartbeat, so a fault
		/// that has already been recovered from is otherwise invisible in every view there is.
		/// </para>
		/// <para>
		/// <b>It is not process output, and it must never become it.</b> Nothing a supervised
		/// process wrote reaches here: every row is derived in the database service by comparing
		/// one heartbeat against the stored one, from two status values and two integers, and the
		/// sentence describing the transition is composed from those. Server logs carry connection
		/// strings, tokens and paths inside exception messages, so a route that relayed them would
		/// turn this read-only view into a way of reading secrets out of a game host — and would
		/// let a compromised daemon put text of its choosing in front of an administrator.
		/// </para>
		/// <para>
		/// A read, at the same policy as the other two reads here, and deliberately not audited —
		/// looking at a history is not an action taken on anything.
		/// </para>
		/// </remarks>
		[HttpGet("app-events")]
		[Authorize(Policy = PanelPolicies.Operator)]
		public async Task<IActionResult> AppEvents(
			[FromQuery] string hostName,
			[FromQuery] string appName,
			[FromQuery] int? status,
			[FromQuery] DateTime? from,
			[FromQuery] DateTime? to,
			[FromQuery] int page = 1,
			[FromQuery] int pageSize = 50)
		{
			/* Bounded here as well as in the service. It arrives as an integer in a query string,
			 * and a value outside the enumeration describes no observation any daemon can make;
			 * answering it with an empty page would say "nothing happened" to a question that was
			 * never askable. */
			if (status.HasValue && !Enum.IsDefined(typeof(DaemonAppStatus), status.Value))
			{
				return BadRequest(new { error = "That is not an application status a daemon reports." });
			}

			var result = await daemon.FetchAppEventsAsync(
				hostName,
				appName,
				status.HasValue ? (DaemonAppStatus)status.Value : null,
				AsUtc(from),
				AsUtc(to),
				page,
				pageSize,
				HttpContext.RequestAborted);

			if (!result.IsSuccess)
			{
				/* A validation failure is the caller's fault and says which; anything else is the
				 * database being unreachable, and must not read as an empty history. */
				if (result.ErrorCode == DatabaseErrorCodes.ValidationError)
				{
					return BadRequest(new { error = result.ErrorMessage });
				}
				return StatusCode(StatusCodes.Status503ServiceUnavailable,
					new { error = "The supervision history could not be read." });
			}

			var data = result.Data;
			return Ok(new
			{
				items = data.Items.Select(e => new
				{
					id = e.ID,
					hostName = e.HostName,
					appName = e.AppName,
					previousStatus = e.PreviousStatus.HasValue ? (int?)e.PreviousStatus.Value : null,
					previousStatusName = e.PreviousStatus?.ToString(),
					status = (int)e.Status,
					statusName = e.Status.ToString(),
					processId = e.ProcessID,
					previousProcessId = e.PreviousProcessID,
					restartAttempts = e.RestartAttempts,
					previousRestartAttempts = e.PreviousRestartAttempts,
					observedUtc = e.ObservedUtc,
					/* Composed server-side from the enum values and the integers beside them, so
					 * the one piece of prose on the page is written by this deployment and not by
					 * a machine reporting into it. */
					description = e.Describe(),
				}),
				page = data.Page,
				pageSize = data.PageSize,
				totalCount = data.TotalCount,
			});
		}

		/// <summary>
		/// Reads a filter timestamp as the instant it names.
		/// </summary>
		/// <remarks>
		/// The window is compared against columns holding UTC. A local-kind value — which is what
		/// binding an ISO string with a zone can produce — would move the window by this server's
		/// offset, quietly hiding hours of history at one end and inventing them at the other. A
		/// value with no zone at all is taken as UTC, because that is the only thing the panel
		/// sends and the only thing this history is written in.
		/// </remarks>
		private static DateTime? AsUtc(DateTime? value) => value switch
		{
			null => null,
			{ Kind: DateTimeKind.Utc } => value,
			{ Kind: DateTimeKind.Local } => value.Value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc),
		};

		/// <summary>
		/// Queues a start, stop or restart.
		/// </summary>
		/// <remarks>
		/// The verb is bounded against the enumeration here as well as in the service. It
		/// arrives as an integer in a JSON body, and a value outside the enum must never be
		/// stored: a daemon reading a command it cannot name would have to decide what to do
		/// with it, and there is no safe answer to that question.
		/// </remarks>
		[HttpPost("commands")]
		[Authorize(Policy = PanelPolicies.OperatorStepUp)]
		[Audited(AuditActions.DaemonCommand, TargetType = "daemon")]
		public async Task<IActionResult> Queue([FromBody] CommandRequest request)
		{
			if (string.IsNullOrWhiteSpace(request?.Reason))
			{
				return BadRequest(new { error = "A reason is required." });
			}
			if (string.IsNullOrWhiteSpace(request.HostName) || string.IsNullOrWhiteSpace(request.AppName))
			{
				return BadRequest(new { error = "Name the host and the application." });
			}
			if (request.Verb == null || !Enum.IsDefined(typeof(DaemonCommandVerb), request.Verb.Value))
			{
				audit.Outcome = "Refused: not a daemon command.";
				audit.Details = new { attempted = request.Verb };
				return BadRequest(new { error = "A daemon command is start, stop or restart." });
			}

			var verb = (DaemonCommandVerb)request.Verb.Value;

			audit.TargetID = $"{request.HostName}/{request.AppName}";
			audit.TargetName = request.AppName;
			audit.Details = new { host = request.HostName, app = request.AppName, verb = verb.ToString() };

			var result = await daemon.EnqueueCommandAsync(
				request.HostName,
				request.AppName,
				verb,
				User.Identity?.Name,
				request.Reason,
				CommandLifetime,
				HttpContext.RequestAborted);

			if (!result.IsSuccess)
			{
				audit.Outcome = result.ErrorMessage;
				/* The service refuses a host or application no daemon has reported. That is not
				 * a lookup convenience — it keeps what a command can name to what some daemon
				 * already supervises, so the queue cannot be used to invent a target. */
				return BadRequest(new { error = result.ErrorMessage ?? "That command could not be queued." });
			}

			log.LogWarning("Daemon command {Verb} queued for '{App}' on '{Host}' by '{Actor}'. Reason: {Reason}",
				verb, request.AppName, request.HostName, User.Identity?.Name, request.Reason);

			return Ok(new
			{
				id = result.Data,
				message = $"{verb} queued for {request.AppName} on {request.HostName}. " +
					$"The daemon collects it on its next poll; if it does not within {CommandLifetime.TotalMinutes:0} minutes the command is abandoned rather than run late.",
			});
		}

		/// <summary>A queued daemon command.</summary>
		public sealed class CommandRequest
		{
			/// <summary>The host to act on.</summary>
			public string HostName { get; set; }

			/// <summary>The supervised application, by the name the daemon knows.</summary>
			public string AppName { get; set; }

			/// <summary>Start 0, Stop 1, Restart 2. Nothing else exists.</summary>
			public int? Verb { get; set; }

			/// <summary>Why. Recorded here and in the audit log.</summary>
			public string Reason { get; set; } = "";
		}
	}
}
