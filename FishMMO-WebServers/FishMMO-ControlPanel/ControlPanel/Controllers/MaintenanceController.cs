using FishMMO.ControlPanel.Auth;
using FishMMO.ControlPanel.Services;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Npgsql.Services;
using FishMMO.Database.Npgsql.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FishMMO.ControlPanel.Controllers
{
	/// <summary>
	/// Maintenance windows: locking a set of servers, draining them, and stopping them together.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Nothing new reaches a server here.</b> A window is the two controls on the server board
	/// — lock, and schedule a shutdown — applied to a set of servers under one record, because
	/// doing that by hand across a dozen servers is where one gets missed. Starting a window
	/// writes each server's own row through the same service the per-server buttons use.
	/// </para>
	/// <para>
	/// <b>The plan is rows, so the operator can close their laptop.</b> The shutdown deadline is
	/// an absolute instant written onto each server; the server counts down to it inside its own
	/// process, warns its players as it passes each mark, and stops. A twenty minute drain does
	/// not need this panel, this browser or this process to be running for the shard to go down
	/// on time. What the panel does afterwards is <em>observe</em>: the status on these responses
	/// is derived from what the servers' rows say now, and is brought up to date by each read.
	/// </para>
	/// <para>
	/// <b>Cancelling does not unlock anything, and this controller says so every time.</b>
	/// Scheduling a shutdown sets <c>locked = true</c> in the same statement, and cancelling
	/// clears the deadline alone. A cancelled window therefore leaves every target closed to new
	/// arrivals until somebody unlocks it on the board. Getting that wrong strands a shard with
	/// nobody able to log in and the cause nowhere near where anyone will look, so the
	/// acknowledgement names the servers that are still locked rather than merely warning in
	/// general terms.
	/// </para>
	/// <para>
	/// Operator to read, Operator plus a step-up to write, matching the per-server controls: a
	/// window is those controls applied twelve times, and the weaker of two faces of one
	/// capability is the way around the stronger.
	/// </para>
	/// </remarks>
	[ApiController]
	[Route("api/servers/maintenance")]
	public sealed class MaintenanceController : ControllerBase
	{
		private readonly IMaintenanceService maintenance;
		private readonly AuditScope audit;
		private readonly ILogger<MaintenanceController> log;

		public MaintenanceController(
			IMaintenanceService maintenance,
			AuditScope audit,
			ILogger<MaintenanceController> log)
		{
			this.maintenance = maintenance;
			this.audit = audit;
			this.log = log;
		}

		/// <summary>Every maintenance window, newest first, each with its targets.</summary>
		[HttpGet]
		[Authorize(Policy = PanelPolicies.Operator)]
		public async Task<IActionResult> List([FromQuery] int limit = 50)
		{
			var result = await maintenance.ListAsync(limit, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return StatusCode(StatusCodes.Status503ServiceUnavailable,
					new { error = "The maintenance windows could not be read." });
			}

			DateTime now = DateTime.UtcNow;
			return Ok(new
			{
				staleAfterSeconds = MaintenanceService.StaleAfterSeconds,
				maxDrainSeconds = MaintenanceService.MaxDrainSeconds,
				maxTargets = MaintenanceService.MaxTargets,
				operations = result.Data.Select(o => Project(o, now)),
			});
		}

		/// <summary>One window, with per-target progress.</summary>
		[HttpGet("{id:long}")]
		[Authorize(Policy = PanelPolicies.Operator)]
		public async Task<IActionResult> Fetch(long id)
		{
			var result = await maintenance.FetchAsync(id, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return result.ErrorCode == DatabaseErrorCodes.NotFound
					? NotFound(new { error = $"There is no maintenance window {id}." })
					: StatusCode(StatusCodes.Status503ServiceUnavailable,
						new { error = "That maintenance window could not be read." });
			}

			return Ok(Project(result.Data, DateTime.UtcNow));
		}

		/// <summary>
		/// Plans a window: locks every target now, and schedules the shutdown for the end of the
		/// drain.
		/// </summary>
		[HttpPost]
		[Authorize(Policy = PanelPolicies.OperatorStepUp)]
		[Audited(AuditActions.MaintenanceStart, TargetType = "maintenance")]
		public async Task<IActionResult> Start([FromBody] StartRequest request)
		{
			if (string.IsNullOrWhiteSpace(request?.Reason))
			{
				return BadRequest(new { error = "A reason is required." });
			}
			if (request.Targets == null || request.Targets.Count == 0)
			{
				audit.Outcome = "Refused: no targets.";
				return BadRequest(new { error = "Pick at least one server." });
			}
			if (request.DrainSeconds == null)
			{
				return BadRequest(new { error = "Say how long the drain is, in seconds." });
			}

			/* Bounds checked here as well as in the service so the refusal arrives before the
			 * operator's reason is lost, and mirrored on the dialog for the same reason. Zero is
			 * allowed and means "stop at the next pulse"; negative is not, because it writes a
			 * deadline in the past and every server acts on it the instant it reads the row. */
			if (request.DrainSeconds < 0)
			{
				audit.Outcome = "Refused: negative drain.";
				return BadRequest(new { error = "The drain cannot be negative. Use 0 to stop at the next pulse." });
			}
			if (request.DrainSeconds > MaintenanceService.MaxDrainSeconds)
			{
				audit.Outcome = "Refused: drain beyond the 24 hour ceiling.";
				return BadRequest(new { error = $"The drain cannot exceed {MaintenanceService.MaxDrainSeconds} seconds (24 hours)." });
			}
			if (request.Targets.Count > MaintenanceService.MaxTargets)
			{
				audit.Outcome = "Refused: too many targets.";
				return BadRequest(new { error = $"A maintenance window takes at most {MaintenanceService.MaxTargets} servers." });
			}

			var targets = request.Targets
				.Where(t => t != null)
				.Select(t => new MaintenanceTargetRequest { Kind = t.Kind, ServerID = t.ServerId })
				.ToList();

			audit.TargetName = string.IsNullOrWhiteSpace(request.Name) ? "Maintenance" : request.Name.Trim();
			audit.Details = new
			{
				drainSeconds = request.DrainSeconds,
				targets = targets.Select(t => new { kind = t.Kind, serverId = t.ServerID }),
			};

			var result = await maintenance.StartAsync(
				request.Name,
				User.Identity?.Name ?? "",
				request.Reason,
				request.DrainSeconds.Value,
				targets,
				HttpContext.RequestAborted);

			if (!result.IsSuccess)
			{
				audit.Outcome = result.ErrorMessage;
				return BadRequest(new { error = result.ErrorMessage ?? "That maintenance window could not be started." });
			}

			var operation = result.Data;
			// The surrogate key is what the audit row should point at, and it exists only now.
			audit.TargetID = operation.ID.ToString();

			log.LogWarning(
				"Maintenance window {Id} ('{Name}') started by '{Actor}' over {Targets} server(s), deadline {Deadline:O}. Reason: {Reason}",
				operation.ID, operation.Name, User.Identity?.Name, operation.Targets.Count, operation.DeadlineUtc, request.Reason);

			DateTime now = DateTime.UtcNow;
			var written = operation.Targets.Where(t => t.ShutdownWrittenUtc != null).ToList();
			var unwritten = operation.Targets.Where(t => t.ShutdownWrittenUtc == null).ToList();

			/* What was WRITTEN, not what the servers have done. At this moment no server has read
			 * anything; the first of them will on its next pulse. */
			var warnings = new List<string>();
			if (unwritten.Count > 0)
			{
				warnings.Add($"{unwritten.Count} server(s) could not be written: " +
					string.Join(", ", unwritten.Select(t => t.ServerName)) + ". They are not part of this drain.");
			}

			var silent = written.Where(t => !t.PulsingAtStart).ToList();
			if (silent.Count > 0)
			{
				warnings.Add($"{silent.Count} server(s) are not pulsing: " +
					string.Join(", ", silent.Select(t => t.ServerName)) +
					". The rows are saved, but nothing has read them and nothing will until those processes are running.");
			}

			return Ok(new
			{
				operation = Project(operation, now),
				message = $"{written.Count} server(s) locked, and their shutdown written for " +
					$"{operation.DeadlineUtc:HH:mm:ss} UTC. Each one acts on both once it reads its row. " +
					"Cancelling later clears the deadline but leaves them LOCKED.",
				warnings,
			});
		}

		/// <summary>
		/// Calls off a window's scheduled shutdowns. <b>The servers stay locked.</b>
		/// </summary>
		[HttpDelete("{id:long}")]
		[Authorize(Policy = PanelPolicies.OperatorStepUp)]
		[Audited(AuditActions.MaintenanceCancel, TargetType = "maintenance", TargetRouteValue = "id")]
		public async Task<IActionResult> Cancel(long id, [FromBody] ReasonRequest request)
		{
			if (string.IsNullOrWhiteSpace(request?.Reason))
			{
				return BadRequest(new { error = "A reason is required." });
			}

			var result = await maintenance.CancelAsync(id, User.Identity?.Name ?? "", request.Reason, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				audit.Outcome = result.ErrorMessage;
				return result.ErrorCode == DatabaseErrorCodes.NotFound
					? NotFound(new { error = $"There is no maintenance window {id}." })
					: BadRequest(new { error = result.ErrorMessage ?? "That maintenance window could not be cancelled." });
			}

			var operation = result.Data;
			audit.TargetName = operation.Name;

			/* The servers this leaves locked, by name. The single-server control warns about the
			 * lock in a sentence; a window can leave a dozen servers locked at once, so listing
			 * them is the difference between a warning and something an operator can act on.
			 *
			 * A target that COMPLETED is not in this list: it stopped, and a stopped server's lock
			 * matters only if it comes back — a world server's row is gone entirely, and a scene
			 * server clears its own lock as it exits. */
			var stillLocked = operation.Targets
				.Where(t => t.LockWrittenUtc != null && t.Status != MaintenanceStatus.Completed)
				.Select(t => new { kind = t.Kind, serverId = t.ServerID, serverName = t.ServerName })
				.ToList();

			audit.Details = new { stillLocked = stillLocked.Count };

			log.LogWarning(
				"Maintenance window {Id} cancelled by '{Actor}'. {Locked} server(s) remain LOCKED. Reason: {Reason}",
				id, User.Identity?.Name, stillLocked.Count, request.Reason);

			string message = stillLocked.Count > 0
				? $"Shutdowns cleared. {stillLocked.Count} server(s) are STILL LOCKED — scheduling locked them and " +
					"cancelling does not lift that. Unlock them on the server board, or nobody can log in. " +
					"Any server that had already begun stopping will not come back."
				: "Shutdowns cleared. Nothing had been written to any server, so nothing is locked by this window.";

			return Ok(new
			{
				operation = Project(operation, DateTime.UtcNow),
				message,
				stillLocked,
			});
		}

		/// <summary>
		/// One window, as the browser needs it.
		/// </summary>
		/// <remarks>
		/// Identical between the listing and the single fetch on purpose: the list shows live
		/// progress, so it needs the same per-target detail, and one shape means one renderer.
		/// </remarks>
		private static object Project(MaintenanceOperationData operation, DateTime now) => new
		{
			id = operation.ID,
			name = operation.Name,
			status = (int)operation.Status,
			statusName = operation.Status.ToString(),
			active = operation.Status == MaintenanceStatus.Draining ||
				operation.Status == MaintenanceStatus.ShuttingDown,
			startedBy = operation.StartedBy,
			reason = operation.Reason,
			drainSeconds = operation.DrainSeconds,
			startedUtc = operation.StartedUtc,
			deadlineUtc = operation.DeadlineUtc,
			secondsUntilDeadline = Math.Round((operation.DeadlineUtc - now).TotalSeconds, 1),
			completedUtc = operation.CompletedUtc,
			cancelledBy = operation.CancelledBy,
			cancelledUtc = operation.CancelledUtc,
			cancelReason = operation.CancelReason,
			outcome = operation.Outcome,
			// Players still on the servers that have not finished: the drain, in one number.
			playersRemaining = operation.Targets
				.Where(t => t.Status == MaintenanceStatus.Draining ||
					t.Status == MaintenanceStatus.ShuttingDown)
				.Sum(t => t.ObservedCharacterCount),
			stillLockedCount = operation.Targets
				.Count(t => t.LockWrittenUtc != null &&
					t.Status != MaintenanceStatus.Completed),
			counts = new
			{
				total = operation.Targets.Count,
				draining = operation.Targets.Count(t => t.Status == MaintenanceStatus.Draining),
				shuttingDown = operation.Targets.Count(t => t.Status == MaintenanceStatus.ShuttingDown),
				completed = operation.Targets.Count(t => t.Status == MaintenanceStatus.Completed),
				cancelled = operation.Targets.Count(t => t.Status == MaintenanceStatus.Cancelled),
				failed = operation.Targets.Count(t => t.Status == MaintenanceStatus.Failed),
			},
			targets = operation.Targets.Select(t => new
			{
				id = t.ID,
				kind = t.Kind,
				serverId = t.ServerID,
				serverName = t.ServerName,
				status = (int)t.Status,
				statusName = t.Status.ToString(),
				scheduledShutdownUtc = t.ScheduledShutdownUtc,
				lockWrittenUtc = t.LockWrittenUtc,
				shutdownWrittenUtc = t.ShutdownWrittenUtc,
				shutdownClearedUtc = t.ShutdownClearedUtc,
				// Whether THIS target is one an operator still has to unlock by hand.
				stillLocked = t.LockWrittenUtc != null &&
					t.Status != MaintenanceStatus.Completed,
				pulsingAtStart = t.PulsingAtStart,
				observedUtc = t.ObservedUtc,
				observedPlayers = t.ObservedCharacterCount,
				observedLocked = t.ObservedLocked,
				observedShutdownUtc = t.ObservedShutdownUtc,
				observedLastPulseUtc = t.ObservedLastPulseUtc,
				observedRegistered = t.ObservedRegistered,
				observedPulseAgeSeconds = t.ObservedLastPulseUtc.HasValue
					? (double?)Math.Round((now - t.ObservedLastPulseUtc.Value).TotalSeconds, 1)
					: null,
				observedStale = t.ObservedLastPulseUtc.HasValue &&
					(now - t.ObservedLastPulseUtc.Value).TotalSeconds > MaintenanceService.StaleAfterSeconds,
				note = t.Note,
			}),
		};

		/// <summary>A new maintenance window.</summary>
		public sealed class StartRequest
		{
			/// <summary>What to call it, for the listing. Optional.</summary>
			public string Name { get; set; } = "";

			/// <summary>The servers to take down.</summary>
			public List<TargetRequest> Targets { get; set; } = new List<TargetRequest>();

			/// <summary>How long players get, in seconds. Zero stops at the next pulse.</summary>
			public int? DrainSeconds { get; set; }

			/// <summary>Why the action was taken. Recorded.</summary>
			public string Reason { get; set; } = "";
		}

		/// <summary>One server in a window.</summary>
		public sealed class TargetRequest
		{
			/// <summary><c>world</c> or <c>scene</c>, as on the per-server routes.</summary>
			public string Kind { get; set; } = "";

			/// <summary>The server's id, within its tier.</summary>
			public long ServerId { get; set; }
		}

		/// <summary>A request carrying only the audit reason.</summary>
		public sealed class ReasonRequest
		{
			/// <summary>Why the action was taken. Recorded.</summary>
			public string Reason { get; set; } = "";
		}
	}
}
