using FishMMO.ControlPanel.Auth;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Npgsql.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FishMMO.ControlPanel.Controllers
{
	/// <summary>
	/// A player's own support tickets.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A separate controller from the staff one, on purpose.</b> The difference between the
	/// two views is whether internal staff notes are returned, and that is too important to be
	/// an <c>if</c> inside a shared action. Here every read passes
	/// <c>includeInternal: false</c>, and there is no parameter by which a caller could ask
	/// otherwise.
	/// </para>
	/// <para>
	/// Every action re-checks that the ticket belongs to the signed-in account, and answers
	/// <b>404, not 403</b>, when it does not. A 403 would confirm that ticket 812 exists and
	/// belongs to somebody else, which is an enumeration oracle over other players' support
	/// history.
	/// </para>
	/// <para>
	/// These are <see cref="PanelPolicies.Self"/> endpoints, so the audit filter deliberately
	/// ignores them: a player writing to their own ticket is not an operator action, and mixing
	/// the two populations would make the operator log unreadable.
	/// </para>
	/// </remarks>
	[ApiController]
	[Route("api/support/tickets/mine")]
	public sealed class MyTicketsController : ControllerBase
	{
		/// <summary>The most tickets one page will return.</summary>
		private const int MaxPageSize = 50;

		private readonly ISupportTicketService tickets;
		private readonly ILogger<MyTicketsController> log;

		public MyTicketsController(ISupportTicketService tickets, ILogger<MyTicketsController> log)
		{
			this.tickets = tickets;
			this.log = log;
		}

		/// <summary>The signed-in account's tickets.</summary>
		[HttpGet]
		[Authorize(Policy = PanelPolicies.Self)]
		public async Task<IActionResult> Mine([FromQuery] int page = 1, [FromQuery] int pageSize = 25)
		{
			var result = await tickets.SearchAsync(new SupportTicketQuery
			{
				// Scoped to the signed-in account by the server. There is no parameter for this.
				ReporterAccount = User.Identity?.Name,
				Page = page,
				PageSize = pageSize > MaxPageSize ? MaxPageSize : pageSize,
			}, HttpContext.RequestAborted);

			if (!result.IsSuccess)
			{
				return BadRequest(new { error = "Your tickets could not be loaded." });
			}

			var data = result.Data;
			return Ok(new
			{
				items = data.Items.Select(t => SupportTicketsController.Project(t, includeMessages: false)),
				page = data.Page,
				pageSize = data.PageSize,
				totalCount = data.TotalCount,
			});
		}

		/// <summary>
		/// Files a ticket from the panel.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The game has <c>/report</c>, <c>/bug</c> and <c>/helpme</c>, which is where most
		/// tickets come from. This exists for the player who cannot get in-game to file one —
		/// the client will not start, or they are on a phone — and for following up in writing
		/// at length, which a chat line is a poor place for.
		/// </para>
		/// <para>
		/// <b>It does not help a banned player.</b> A banned account cannot authenticate here
		/// any more than it can in the game, so an appeal cannot be filed through this route.
		/// That is a real gap and it needs a channel outside the authenticated panel; it is not
		/// something this endpoint can quietly pretend to cover.
		/// </para>
		/// <para>
		/// The reporter is the signed-in account, never a field in the body. Status, priority
		/// and assignee are not accepted at all — see <c>SupportTicketCreate</c>.
		/// </para>
		/// </remarks>
		[HttpPost]
		[Authorize(Policy = PanelPolicies.Self)]
		public async Task<IActionResult> File([FromBody] FileRequest request)
		{
			if (request == null)
			{
				return BadRequest(new { error = "A ticket is required." });
			}
			if (request.Category == null || !Enum.IsDefined(typeof(SupportTicketCategory), request.Category.Value))
			{
				return BadRequest(new { error = "Choose what this is about." });
			}

			var category = (SupportTicketCategory)request.Category.Value;

			var result = await tickets.CreateAsync(new SupportTicketCreate
			{
				ReporterAccount = User.Identity?.Name,
				// No character: a ticket filed from the panel was not filed from a character.
				ReporterCharacterName = null,
				ReporterCharacterID = 0,
				Category = category,
				Subject = request.Subject,
				Body = request.Body,
				TargetCharacterName = request.TargetCharacterName,
				SceneName = null,
			}, HttpContext.RequestAborted);

			if (!result.IsSuccess)
			{
				/* The service's message is written to be read by the player — it says how many
				 * tickets they already have open, or that they filed one moments ago. Replacing
				 * it with something generic would leave them guessing. */
				return BadRequest(new { error = result.ErrorMessage ?? "That ticket could not be filed." });
			}

			log.LogInformation("Ticket {Id} filed by '{Account}' from the panel.", result.Data, User.Identity?.Name);
			return Ok(new { id = result.Data, message = $"Ticket #{result.Data} filed. Staff will reply here." });
		}

		/// <summary>One of the signed-in account's tickets, without internal notes.</summary>
		[HttpGet("{id:long}")]
		[Authorize(Policy = PanelPolicies.Self)]
		public async Task<IActionResult> Get(long id)
		{
			var (ticket, failure) = await ResolveOwnAsync(id);
			if (failure != null)
			{
				return failure;
			}
			return Ok(SupportTicketsController.Project(ticket, includeMessages: true));
		}

		/// <summary>Adds the player's reply.</summary>
		/// <remarks>
		/// A reply to a resolved ticket reopens it, in the service. The player is telling staff
		/// it was not resolved, and the alternative is a message that lands where nobody looks.
		/// </remarks>
		[HttpPost("{id:long}/reply")]
		[Authorize(Policy = PanelPolicies.Self)]
		public async Task<IActionResult> Reply(long id, [FromBody] ReplyRequest request)
		{
			if (string.IsNullOrWhiteSpace(request?.Body))
			{
				return BadRequest(new { error = "Write a message first." });
			}

			var (ticket, failure) = await ResolveOwnAsync(id);
			if (failure != null)
			{
				return failure;
			}

			var result = await tickets.AppendMessageAsync(
				id,
				User.Identity?.Name,
				// Never staff, and never internal, whatever the body says. These two arguments
				// are the entire security boundary of the player's side of a ticket.
				authorIsStaff: false,
				internalNote: false,
				request.Body,
				HttpContext.RequestAborted);

			if (!result.IsSuccess)
			{
				return BadRequest(new { error = result.ErrorMessage ?? "That reply could not be added." });
			}

			return Ok(new
			{
				message = ticket.Status == SupportTicketStatus.Resolved
					? "Reply sent. The ticket has been reopened."
					: "Reply sent.",
			});
		}

		/// <summary>
		/// Loads a ticket and refuses it if it is not the caller's.
		/// </summary>
		/// <remarks>
		/// One helper rather than the same check in each action; the copy that gets skipped is
		/// how another player's support history becomes readable.
		/// </remarks>
		private async Task<(SupportTicketData Ticket, IActionResult Failure)> ResolveOwnAsync(long id)
		{
			var result = await tickets.FetchAsync(id, includeInternal: false, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return (null, NotFound(new { error = "No such ticket." }));
			}

			if (!string.Equals(result.Data.ReporterAccount, User.Identity?.Name, StringComparison.OrdinalIgnoreCase))
			{
				// 404, not 403. See the remarks on this controller.
				log.LogWarning("'{Actor}' asked for ticket {Id}, which belongs to '{Owner}'.",
					User.Identity?.Name, id, result.Data.ReporterAccount);
				return (null, NotFound(new { error = "No such ticket." }));
			}
			return (result.Data, null);
		}

		/// <summary>A ticket as a player files it.</summary>
		public sealed class FileRequest
		{
			/// <summary>What it is about, as the numeric enum value.</summary>
			public int? Category { get; set; }

			/// <summary>One-line summary.</summary>
			public string Subject { get; set; } = "";

			/// <summary>The description.</summary>
			public string Body { get; set; } = "";

			/// <summary>The character being reported, for a player report.</summary>
			public string TargetCharacterName { get; set; }
		}

		/// <summary>A player's reply.</summary>
		public sealed class ReplyRequest
		{
			/// <summary>The text.</summary>
			public string Body { get; set; } = "";
		}
	}
}
