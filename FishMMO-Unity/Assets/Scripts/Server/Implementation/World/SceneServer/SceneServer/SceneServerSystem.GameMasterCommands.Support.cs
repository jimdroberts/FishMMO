using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// <c>/gm</c> support: reading the ticket queue and working tickets without leaving the game.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The same service calls, and the same rules, as the Control Panel's ticket queue: taking a
	/// ticket moves it to InProgress, a resolution is required to resolve one, and a staff note is
	/// the same message row as a reply with its internal flag set — so a note written here is as
	/// invisible to the player as one written in the panel.
	/// </para>
	/// <para>
	/// <b>Reply, note and resolution text is withheld from the audit log.</b> The row records that
	/// the operator wrote to the ticket and how much; the text itself lives on the ticket. A staff
	/// reply can carry a player's personal details, and the audit log is read by more people and
	/// kept longer than the ticket — the panel withholds it for the same reason.
	/// </para>
	/// </remarks>
	public partial class SceneServerSystem
	{
		/// <summary>Most tickets <c>/gm tickets</c> lists in chat. The staff console pages the rest.</summary>
		private const int MaxTicketsListedInChat = 8;

		/// <summary>The statuses a staff queue considers unfinished.</summary>
		private static readonly SupportTicketStatus[] StaffUnfinishedTicketStatuses =
		{
			SupportTicketStatus.Open,
			SupportTicketStatus.InProgress,
			SupportTicketStatus.AwaitingPlayer,
		};

		/// <summary>The support part of the <c>/gm</c> table.</summary>
		private IEnumerable<OperatorCommand> BuildGameMasterSupportCommands()
		{
			return new List<OperatorCommand>()
			{
				new OperatorCommand
				{
					Name = "tickets", Category = "Support",
					Summary = "Lists unfinished support tickets: unassigned by default, or yours, or all.",
					Arguments = "filter:Choice=unassigned,mine,all?",
					Run = ReportTickets,
				},
				new OperatorCommand
				{
					Name = "ticket", Category = "Support",
					Summary = "Shows a ticket: who filed it, what they wrote, and its latest messages and notes.",
					Arguments = "ticket:Ticket", TicketAction = true,
					Run = ReportTicket,
				},
				new OperatorCommand
				{
					Name = "claim", Category = "Support",
					Summary = "Assigns a ticket to you and marks it in progress.",
					Arguments = "ticket:Ticket", TicketAction = true,
					Run = (c, a) => AssignTicket(c, a, claim: true),
				},
				new OperatorCommand
				{
					Name = "unclaim", Category = "Support",
					Summary = "Hands a ticket back to the queue.",
					Arguments = "ticket:Ticket", TicketAction = true,
					Run = (c, a) => AssignTicket(c, a, claim: false),
				},
				new OperatorCommand
				{
					Name = "reply", Category = "Support",
					Summary = "Replies on a ticket. The player sees it.",
					Arguments = "ticket:Ticket;message:Text", TicketAction = true,
					Run = (c, a) => AppendTicketMessage(c, a, internalNote: false),
				},
				new OperatorCommand
				{
					Name = "note", Category = "Support",
					Summary = "Adds a staff-only note to a ticket. The player never sees it.",
					Arguments = "ticket:Ticket;note:Text", TicketAction = true,
					Run = (c, a) => AppendTicketMessage(c, a, internalNote: true),
				},
				new OperatorCommand
				{
					Name = "resolve", Category = "Support",
					Summary = "Resolves a ticket, recording what was decided. A reply from the player reopens it.",
					Arguments = "ticket:Ticket;resolution:Text", TicketAction = true,
					Run = ResolveTicket,
				},
			};
		}

		/// <summary>
		/// Rewrites <c>/gm</c> argument text for the audit log, withholding ticket message bodies.
		/// </summary>
		/// <remarks>Installed at the access gate; see <see cref="ChatHelper.SetAuditRedactor"/>.</remarks>
		private static string RedactGameMasterAudit(string arguments)
		{
			string word = OperatorCommandParsing.SplitFirstWord(arguments, out string rest);
			if (word.Equals("reply", StringComparison.OrdinalIgnoreCase) ||
				word.Equals("note", StringComparison.OrdinalIgnoreCase) ||
				word.Equals("resolve", StringComparison.OrdinalIgnoreCase))
			{
				string ticket = OperatorCommandParsing.SplitFirstWord(rest, out string body);
				return $"{word} {ticket} [text withheld, {body.Length} characters]";
			}
			return arguments ?? string.Empty;
		}

		/// <summary>Lists unfinished tickets in chat.</summary>
		private void ReportTickets(IPlayerCharacter character, string arguments)
		{
			string word = OperatorCommandParsing.SplitFirstWord(arguments, out _).ToLowerInvariant();
			StaffTicketFilter filter;
			switch (word)
			{
				case "":
				case "unassigned": filter = StaffTicketFilter.Unassigned; break;
				case "mine": filter = StaffTicketFilter.Mine; break;
				case "all": filter = StaffTicketFilter.AllOpen; break;
				default:
					ReplyUsage(character, gameMasterCommands, "tickets");
					return;
			}

			string account = character.Account;
			RunOperatorLines(character, async () =>
			{
				if (!TryGetDbService(out ISupportTicketService tickets))
				{
					return new[] { "Support tickets are unavailable." };
				}

				DatabaseResult<SupportTicketPage> result = await tickets.SearchAsync(BuildStaffTicketQuery(filter, account, 1, MaxTicketsListedInChat));
				if (!result.IsSuccess || result.Data == null)
				{
					return new[] { $"The ticket queue could not be read: {result.ErrorMessage}" };
				}

				var items = result.Data.Items ?? Array.Empty<SupportTicketData>();
				var lines = new List<string>() { $"{result.Data.TotalCount} {DescribeTicketFilter(filter)} ticket(s)." };
				foreach (SupportTicketData ticket in items.Where(t => t != null))
				{
					lines.Add(OperatorCommandParsing.Truncate(
						$"#{ticket.ID} [{ticket.Status}] {ticket.Category}: {ticket.Subject} ({ticket.ReporterAccount}, {DescribeSince(ticket.LastActivityUtc)})",
						OperatorLineLength));
				}
				if (result.Data.TotalCount > items.Count)
				{
					lines.Add($"...and {result.Data.TotalCount - items.Count} more. /gm console lists them all.");
				}
				return lines;
			});
		}

		/// <summary>Shows one ticket in chat, internal notes included.</summary>
		private void ReportTicket(IPlayerCharacter character, string arguments)
		{
			if (!TryParseTicketArgument(character, arguments, "ticket", out long ticketID, out _))
			{
				return;
			}

			RunOperatorLines(character, async () =>
			{
				if (!TryGetDbService(out ISupportTicketService tickets))
				{
					return new[] { "Support tickets are unavailable." };
				}

				DatabaseResult<SupportTicketData> result = await tickets.FetchAsync(ticketID, includeInternal: true);
				if (!result.IsSuccess || result.Data == null)
				{
					return new[] { $"No ticket #{ticketID}." };
				}

				SupportTicketData ticket = result.Data;
				var lines = new List<string>()
				{
					$"#{ticket.ID} [{ticket.Status}] {ticket.Category}, priority {ticket.Priority}, filed by {ticket.ReporterAccount} ({ticket.ReporterCharacterName}) {DescribeSince(ticket.CreatedUtc)}.",
					$"Assigned to {(string.IsNullOrEmpty(ticket.AssignedTo) ? "nobody" : ticket.AssignedTo)}" +
						(string.IsNullOrEmpty(ticket.TargetCharacterName) ? string.Empty : $"; reports {ticket.TargetCharacterName}") +
						$"; scene '{ticket.SceneName}'.",
					"Subject: " + ticket.Subject,
				};
				lines.AddRange(OperatorCommandParsing.PackLines(
					(ticket.Body ?? string.Empty).Split(' '), "| ", " ", OperatorLineLength).Take(4));
				if (!string.IsNullOrEmpty(ticket.Resolution))
				{
					lines.Add("Resolution: " + ticket.Resolution);
				}

				var messages = (ticket.Messages ?? Array.Empty<SupportTicketMessageData>())
					.Where(m => m != null)
					.OrderBy(m => m.CreatedUtc)
					.ToList();
				foreach (SupportTicketMessageData message in messages.Skip(Math.Max(0, messages.Count - 3)))
				{
					lines.Add($"{(message.Internal ? "[staff note] " : string.Empty)}{message.AuthorAccount}: {message.Body}");
				}
				if (messages.Count > 3)
				{
					lines.Add($"{messages.Count - 3} earlier message(s) are in /gm console.");
				}
				return lines;
			});
		}

		/// <summary>Takes a ticket, or hands it back to the queue.</summary>
		private void AssignTicket(IPlayerCharacter character, string arguments, bool claim)
		{
			if (!TryParseTicketArgument(character, arguments, claim ? "claim" : "unclaim", out long ticketID, out _))
			{
				return;
			}

			string account = character.Account;
			RunOperatorAction(character, async () =>
			{
				if (!TryGetDbService(out ISupportTicketService tickets))
				{
					return "Support tickets are unavailable.";
				}

				DatabaseResult result = await tickets.AssignAsync(ticketID, claim ? account : null);
				if (!result.IsSuccess)
				{
					return $"Ticket #{ticketID} could not be {(claim ? "assigned" : "returned")}: {result.ErrorMessage}";
				}

				await Log.Info("SceneServerSystem", $"Ticket {ticketID} {(claim ? "claimed by" : "returned to the queue by")} '{account}'.");
				return claim ? $"Ticket #{ticketID} is yours and in progress." : $"Ticket #{ticketID} is back in the queue.";
			});
		}

		/// <summary>Adds a player-visible reply or a staff-only note to a ticket.</summary>
		private void AppendTicketMessage(IPlayerCharacter character, string arguments, bool internalNote)
		{
			if (!TryParseTicketArgument(character, arguments, internalNote ? "note" : "reply", out long ticketID, out string body))
			{
				return;
			}
			if (body.Length == 0)
			{
				ReplyUsage(character, gameMasterCommands, internalNote ? "note" : "reply");
				return;
			}

			string account = character.Account;
			RunOperatorAction(character, async () =>
			{
				if (!TryGetDbService(out ISupportTicketService tickets))
				{
					return "Support tickets are unavailable.";
				}

				DatabaseResult<long> result = await tickets.AppendMessageAsync(ticketID, account, authorIsStaff: true, internalNote: internalNote, body);
				if (!result.IsSuccess)
				{
					return $"Ticket #{ticketID} could not be written to: {result.ErrorMessage}";
				}

				return internalNote
					? $"Note added to #{ticketID}. The player cannot see it."
					: $"Reply added to #{ticketID}. The player sees it.";
			});
		}

		/// <summary>Resolves a ticket with a recorded resolution.</summary>
		private void ResolveTicket(IPlayerCharacter character, string arguments)
		{
			if (!TryParseTicketArgument(character, arguments, "resolve", out long ticketID, out string resolution))
			{
				return;
			}
			if (resolution.Length == 0)
			{
				ReplyUsage(character, gameMasterCommands, "resolve");
				return;
			}

			string account = character.Account;
			RunOperatorAction(character, async () =>
			{
				if (!TryGetDbService(out ISupportTicketService tickets))
				{
					return "Support tickets are unavailable.";
				}

				DatabaseResult result = await tickets.SetStatusAsync(ticketID, SupportTicketStatus.Resolved, account, resolution);
				if (!result.IsSuccess)
				{
					return $"Ticket #{ticketID} could not be resolved: {result.ErrorMessage}";
				}

				await Log.Info("SceneServerSystem", $"Ticket {ticketID} resolved by '{account}'.");
				return $"Ticket #{ticketID} is resolved.";
			});
		}

		/// <summary>Parses the leading ticket number, answering the caller when it is missing or malformed.</summary>
		private bool TryParseTicketArgument(IPlayerCharacter character, string arguments, string command, out long ticketID, out string rest)
		{
			string word = OperatorCommandParsing.SplitFirstWord(arguments, out rest);
			if (!OperatorCommandParsing.TryParseTicketID(word, out ticketID))
			{
				ReplyUsage(character, gameMasterCommands, command);
				return false;
			}
			return true;
		}

		/// <summary>The ticket search a staff filter stands for.</summary>
		private static SupportTicketQuery BuildStaffTicketQuery(StaffTicketFilter filter, string account, int page, int pageSize)
		{
			return new SupportTicketQuery()
			{
				Statuses = StaffUnfinishedTicketStatuses,
				UnassignedOnly = filter == StaffTicketFilter.Unassigned,
				AssignedTo = filter == StaffTicketFilter.Mine ? account : null,
				Page = page,
				PageSize = pageSize,
			};
		}

		/// <summary>The word a filter is described by.</summary>
		private static string DescribeTicketFilter(StaffTicketFilter filter)
		{
			switch (filter)
			{
				case StaffTicketFilter.Mine: return "unfinished assigned-to-you";
				case StaffTicketFilter.AllOpen: return "unfinished";
				default: return "unassigned unfinished";
			}
		}
	}
}
