using FishMMO.ControlPanel.Auth;
using FishMMO.ControlPanel.Services;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FishMMO.ControlPanel.Controllers
{
	/// <summary>
	/// The server board, and per-server lock and shutdown control.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Every control here writes a database row. Nothing reaches into a process.</b> Each
	/// server adopts its own row on its next pulse, which is what lets a command issued here
	/// reach a machine the panel cannot address — and it is why every acknowledgement says what
	/// was <em>written</em> rather than claiming the server has already done it. An operator
	/// who believes a world is locked when the row has not been read yet will let the next
	/// player in and not understand why.
	/// </para>
	/// <para>
	/// <b>There is no start, stop or restart, and there cannot be one here.</b> A stopped
	/// process polls nothing, so no row will ever reach it. Those need a supervising daemon on
	/// each host, which is a separate component with its own trust boundary; pretending
	/// otherwise with a button that writes a row nobody reads would be the worst kind of
	/// control panel lie.
	/// </para>
	/// <para>
	/// The same capability exists in-game as <c>/admin lockserver</c> and <c>/admin shutdown</c>,
	/// at Admin. These sit at Operator plus a step-up so the two surfaces agree about who may
	/// use them: two faces of one capability that disagree means the weaker is the way around
	/// the stronger.
	/// </para>
	/// </remarks>
	[ApiController]
	[Route("api/servers")]
	public sealed class ServersController : ControllerBase
	{
		/// <summary>
		/// How long without a pulse before a server is shown as stale.
		/// </summary>
		/// <remarks>
		/// Reported to the client rather than applied here, so the board can show the age and
		/// let the operator judge a server that is merely slow. Matches the idle timeout the
		/// per-tier services use for their own "fetch active" reads.
		/// </remarks>
		private const int StaleAfterSeconds = 60;

		/// <summary>Longest shutdown delay that may be scheduled: 24 hours.</summary>
		/// <remarks>
		/// Bounds a typo rather than a policy, exactly as the in-game command does:
		/// <c>shutdown 6000000</c> should be refused rather than quietly locking the world for
		/// eleven weeks.
		/// </remarks>
		private const int MaxShutdownDelaySeconds = 86_400;

		private readonly IServerBoardService board;
		private readonly IWorldServerService worldServers;
		private readonly ISceneServerService sceneServers;
		private readonly AuditScope audit;
		private readonly ILogger<ServersController> log;

		public ServersController(
			IServerBoardService board,
			IWorldServerService worldServers,
			ISceneServerService sceneServers,
			AuditScope audit,
			ILogger<ServersController> log)
		{
			this.board = board;
			this.worldServers = worldServers;
			this.sceneServers = sceneServers;
			this.audit = audit;
			this.log = log;
		}

		/// <summary>Every server, in one read.</summary>
		[HttpGet]
		[Authorize(Policy = PanelPolicies.Operator)]
		public async Task<IActionResult> Board()
		{
			var result = await board.FetchBoardAsync(HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "The server board could not be read." });
			}

			DateTime now = DateTime.UtcNow;
			var data = result.Data;
			return Ok(new
			{
				staleAfterSeconds = StaleAfterSeconds,
				loginServers = data.LoginServers.Select(s => Project(s, now)),
				worldServers = data.WorldServers.Select(s => Project(s, now)),
				sceneServers = data.SceneServers.Select(s => Project(s, now)),
				sceneInstanceCount = data.SceneInstanceCount,
			});
		}

		/// <summary>Live scene instances.</summary>
		[HttpGet("scenes")]
		[Authorize(Policy = PanelPolicies.Operator)]
		public async Task<IActionResult> Scenes(
			[FromQuery] int? status,
			[FromQuery] long? sceneServerId,
			[FromQuery] int page = 1,
			[FromQuery] int pageSize = 50)
		{
			var result = await board.FetchScenesAsync(status, sceneServerId, page, pageSize, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "The scene list could not be read." });
			}

			var data = result.Data;
			return Ok(new
			{
				items = data.Items.Select(s => new
				{
					id = s.ID,
					sceneServerId = s.SceneServerID,
					worldServerId = s.WorldServerID,
					sceneName = s.SceneName,
					sceneHandle = s.SceneHandle,
					status = s.Status,
					statusName = ((FishMMO.Database.Data.Enums.SceneStatus)s.Status).ToString(),
					type = s.Type,
					typeName = ((FishMMO.Database.Data.Enums.SceneType)s.Type).ToString(),
					characterCount = s.CharacterCount,
					characterId = s.CharacterID,
					partyId = s.PartyID,
					isPrivate = s.IsPrivate,
					timeCreatedUtc = s.TimeCreated,
				}),
				page = data.Page,
				pageSize = data.PageSize,
				totalCount = data.TotalCount,
			});
		}

		/// <summary>Refuses new logins above Player on one server.</summary>
		[HttpPost("{kind}/{id:long}/lock")]
		[Authorize(Policy = PanelPolicies.OperatorStepUp)]
		[Audited(AuditActions.ServerLock, TargetType = "server", TargetRouteValue = "id")]
		public Task<IActionResult> Lock(string kind, long id, [FromBody] ReasonRequest request) =>
			SetLockAsync(kind, id, request, locked: true);

		/// <summary>Accepts logins again.</summary>
		[HttpPost("{kind}/{id:long}/unlock")]
		[Authorize(Policy = PanelPolicies.OperatorStepUp)]
		[Audited(AuditActions.ServerUnlock, TargetType = "server", TargetRouteValue = "id")]
		public Task<IActionResult> Unlock(string kind, long id, [FromBody] ReasonRequest request) =>
			SetLockAsync(kind, id, request, locked: false);

		private async Task<IActionResult> SetLockAsync(string kind, long id, ReasonRequest request, bool locked)
		{
			if (string.IsNullOrWhiteSpace(request?.Reason))
			{
				return BadRequest(new { error = "A reason is required." });
			}
			if (!TryResolveKind(kind, out bool isWorld))
			{
				audit.Outcome = "Refused: not a server kind.";
				return BadRequest(new { error = "A server is either 'world' or 'scene'." });
			}

			audit.TargetName = $"{kind}/{id}";
			audit.Details = new { kind, locked };

			var result = isWorld
				? await worldServers.SetLockedAsync(id, locked, HttpContext.RequestAborted)
				: await sceneServers.SetLockedAsync(id, locked, HttpContext.RequestAborted);

			if (!result.IsSuccess)
			{
				audit.Outcome = result.ErrorMessage;
				return BadRequest(new { error = result.ErrorMessage ?? "That server could not be updated." });
			}

			log.LogWarning("{Kind} server {Id} {State} by '{Actor}'. Reason: {Reason}",
				kind, id, locked ? "LOCKED" : "UNLOCKED", User.Identity?.Name, request.Reason);

			// What was written, not what the server has done. See the remarks on this controller.
			return Ok(new
			{
				message = locked
					? "Lock written. The server applies it on its next pulse; players already online are unaffected."
					: "Unlock written. The server accepts logins again on its next pulse.",
			});
		}

		/// <summary>Schedules a shutdown.</summary>
		[HttpPost("{kind}/{id:long}/shutdown")]
		[Authorize(Policy = PanelPolicies.OperatorStepUp)]
		[Audited(AuditActions.ServerShutdown, TargetType = "server", TargetRouteValue = "id")]
		public async Task<IActionResult> Shutdown(string kind, long id, [FromBody] ShutdownRequest request)
		{
			if (string.IsNullOrWhiteSpace(request?.Reason))
			{
				return BadRequest(new { error = "A reason is required." });
			}
			if (!TryResolveKind(kind, out bool isWorld))
			{
				audit.Outcome = "Refused: not a server kind.";
				return BadRequest(new { error = "A server is either 'world' or 'scene'." });
			}
			if (request.Seconds == null)
			{
				return BadRequest(new { error = "Say how many seconds from now." });
			}

			/* Zero means now and is allowed. Negative is not: it schedules a deadline in the
			 * past, which every server acts on the instant it reads the row — an immediate
			 * shutdown with no warning, from what looks like a typo. The in-game command
			 * refuses it for the same reason. */
			if (request.Seconds < 0)
			{
				audit.Outcome = "Refused: negative delay.";
				return BadRequest(new { error = "The delay cannot be negative. Use 0 to shut down immediately." });
			}
			if (request.Seconds > MaxShutdownDelaySeconds)
			{
				audit.Outcome = "Refused: delay beyond the 24 hour ceiling.";
				return BadRequest(new { error = $"The delay cannot exceed {MaxShutdownDelaySeconds} seconds (24 hours)." });
			}

			DateTime deadline = DateTime.UtcNow.AddSeconds(request.Seconds.Value);
			audit.TargetName = $"{kind}/{id}";
			audit.Details = new { kind, seconds = request.Seconds, deadlineUtc = deadline };

			var result = isWorld
				? await worldServers.SetShutdownAsync(id, deadline, HttpContext.RequestAborted)
				: await sceneServers.SetShutdownAsync(id, deadline, HttpContext.RequestAborted);

			if (!result.IsSuccess)
			{
				audit.Outcome = result.ErrorMessage;
				return BadRequest(new { error = result.ErrorMessage ?? "That shutdown could not be scheduled." });
			}

			log.LogWarning("{Kind} server {Id} shutdown scheduled for {Deadline:O} by '{Actor}'. Reason: {Reason}",
				kind, id, deadline, User.Identity?.Name, request.Reason);

			/* Scheduling also LOCKS the server — SetShutdownAsync writes shutdown_at_utc and
			 * locked = true in one statement, so nobody new joins a server that is about to
			 * stop. Saying so matters because the operator did not ask for the lock. */
			return Ok(new
			{
				deadlineUtc = deadline,
				message = $"Shutdown written for {deadline:HH:mm:ss} UTC, and the server is locked so nobody new joins it. " +
					"It acts on both once it reads the row.",
			});
		}

		/// <summary>Cancels a scheduled shutdown.</summary>
		[HttpDelete("{kind}/{id:long}/shutdown")]
		[Authorize(Policy = PanelPolicies.OperatorStepUp)]
		[Audited(AuditActions.ServerShutdownCancel, TargetType = "server", TargetRouteValue = "id")]
		public async Task<IActionResult> CancelShutdown(string kind, long id, [FromBody] ReasonRequest request)
		{
			if (string.IsNullOrWhiteSpace(request?.Reason))
			{
				return BadRequest(new { error = "A reason is required." });
			}
			if (!TryResolveKind(kind, out bool isWorld))
			{
				audit.Outcome = "Refused: not a server kind.";
				return BadRequest(new { error = "A server is either 'world' or 'scene'." });
			}

			audit.TargetName = $"{kind}/{id}";
			audit.Details = new { kind };

			var result = isWorld
				? await worldServers.SetShutdownAsync(id, null, HttpContext.RequestAborted)
				: await sceneServers.SetShutdownAsync(id, null, HttpContext.RequestAborted);

			if (!result.IsSuccess)
			{
				audit.Outcome = result.ErrorMessage;
				return BadRequest(new { error = result.ErrorMessage ?? "That shutdown could not be cancelled." });
			}

			log.LogWarning("{Kind} server {Id} shutdown cancelled by '{Actor}'. Reason: {Reason}",
				kind, id, User.Identity?.Name, request.Reason);

			/* Two things the operator does not otherwise know, and both bite.
			 *
			 * A server that has already read the deadline and begun shutting down will not come
			 * back because the row was cleared — so a cancellation can arrive too late without
			 * saying so.
			 *
			 * And the LOCK that scheduling applied is still there: cancelling only nulls the
			 * deadline. An operator who cancels a shutdown and walks away leaves a server that
			 * nobody can log into, and will look for the cause anywhere but here. */
			return Ok(new
			{
				message = "Shutdown cleared, but the server is still LOCKED — scheduling locked it, and cancelling does not lift that. " +
					"Unlock it separately. A server that already began shutting down will not stop.",
			});
		}

		/// <summary>The two tiers that can be locked and shut down.</summary>
		/// <remarks>
		/// A login server is deliberately not one of them: it has no players to protect and no
		/// control columns, and the way to stop authentication is to lock the world.
		/// </remarks>
		private static bool TryResolveKind(string kind, out bool isWorld)
		{
			isWorld = string.Equals(kind, "world", StringComparison.OrdinalIgnoreCase);
			return isWorld || string.Equals(kind, "scene", StringComparison.OrdinalIgnoreCase);
		}

		private static object Project(ServerAdminData s, DateTime now) => new
		{
			id = s.ID,
			name = s.Name,
			address = s.Address,
			port = s.Port,
			lastPulseUtc = s.LastPulse,
			pulseAgeSeconds = Math.Round((now - s.LastPulse).TotalSeconds, 1),
			stale = (now - s.LastPulse).TotalSeconds > StaleAfterSeconds,
			startedUtc = s.TimeCreated,
			characterCount = s.CharacterCount,
			locked = s.Locked,
			shutdownAtUtc = s.ShutdownAtUtc,
			secondsUntilShutdown = s.ShutdownAtUtc.HasValue
				? (double?)Math.Round((s.ShutdownAtUtc.Value - now).TotalSeconds, 1)
				: null,
		};

		/// <summary>A request carrying only the audit reason.</summary>
		public sealed class ReasonRequest
		{
			/// <summary>Why the action was taken. Recorded.</summary>
			public string Reason { get; set; } = "";
		}

		/// <summary>A scheduled shutdown.</summary>
		public sealed class ShutdownRequest
		{
			/// <summary>Seconds from now. Zero means immediately.</summary>
			public int? Seconds { get; set; }

			/// <summary>Why the action was taken. Recorded.</summary>
			public string Reason { get; set; } = "";
		}
	}
}
