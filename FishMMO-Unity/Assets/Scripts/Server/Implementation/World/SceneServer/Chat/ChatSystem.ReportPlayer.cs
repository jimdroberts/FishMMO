using FishNet.Connection;
using System;
using System.Collections.Generic;
using FishMMO.Database.Data.Enums;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using Channel = FishNet.Transporting.Channel;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The report panel's server half: a <see cref="ReportPlayerBroadcast"/> filed through the same
	/// <c>SubmitTicket</c> as <c>/report</c>, answered with a <see cref="ReportPlayerResultBroadcast"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One filing body.</b> Everything that touches the database — the copy off the character,
	/// the queued create, the service's refusal text — is <c>SubmitTicket</c> in
	/// <c>ChatSystem.SupportCommands.cs</c>. This file only validates the request, resolves the
	/// target and chooses where the answer goes. A second copy of the filing would be a second
	/// place for the length bounds and the reply wording to drift apart, and the player would be
	/// told one thing by the chat line and another by the panel.
	/// </para>
	/// <para>
	/// <b>No CanAct gate.</b> A report is not an action in the world, and the player who most needs
	/// to file one is frequently dead, stunned or rooted — by the person they are reporting. Gating
	/// it on <c>CanAct</c> would make the report unavailable at exactly the moment it is wanted, and
	/// nothing the request leads to changes game state, so there is no later gate that needs to
	/// cover it.
	/// </para>
	/// <para>
	/// <b>Every path answers</b>, as in the chat commands, and for the same reason: a Submit that
	/// appears to do nothing gets pressed again.
	/// </para>
	/// </remarks>
	public partial class ChatSystem
	{
		/// <summary>Least time between two panel reports from one account.</summary>
		/// <remarks>
		/// The support ticket service is the real limit — its cooldown is enforced inside the insert
		/// transaction and covers <c>/report</c> too. This is the guard in front of it, so that a
		/// client repeating the broadcast is answered from memory rather than by a database round
		/// trip each time. Keyed by account rather than connection, because reconnecting is cheap
		/// and would otherwise reset it.
		/// </remarks>
		private static readonly long ReportPlayerIntervalTicks = TimeSpan.FromSeconds(60).Ticks;

		/// <summary>Most accounts the report throttle remembers before it starts again.</summary>
		private const int MaxReportThrottleEntries = 4096;

		/// <summary>When each account may next file a report from the panel, in UTC ticks.</summary>
		private readonly Dictionary<string, long> reportPlayerNextTicks =
			new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Registers the report panel's request.</summary>
		private void RegisterReportPlayerBroadcast()
		{
			Server.NetworkWrapper.RegisterBroadcast<ReportPlayerBroadcast>(OnServerReportPlayerBroadcastReceived, true);
		}

		/// <summary>Unregisters the report panel's request and forgets the throttle.</summary>
		/// <remarks>
		/// The throttle lives on this <c>ScriptableObject</c>, which outlives a play session in the
		/// editor; left alone it would carry one session's cooldowns into the next.
		/// </remarks>
		private void UnregisterReportPlayerBroadcast()
		{
			if (Server?.NetworkWrapper != null)
			{
				Server.NetworkWrapper.UnregisterBroadcast<ReportPlayerBroadcast>(OnServerReportPlayerBroadcastReceived);
			}
			reportPlayerNextTicks.Clear();
		}

		/// <summary>
		/// Files a player report sent from the report panel.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The target is resolved by id against this scene server's online mapping, which fills the
		/// account and canonical name staff act on. When the id does not resolve, the typed name is
		/// tried against the same mapping and then, failing that, filed as typed with a zero id —
		/// the same fallback as <c>/report</c>, so a player who logs off the moment they are reported
		/// does not thereby become unreportable.
		/// </para>
		/// <para>
		/// The throttle is taken only once the request is known to be fileable. A refusal for a
		/// missing reason or an empty description costs the server nothing, and charging the
		/// player's cooldown for one would leave them unable to file the corrected report.
		/// </para>
		/// </remarks>
		private void OnServerReportPlayerBroadcastReceived(NetworkConnection conn, ReportPlayerBroadcast msg, Channel channel)
		{
			/* SkipCanAct: a dead or stunned player must be able to report whoever did it. See the
			 * class remarks; nothing downstream changes game state, so nothing needs to gate it. */
			if (!TryBeginPlayerRequest(conn, out PlayerRequestContext request, PlayerRequestGate.SkipCanAct))
			{
				return;
			}

			IPlayerCharacter character = request.Character;

			if (!PlayerReportReasons.IsDefined(msg.Reason))
			{
				AnswerReport(conn, false, 0, "Choose a reason for the report.");
				return;
			}

			string description = ClampReportText(msg.Description, ReportPlayerBroadcast.MaxDescriptionLength);
			if (description.Length < 1)
			{
				AnswerReport(conn, false, 0, "Say what happened.");
				return;
			}

			/* Resolved here, synchronously, and unpacked into plain values before anything is
			 * queued — the same rule as /report, for the same pooled-instance reason. */
			string typedName = ClampReportText(msg.TargetCharacterName, ReportPlayerBroadcast.MaxTargetNameLength);
			string targetAccount = null;
			string resolvedName = typedName;
			long targetCharacterID = 0;

			if (TryGetOnlineCharacterMapping(out var mapping))
			{
				IPlayerCharacter target = null;
				if (msg.TargetCharacterID > 0)
				{
					mapping.CharactersByID.TryGetValue(msg.TargetCharacterID, out target);
				}
				if (target == null && typedName.Length > 0)
				{
					mapping.CharactersByLowerCaseName.TryGetValue(typedName.ToLowerInvariant(), out target);
				}
				if (target != null)
				{
					targetAccount = target.Account;
					resolvedName = target.CharacterName ?? typedName;
					targetCharacterID = target.ID;
				}
			}

			if (string.IsNullOrWhiteSpace(resolvedName))
			{
				AnswerReport(conn, false, 0, "Name a player to report.");
				return;
			}

			if (targetCharacterID == character.ID ||
				string.Equals(resolvedName, character.CharacterName, StringComparison.OrdinalIgnoreCase))
			{
				AnswerReport(conn, false, 0, "You cannot report yourself. Use /helpme to ask staff for help.");
				return;
			}

			string account = character.Account ?? string.Empty;
			long now = DateTime.UtcNow.Ticks;
			if (reportPlayerNextTicks.TryGetValue(account, out long next) && next > now)
			{
				long seconds = Math.Max(1, (long)Math.Ceiling(TimeSpan.FromTicks(next - now).TotalSeconds));
				AnswerReport(conn, false, 0, $"You filed a report moments ago. Please wait {seconds}s before filing another.");
				return;
			}
			if (reportPlayerNextTicks.Count >= MaxReportThrottleEntries)
			{
				reportPlayerNextTicks.Clear();
			}
			/* Stamped before the work is queued, not when it completes: a second request arriving
			 * while the first is still in the database is exactly the one this exists to stop. */
			reportPlayerNextTicks[account] = now + ReportPlayerIntervalTicks;

			/* A report the server was too busy to queue is not a report filed: the player was told to
			 * try again in a moment, so the moment must not be sixty seconds. */
			if (!SubmitTicket(
				character,
				SupportTicketCategory.PlayerReport,
				BuildReportSubject(resolvedName, msg.Reason),
				description,
				targetAccount,
				resolvedName,
				targetCharacterID,
				AnswerReportByCharacterID))
			{
				reportPlayerNextTicks.Remove(account);
			}
		}

		/// <summary>
		/// Builds <c>Report: {name} ({reason})</c> within the subject bound.
		/// </summary>
		/// <remarks>
		/// The name is the part that gives way. <c>SubmitTicket</c> truncates the whole subject from
		/// the end, which on a long name would cut off the reason — the half of the subject a
		/// moderator sorting the queue actually reads.
		/// </remarks>
		private static string BuildReportSubject(string name, PlayerReportReason reason)
		{
			const string prefix = "Report: ";
			string suffix = $" ({PlayerReportReasons.DisplayName(reason)})";
			int room = Math.Max(1, SupportSubjectLength - prefix.Length - suffix.Length);
			return prefix + Truncate(name, room) + suffix;
		}

		/// <summary>Trims player-supplied text and cuts it to a hard bound.</summary>
		/// <remarks>
		/// Cut, not marked with an ellipsis: the panel already stops typing at the bound, so anything
		/// longer is a client that ignored it, and its text is not owed a tidy ending.
		/// </remarks>
		private static string ClampReportText(string text, int maxLength)
		{
			if (string.IsNullOrEmpty(text))
			{
				return string.Empty;
			}
			string value = text.Length > maxLength ? text.Substring(0, maxLength) : text;
			return value.Trim();
		}

		/// <summary>Sends the panel its answer. Main thread only.</summary>
		private void AnswerReport(NetworkConnection conn, bool filed, long ticketID, string message)
		{
			if (conn == null || !conn.IsActive)
			{
				return;
			}

			Server.NetworkWrapper.Broadcast(conn, new ReportPlayerResultBroadcast()
			{
				Filed = filed,
				TicketID = filed ? ticketID : 0,
				Message = Truncate(message ?? string.Empty, ReportPlayerResultBroadcast.MaxMessageLength),
			}, true, Channel.Reliable);
		}

		/// <summary>
		/// Sends the panel its answer once the database work has finished.
		/// </summary>
		/// <remarks>
		/// Resolved by id for the reason <c>ReplySupportByCharacterID</c> is: the reporter may have
		/// gone while the create ran. A reporter who has gone is not told; the ticket is filed.
		/// </remarks>
		private void AnswerReportByCharacterID(long characterID, bool filed, long ticketID, string message)
		{
			if (!TryGetOnlineCharacterMapping(out var mapping) ||
				!mapping.CharactersByID.TryGetValue(characterID, out IPlayerCharacter character) ||
				character == null)
			{
				return;
			}
			AnswerReport(character.Owner, filed, ticketID, message);
		}
	}
}
