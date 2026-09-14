using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Auth.Core;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// <c>/gm</c> communication and moderation: private staff messages, warnings, kicks, chat mutes
	/// on a character or an account, and capped temporary bans.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Every guard compares against the account row, not the character.</b> A mute or ban lands
	/// on an account's standing, so the level that decides whether the operator may act is the
	/// account's, read fresh from the database. An operator may never act on their own account or
	/// on one at or above their own level.
	/// </para>
	/// <para>
	/// <b>Game masters are capped; administrators are not.</b> A game master's mute or ban lasts at
	/// most <see cref="OperatorCommandParsing.GameMasterMaximumDuration"/> and always ends; a
	/// permanent one needs an administrator. A game master may lift a temporary ban, never a
	/// permanent one, and may not shorten a longer ban already in place.
	/// </para>
	/// <para>
	/// <b>A mute is applied in memory only on this scene server.</b> The row is authoritative and
	/// is read whenever a character loads; a character this server holds is updated at once, and
	/// one held elsewhere is silenced when it next loads. Every acknowledgement says so.
	/// </para>
	/// </remarks>
	public partial class SceneServerSystem
	{
		/// <summary>The communication and moderation part of the <c>/gm</c> table.</summary>
		private IEnumerable<OperatorCommand> BuildGameMasterModerationCommands()
		{
			return new List<OperatorCommand>()
			{
				new OperatorCommand
				{
					Name = "msg", Aliases = new[] { "whisper" }, Category = "Communication",
					Summary = "Sends a character on this scene server a private message marked as coming from staff.",
					Arguments = "character:Character;message:Text", RosterAction = true,
					Run = (c, a) => MessageCharacter(c, a, warning: false),
				},
				new OperatorCommand
				{
					Name = "warn", Category = "Communication",
					Summary = "Sends a character on this scene server a formal staff warning.",
					Arguments = "character:Character;reason:Text", RosterAction = true,
					Run = (c, a) => MessageCharacter(c, a, warning: true),
				},

				new OperatorCommand
				{
					Name = "kick", Category = "Moderation",
					Summary = "Disconnects every session on a character's account. They can sign straight back in.",
					Arguments = "character:Character", RosterAction = true, Destructive = true,
					Run = KickCharacter,
				},
				new OperatorCommand
				{
					Name = "mute", Category = "Moderation",
					Summary = "Mutes one character in chat, for a duration such as 30m, 2h or 7d. Commands still work.",
					Arguments = "character:Character;duration:Duration;reason:Text?", RosterAction = true,
					Run = (c, a) => MuteCharacter(c, a, accountWide: false),
				},
				new OperatorCommand
				{
					Name = "unmute", Category = "Moderation",
					Summary = "Lifts a character's own chat mute. An account mute stays in force.",
					Arguments = "character:Character", RosterAction = true,
					Run = (c, a) => UnmuteCharacter(c, a, accountWide: false),
				},
				new OperatorCommand
				{
					Name = "muteaccount", Category = "Moderation",
					Summary = "Mutes every character on the named character's account, for a duration.",
					Arguments = "character:Character;duration:Duration;reason:Text?", RosterAction = true,
					Run = (c, a) => MuteCharacter(c, a, accountWide: true),
				},
				new OperatorCommand
				{
					Name = "unmuteaccount", Category = "Moderation",
					Summary = "Lifts the account-wide mute on the named character's account.",
					Arguments = "character:Character", RosterAction = true,
					Run = (c, a) => UnmuteCharacter(c, a, accountWide: true),
				},
				new OperatorCommand
				{
					Name = "tempban", Category = "Moderation",
					Summary = "Bans an account for a duration, revoking its sessions and kicking it. It lifts itself.",
					Arguments = "account:Account;duration:Duration;reason:Text", Destructive = true,
					Run = TempBanAccount,
				},
				new OperatorCommand
				{
					Name = "unban", Category = "Moderation",
					Summary = "Lifts a ban. Game masters may lift temporary bans only.",
					Arguments = "account:Account",
					Run = UnbanAccount,
				},
			};
		}

		#region Communication

		/// <summary>Sends a character a staff message or a formal warning.</summary>
		/// <remarks>
		/// On the System channel, which only the server can send on, and signed with the operator's
		/// character name. A player told to stop by an anonymous line cannot tell staff from a
		/// prankster, and one who knows which staff member spoke to them can refer back to it.
		/// </remarks>
		private void MessageCharacter(IPlayerCharacter character, string arguments, bool warning)
		{
			string name = OperatorCommandParsing.SplitFirstWord(arguments, out string text);
			if (name.Length == 0 || text.Length == 0)
			{
				ReplyUsage(character, gameMasterCommands, warning ? "warn" : "msg");
				return;
			}
			if (!TryResolveTarget(character, name, out IPlayerCharacter target))
			{
				return;
			}

			Reply(target, warning
				? $"Staff warning from {character.CharacterName}: {text}"
				: $"[Staff] {character.CharacterName}: {text}");

			Log.Warning("SceneServerSystem",
				$"'{character.CharacterName}' ({character.Account}) {(warning ? "warned" : "messaged")} '{target.CharacterName}': {text}");

			Reply(character, warning ? $"Warned {target.CharacterName}." : $"Sent to {target.CharacterName}.");
		}

		#endregion

		#region Kick

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

			if (target.ID != character.ID && !Outranks(character, target.AccessLevel))
			{
				Reply(character, $"{target.CharacterName} is {target.AccessLevel}; you cannot kick them.");
				return;
			}

			string accountName = target.Account;
			string targetName = target.CharacterName;
			string actorName = character.CharacterName;

			RunOperatorAction(character, async () =>
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

		#region Mutes

		/// <summary>Applies a character or account chat mute.</summary>
		private void MuteCharacter(IPlayerCharacter character, string arguments, bool accountWide)
		{
			string name = OperatorCommandParsing.SplitFirstWord(arguments, out string rest);
			string durationText = OperatorCommandParsing.SplitFirstWord(rest, out string reason);
			if (name.Length == 0 || durationText.Length == 0)
			{
				ReplyUsage(character, gameMasterCommands, accountWide ? "muteaccount" : "mute");
				return;
			}
			if (!TryResolveModerationSpan(character, durationText, "mute", out DateTime? untilUtc, out string span))
			{
				return;
			}

			string actorAccount = character.Account;
			string storedReason = reason.Length == 0 ? null : reason;

			RunModerationOnCharacter(character, name, "mute", async (characters, accounts, target) =>
			{
				DatabaseResult write = accountWide
					? await accounts.PersistMuteAsync(target.Account, untilUtc, actorAccount, storedReason)
					: await characters.PersistMuteAsync(target.CharacterID, untilUtc, actorAccount, storedReason);
				if (!write.IsSuccess)
				{
					return $"Could not mute {target.Name}: {write.ErrorMessage}";
				}

				string account = target.Account;
				TryEnqueueMainThread(() => RefreshChatMutes(account));

				await Log.Warning("SceneServerSystem",
					$"'{actorAccount}' muted {(accountWide ? "account '" + target.Account + "'" : "character '" + target.Name + "'")} {span}: {storedReason ?? "no reason"}");

				return $"Muted {(accountWide ? "the account of " : string.Empty)}{target.Name} {span}. " +
					"It applies now on this scene server and elsewhere when they next load.";
			});
		}

		/// <summary>Lifts a character or account chat mute.</summary>
		private void UnmuteCharacter(IPlayerCharacter character, string arguments, bool accountWide)
		{
			string name = OperatorCommandParsing.SplitFirstWord(arguments, out _);
			if (name.Length == 0)
			{
				ReplyUsage(character, gameMasterCommands, accountWide ? "unmuteaccount" : "unmute");
				return;
			}

			string actorAccount = character.Account;

			RunModerationOnCharacter(character, name, "unmute", async (characters, accounts, target) =>
			{
				DatabaseResult write = accountWide
					? await accounts.ClearMuteAsync(target.Account)
					: await characters.ClearMuteAsync(target.CharacterID);
				if (!write.IsSuccess)
				{
					return $"Could not unmute {target.Name}: {write.ErrorMessage}";
				}

				string account = target.Account;
				TryEnqueueMainThread(() => RefreshChatMutes(account));

				await Log.Warning("SceneServerSystem",
					$"'{actorAccount}' unmuted {(accountWide ? "account '" + target.Account + "'" : "character '" + target.Name + "'")}.");

				return accountWide
					? $"The account mute on {target.Name}'s account is lifted. A character mute, if any, stays."
					: $"{target.Name}'s own mute is lifted. An account mute, if any, stays.";
			});
		}

		/// <summary>A character a moderation command acts on, resolved to the rows it touches.</summary>
		private readonly struct ModerationTarget
		{
			public readonly long CharacterID;
			public readonly string Name;
			public readonly string Account;

			public ModerationTarget(long characterID, string name, string account)
			{
				CharacterID = characterID;
				Name = name;
				Account = account;
			}
		}

		/// <summary>
		/// Resolves a character by name — held here, or anywhere in the database — checks that the
		/// operator may act on its account, then runs <paramref name="act"/>.
		/// </summary>
		/// <remarks>
		/// Offline characters are resolved too, because the player an operator most needs to mute is
		/// the one who logged off the moment they were reported. The account's level is always read
		/// from the account row, never from the character in memory, which may be a session old.
		/// </remarks>
		private void RunModerationOnCharacter(
			IPlayerCharacter character, string name, string verb,
			Func<ICharacterService, IAccountService, ModerationTarget, Task<string>> act)
		{
			long onlineID = 0;
			string onlineName = null;
			string onlineAccount = null;
			if (TryGetOnlineCharacters(out var mapping) &&
				mapping.CharactersByLowerCaseName.TryGetValue(name.ToLowerInvariant(), out IPlayerCharacter online) &&
				online != null)
			{
				onlineID = online.ID;
				onlineName = online.CharacterName;
				onlineAccount = online.Account;
			}

			string actorAccount = character.Account;
			AccessLevel actorLevel = character.AccessLevel;

			RunOperatorAction(character, async () =>
			{
				if (!TryGetDbService(out ICharacterService characters) || !TryGetDbService(out IAccountService accounts))
				{
					return "The character or account service is unavailable.";
				}

				var target = new ModerationTarget(onlineID, onlineName, onlineAccount);
				if (target.CharacterID <= 0)
				{
					DatabaseResult<CharacterData?> found = await characters.FetchAsync(name, null);
					if (!found.IsSuccess)
					{
						return $"Could not look '{OperatorCommandParsing.Truncate(name, 32)}' up: {found.ErrorMessage}";
					}
					if (found.Data == null)
					{
						return $"No character named '{OperatorCommandParsing.Truncate(name, 32)}'.";
					}
					CharacterData data = (CharacterData)found.Data;
					target = new ModerationTarget(data.ID, data.Name, data.Account);
				}

				if (string.Equals(target.Account, actorAccount, StringComparison.OrdinalIgnoreCase))
				{
					return $"You cannot {verb} your own account.";
				}

				DatabaseResult<AccountAdminData> account = await accounts.FetchAdminAsync(target.Account);
				if (!account.IsSuccess || account.Data == null)
				{
					return $"The account of {target.Name} could not be read.";
				}
				var level = (AccessLevel)account.Data.AccessLevel;
				if (level >= actorLevel)
				{
					return $"{target.Name}'s account is {level}; you cannot {verb} them.";
				}

				return await act(characters, accounts, target);
			});
		}

		/// <summary>
		/// Re-reads the mute state of every character on an account that this scene server holds, and applies it.
		/// </summary>
		/// <remarks>
		/// Re-read rather than computed from the write that just happened. A character mute and an
		/// account mute both apply, so lifting one must leave the other in force, and only the rows
		/// know whether there is another. Main thread; the reads run on the worker.
		/// </remarks>
		private void RefreshChatMutes(string account)
		{
			if (string.IsNullOrEmpty(account) || !TryGetOnlineCharacters(out var mapping))
			{
				return;
			}

			foreach (IPlayerCharacter online in mapping.CharactersByID.Values)
			{
				if (online == null || !string.Equals(online.Account, account, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				long characterID = online.ID;
				TryEnqueueAsyncWork(async () =>
				{
					if (!TryGetDbService(out ICharacterService characters))
					{
						return;
					}

					DatabaseResult<CharacterChatMuteState> state = await characters.FetchChatMuteAsync(characterID);
					if (!state.IsSuccess)
					{
						await Log.Warning("SceneServerSystem",
							$"Chat mute for character {characterID} could not be re-read: {state.ErrorMessage}");
						return;
					}

					long untilTicks = ChatMutePolicy.ResolveUntilTicks(state.Data, DateTime.UtcNow, out string reason);
					TryEnqueueMainThread(() =>
					{
						if (TryGetOnlineCharacters(out var current) &&
							current.CharactersByID.TryGetValue(characterID, out IPlayerCharacter held) &&
							held != null)
						{
							held.ChatMutedUntilTicks = untilTicks;
							held.ChatMuteReason = reason;
						}
					});
				}, characterID);
			}
		}

		#endregion

		#region Bans

		/// <summary>Bans an account for a duration.</summary>
		private void TempBanAccount(IPlayerCharacter character, string arguments)
		{
			string accountName = OperatorCommandParsing.SplitFirstWord(arguments, out string rest);
			string durationText = OperatorCommandParsing.SplitFirstWord(rest, out string reason);
			if (accountName.Length == 0 || durationText.Length == 0 || reason.Length == 0)
			{
				ReplyUsage(character, gameMasterCommands, "tempban");
				return;
			}
			if (OperatorCommandParsing.IsPermanent(durationText))
			{
				Reply(character, "A temporary ban always ends. Administrators ban permanently with /admin ban.");
				return;
			}
			if (!TryResolveModerationSpan(character, durationText, "ban", out DateTime? untilUtc, out string span) || untilUtc == null)
			{
				return;
			}
			if (string.Equals(accountName, character.Account, StringComparison.OrdinalIgnoreCase))
			{
				Reply(character, "You cannot ban your own account.");
				return;
			}

			string actorAccount = character.Account;
			AccessLevel actorLevel = character.AccessLevel;
			DateTime until = untilUtc.Value;

			RunOperatorAction(character, async () =>
			{
				if (!TryGetDbService(out IAccountService accounts))
				{
					return "The account service is unavailable.";
				}

				DatabaseResult<AccountAdminData> existing = await accounts.FetchAdminAsync(accountName);
				if (!existing.IsSuccess || existing.Data == null)
				{
					return $"No account named '{OperatorCommandParsing.Truncate(accountName, 32)}'.";
				}

				var level = (AccessLevel)existing.Data.AccessLevel;
				if (level >= actorLevel)
				{
					return $"'{accountName}' is {level}; you cannot ban them.";
				}
				if (level == AccessLevel.Banned)
				{
					/* Never shorten a ban already in place. Replacing a permanent ban, or a longer
					 * temporary one, with this would be a way to release somebody early that reads as
					 * a ban in the log. Lifting a ban is its own command. */
					if (existing.Data.BannedUntil == null)
					{
						return $"'{accountName}' is already banned permanently.";
					}
					if (existing.Data.BannedUntil.Value >= until)
					{
						return $"'{accountName}' is already banned until {existing.Data.BannedUntil.Value:yyyy-MM-dd HH:mm} UTC, which is longer.";
					}
				}

				DatabaseResult result = await accounts.BanAsync(accountName, until, actorAccount, reason);
				if (!result.IsSuccess)
				{
					return $"Could not ban '{accountName}': {result.ErrorMessage}";
				}

				await Log.Warning("SceneServerSystem",
					$"'{actorAccount}' banned account '{accountName}' {span} (until {until:u}): {reason}");

				return $"'{accountName}' is banned {span}, until {until:yyyy-MM-dd HH:mm} UTC. Sessions revoked and a kick written.";
			});
		}

		/// <summary>Lifts a ban: any ban for an administrator, a temporary one for a game master.</summary>
		private void UnbanAccount(IPlayerCharacter character, string arguments)
		{
			string accountName = OperatorCommandParsing.SplitFirstWord(arguments, out _);
			if (accountName.Length == 0)
			{
				ReplyUsage(character, gameMasterCommands, "unban");
				return;
			}

			string actorAccount = character.Account;
			AccessLevel actorLevel = character.AccessLevel;

			RunOperatorAction(character, async () =>
			{
				if (!TryGetDbService(out IAccountService accounts))
				{
					return "The account service is unavailable.";
				}

				DatabaseResult<AccountAdminData> existing = await accounts.FetchAdminAsync(accountName);
				if (!existing.IsSuccess || existing.Data == null)
				{
					return $"No account named '{OperatorCommandParsing.Truncate(accountName, 32)}'.";
				}
				if ((AccessLevel)existing.Data.AccessLevel != AccessLevel.Banned)
				{
					return $"'{accountName}' is not banned.";
				}
				if (existing.Data.BannedUntil == null && actorLevel < AccessLevel.Admin)
				{
					return $"'{accountName}' is banned permanently; only an administrator can lift it.";
				}

				DatabaseResult result = await accounts.UnbanAsync(accountName);
				if (!result.IsSuccess)
				{
					return $"Could not lift the ban on '{accountName}': {result.ErrorMessage}";
				}

				await Log.Warning("SceneServerSystem", $"'{actorAccount}' lifted the ban on account '{accountName}'.");

				return $"The ban on '{accountName}' is lifted. They are a Player again and must sign in afresh.";
			});
		}

		#endregion

		/// <summary>
		/// Turns a typed duration into when a mute or ban ends, applying the game master cap.
		/// </summary>
		/// <param name="character">The operator; their level decides the cap.</param>
		/// <param name="text">The typed duration, or <c>perm</c>.</param>
		/// <param name="noun">What is being applied, for the refusal.</param>
		/// <param name="untilUtc">When it ends, or null for no end.</param>
		/// <param name="description">"for 2h" or "with no end", for the acknowledgement.</param>
		/// <returns>False, having told the operator why, when the duration is not allowed.</returns>
		private bool TryResolveModerationSpan(IPlayerCharacter character, string text, string noun, out DateTime? untilUtc, out string description)
		{
			untilUtc = null;
			description = null;

			if (OperatorCommandParsing.IsPermanent(text))
			{
				if (character.AccessLevel < AccessLevel.Admin)
				{
					Reply(character, $"Only an administrator can {noun} with no end. Game masters may set up to " +
						$"{OperatorCommandParsing.DescribeDuration(OperatorCommandParsing.GameMasterMaximumDuration)}.");
					return false;
				}
				description = "with no end";
				return true;
			}

			if (!OperatorCommandParsing.TryParseDuration(text, out TimeSpan duration))
			{
				Reply(character, $"'{OperatorCommandParsing.Truncate(text, 16)}' is not a duration. Use a number with s, m, h, d or w, such as 30m or 7d.");
				return false;
			}
			if (character.AccessLevel < AccessLevel.Admin && duration > OperatorCommandParsing.GameMasterMaximumDuration)
			{
				Reply(character, $"Game masters may {noun} for at most " +
					$"{OperatorCommandParsing.DescribeDuration(OperatorCommandParsing.GameMasterMaximumDuration)}.");
				return false;
			}

			untilUtc = DateTime.UtcNow + duration;
			description = "for " + OperatorCommandParsing.DescribeDuration(duration);
			return true;
		}
	}
}
