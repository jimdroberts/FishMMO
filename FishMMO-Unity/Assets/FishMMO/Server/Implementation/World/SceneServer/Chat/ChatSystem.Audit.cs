using System;
using System.Threading.Tasks;
using FishMMO.Auth.Core;
using FishMMO.Database.Data;
using FishMMO.Database;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Records elevated chat commands to the operator audit log.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The same <c>admin_audit_log</c> table the Control Panel writes to, on purpose. "Show me
	/// everything this person did" must not be two queries against two logs with two shapes, and
	/// an operator who cannot do something in the panel and then does it from a chat command
	/// must not thereby leave a gap in the record.
	/// </para>
	/// <para>
	/// <b>The actor is the account, not the character.</b> A person is an account; the character
	/// they happened to be standing in is a detail of the moment, and recording that as the
	/// actor would split one operator's history across every character they own.
	/// </para>
	/// <para>
	/// Subscribed to <see cref="ChatHelper.OnElevatedCommand"/>, which fires at the single access
	/// gate. Nothing here enumerates commands, so a command added later is recorded because it is
	/// registered with an elevated access level — not because somebody remembered to add it to a
	/// list.
	/// </para>
	/// </remarks>
	public partial class ChatSystem
	{
		/// <summary>Action identifier for a command that ran.</summary>
		private const string AuditActionCommand = "game.command";

		/// <summary>Action identifier for a command refused for want of access.</summary>
		private const string AuditActionCommandRefused = "game.command.refused";

		/// <summary>Marks these rows as coming from in-game rather than the panel.</summary>
		private const string AuditSourceGame = "game";

		/// <summary>
		/// Records an elevated command that was allowed to run.
		/// </summary>
		/// <remarks>
		/// Recorded as attempted, not as succeeded-and-completed: the gate fires before the
		/// handler, so this says the operator ran the command and what they typed. Whether the
		/// underlying database write then worked is the handler's own business, and the handler
		/// answers the operator directly. Recording only completed commands would omit exactly
		/// the ones worth reading about — the one that threw, or the one that shut the world down.
		/// </remarks>
		private void ChatHelper_OnElevatedCommand(IPlayerCharacter sender, string command, string arguments, AccessLevel required)
		{
			WriteCommandAudit(sender, command, arguments, required, succeeded: true, outcome: null);
		}

		/// <summary>
		/// Records an elevated command that was refused.
		/// </summary>
		/// <remarks>
		/// A player probing for admin command names produces a run of these against one account,
		/// which is the pattern the log exists to make visible. The sender is told nothing, by
		/// design; this row and the warning log line are the only traces.
		/// </remarks>
		private void ChatHelper_OnElevatedCommandRefused(IPlayerCharacter sender, string command, AccessLevel required)
		{
			/* Only elevated commands. OnCommandRefused also fires for ordinary ones — a banned
			 * character typing /say is refused through the same path — and recording those would
			 * bury the handful of rows that matter under routine noise. */
			if (required <= AccessLevel.Player)
			{
				return;
			}

			WriteCommandAudit(sender, command, arguments: null, required,
				succeeded: false, outcome: $"Refused: requires {required}, has {sender?.AccessLevel}.");
		}

		/// <summary>
		/// Queues the audit write off the main thread.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Everything needed is copied out of the character here, synchronously, before the work
		/// is queued. Holding the character across an await is how a stale reference to a pooled
		/// instance — by then belonging to somebody else — ends up in the record.
		/// </para>
		/// <para>
		/// A failed write never affects the command. The command has already been allowed by the
		/// time this runs, and refusing it because the log was unreachable would make the audit
		/// log an availability dependency of the game's admin tooling. The failure is logged as
		/// an error instead: a shard that cannot record what its operators do needs attention.
		/// </para>
		/// </remarks>
		private void WriteCommandAudit(
			IPlayerCharacter sender, string command, string arguments, AccessLevel required,
			bool succeeded, string outcome)
		{
			if (sender == null)
			{
				return;
			}

			string account = sender.Account;
			string characterName = sender.CharacterName;
			long characterID = sender.ID;
			byte accessLevel = (byte)sender.AccessLevel;
			string action = succeeded ? AuditActionCommand : AuditActionCommandRefused;

			/* The full command line is the reason, because for a chat command there is nothing
			 * else: nobody types a justification into /admin shutdown. What they typed is the
			 * most honest record available of what they intended. */
			string typed = string.IsNullOrWhiteSpace(arguments) ? command : $"{command} {arguments.Trim()}";

			if (!TryEnqueueAsyncWork(async () =>
			{
				try
				{
					if (!TryGetDbService(out IAdminAuditService auditService))
					{
						await Log.Error("ChatSystem",
							$"AUDIT WRITE FAILED for '{typed}' by '{account}': the audit service is unavailable.");
						return;
					}

					DatabaseResult<long> result = await auditService.AppendAsync(new AdminAuditData()
					{
						OccurredUtc = DateTime.UtcNow,
						ActorName = account,
						ActorAccessLevel = accessLevel,
						ActorSessionID = null,
						Action = action,
						TargetType = "command",
						TargetID = command,
						TargetName = characterName,
						Reason = typed,
						Succeeded = succeeded,
						Outcome = outcome,
						Details = BuildDetails(characterName, characterID, required),
						IpAddress = null,
						Source = AuditSourceGame,
					});

					if (!result.IsSuccess)
					{
						await Log.Error("ChatSystem",
							$"AUDIT WRITE FAILED for '{typed}' by '{account}': [{result.ErrorCode}] {result.ErrorMessage}");
					}
				}
				catch (Exception ex)
				{
					await Log.Error("ChatSystem", $"AUDIT WRITE FAILED for '{typed}' by '{account}': {ex}");
				}
			}))
			{
				Log.Error("ChatSystem",
					$"AUDIT WRITE DROPPED for '{typed}' by '{account}': the server could not queue the write.");
			}
		}

		/// <summary>
		/// The character context, as compact JSON.
		/// </summary>
		/// <remarks>
		/// Hand-built rather than serialized: this runs on the server's work queue for every
		/// elevated command, the shape is three fields, and the values are a name, a number and
		/// an enum. The name is the only one that could carry a quote, and it is escaped.
		/// </remarks>
		private static string BuildDetails(string characterName, long characterID, AccessLevel required)
		{
			string safeName = (characterName ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
			return $"{{\"character\":\"{safeName}\",\"characterId\":{characterID},\"requires\":\"{required}\"}}";
		}
	}
}
