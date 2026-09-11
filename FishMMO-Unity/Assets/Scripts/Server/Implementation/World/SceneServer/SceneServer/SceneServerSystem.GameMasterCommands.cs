using FishNet.Connection;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using FishMMO.Auth.Core;
using FishMMO.Database;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The in-game <c>/gm</c> command set: finding players, moving to them, and removing them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Registered once, as <c>/gm</c>, at <see cref="AccessLevel.GameMaster"/>. The sub-command
	/// is the first word of the remainder, so one registration and therefore one access check
	/// covers every operation — the same shape as <c>/admin</c>, and for the same reason: there
	/// is no way to add a sub-command that forgets to be gated.
	/// </para>
	/// <para>
	/// Every command here is recorded in the operator audit log, including refusals, by the
	/// chat system's subscription to the access gate. Nothing in this file writes an audit row,
	/// and nothing in this file should: a command audited because its author remembered is a
	/// command that stops being audited the day somebody forgets.
	/// </para>
	/// <para>
	/// <b>What is deliberately not here.</b> Nothing that creates or destroys a character, and
	/// nothing that grants access. Moving a player and disconnecting them are reversible;
	/// deletion is not, and promotion belongs to <c>/admin</c> where the extra level is the
	/// point.
	/// </para>
	/// </remarks>
	public partial class SceneServerSystem
	{
		/// <summary>Most characters <c>/gm who</c> will name before it stops listing.</summary>
		/// <remarks>
		/// A populated scene server holds far more than a chat window can show, and a reply per
		/// character would flood the operator out of their own log. The count is always reported
		/// even when the list is cut.
		/// </remarks>
		private const int MaxWhoListed = 20;

		/// <summary>Registers the <c>/gm</c> command.</summary>
		private void RegisterGameMasterCommands()
		{
			ChatHelper.AddCommands(new Dictionary<string, ChatCommand>()
			{
				{ "/gm", OnGameMasterCommand },
			}, AccessLevel.GameMaster);
		}

		/// <summary>Unregisters the <c>/gm</c> command.</summary>
		private void UnregisterGameMasterCommands()
		{
			ChatHelper.RemoveCommands(new[] { "/gm" });
		}

		/// <summary>
		/// Dispatches a <c>/gm</c> sub-command.
		/// </summary>
		/// <remarks>
		/// Access has already been checked by <see cref="ChatHelper.TryParseCommand"/> against
		/// the registration above; reaching this method means the caller is at least a game
		/// master.
		/// </remarks>
		private bool OnGameMasterCommand(IPlayerCharacter character, ChatBroadcast msg)
		{
			if (character == null)
			{
				return true;
			}

			string remainder = msg.Text ?? string.Empty;
			string subCommand = ChatHelper.GetWordAndTrimmed(remainder, out string arguments);

			// A single-word sub-command leaves the whole remainder as the "trimmed" part.
			if (string.IsNullOrWhiteSpace(subCommand))
			{
				subCommand = arguments;
				arguments = string.Empty;
			}

			switch (subCommand.Trim().ToLowerInvariant())
			{
				case "who":
				case "online":
					ReportWho(character);
					return true;

				case "where":
				case "find":
					ReportWhere(character, arguments);
					return true;

				case "info":
					ReportCharacterInfo(character, arguments);
					return true;

				case "goto":
				case "tp":
					GoToCharacter(character, arguments);
					return true;

				case "summon":
				case "bring":
					SummonCharacter(character, arguments);
					return true;

				case "kick":
					KickCharacter(character, arguments);
					return true;

				default:
					/* Split across replies to stay inside ChatBroadcast.MaxTextLength (128), the
					 * same constraint the admin help text is written to. */
					Reply(character, "GM: /gm who | where <name> | info <name>");
					Reply(character, "GM: /gm goto <name> | summon <name> | kick <name>");
					return true;
			}
		}

		#region Lookups

		/// <summary>Lists the characters this scene server currently holds.</summary>
		private void ReportWho(IPlayerCharacter character)
		{
			if (!TryGetOnlineCharacters(out var mapping))
			{
				Reply(character, "The character mapping is unavailable.");
				return;
			}

			var names = mapping.CharactersByID.Values
				.Where(c => c != null)
				.Select(c => c.CharacterName)
				.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
				.ToList();

			Reply(character, $"{names.Count} character(s) on this scene server.");
			if (names.Count == 0)
			{
				return;
			}

			/* Batched into lines rather than one reply per name: twenty replies is twenty
			 * broadcasts and scrolls the operator's own chat away. */
			foreach (string line in BatchNames(names.Take(MaxWhoListed)))
			{
				Reply(character, line);
			}
			if (names.Count > MaxWhoListed)
			{
				Reply(character, $"...and {names.Count - MaxWhoListed} more. Narrow it with /gm where <name>.");
			}
		}

		/// <summary>Reports where a character is, if this scene server holds them.</summary>
		private void ReportWhere(IPlayerCharacter character, string arguments)
		{
			if (!TryResolveTarget(character, arguments, out IPlayerCharacter target))
			{
				return;
			}

			Vector3 position = target.Transform != null ? target.Transform.position : Vector3.zero;
			Reply(character, $"{target.CharacterName} is in '{target.SceneName}' at " +
				$"{position.x.ToString("0.0", CultureInfo.InvariantCulture)}, " +
				$"{position.y.ToString("0.0", CultureInfo.InvariantCulture)}, " +
				$"{position.z.ToString("0.0", CultureInfo.InvariantCulture)}.");
		}

		/// <summary>Reports a character's account and standing.</summary>
		/// <remarks>
		/// Account name and access level, and nothing else. A game master needs to know who they
		/// are dealing with; they do not need the account's email address, and a command that
		/// prints it makes every game master a place that address can leak from.
		/// </remarks>
		private void ReportCharacterInfo(IPlayerCharacter character, string arguments)
		{
			if (!TryResolveTarget(character, arguments, out IPlayerCharacter target))
			{
				return;
			}

			Reply(character, $"{target.CharacterName} (id {target.ID}) on account '{target.Account}'.");
			Reply(character, $"Access {target.AccessLevel}, scene '{target.SceneName}', world {target.WorldServerID}.");
		}

		#endregion

		#region Movement

		/// <summary>Moves the caller to a character.</summary>
		private void GoToCharacter(IPlayerCharacter character, string arguments)
		{
			if (!TryResolveTarget(character, arguments, out IPlayerCharacter target))
			{
				return;
			}
			if (target.ID == character.ID)
			{
				Reply(character, "You are already there.");
				return;
			}
			if (!TryMove(character, character, target, out string failure))
			{
				Reply(character, failure);
				return;
			}
			Reply(character, $"Moved to {target.CharacterName}.");
		}

		/// <summary>Moves a character to the caller.</summary>
		/// <remarks>
		/// The summoned player is told, and told by whom. Being moved without explanation is
		/// indistinguishable from a bug or an exploit, and a player who cannot tell those apart
		/// files a report about the wrong thing.
		/// </remarks>
		private void SummonCharacter(IPlayerCharacter character, string arguments)
		{
			if (!TryResolveTarget(character, arguments, out IPlayerCharacter target))
			{
				return;
			}
			if (target.ID == character.ID)
			{
				Reply(character, "You cannot summon yourself.");
				return;
			}
			if (!TryMove(character, target, character, out string failure))
			{
				Reply(character, failure);
				return;
			}

			Reply(character, $"Summoned {target.CharacterName}.");
			Reply(target, $"You have been summoned by {character.CharacterName}.");
		}

		/// <summary>
		/// Places <paramref name="moved"/> at <paramref name="destination"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Same scene only. Both characters are held by this scene server, but a scene server
		/// runs several scenes: writing one character's position into another's scene would put
		/// them at valid coordinates in a place they are not loaded into, which reads to the
		/// player as falling through the world.
		/// </para>
		/// <para>
		/// Moves through the motor rather than the transform. The motor is what the prediction
		/// system reconciles against; setting the transform directly is corrected away on the
		/// next tick, so the character snaps back and the command appears to have done nothing.
		/// </para>
		/// </remarks>
		private bool TryMove(IPlayerCharacter actor, IPlayerCharacter moved, IPlayerCharacter destination, out string failure)
		{
			if (moved.SceneHandle != destination.SceneHandle)
			{
				failure = $"{moved.CharacterName} and {destination.CharacterName} are in different scenes.";
				return false;
			}
			if (moved.Motor == null || destination.Transform == null)
			{
				failure = "That character cannot be moved right now.";
				return false;
			}

			// Slightly behind the destination rather than inside it: two capsules at identical
			// coordinates resolve by shoving each other apart, which looks like a bug.
			Vector3 offset = destination.Transform.forward * -1.5f;
			moved.Motor.SetPositionAndRotationAndVelocity(
				destination.Transform.position + offset,
				destination.Transform.rotation,
				Vector3.zero);

			Log.Warning("SceneServerSystem",
				$"'{actor.CharacterName}' ({actor.Account}) moved '{moved.CharacterName}' to '{destination.CharacterName}'.");

			failure = null;
			return true;
		}

		#endregion

		#region Removal

		/// <summary>
		/// Writes a kick request for a character's account.
		/// </summary>
		/// <remarks>
		/// The account, not the connection. A kick request is the mechanism the login and scene
		/// servers already poll, and it removes every session the account holds; severing one
		/// connection here would leave the account's other characters logged in and would be
		/// undone by a reconnect a second later.
		/// </remarks>
		private void KickCharacter(IPlayerCharacter character, string arguments)
		{
			if (!TryResolveTarget(character, arguments, out IPlayerCharacter target))
			{
				return;
			}

			/* An operator must not be able to remove somebody at or above their own level. Two
			 * game masters kicking each other in a loop is the harmless version; the damaging
			 * one is a compromised game master account removing the administrators who would
			 * notice. */
			if (target.AccessLevel >= character.AccessLevel && target.ID != character.ID)
			{
				Reply(character, $"{target.CharacterName} is {target.AccessLevel}; you cannot kick them.");
				return;
			}

			string accountName = target.Account;
			string targetName = target.CharacterName;
			string actorName = character.CharacterName;

			RunAdminAction(character, async () =>
			{
				if (!TryGetDbService(out IKickRequestService kickRequests))
				{
					return "The kick request service is unavailable.";
				}

				DatabaseResult result = await kickRequests.PersistAsync(accountName);
				if (!result.IsSuccess)
				{
					return $"Could not write the kick request: {result.ErrorCode} - {result.ErrorMessage}";
				}

				await Log.Warning("SceneServerSystem",
					$"Game master '{actorName}' requested a kick for account '{accountName}' ('{targetName}').");

				return $"Kick request written for {targetName}. The servers act on it on their next poll.";
			});
		}

		#endregion

		#region Helpers

		/// <summary>Resolves this scene server's online-character mapping.</summary>
		private bool TryGetOnlineCharacters(out ICharacterMappingData<NetworkConnection> mapping)
		{
			return Server.DataContainerRegistry.TryGet(out mapping) && mapping != null;
		}

		/// <summary>
		/// Resolves a character by name, answering the caller when it cannot.
		/// </summary>
		/// <remarks>
		/// Only characters this scene server holds. A game master on one scene server cannot act
		/// on a player held by another, and saying so is better than a lookup that reaches across
		/// processes and acts on a character whose authoritative state lives elsewhere.
		/// </remarks>
		private bool TryResolveTarget(IPlayerCharacter character, string arguments, out IPlayerCharacter target)
		{
			target = null;

			string name = (arguments ?? string.Empty).Trim();
			if (string.IsNullOrWhiteSpace(name))
			{
				Reply(character, "Name a character.");
				return false;
			}
			if (!TryGetOnlineCharacters(out var mapping))
			{
				Reply(character, "The character mapping is unavailable.");
				return false;
			}

			if (!mapping.CharactersByLowerCaseName.TryGetValue(name.ToLowerInvariant(), out target) || target == null)
			{
				/* Echo a bounded prefix. This only goes back to the operator who typed it and the
				 * text is already sanitized, but a reply whose length is driven by input still
				 * pushes past ChatBroadcast.MaxTextLength. */
				string shown = name.Length > 32 ? name.Substring(0, 32) + "..." : name;
				Reply(character, $"'{shown}' is not on this scene server.");
				return false;
			}
			return true;
		}

		/// <summary>Packs names into lines that stay inside the chat message limit.</summary>
		private static IEnumerable<string> BatchNames(IEnumerable<string> names)
		{
			var line = new StringBuilder();
			foreach (string name in names)
			{
				// 120, not MaxTextLength: leaves room for the separator and avoids a line that is
				// exactly at the cap being truncated by a later prefix.
				if (line.Length > 0 && line.Length + name.Length + 2 > 120)
				{
					yield return line.ToString();
					line.Clear();
				}
				if (line.Length > 0)
				{
					line.Append(", ");
				}
				line.Append(name);
			}
			if (line.Length > 0)
			{
				yield return line.ToString();
			}
		}

		#endregion
	}
}
