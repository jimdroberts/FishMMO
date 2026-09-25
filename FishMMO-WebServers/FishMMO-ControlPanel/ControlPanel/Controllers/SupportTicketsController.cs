using FishMMO.Auth.Core;
using FishMMO.ControlPanel.Auth;
using FishMMO.ControlPanel.Services;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Npgsql.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FishMMO.ControlPanel.Controllers
{
	/// <summary>
	/// The staff side of the support queue.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Game master and above. The player's own view of the same tickets lives in
	/// <see cref="MyTicketsController"/>, as a separate controller rather than a branch inside
	/// these actions — because the difference between them is whether internal staff notes are
	/// returned, and a single action deciding that from a claim is one refactor away from
	/// getting it wrong.
	/// </para>
	/// <para>
	/// Every write here is recorded by <see cref="AuditActionFilter"/>. Nothing in this file
	/// calls an audit method.
	/// </para>
	/// </remarks>
	[ApiController]
	[Route("api/support/tickets")]
	public sealed class SupportTicketsController : ControllerBase
	{
		private readonly ISupportTicketService tickets;
		private readonly IAccountService accounts;
		private readonly AuditScope audit;
		private readonly ILogger<SupportTicketsController> log;

		public SupportTicketsController(ISupportTicketService tickets, IAccountService accounts, AuditScope audit, ILogger<SupportTicketsController> log)
		{
			this.tickets = tickets;
			this.accounts = accounts;
			this.audit = audit;
			this.log = log;
		}

		/// <summary>
		/// The signed-in staff member's access level, which is also the highest support tier they work.
		/// </summary>
		private byte ActorTier => (byte)ModerationGuards.ActorLevel(this);

		/// <summary>The queue.</summary>
		[HttpGet]
		[Authorize(Policy = PanelPolicies.Support)]
		public async Task<IActionResult> Search(
			[FromQuery(Name = "status")] int[] status,
			[FromQuery] int? category,
			[FromQuery] string assignedTo,
			[FromQuery] bool unassigned = false,
			[FromQuery] string reporter = null,
			[FromQuery] string target = null,
			[FromQuery] string subject = null,
			[FromQuery] int page = 1,
			[FromQuery] int pageSize = 25,
			[FromQuery] int? tier = null)
		{
			var query = new SupportTicketQuery
			{
				Statuses = status?.Where(s => Enum.IsDefined(typeof(SupportTicketStatus), s))
					.Select(s => (SupportTicketStatus)s).ToList(),
				Category = category.HasValue && Enum.IsDefined(typeof(SupportTicketCategory), category.Value)
					? (SupportTicketCategory)category.Value
					: null,
				AssignedTo = assignedTo,
				UnassignedOnly = unassigned,
				ReporterAccount = reporter,
				TargetAccount = target,
				Subject = subject,
				// Their own tier and below, in the query. A ticket promoted past them leaves their
				// queue rather than sitting in it refusing every action.
				MaxRequiredAccessLevel = ActorTier,
				RequiredAccessLevel = tier is 2 or 3 ? (byte)tier.Value : null,
				Page = page,
				PageSize = pageSize,
			};

			var result = await tickets.SearchAsync(query, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return DatabaseReplies.Failure(this, result, log, "That search could not be run.");
			}

			var data = result.Data;
			return Ok(new
			{
				items = data.Items.Select(t => Project(t, includeMessages: false)),
				page = data.Page,
				pageSize = data.PageSize,
				totalCount = data.TotalCount,
			});
		}

		/// <summary>One ticket, with the whole conversation including internal notes.</summary>
		[HttpGet("{id:long}")]
		[Authorize(Policy = PanelPolicies.Support)]
		public async Task<IActionResult> Get(long id)
		{
			// includeInternal: true — this is the staff view, and the notes are why it exists.
			var result = await tickets.FetchAsync(id, includeInternal: true, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return DatabaseReplies.Failure(this, result, log, "That ticket could not be read.", notFound: "No such ticket.");
			}
			if (result.Data.RequiredAccessLevel > ActorTier)
			{
				return StatusCode(StatusCodes.Status403Forbidden, new { error = "That ticket has been escalated beyond your tier." });
			}
			return Ok(Project(result.Data, includeMessages: true));
		}

		/// <summary>Takes a ticket, or hands it back.</summary>
		[HttpPost("{id:long}/assign")]
		[Authorize(Policy = PanelPolicies.Support)]
		[Audited(AuditActions.TicketAssign, TargetType = "ticket", TargetRouteValue = "id")]
		public async Task<IActionResult> Assign(long id, [FromBody] AssignRequest request)
		{
			if (string.IsNullOrWhiteSpace(request?.Reason))
			{
				return BadRequest(new { error = "A reason is required." });
			}

			/* An empty assignee means "unassign", which is different from an absent one. A staff
			 * member handing a ticket back to the queue is a normal thing to do and must not be
			 * mistaken for a malformed request. */
			string assignee = string.IsNullOrWhiteSpace(request.Assignee) ? null : request.Assignee.Trim();
			audit.Details = new { assignee };

			/* The assignee must be able to work the ticket at its tier. The service checks the ACTOR
			 * against the tier on every write; it cannot check the person named, so this does. A
			 * promoted ticket assigned back to a game master would sit in a list they cannot open. */
			var current = await tickets.FetchAsync(id, includeInternal: false, HttpContext.RequestAborted);
			if (!current.IsSuccess)
			{
				audit.Outcome = DatabaseReplies.IsNotFound(current.ErrorCode) ? "Refused: no such ticket." : DatabaseReplies.Outcome(current);
				return DatabaseReplies.Failure(this, current, log, "That ticket could not be read.", notFound: "No such ticket.");
			}

			/* The tier first, before anything is looked up about the named assignee: a refusal must not
			 * tell a game master whether an account exists or what tier it works on a ticket they may not
			 * see at all. Same answer as Get. */
			if (current.Data.RequiredAccessLevel > ActorTier)
			{
				audit.Outcome = "Refused: ticket above the operator's tier.";
				return StatusCode(StatusCodes.Status403Forbidden, new { error = "That ticket has been escalated beyond your tier." });
			}

			if (assignee != null)
			{

				var staff = await accounts.FetchAdminAsync(assignee, HttpContext.RequestAborted);
				if (!staff.IsSuccess)
				{
					/* A name that is not an account is the operator's typo, and a 400 about the field.
					 * A database that did not answer says nothing about the name, and must not claim to. */
					if (DatabaseReplies.IsFault(staff.ErrorCode))
					{
						audit.Outcome = DatabaseReplies.Outcome(staff);
						return DatabaseReplies.Failure(this, staff, log, "The assignee's account could not be read.");
					}
					audit.Outcome = "Refused: no such account.";
					return BadRequest(new { error = $"There is no account called {assignee}." });
				}

				byte needed = Math.Max((byte)FishMMO.Auth.Core.AccessLevel.GameMaster, current.Data.RequiredAccessLevel);
				if (staff.Data.AccessLevel < needed)
				{
					audit.Outcome = "Refused: assignee below the ticket's tier.";
					return BadRequest(new { error = $"{staff.Data.Name} cannot work tickets at this tier." });
				}
				assignee = staff.Data.Name;
			}

			var result = await tickets.AssignAsync(id, assignee, ActorTier, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				audit.Outcome = DatabaseReplies.Outcome(result);
				return DatabaseReplies.Failure(this, result, log, "That ticket could not be assigned.");
			}

			log.LogInformation("Ticket {Id} assigned to '{Assignee}' by '{Actor}'.", id, assignee ?? "(nobody)", User.Identity?.Name);
			return Ok(new { message = assignee == null ? "Returned to the queue." : $"Assigned to {assignee}." });
		}

		/// <summary>Replies to the player, or writes a note only staff can see.</summary>
		[HttpPost("{id:long}/reply")]
		[Authorize(Policy = PanelPolicies.Support)]
		[Audited(AuditActions.TicketReply, TargetType = "ticket", TargetRouteValue = "id")]
		public async Task<IActionResult> Reply(long id, [FromBody] ReplyRequest request)
		{
			if (string.IsNullOrWhiteSpace(request?.Body))
			{
				return BadRequest(new { error = "A message is required." });
			}

			/* Recorded in the audit log, but the BODY is not: a staff reply can contain a
			 * player's personal details, and the audit log is read by more people and kept
			 * longer than the ticket. Whether it was internal is what an auditor needs. */
			audit.Details = new { request.Internal };

			var result = await tickets.AppendMessageAsync(
				id,
				User.Identity?.Name,
				authorIsStaff: true,
				internalNote: request.Internal,
				request.Body,
				ActorTier,
				HttpContext.RequestAborted);

			if (!result.IsSuccess)
			{
				audit.Outcome = DatabaseReplies.Outcome(result);
				return DatabaseReplies.Failure(this, result, log, "That message could not be added.");
			}

			return Ok(new { message = request.Internal ? "Note added. The player cannot see it." : "Reply sent." });
		}

		/// <summary>Moves a ticket's status, and records what was decided when finishing it.</summary>
		[HttpPost("{id:long}/status")]
		[Authorize(Policy = PanelPolicies.Support)]
		[Audited(AuditActions.TicketStatus, TargetType = "ticket", TargetRouteValue = "id")]
		public async Task<IActionResult> SetStatus(long id, [FromBody] StatusRequest request)
		{
			if (string.IsNullOrWhiteSpace(request?.Reason))
			{
				return BadRequest(new { error = "A reason is required." });
			}
			if (request.Status == null || !Enum.IsDefined(typeof(SupportTicketStatus), request.Status.Value))
			{
				audit.Outcome = "Refused: not a ticket status.";
				return BadRequest(new { error = "That is not a ticket status." });
			}

			var status = (SupportTicketStatus)request.Status.Value;
			audit.Details = new { status = status.ToString() };

			var result = await tickets.SetStatusAsync(id, status, User.Identity?.Name, request.Resolution, ActorTier, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				audit.Outcome = DatabaseReplies.Outcome(result);
				return DatabaseReplies.Failure(this, result, log, "That status could not be set.");
			}

			log.LogInformation("Ticket {Id} set to {Status} by '{Actor}'.", id, status, User.Identity?.Name);
			return Ok(new { message = $"Ticket is now {status}." });
		}

		/// <summary>Sets the staff priority.</summary>
		[HttpPost("{id:long}/priority")]
		[Authorize(Policy = PanelPolicies.Support)]
		[Audited(AuditActions.TicketPriority, TargetType = "ticket", TargetRouteValue = "id")]
		public async Task<IActionResult> SetPriority(long id, [FromBody] PriorityRequest request)
		{
			if (string.IsNullOrWhiteSpace(request?.Reason))
			{
				return BadRequest(new { error = "A reason is required." });
			}
			if (request.Priority == null)
			{
				return BadRequest(new { error = "A priority is required." });
			}

			audit.Details = new { priority = request.Priority };

			var result = await tickets.SetPriorityAsync(id, request.Priority.Value, ActorTier, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				audit.Outcome = DatabaseReplies.Outcome(result);
				return DatabaseReplies.Failure(this, result, log, "That priority could not be set.");
			}
			return Ok(new { message = $"Priority set to {request.Priority}." });
		}

		/// <summary>
		/// Moves a ticket to another support tier: promotes it to the administrators, or hands it back.
		/// </summary>
		/// <remarks>
		/// Tiers are access levels — <c>GameMaster</c> (2) and <c>Admin</c> (3). The service clears the
		/// assignee and writes the reason onto the ticket as a staff note in one transaction. A game
		/// master may promote; only an administrator may hand a ticket back down, because only they
		/// can act on it once it is there.
		/// </remarks>
		[HttpPost("{id:long}/tier")]
		[Authorize(Policy = PanelPolicies.Support)]
		[Audited(AuditActions.TicketTier, TargetType = "ticket", TargetRouteValue = "id")]
		public async Task<IActionResult> SetTier(long id, [FromBody] TierRequest request)
		{
			if (string.IsNullOrWhiteSpace(request?.Reason))
			{
				return BadRequest(new { error = "A reason is required." });
			}
			if (request.Tier is not (2 or 3))
			{
				audit.Outcome = "Refused: not a support tier.";
				return BadRequest(new { error = "A ticket's tier is Game Master (2) or Admin (3)." });
			}

			audit.Details = new { tier = request.Tier };

			var result = await tickets.SetTierAsync(id, (byte)request.Tier, User.Identity?.Name, request.Reason, ActorTier, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				audit.Outcome = DatabaseReplies.Outcome(result);
				return DatabaseReplies.Failure(this, result, log, "That ticket could not be moved.");
			}

			log.LogInformation("Ticket {Id} moved to tier {Tier} by '{Actor}'.", id, request.Tier, User.Identity?.Name);
			return Ok(new
			{
				message = request.Tier == 3
					? "Promoted to the administrators. It is in their queue, unassigned."
					: "Handed back to the game masters. It is in their queue, unassigned.",
			});
		}

		/// <summary>
		/// The wire shape of a ticket.
		/// </summary>
		/// <remarks>
		/// Shared with the player's controller deliberately, so the two views cannot drift into
		/// different shapes. What differs between them is what the SERVICE was asked to load,
		/// not what this method chooses to emit — a projection that filtered internal notes here
		/// would mean the private text had already been read out of the database and was one
		/// careless change away from being serialized.
		/// </remarks>
		internal static object Project(SupportTicketData t, bool includeMessages)
		{
			var projected = new Dictionary<string, object>
			{
				["id"] = t.ID,
				["createdUtc"] = t.CreatedUtc,
				["lastActivityUtc"] = t.LastActivityUtc,
				["reporterAccount"] = t.ReporterAccount,
				["reporterCharacterName"] = t.ReporterCharacterName,
				["reporterCharacterId"] = t.ReporterCharacterID,
				["category"] = (int)t.Category,
				["categoryName"] = t.Category.ToString(),
				["status"] = (int)t.Status,
				["statusName"] = t.Status.ToString(),
				["priority"] = t.Priority,
				["subject"] = t.Subject,
				["body"] = t.Body,
				["targetAccount"] = t.TargetAccount,
				["targetCharacterName"] = t.TargetCharacterName,
				["targetCharacterId"] = t.TargetCharacterID,
				["sceneName"] = t.SceneName,
				["assignedTo"] = t.AssignedTo,
				["resolution"] = t.Resolution,
				["closedUtc"] = t.ClosedUtc,
				["closedBy"] = t.ClosedBy,
				["requiredAccessLevel"] = t.RequiredAccessLevel,
				["escalatedBy"] = t.EscalatedBy,
				["escalatedUtc"] = t.EscalatedUtc,
				["messageCount"] = t.MessageCount,
			};

			if (includeMessages)
			{
				projected["messages"] = t.Messages.Select(m => new
				{
					id = m.ID,
					createdUtc = m.CreatedUtc,
					authorAccount = m.AuthorAccount,
					authorIsStaff = m.AuthorIsStaff,
					@internal = m.Internal,
					body = m.Body,
				});
			}
			return projected;
		}

		/// <summary>A move to another support tier.</summary>
		public sealed class TierRequest
		{
			/// <summary>The new tier, as an access level: 2 for Game Master, 3 for Admin.</summary>
			public int? Tier { get; set; }

			/// <summary>Why. Recorded, and written onto the ticket as a staff note.</summary>
			public string Reason { get; set; } = "";
		}

		/// <summary>An assignment.</summary>
		public sealed class AssignRequest
		{
			/// <summary>The staff account to assign to, or null to return it to the queue.</summary>
			public string Assignee { get; set; }

			/// <summary>Why. Recorded.</summary>
			public string Reason { get; set; } = "";
		}

		/// <summary>A staff message.</summary>
		public sealed class ReplyRequest
		{
			/// <summary>The text.</summary>
			public string Body { get; set; } = "";

			/// <summary>Whether the player must never see it.</summary>
			public bool Internal { get; set; }

			/// <summary>Why. Recorded.</summary>
			public string Reason { get; set; } = "";
		}

		/// <summary>A status change.</summary>
		public sealed class StatusRequest
		{
			/// <summary>The new status, as the numeric enum value.</summary>
			public int? Status { get; set; }

			/// <summary>What was decided. Required when resolving or closing.</summary>
			public string Resolution { get; set; }

			/// <summary>Why. Recorded.</summary>
			public string Reason { get; set; } = "";
		}

		/// <summary>A priority change.</summary>
		public sealed class PriorityRequest
		{
			/// <summary>0 to 3, higher is more urgent.</summary>
			public int? Priority { get; set; }

			/// <summary>Why. Recorded.</summary>
			public string Reason { get; set; } = "";
		}
	}
}
