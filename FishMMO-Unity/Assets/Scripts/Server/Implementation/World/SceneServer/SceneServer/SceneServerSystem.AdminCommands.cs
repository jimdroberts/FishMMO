using FishNet.Connection;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
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
	/// The in-game <c>/admin</c> command set for locking servers and scheduling maintenance
	/// shutdowns.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Registered once, as <c>/admin</c>, at <see cref="AccessLevel.Admin"/>. The sub-command is
	/// the first word of the remainder, so one registration and therefore one access check covers
	/// every operation — there is no way to add a sub-command that forgets to be gated.
	/// </para>
	/// <para>
	/// Every command writes the database row for the server it targets and returns; it never
	/// mutates a server's state directly, not even this one's. Each process adopts its own row on
	/// its next pulse, which is what lets a command typed on one scene server reach the world
	/// server and every other scene server under it. The cost is that changes take effect within
	/// a pulse or two rather than instantly, which is why the acknowledgement says what was
	/// written rather than claiming the server has already done it.
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

		/// <summary>
		/// Registers the <c>/admin</c> command. Called from the scene server's initialization.
		/// </summary>
		private void RegisterAdminCommands()
		{
			ChatHelper.AddCommands(new Dictionary<string, ChatCommand>()
			{
				{ "/admin", OnAdminCommand },
			}, AccessLevel.Admin);
		}

		/// <summary>
		/// Unregisters the <c>/admin</c> command. Called from the scene server's teardown.
		/// </summary>
		private void UnregisterAdminCommands()
		{
			ChatHelper.RemoveCommands(new[] { "/admin" });
		}

		/// <summary>
		/// Dispatches an <c>/admin</c> sub-command.
		/// </summary>
		/// <remarks>
		/// Access has already been checked by <see cref="ChatHelper.TryParseCommand"/> against
		/// the registration above; reaching this method means the caller is an administrator.
		/// </remarks>
		/// <param name="character">Administrator running the command.</param>
		/// <param name="msg">Chat message whose text is the sub-command and its arguments.</param>
		/// <returns>Always true: the command is consumed and never echoed to chat.</returns>
		private bool OnAdminCommand(IPlayerCharacter character, ChatBroadcast msg)
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
				case "lockserver":
				case "lockworld":
					SetWorldLock(character, locked: true);
					return true;

				case "unlockserver":
				case "unlockworld":
					SetWorldLock(character, locked: false);
					return true;

				case "lockscene":
					SetSceneLock(character, locked: true);
					return true;

				case "unlockscene":
					SetSceneLock(character, locked: false);
					return true;

				case "shutdown":
					ScheduleWorldShutdown(character, arguments);
					return true;

				case "stopshutdown":
					CancelWorldShutdown(character);
					return true;

				case "shutdownscene":
					ScheduleSceneShutdown(character, arguments);
					return true;

				case "stopshutdownscene":
					CancelSceneShutdown(character);
					return true;

				case "status":
				case "serverstatus":
					ReportStatus(character);
					return true;

				default:
					/* Split across replies to stay inside ChatBroadcast.MaxTextLength (128).
					 * Nothing enforces that constant today, but writing messages that exceed a
					 * documented wire limit is how it stops being true quietly. */
					Reply(character, "Admin: /admin status | lockserver | unlockserver | shutdown <seconds> | stopshutdown");
					Reply(character, "Admin: /admin lockscene | unlockscene | shutdownscene <seconds> | stopshutdownscene");
					return true;
			}
		}

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
			RunAdminAction(character, async () =>
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
					? "World locked. New logins refused except above Player. Players already online are unaffected."
					: "World unlocked. It is accepting logins again.";
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

			DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
			string adminName = character.CharacterName;

			RunAdminAction(character, async () =>
			{
				if (!TryGetDbService(out IWorldServerService worldServerService))
				{
					return "The world server service is unavailable.";
				}

				DatabaseResult result = await worldServerService.SetShutdownAsync(worldServerID, deadline);
				if (!result.IsSuccess)
				{
					return $"Could not schedule the shutdown: {result.ErrorCode} - {result.ErrorMessage}";
				}

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
			RunAdminAction(character, async () =>
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

				return "World shutdown cancelled. The world is still LOCKED — /admin unlockserver reopens it.";
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
			RunAdminAction(character, async () =>
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
					? $"Scene server {sceneServerID} locked. No new players will be routed here and no new scenes will load."
					: $"Scene server {sceneServerID} unlocked.";
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

			DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
			string adminName = character.CharacterName;

			RunAdminAction(character, async () =>
			{
				if (!TryGetDbService(out ISceneServerService sceneServerService))
				{
					return "The scene server service is unavailable.";
				}

				DatabaseResult result = await sceneServerService.SetShutdownAsync(sceneServerID, deadline);
				if (!result.IsSuccess)
				{
					return $"Could not schedule the shutdown: {result.ErrorCode} - {result.ErrorMessage}";
				}

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
			RunAdminAction(character, async () =>
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

				return $"Scene server {sceneServerID} shutdown cancelled. Still LOCKED — /admin unlockscene reopens it.";
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
			DateTime nowUtc = DateTime.UtcNow;

			long sceneServerID = Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeData)
				? runtimeData.ID
				: 0;

			Reply(character, $"Scene server {sceneServerID}: {(IsLockedNow() ? "LOCKED" : "open")}" +
				(sceneControlState.HasShutdown
					? $", shutting down in {sceneControlState.SecondsUntilShutdown(nowUtc):F0}s"
					: ", no shutdown scheduled") + ".");

			if (worldControlStates.Count == 0)
			{
				Reply(character, "No world control state has been read yet.");
				return;
			}

			foreach (var kvp in worldControlStates)
			{
				Reply(character, $"World {kvp.Key}: {(kvp.Value.Locked ? "LOCKED" : "open")}" +
					(kvp.Value.HasShutdown
						? $", shutting down in {kvp.Value.SecondsUntilShutdown(nowUtc):F0}s"
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
				Reply(character, "Usage: /admin shutdown <seconds>");
				return false;
			}

			if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds))
			{
				/* Echo at most a short prefix of what was typed. The text has already been
				 * stripped of rich-text tags on the way in, and this only ever goes back to the
				 * administrator who typed it, but a reply whose length is driven by input is
				 * still input-driven — and it would push past ChatBroadcast.MaxTextLength. */
				string shown = trimmed.Length > 32 ? trimmed.Substring(0, 32) + "..." : trimmed;
				Reply(character, $"'{shown}' is not a number of seconds.");
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

		/// <summary>
		/// Runs an administrative database action and reports its outcome back to the caller.
		/// </summary>
		/// <remarks>
		/// Every path answers. An operator command that appears to do nothing is worse than one
		/// that fails loudly — they will run it again, and on a shutdown that means a second
		/// deadline replacing the first. The reply is marshalled back to the main thread because
		/// the broadcast is a network call.
		/// </remarks>
		/// <param name="character">Administrator to answer.</param>
		/// <param name="action">Work to run; returns the message to send back.</param>
		private void RunAdminAction(IPlayerCharacter character, Func<Task<string>> action)
		{
			long characterID = character.ID;

			if (!TryEnqueueAsyncWork(async () =>
			{
				string reply;
				try
				{
					reply = await action();
				}
				catch (Exception ex)
				{
					await Log.Error("SceneServerSystem", $"Admin command failed: {ex}");
					reply = "The command failed. See the server log.";
				}

				string finalReply = reply;
				TryEnqueueMainThread(() => ReplyByCharacterID(characterID, finalReply));
			}, characterID))
			{
				Reply(character, "The server is busy and could not run that command. Try again in a moment.");
			}
		}

		/// <summary>Sends a system-channel line to a character. Main thread only.</summary>
		private void Reply(IPlayerCharacter character, string text)
		{
			NetworkConnection conn = character?.Owner;
			if (conn == null || !conn.IsActive || Server?.NetworkWrapper == null)
			{
				return;
			}

			Server.NetworkWrapper.Broadcast(conn, new ChatBroadcast()
			{
				Channel = ChatChannel.System,
				Text = text,
			}, true, FishNet.Transporting.Channel.Reliable);
		}

		/// <summary>
		/// Sends a system-channel line to a character resolved by id.
		/// </summary>
		/// <remarks>
		/// Resolved by id rather than by holding the character reference across the await: the
		/// administrator may have logged out, changed scene server or been despawned while the
		/// database work ran, and the object would then be a stale reference to a pooled
		/// instance now belonging to somebody else.
		/// </remarks>
		private void ReplyByCharacterID(long characterID, string text)
		{
			if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var charMapping) ||
				!charMapping.CharactersByID.TryGetValue(characterID, out IPlayerCharacter character))
			{
				return;
			}
			Reply(character, text);
		}

		#endregion
	}
}
