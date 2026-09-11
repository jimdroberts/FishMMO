using FishNet.Connection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Auth.Core;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;

/* Both FishMMO.Auth.Core and FishMMO.Database.Data.Enums declare an AccessLevel, and this file
 * needs types from both namespaces. The registration gate is the authentication one — the same
 * enum ChatHelper checks the character's own level against — so it is named here once rather
 * than qualified at every mention. */
using AccessLevel = FishMMO.Auth.Core.AccessLevel;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The player-facing support commands: <c>/report</c>, <c>/bug</c>, <c>/helpme</c> and
	/// <c>/tickets</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// All four are registered at <see cref="AccessLevel.Player"/>. They are player features, not
	/// staff tooling: the person who needs to report a cheater is by definition an ordinary player,
	/// and a support channel only a game master can reach is not a support channel.
	/// </para>
	/// <para>
	/// <b>Nothing here writes an audit row.</b> The operator audit log records elevated commands,
	/// and the hook at the access gate deliberately ignores anything at Player level. A player
	/// asking for help is not an operator action, and recording every one of them would bury the
	/// handful of rows that log exists for.
	/// </para>
	/// <para>
	/// <b>The subject is derived, the body is what they typed.</b> Nobody types two fields into a
	/// chat line, and a command that demands a subject and a body is a command players use once and
	/// then give up on — which shows up as fewer reports, not as fewer problems.
	/// </para>
	/// <para>
	/// <b>Every path answers.</b> A support command that appears to do nothing gets typed again,
	/// and four identical tickets is the visible result. Refusals are answered with the reason the
	/// database gave, verbatim where it is a player-facing one: the flood limit's message already
	/// says exactly why, and paraphrasing it here would be a second copy to keep in step.
	/// </para>
	/// </remarks>
	public partial class ChatSystem
	{
		/// <summary>How much of the message becomes the derived subject.</summary>
		/// <remarks>
		/// Long enough to be recognisable in a queue listing, short enough that the rest of the
		/// line still reads as a summary. The full text is always kept as the body, so nothing a
		/// player wrote is lost to this.
		/// </remarks>
		private const int SupportSubjectLength = 60;

		/// <summary>Hard cap on the description before it reaches the database.</summary>
		/// <remarks>
		/// The chat pipeline has already clamped the incoming text to <c>maxMessageLength</c>,
		/// which is itself clamped to <see cref="ChatBroadcast.MaxTextLength"/>. This is the
		/// second cap, held here so the bound on what this file sends does not depend on a
		/// serialized inspector field somebody may raise later.
		/// </remarks>
		private const int SupportBodyLength = 512;

		/// <summary>Longest prefix of player-typed text echoed back in a reply.</summary>
		/// <remarks>
		/// Replies whose length is driven by input push past
		/// <see cref="ChatBroadcast.MaxTextLength"/> and are then discarded by the client's own
		/// parser — which reads, to the player, as the command having done nothing.
		/// </remarks>
		private const int SupportEchoLength = 32;

		/// <summary>Most tickets <c>/tickets</c> will list before it reports a count instead.</summary>
		/// <remarks>
		/// The service refuses a sixth unfinished ticket per account, so five is the whole of what
		/// a player can normally have outstanding; the count is still reported when a page is cut.
		/// </remarks>
		private const int MaxTicketsListed = 5;

		/// <summary>The statuses <c>/tickets</c> considers still outstanding.</summary>
		/// <remarks>
		/// The same three the service's flood limit counts. A player who cannot file another ticket
		/// must be able to see exactly the tickets that are stopping them.
		/// </remarks>
		private static readonly SupportTicketStatus[] UnfinishedTicketStatuses =
		{
			SupportTicketStatus.Open,
			SupportTicketStatus.InProgress,
			SupportTicketStatus.AwaitingPlayer,
		};

		/// <summary>Registers the player support commands.</summary>
		/// <remarks>
		/// <c>/helpme</c>, not <c>/help</c>. Nothing registers <c>/help</c> today, but it is the
		/// obvious name for a command listing commands, and taking it here for a ticket filing
		/// would mean whoever writes that listing later either collides with support or has to
		/// rename a command players have learned.
		/// </remarks>
		private void RegisterSupportCommands()
		{
			ChatHelper.AddCommands(new Dictionary<string, ChatCommand>()
			{
				{ "/report", OnSupportReportCommand },
				{ "/bug", OnSupportBugCommand },
				{ "/helpme", OnSupportHelpCommand },
				{ "/tickets", OnSupportTicketsCommand },
			}, AccessLevel.Player);
		}

		/// <summary>Unregisters the player support commands.</summary>
		/// <remarks>
		/// <see cref="ChatHelper.Commands"/> is static and holds delegates bound to this
		/// <c>ScriptableObject</c>. It outlives a play-session restart in the editor while this
		/// object does not, so a command left registered runs against a destroyed instance.
		/// </remarks>
		private void UnregisterSupportCommands()
		{
			ChatHelper.RemoveCommands(new[] { "/report", "/bug", "/helpme", "/tickets" });
		}

		#region Filing

		/// <summary>
		/// <c>/report &lt;character name&gt; &lt;what happened&gt;</c> — files a player report.
		/// </summary>
		/// <remarks>
		/// The named character is resolved against this scene server's online mapping when it can
		/// be, which fills the account and character id staff actually act on. When it cannot be,
		/// the ticket is filed anyway with the typed name and a zero id: a player who logs off the
		/// moment they are reported must not thereby become unreportable, and the name is still
		/// enough for staff to find them. The reply says which of the two happened, so the reporter
		/// knows whether to expect the name to have been matched.
		/// </remarks>
		private bool OnSupportReportCommand(IPlayerCharacter character, ChatBroadcast msg)
		{
			if (character == null)
			{
				return true;
			}

			string remainder = (msg.Text ?? string.Empty).Trim();
			string targetName = ChatHelper.GetWordAndTrimmed(remainder, out string details);

			/* A single word leaves the whole remainder as the "trimmed" part — that is a name with
			 * nothing said about it, which staff can only close with "what did they do?". */
			if (string.IsNullOrWhiteSpace(targetName))
			{
				targetName = details;
				details = string.Empty;
			}

			if (string.IsNullOrWhiteSpace(targetName))
			{
				ReplySupport(character, "Name a player: /report <name> <what happened>");
				return true;
			}
			if (string.IsNullOrWhiteSpace(details))
			{
				ReplySupport(character, "Say what happened: /report <name> <what happened>");
				return true;
			}

			targetName = targetName.Trim();
			if (string.Equals(targetName, character.CharacterName, StringComparison.OrdinalIgnoreCase))
			{
				ReplySupport(character, "You cannot report yourself. Use /helpme to ask staff for help.");
				return true;
			}

			/* Resolved here, synchronously, and unpacked into plain values before anything is
			 * queued. Holding an IPlayerCharacter across an await is how a stale reference to a
			 * pooled instance — by then somebody else's character — ends up on a ticket. */
			string targetAccount = null;
			string resolvedName = targetName;
			long targetCharacterID = 0;
			bool resolved = false;

			if (TryGetOnlineCharacterMapping(out var mapping) &&
				mapping.CharactersByLowerCaseName.TryGetValue(targetName.ToLowerInvariant(), out IPlayerCharacter target) &&
				target != null)
			{
				targetAccount = target.Account;
				resolvedName = target.CharacterName;
				targetCharacterID = target.ID;
				resolved = true;
			}

			string shownName = Truncate(resolvedName, SupportEchoLength);
			ReplySupport(character, resolved
				? $"Reporting {shownName}, who is on this scene server."
				: $"Reporting {shownName}, who is not on this scene server; filing by name.");

			SubmitTicket(
				character,
				SupportTicketCategory.PlayerReport,
				$"Report: {Truncate(resolvedName, SupportSubjectLength)}",
				details,
				targetAccount,
				resolvedName,
				targetCharacterID);

			return true;
		}

		/// <summary><c>/bug &lt;what happened&gt;</c> — files a bug report.</summary>
		private bool OnSupportBugCommand(IPlayerCharacter character, ChatBroadcast msg)
		{
			return FileSimpleTicket(character, msg, SupportTicketCategory.Bug,
				"Describe the bug: /bug <what happened>");
		}

		/// <summary><c>/helpme &lt;what you need&gt;</c> — asks staff for help.</summary>
		private bool OnSupportHelpCommand(IPlayerCharacter character, ChatBroadcast msg)
		{
			return FileSimpleTicket(character, msg, SupportTicketCategory.Help,
				"Say what you need: /helpme <what you need>");
		}

		/// <summary>
		/// Files a ticket whose whole argument is the description.
		/// </summary>
		/// <remarks>
		/// <c>/bug</c> and <c>/helpme</c> differ only in category and in the line that tells an
		/// empty caller what to type. Two copies of this would be two places for the length bound
		/// or the derived subject to drift.
		/// </remarks>
		private bool FileSimpleTicket(IPlayerCharacter character, ChatBroadcast msg, SupportTicketCategory category, string usage)
		{
			if (character == null)
			{
				return true;
			}

			string body = (msg.Text ?? string.Empty).Trim();
			if (string.IsNullOrWhiteSpace(body))
			{
				ReplySupport(character, usage);
				return true;
			}

			SubmitTicket(character, category, Truncate(body, SupportSubjectLength), body,
				targetAccount: null, targetCharacterName: null, targetCharacterID: 0);
			return true;
		}

		/// <summary>
		/// Queues the create and answers the caller with its outcome.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Everything the ticket needs is copied off the character here, before the work is queued.
		/// The caller may have logged out, changed scene server or been despawned by the time the
		/// database returns, and the reply is therefore addressed by character id and resolved
		/// again on the main thread rather than by holding the object.
		/// </para>
		/// <para>
		/// A refusal is reported with the service's own message. It is written to be read by the
		/// player — the flood limit says how many tickets they already have open and what to do
		/// about it — and inventing a second wording here would mean a player told one thing by
		/// the game and another by the panel.
		/// </para>
		/// </remarks>
		private void SubmitTicket(
			IPlayerCharacter character, SupportTicketCategory category, string subject, string body,
			string targetAccount, string targetCharacterName, long targetCharacterID)
		{
			long characterID = character.ID;
			string account = character.Account;

			SupportTicketCreate ticket = new SupportTicketCreate()
			{
				ReporterAccount = account,
				ReporterCharacterName = character.CharacterName,
				ReporterCharacterID = characterID,
				Category = category,
				Subject = Truncate(subject, SupportSubjectLength),
				Body = Truncate(body, SupportBodyLength),
				TargetAccount = targetAccount,
				TargetCharacterName = targetCharacterName,
				TargetCharacterID = targetCharacterID,
				SceneName = character.SceneName,
			};

			if (!TryEnqueueAsyncWork(async () =>
			{
				string reply;
				try
				{
					if (!TryGetDbService(out ISupportTicketService ticketService))
					{
						reply = "Support is unavailable right now. Please try again shortly.";
					}
					else
					{
						DatabaseResult<long> result = await ticketService.CreateAsync(ticket);
						if (!result.IsSuccess)
						{
							/* The service's message, not ours: it says exactly why the filing was
							 * refused, and it was written to be shown to the player as-is. */
							reply = string.IsNullOrWhiteSpace(result.ErrorMessage)
								? "Your ticket could not be filed. Please try again shortly."
								: result.ErrorMessage;
						}
						else
						{
							reply = $"Ticket #{result.Data} filed. Staff will reply in game; check it with /tickets.";
						}
					}
				}
				catch (Exception ex)
				{
					await Log.Error("ChatSystem", $"Support ticket ({category}) failed for '{account}': {ex}");
					reply = "Your ticket could not be filed. Please try again shortly.";
				}

				string finalReply = reply;
				if (!TryEnqueueMainThread(() => ReplySupportByCharacterID(characterID, finalReply)))
				{
					await Log.Warning("ChatSystem",
						$"Support ticket ({category}) for '{account}' completed but the reply could not be queued.");
				}
			}, characterID))
			{
				ReplySupport(character, "The server is busy. Please try that again in a moment.");
			}
		}

		#endregion

		#region Listing

		/// <summary>
		/// <c>/tickets</c> — lists the caller's own unfinished tickets.
		/// </summary>
		/// <remarks>
		/// The caller's, by account, and never anybody else's: the account is taken from the
		/// character the server loaded, so there is no argument a player could point at somebody
		/// else's tickets with. Only the id, status, subject and age — this is how a player learns
		/// their ticket number and whether anybody has picked it up, and nothing in it should need
		/// a staff-visibility decision to be made correctly by this file.
		/// </remarks>
		private bool OnSupportTicketsCommand(IPlayerCharacter character, ChatBroadcast msg)
		{
			if (character == null)
			{
				return true;
			}

			long characterID = character.ID;
			string account = character.Account;

			if (!TryEnqueueAsyncWork(async () =>
			{
				List<string> lines = new List<string>();
				try
				{
					if (!TryGetDbService(out ISupportTicketService ticketService))
					{
						lines.Add("Support is unavailable right now. Please try again shortly.");
					}
					else
					{
						DatabaseResult<SupportTicketPage> result = await ticketService.SearchAsync(new SupportTicketQuery()
						{
							ReporterAccount = account,
							Statuses = UnfinishedTicketStatuses,
							Page = 1,
							PageSize = MaxTicketsListed,
						});

						if (!result.IsSuccess || result.Data == null)
						{
							lines.Add(string.IsNullOrWhiteSpace(result.ErrorMessage)
								? "Your tickets could not be read. Please try again shortly."
								: result.ErrorMessage);
						}
						else
						{
							BuildTicketLines(result.Data, lines);
						}
					}
				}
				catch (Exception ex)
				{
					await Log.Error("ChatSystem", $"Support ticket listing failed for '{account}': {ex}");
					lines.Add("Your tickets could not be read. Please try again shortly.");
				}

				List<string> finalLines = lines;
				if (!TryEnqueueMainThread(() =>
				{
					for (int i = 0; i < finalLines.Count; ++i)
					{
						ReplySupportByCharacterID(characterID, finalLines[i]);
					}
				}))
				{
					await Log.Warning("ChatSystem",
						$"Support ticket listing for '{account}' completed but the reply could not be queued.");
				}
			}, characterID))
			{
				ReplySupport(character, "The server is busy. Please try that again in a moment.");
			}

			return true;
		}

		/// <summary>
		/// Renders a page of tickets as chat lines.
		/// </summary>
		/// <remarks>
		/// One line per ticket, each built to stay inside <see cref="ChatBroadcast.MaxTextLength"/>
		/// — the subject is the only part whose length is not fixed, and it is truncated against
		/// what the rest of the line costs rather than against a guess.
		/// </remarks>
		private static void BuildTicketLines(SupportTicketPage page, List<string> lines)
		{
			var items = page.Items;
			if (items == null || items.Count < 1)
			{
				lines.Add("You have no open tickets. File one with /bug, /helpme or /report.");
				return;
			}

			lines.Add($"You have {page.TotalCount} open ticket(s):");

			for (int i = 0; i < items.Count; ++i)
			{
				SupportTicketData ticket = items[i];
				if (ticket == null)
				{
					continue;
				}

				string prefix = $"#{ticket.ID} [{ticket.Status}] ";
				string suffix = $" - {DescribeAge(ticket.LastActivityUtc)}";
				int room = ChatBroadcast.MaxTextLength - prefix.Length - suffix.Length;
				if (room < 1)
				{
					room = 1;
				}

				lines.Add(prefix + Truncate(ticket.Subject ?? string.Empty, room) + suffix);
			}

			if (page.TotalCount > items.Count)
			{
				lines.Add($"...and {page.TotalCount - items.Count} more.");
			}
		}

		/// <summary>Describes how long ago something happened, in one short phrase.</summary>
		/// <remarks>
		/// Relative, not a timestamp. A UTC clock time means nothing to a player in an unknown
		/// time zone, and "2h ago" answers the only question they are actually asking, which is
		/// whether anybody has looked at it lately.
		/// </remarks>
		private static string DescribeAge(DateTime utc)
		{
			if (utc == default)
			{
				return "unknown";
			}

			TimeSpan age = DateTime.UtcNow - utc;
			if (age.TotalMinutes < 1)
			{
				return "just now";
			}
			if (age.TotalHours < 1)
			{
				return $"{(int)age.TotalMinutes}m ago";
			}
			if (age.TotalDays < 1)
			{
				return $"{(int)age.TotalHours}h ago";
			}
			return $"{(int)age.TotalDays}d ago";
		}

		#endregion

		#region Helpers

		/// <summary>Resolves this scene server's online-character mapping.</summary>
		private bool TryGetOnlineCharacterMapping(out ICharacterMappingData<NetworkConnection> mapping)
		{
			mapping = null;
			return Server?.DataContainerRegistry.TryGet(out mapping) == true && mapping != null;
		}

		/// <summary>Sends a system-channel line to a character. Main thread only.</summary>
		private void ReplySupport(IPlayerCharacter character, string text)
		{
			NetworkConnection conn = character?.Owner;
			if (conn == null || !conn.IsActive)
			{
				return;
			}

			/* Clamped rather than split. Every line in this file is written to fit; the clamp is
			 * the backstop for the one part that is not ours — a message from the database — and
			 * a message the client silently discards for length reads as no answer at all. */
			OnSendSystemMessage(conn, Truncate(text, ChatBroadcast.MaxTextLength));
		}

		/// <summary>
		/// Sends a system-channel line to a character resolved by id.
		/// </summary>
		/// <remarks>
		/// Resolved by id rather than by holding the character across the await: the player may
		/// have logged out, changed scene server or been despawned while the database work ran,
		/// and the object would then be a stale reference to a pooled instance now belonging to
		/// somebody else. A player who has gone simply is not told; the ticket is already filed.
		/// </remarks>
		private void ReplySupportByCharacterID(long characterID, string text)
		{
			if (!TryGetOnlineCharacterMapping(out var mapping) ||
				!mapping.CharactersByID.TryGetValue(characterID, out IPlayerCharacter character))
			{
				return;
			}
			ReplySupport(character, text);
		}

		/// <summary>Cuts text to a length, marking it when anything was removed.</summary>
		private static string Truncate(string text, int maxLength)
		{
			if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
			{
				return text;
			}
			if (maxLength <= 3)
			{
				return text.Substring(0, maxLength);
			}
			return text.Substring(0, maxLength - 3) + "...";
		}

		#endregion
	}
}
