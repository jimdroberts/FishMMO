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
		private readonly AuditScope audit;
		private readonly ILogger<SupportTicketsController> log;

		public SupportTicketsController(ISupportTicketService tickets, AuditScope audit, ILogger<SupportTicketsController> log)
		{
			this.tickets = tickets;
			this.audit = audit;
			this.log = log;
		}

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
			[FromQuery] int pageSize = 25)
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
				Page = page,
				PageSize = pageSize,
			};

			var result = await tickets.SearchAsync(query, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return BadRequest(new { error = result.ErrorMessage ?? "That search could not be run." });
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
				return NotFound(new { error = "No such ticket." });
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

			var result = await tickets.AssignAsync(id, assignee, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				audit.Outcome = result.ErrorMessage;
				return BadRequest(new { error = result.ErrorMessage ?? "That ticket could not be assigned." });
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
				HttpContext.RequestAborted);

			if (!result.IsSuccess)
			{
				audit.Outcome = result.ErrorMessage;
				return BadRequest(new { error = result.ErrorMessage ?? "That message could not be added." });
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

			var result = await tickets.SetStatusAsync(id, status, User.Identity?.Name, request.Resolution, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				audit.Outcome = result.ErrorMessage;
				return BadRequest(new { error = result.ErrorMessage ?? "That status could not be set." });
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

			var result = await tickets.SetPriorityAsync(id, request.Priority.Value, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				audit.Outcome = result.ErrorMessage;
				return BadRequest(new { error = result.ErrorMessage ?? "That priority could not be set." });
			}
			return Ok(new { message = $"Priority set to {request.Priority}." });
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
