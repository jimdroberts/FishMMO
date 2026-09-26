using FishNet.Connection;
using System;
using System.Collections.Generic;
using System.Globalization;
using FishMMO.Auth.Core;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The in-game <c>/admin</c> command set: server control, account access, and everything that
	/// changes the game itself — currency, items, attributes, life and death.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Registered once, as <c>/admin</c>, at <see cref="AccessLevel.Admin"/>, with its sub-commands
	/// in one table. See <c>SceneServerSystem.OperatorCommands</c> for the shape and why it matters.
	/// Administrators also hold every <c>/gm</c> command, which is registered at a lower level.
	/// </para>
	/// <para>
	/// Server control writes the database row for the server it targets and returns; it never
	/// mutates a server's state directly, not even this one's. Each process adopts its own row on
	/// its next pulse, which is what lets a command typed on one scene server reach the world
	/// server and every other scene server under it. The cost is that changes take effect within
	/// a pulse or two rather than instantly, which is why the acknowledgement says what was
	/// written rather than claiming the server has already done it.
	/// </para>
	/// <para>
	/// The game-changing commands live in <c>SceneServerSystem.AdminCommands.Economy</c> and
	/// <c>SceneServerSystem.AdminCommands.Character</c>. They are administrator-only by design: a
	/// game master who can mint currency or items is an exploit waiting for one stolen password.
	/// </para>
	/// </remarks>
	public partial class SceneServerSystem
	{
		/// <summary>Longest shutdown delay an operator may schedule, in seconds (24 hours).</summary>
		/// <remarks>
		/// Bounds a typo rather than a policy: <c>/admin shutdown 6000000</c> should be refused
		/// rather than quietly locking the world for eleven weeks. Nothing depends on the value
		/// beyond it being obviously longer than any real maintenance window.
		/// </remarks>
		private const int MaxShutdownDelaySeconds = 86_400;

		/// <summary>Longest announcement accepted, leaving room for nothing else on the line.</summary>
		private const int MaxAnnouncementLength = 96;

		/// <summary>Registers the <c>/admin</c> command. Called from the scene server's initialization.</summary>
		private void RegisterAdminCommands()
		{
			adminCommands = new OperatorCommandSet("/admin", AccessLevel.Admin, BuildAdminCommands());

			ChatHelper.AddCommands(new Dictionary<string, ChatCommand>()
			{
				{ "/admin", OnAdminCommand },
			}, AccessLevel.Admin);

			ChatHelper.SetCommandHelp("/admin", new ChatCommandHelp()
			{
				Category = "Staff",
				Arguments = "<command>",
				Summary = "Administrator commands. /admin help lists them.",
			});
		}

		/// <summary>Unregisters the <c>/admin</c> command. Called from the scene server's teardown.</summary>
		private void UnregisterAdminCommands()
		{
			ChatHelper.RemoveCommands(new[] { "/admin" });
		}

		/// <summary>Dispatches an <c>/admin</c> sub-command.</summary>
		private bool OnAdminCommand(IPlayerCharacter character, ChatBroadcast msg)
		{
			return DispatchOperatorCommand(adminCommands, character, msg);
		}

		/// <summary>The whole <c>/admin</c> table, in presentation order.</summary>
		private IEnumerable<OperatorCommand> BuildAdminCommands()
		{
			var commands = new List<OperatorCommand>()
			{
				new OperatorCommand
				{
					Name = "status", Aliases = new[] { "serverstatus" }, Category = "Server",
					Summary = "Reports this scene server's lock and shutdown state, and that of its worlds.",
					Run = (c, a) => ReportStatus(c),
				},
				new OperatorCommand
				{
					Name = "lockserver", Aliases = new[] { "lockworld" }, Category = "Server",
					Summary = "Locks your world server: no new logins below GameMaster. Players online stay.",
					Destructive = true,
					Run = (c, a) => SetWorldLock(c, locked: true),
				},
				new OperatorCommand
				{
					Name = "unlockserver", Aliases = new[] { "unlockworld" }, Category = "Server",
					Summary = "Reopens your world server to logins.",
					Run = (c, a) => SetWorldLock(c, locked: false),
				},
				new OperatorCommand
				{
					Name = "shutdown", Category = "Server",
					Summary = "Locks the world and shuts it down after a delay, warning players as it counts down.",
					Arguments = "seconds:Integer", Destructive = true,
					Run = ScheduleWorldShutdown,
				},
				new OperatorCommand
				{
					Name = "stopshutdown", Category = "Server",
					Summary = "Cancels the world's shutdown. The world stays locked until unlockserver.",
					Run = (c, a) => CancelWorldShutdown(c),
				},
				new OperatorCommand
				{
					Name = "lockscene", Category = "Server",
					Summary = "Locks this scene server: no new players or scenes are routed here.",
					Destructive = true,
					Run = (c, a) => SetSceneLock(c, locked: true),
				},
				new OperatorCommand
				{
					Name = "unlockscene", Category = "Server",
					Summary = "Reopens this scene server.",
					Run = (c, a) => SetSceneLock(c, locked: false),
				},
				new OperatorCommand
				{
					Name = "shutdownscene", Category = "Server",
					Summary = "Locks this scene server and shuts it down after a delay; its players are moved on.",
					Arguments = "seconds:Integer", Destructive = true,
					Run = ScheduleSceneShutdown,
				},
				new OperatorCommand
				{
					Name = "stopshutdownscene", Category = "Server",
					Summary = "Cancels this scene server's shutdown. It stays locked until unlockscene.",
					Run = (c, a) => CancelSceneShutdown(c),
				},
				new OperatorCommand
				{
					Name = "announce", Aliases = new[] { "broadcast" }, Category = "Server",
					Summary = "Sends a system message to every character on this scene server.",
					Arguments = "message:Text",
					Run = Announce,
				},

				new OperatorCommand
				{
					Name = "access", Aliases = new[] { "setaccess" }, Category = "Accounts",
					Summary = "Sets an account's access level, below your own, lifting any ban. /admin ban bans.",
					Arguments = "account:Account;level:Choice=Player,GameMaster,Admin", Destructive = true,
					Run = SetAccountAccessLevel,
				},
				new OperatorCommand
				{
					Name = "ban", Category = "Accounts",
					Summary = "Bans an account permanently, revoking its sessions and kicking it. Game masters use /gm tempban.",
					Arguments = "account:Account;reason:Text", Destructive = true,
					Run = BanAccountPermanently,
				},
			};

			commands.AddRange(BuildAdminEconomyCommands());
			commands.AddRange(BuildAdminCharacterCommands());
			commands.AddRange(BuildAdminWeatherCommands());
			return commands;
		}

		#region Announcements

		/// <summary>
		/// Sends a system-channel message to every character on this scene server.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This scene server only. It does not reach the world's other scene servers, and the
		/// acknowledgement says so: an operator who believes they have warned the whole shard
		/// about a shutdown, and has not, is worse off than one who knows they must repeat it.
		/// A shard-wide announcement needs a row the other processes poll, which is the same
		/// shape as the lock and shutdown commands and is not built yet.
		/// </para>
		/// <para>
		/// The text is sent on the System channel, which the client renders distinctly, so a
		/// player cannot be fooled into thinking an ordinary message came from the staff.
		/// </para>
		/// </remarks>
		private void Announce(IPlayerCharacter character, string arguments)
		{
			string text = (arguments ?? string.Empty).Trim();
			if (string.IsNullOrWhiteSpace(text))
			{
				ReplyUsage(character, adminCommands, "announce");
				return;
			}
			if (text.Length > MaxAnnouncementLength)
			{
				Reply(character, $"Keep it under {MaxAnnouncementLength} characters.");
				return;
			}

			if (!TryGetOnlineCharacters(out var mapping))
			{
				Reply(character, "The character mapping is unavailable.");
				return;
			}

			var broadcast = new ChatBroadcast()
			{
				Channel = ChatChannel.System,
				Text = text,
			};

			int sent = 0;
			foreach (IPlayerCharacter target in mapping.CharactersByID.Values)
			{
				NetworkConnection conn = target?.Owner;
				if (conn == null || !conn.IsActive)
				{
					continue;
				}
				Server.NetworkWrapper.Broadcast(conn, broadcast, true, FishNet.Transporting.Channel.Reliable);
				sent++;
			}

			Log.Warning("SceneServerSystem",
				$"Administrator '{character.CharacterName}' ({character.Account}) announced to {sent} character(s): {text}");

			Reply(character, $"Announced to {sent} character(s) on this scene server only.");
		}

		#endregion

		#region Accounts

		/// <summary>
		/// Sets an account's access level.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Four guards, each closing a way this command could be used to escalate rather than
		/// to administer: an operator may not change their own level, may not act on an account
		/// at or above their own, may not grant a level at or above their own, and may not set
		/// <see cref="AccessLevel.Banned"/> at all. Without the third an administrator could mint
		/// administrators, which makes the level ceiling decorative; see the fourth below.
		/// </para>
		/// <para>
		/// <b>This command lifts a ban; it can never apply one.</b> The write clears the ban
		/// columns, so setting a banned account to Player is the unban and is meant to be — it is
		/// how a ban and a demotion are undone in one action. The same write reached with
		/// <c>Banned</c> would have left a permanent ban with no actor, no reason, no revoked
		/// session and no kick, and would have ERASED the actor and reason of a ban already there.
		/// <c>/admin ban</c> exists for that and records all of it, so this refuses the level
		/// outright rather than quietly writing a worse ban than the ban command would.
		/// </para>
		/// <para>
		/// Existing sessions are not severed here. The Control Panel revokes a session whose
		/// account level has changed on that session's next request, and the game reads the level
		/// from the character row at login; a player already in the world keeps the level they
		/// logged in with until they reconnect. Kick them as well if that matters.
		/// </para>
		/// </remarks>
		private void SetAccountAccessLevel(IPlayerCharacter character, string arguments)
		{
			string accountName = OperatorCommandParsing.SplitFirstWord(arguments, out string levelText);
			if (accountName.Length == 0 || levelText.Length == 0)
			{
				ReplyUsage(character, adminCommands, "access");
				return;
			}

			/* Enum.IsDefined is handed the parsed AccessLevel, whose underlying type is byte. Given
			 * anything with a different underlying type it throws rather than returning false. */
			if (!Enum.TryParse(levelText, ignoreCase: true, out AccessLevel level) ||
				!Enum.IsDefined(typeof(AccessLevel), level))
			{
				Reply(character, $"'{OperatorCommandParsing.Truncate(levelText, 24)}' is not an access level.");
				return;
			}

			/* Banned is not a level this command grants, and the refusal is here as well as in
			 * PersistAccessLevelAsync so the operator gets an answer that names the right command
			 * instead of a database validation message. A ban applied here would carry no actor and
			 * no reason, revoke nothing, kick nobody, and — run on an account already banned — would
			 * erase whoever banned them and why. /admin ban refuses to overwrite an existing
			 * permanent ban for precisely that reason, and this command must not be the way round it. */
			if (level == AccessLevel.Banned)
			{
				// Two lines: one reply over ChatBroadcast.MaxTextLength is cut, and the cut half is the why.
				Reply(character, "/admin access cannot set Banned. Use /admin ban <account> <reason>.");
				Reply(character, "It records who banned them and why; access would record neither.");
				return;
			}

			if (string.Equals(accountName, character.Account, StringComparison.OrdinalIgnoreCase))
			{
				Reply(character, "You cannot change your own access level.");
				return;
			}

			if (level >= character.AccessLevel)
			{
				Reply(character, $"You cannot grant {level}; it is at or above your own level.");
				return;
			}

			string actorName = character.CharacterName;
			string actorAccount = character.Account;
			AccessLevel actorLevel = character.AccessLevel;

			RunOperatorAction(character, async () =>
			{
				if (!TryGetDbService(out IAccountService accountService))
				{
					return "The account service is unavailable.";
				}

				/* The target's current level is read first so an operator cannot act on somebody at
				 * or above them. FetchAdminAsync, not the login fetch: the login fetch reports a
				 * banned account as missing, which made this command unable to lift a ban. */
				DatabaseResult<AccountAdminData> existing = await accountService.FetchAdminAsync(accountName);
				if (!existing.IsSuccess || existing.Data == null)
				{
					return DescribeLookupFailure(existing, $"No account named '{OperatorCommandParsing.Truncate(accountName, 32)}'.", $"the account '{OperatorCommandParsing.Truncate(accountName, 32)}'");
				}

				var currentLevel = (AccessLevel)existing.Data.AccessLevel;
				if (currentLevel >= actorLevel)
				{
					return $"'{accountName}' is {currentLevel}; you cannot change them.";
				}

				DatabaseResult result = await accountService.PersistAccessLevelAsync(accountName, (byte)level);
				if (!result.IsSuccess)
				{
					return $"Could not set the access level: {result.ErrorCode} - {result.ErrorMessage}";
				}

				await Log.Warning("SceneServerSystem",
					$"Administrator '{actorName}' ({actorAccount}) set account '{accountName}' from {currentLevel} to {level}.");

				/* Said out loud when it happens. The write clears the ban columns, so an operator
				 * raising a banned account's level has also lifted the ban, and one who did not mean
				 * to needs to know that now rather than when the player logs in. */
				string lifted = currentLevel == AccessLevel.Banned ? " Ban lifted." : string.Empty;
				return $"'{accountName}' is now {level}.{lifted} They keep their current level until they reconnect.";
			});
		}

		/// <summary>
		/// Bans an account with no end: level, tokens, panel sessions and a kick, in one transaction.
		/// </summary>
		/// <remarks>
		/// Replaces a temporary ban already on the account, which is the usual way this is reached:
		/// a game master's capped ban escalated by an administrator. Refuses an account already banned
		/// permanently, so a second ban cannot quietly overwrite who banned it and why.
		/// </remarks>
		private void BanAccountPermanently(IPlayerCharacter character, string arguments)
		{
			string accountName = OperatorCommandParsing.SplitFirstWord(arguments, out string reason);
			if (accountName.Length == 0 || reason.Length == 0)
			{
				ReplyUsage(character, adminCommands, "ban");
				return;
			}
			if (string.Equals(accountName, character.Account, StringComparison.OrdinalIgnoreCase))
			{
				Reply(character, "You cannot ban your own account.");
				return;
			}

			string actorAccount = character.Account;
			AccessLevel actorLevel = character.AccessLevel;

			RunOperatorAction(character, async () =>
			{
				if (!TryGetDbService(out IAccountService accountService))
				{
					return "The account service is unavailable.";
				}

				DatabaseResult<AccountAdminData> existing = await accountService.FetchAdminAsync(accountName);
				if (!existing.IsSuccess || existing.Data == null)
				{
					return DescribeLookupFailure(existing, $"No account named '{OperatorCommandParsing.Truncate(accountName, 32)}'.", $"the account '{OperatorCommandParsing.Truncate(accountName, 32)}'");
				}

				var currentLevel = (AccessLevel)existing.Data.AccessLevel;
				if (currentLevel >= actorLevel)
				{
					return $"'{accountName}' is {currentLevel}; you cannot ban them.";
				}
				if (currentLevel == AccessLevel.Banned && existing.Data.BannedUntil == null)
				{
					return $"'{accountName}' is already banned permanently.";
				}

				DatabaseResult result = await accountService.BanAsync(accountName, null, actorAccount, reason);
				if (!result.IsSuccess)
				{
					return $"Could not ban '{accountName}': [{result.ErrorCode}] {result.ErrorMessage}";
				}

				await Log.Warning("SceneServerSystem",
					$"Administrator '{actorAccount}' banned account '{accountName}' permanently: {reason}");

				return $"'{accountName}' is banned permanently. Sessions revoked and a kick written.";
			});
		}

		#endregion

		#region World Commands

		/// <summary>Locks or unlocks the world server this character belongs to.</summary>
		private void SetWorldLock(IPlayerCharacter character, bool locked)
		{
			long worldServerID = character.WorldServerID;
			if (worldServerID <= 0)
			{
				Reply(character, "Your character is not bound to a world server, so there is nothing to lock.");
				return;
			}

			string adminName = character.CharacterName;
			RunOperatorAction(character, async () =>
			{
				if (!TryGetDbService(out IWorldServerService worldServerService))
				{
					return "The world server service is unavailable.";
				}

				DatabaseResult result = await worldServerService.SetLockedAsync(worldServerID, locked);
				if (!result.IsSuccess)
				{
					return $"Could not update the world server: {result.ErrorCode} - {result.ErrorMessage}";
				}

				await Log.Warning("SceneServerSystem",
					$"Administrator '{adminName}' {(locked ? "LOCKED" : "UNLOCKED")} world server {worldServerID}.");

				return locked
					? "World lock written. New logins refused except above Player once it is read. Players online are unaffected."
					: "World unlock written. It accepts logins again once it is read.";
			});
		}

		/// <summary>Schedules the world's shutdown after a delay in seconds.</summary>
		private void ScheduleWorldShutdown(IPlayerCharacter character, string arguments)
		{
			if (!TryParseDelaySeconds(character, arguments, out int seconds))
			{
				return;
			}

			long worldServerID = character.WorldServerID;
			if (worldServerID <= 0)
			{
				Reply(character, "Your character is not bound to a world server, so there is nothing to shut down.");
				return;
			}

			string adminName = character.CharacterName;

			RunOperatorAction(character, async () =>
			{
				if (!TryGetDbService(out IWorldServerService worldServerService))
				{
					return "The world server service is unavailable.";
				}

				/* The delay goes to the database, which adds it to its own clock as it writes the
				 * deadline. Every server counts the deadline down against that clock, so an
				 * instant built here from this host's DateTime.UtcNow moved the shutdown by this
				 * host's skew from the database: "shutdown 300" typed on a scene server two
				 * minutes slow gave the world's players three minutes. */
				DatabaseResult<DateTime> result = await worldServerService.SetShutdownInAsync(worldServerID, seconds);
				if (!result.IsSuccess)
				{
					return $"Could not schedule the shutdown: {result.ErrorCode} - {result.ErrorMessage}";
				}

				DateTime deadline = result.Data;

				await Log.Warning("SceneServerSystem",
					$"Administrator '{adminName}' scheduled world server {worldServerID} to shut down at {deadline:u} ({seconds}s).");

				return $"World shutdown scheduled in {seconds}s ({deadline:u}). The world is now locked; " +
					"players are warned as the countdown passes each mark.";
			});
		}

		/// <summary>Cancels the world's scheduled shutdown, leaving the lock in place.</summary>
		private void CancelWorldShutdown(IPlayerCharacter character)
		{
			long worldServerID = character.WorldServerID;
			if (worldServerID <= 0)
			{
				Reply(character, "Your character is not bound to a world server.");
				return;
			}

			string adminName = character.CharacterName;
			RunOperatorAction(character, async () =>
			{
				if (!TryGetDbService(out IWorldServerService worldServerService))
				{
					return "The world server service is unavailable.";
				}

				DatabaseResult result = await worldServerService.SetShutdownAsync(worldServerID, null);
				if (!result.IsSuccess)
				{
					return $"Could not cancel the shutdown: {result.ErrorCode} - {result.ErrorMessage}";
				}

				await Log.Warning("SceneServerSystem",
					$"Administrator '{adminName}' cancelled the scheduled shutdown of world server {worldServerID}.");

				return "World shutdown cancelled. A shutdown already under way cannot be recalled. The world is still LOCKED; /admin unlockserver reopens it.";
			});
		}

		#endregion

		#region Scene Server Commands

		/// <summary>Locks or unlocks this scene server.</summary>
		private void SetSceneLock(IPlayerCharacter character, bool locked)
		{
			if (!TryGetSceneServerID(character, out long sceneServerID))
			{
				return;
			}

			string adminName = character.CharacterName;
			RunOperatorAction(character, async () =>
			{
				if (!TryGetDbService(out ISceneServerService sceneServerService))
				{
					return "The scene server service is unavailable.";
				}

				DatabaseResult result = await sceneServerService.SetLockedAsync(sceneServerID, locked);
				if (!result.IsSuccess)
				{
					return $"Could not update the scene server: {result.ErrorCode} - {result.ErrorMessage}";
				}

				await Log.Warning("SceneServerSystem",
					$"Administrator '{adminName}' {(locked ? "LOCKED" : "UNLOCKED")} scene server {sceneServerID}.");

				return locked
					? $"Scene server {sceneServerID} lock written. Once read, no new players are routed here and no new scenes load."
					: $"Scene server {sceneServerID} unlock written.";
			});
		}

		/// <summary>Schedules this scene server's shutdown after a delay in seconds.</summary>
		private void ScheduleSceneShutdown(IPlayerCharacter character, string arguments)
		{
			if (!TryParseDelaySeconds(character, arguments, out int seconds) ||
				!TryGetSceneServerID(character, out long sceneServerID))
			{
				return;
			}

			string adminName = character.CharacterName;

			RunOperatorAction(character, async () =>
			{
				if (!TryGetDbService(out ISceneServerService sceneServerService))
				{
					return "The scene server service is unavailable.";
				}

				// Timed by the database clock, as the world shutdown above is.
				DatabaseResult<DateTime> result = await sceneServerService.SetShutdownInAsync(sceneServerID, seconds);
				if (!result.IsSuccess)
				{
					return $"Could not schedule the shutdown: {result.ErrorCode} - {result.ErrorMessage}";
				}

				DateTime deadline = result.Data;

				await Log.Warning("SceneServerSystem",
					$"Administrator '{adminName}' scheduled scene server {sceneServerID} to shut down at {deadline:u} ({seconds}s).");

				return $"Scene server shutdown scheduled in {seconds}s ({deadline:u}). It is now locked; " +
					"players here are warned, then moved on.";
			});
		}

		/// <summary>Cancels this scene server's scheduled shutdown, leaving the lock in place.</summary>
		private void CancelSceneShutdown(IPlayerCharacter character)
		{
			if (!TryGetSceneServerID(character, out long sceneServerID))
			{
				return;
			}

			string adminName = character.CharacterName;
			RunOperatorAction(character, async () =>
			{
				if (!TryGetDbService(out ISceneServerService sceneServerService))
				{
					return "The scene server service is unavailable.";
				}

				DatabaseResult result = await sceneServerService.SetShutdownAsync(sceneServerID, null);
				if (!result.IsSuccess)
				{
					return $"Could not cancel the shutdown: {result.ErrorCode} - {result.ErrorMessage}";
				}

				await Log.Warning("SceneServerSystem",
					$"Administrator '{adminName}' cancelled the scheduled shutdown of scene server {sceneServerID}.");

				return $"Scene server {sceneServerID} shutdown cancelled. Still LOCKED; /admin unlockscene reopens it.";
			});
		}

		#endregion

		#region Status

		/// <summary>
		/// Reports this scene server's state and that of the worlds it hosts scenes for.
		/// </summary>
		/// <remarks>
		/// Read from the adopted state rather than the database, so it answers "what is this
		/// process actually doing" — which after a control change is the question worth asking,
		/// because the row and the process agree only after the next pulse.
		/// </remarks>
		private void ReportStatus(IPlayerCharacter character)
		{
			// The countdowns run on the monotonic clock from the database's measure. See ShutdownCountdown.
			double now = FishMMO.Server.Core.MonotonicClock.NowSeconds;

			long sceneServerID = Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeData)
				? runtimeData.ID
				: 0;

			Reply(character, $"Scene server {sceneServerID}: {(IsLockedNow() ? "LOCKED" : "open")}" +
				(sceneShutdownCountdown.IsScheduled
					? $", shutting down in {sceneShutdownCountdown.SecondsRemaining(now):F0}s"
					: ", no shutdown scheduled") + ".");

			if (worldControlStates.Count == 0)
			{
				Reply(character, "No world control state has been read yet.");
				return;
			}

			foreach (var kvp in worldControlStates)
			{
				Reply(character, $"World {kvp.Key}: {(kvp.Value.Locked ? "LOCKED" : "open")}" +
					(worldShutdownCountdowns.TryGetValue(kvp.Key, out var countdown) && countdown.IsScheduled
						? $", shutting down in {countdown.SecondsRemaining(now):F0}s"
						: ", no shutdown scheduled") + ".");
			}
		}

		#endregion

		#region Helpers

		/// <summary>
		/// Parses a delay argument in seconds.
		/// </summary>
		/// <returns>True when a usable delay was parsed; otherwise false, having told the caller why.</returns>
		private bool TryParseDelaySeconds(IPlayerCharacter character, string arguments, out int seconds)
		{
			seconds = 0;

			string trimmed = (arguments ?? string.Empty).Trim();
			if (trimmed.Length == 0)
			{
				Reply(character, "Give a delay in seconds, for example 300. Use 0 to shut down immediately.");
				return false;
			}

			if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds))
			{
				/* Echo at most a short prefix of what was typed: a reply whose length is driven by
				 * input would push past ChatBroadcast.MaxTextLength. */
				Reply(character, $"'{OperatorCommandParsing.Truncate(trimmed, 32)}' is not a number of seconds.");
				return false;
			}

			/* Zero is allowed and means "now". Negative is not: it would schedule a deadline in
			 * the past, which every server would act on the instant it read it — an immediate
			 * shutdown with no warning, from what looks like a typo. */
			if (seconds < 0)
			{
				Reply(character, "The delay cannot be negative. Use 0 to shut down immediately.");
				return false;
			}

			if (seconds > MaxShutdownDelaySeconds)
			{
				Reply(character, $"The delay cannot exceed {MaxShutdownDelaySeconds} seconds (24 hours).");
				return false;
			}

			return true;
		}

		/// <summary>Resolves this scene server's database id, or tells the caller why it cannot.</summary>
		private bool TryGetSceneServerID(IPlayerCharacter character, out long sceneServerID)
		{
			sceneServerID = 0;
			if (!Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeData) ||
				runtimeData.ID <= 0)
			{
				Reply(character, "This scene server is not registered yet.");
				return false;
			}
			sceneServerID = runtimeData.ID;
			return true;
		}

		#endregion
	}
}
