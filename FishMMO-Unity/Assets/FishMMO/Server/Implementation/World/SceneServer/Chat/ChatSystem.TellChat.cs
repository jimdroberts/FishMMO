using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	// Private messaging channel handler: Tell.
	public partial class ChatSystem
	{
		/// <summary>
		/// Handles tell (private) chat messages. A target on this server is delivered to at once; a
		/// live whisper to anybody else is looked up in the database, to tell the sender whether it
		/// was relayed or the target is offline, and persisted so the target's own server delivers it.
		/// Returns false to suppress the caller's DB save — persistence is handled here.
		/// </summary>
		/// <param name="sender">Player character sending the message.</param>
		/// <param name="msg">Chat broadcast message.</param>
		/// <returns>False — persistence is handled inside the async path.</returns>
		public bool OnTellChat(IPlayerCharacter sender, ChatBroadcast msg)
		{
			/* The target is one word, or a name in double quotes: character names may hold spaces,
			 * and the first word of "Aragorn of Arnor" is somebody else. The same rule reads a live
			 * line and a pumped row, since the row is written in the same form (see ChatTellAddress).
			 * No target, an unclosed quote or nothing to say is dropped, as a tell with no body
			 * always was. */
			if (!ChatTellAddress.TryParse(msg.Text, out string targetName, out string trimmed))
			{
				return false;
			}

			// Reject oversized target names before any DB work.
			if (targetName.Length > Authentication.CharacterNameMaxLength)
			{
				return false;
			}

			// Short-circuit self-tell before the async DB round-trip.
			if (sender != null &&
				!string.IsNullOrEmpty(sender.CharacterName) &&
				sender.CharacterName.Equals(targetName, StringComparison.OrdinalIgnoreCase))
			{
				Server.NetworkWrapper.Broadcast(sender.Owner, new ChatBroadcast()
				{
					Channel = msg.Channel,
					SenderID = msg.SenderID,
					Text = ChatHelper.TELL_ERROR_MESSAGE_SELF + " ",
				}, true, Channel.Reliable);
				return false;
			}

			/* A live sender must be in a loaded scene. A null sender must NOT be refused here.
			 *
			 * null means the message came from the database pump, which is the only way a whisper
			 * reaches a target on a different scene server: the origin server persists the row,
			 * every other scene server fetches it and replays it through this handler with no
			 * sender. Refusing that case dropped the whisper on the floor at exactly the moment
			 * it was supposed to be delivered — and the origin server had already sent
			 * FISHMMO_TELL_RELAYED back to the player, so the sender was told the message was
			 * delivered while the recipient never saw it. Cross-scene-server whispers have
			 * therefore never worked, silently, in a way neither party could detect.
			 *
			 * Everything downstream is already null-safe for this: the sender echo is guarded on
			 * senderConn, and `persist` is false so the pump cannot re-persist what it just read. */
			if (sender != null && !sender.IsFlagged(CharacterFlags.IsLoaded))
				return false;

			/* A target on THIS server is resolved from the server's own map, on the main thread.
			 *
			 * Both paths used to look the target up by name in the database first: the live line
			 * on the sender's server, and the pumped copy on every other scene server, each only to
			 * learn whether the target was one of its own players (hot-path audit M17). Whether a
			 * character is here is a question this server answers exactly and for free. */
			if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData) &&
				mappingData.CharactersByLowerCaseName.TryGetValue(targetName.ToLowerInvariant(), out IPlayerCharacter localTarget) &&
				localTarget != null)
			{
				DeliverLocalTell(sender, msg, localTarget, targetName, trimmed);
				return false;
			}

			/* A pumped whisper for somebody who is not here is somebody else's to deliver. The pump
			 * only asks for tells to this server's own players, so this is the rare target who left
			 * between the read and now — and a database lookup could not have found them here either. */
			if (sender == null)
			{
				return false;
			}

			if (Server?.Database?.ServiceRegistry == null)
			{
				return false;
			}

			// Capture immutable data for the async path
			long senderID = msg.SenderID;
			ChatChannel channel = msg.Channel;
			NetworkConnection senderConn = sender?.Owner;
			string characterName = sender?.CharacterName ?? string.Empty;
			string accountName = sender?.Account ?? string.Empty;
			long worldServerID = sender != null ? sender.WorldServerID : 0;

			// Capture the receive timestamp ticks from the broadcast struct (stamped at the network boundary).
			long receivedTicks = msg.ReceivedUtcTicks;

			bool persist = sender != null;
			EnqueuePersistence(() => OnTellChatAsync(senderConn, senderID, channel, targetName, trimmed, characterName, accountName, worldServerID, persist, receivedTicks), senderID);
			return false; // suppress synchronous save — async path handles it
		}

		/// <summary>
		/// Delivers a whisper whose target is on this scene server, synchronously: the sender's
		/// relay confirmation, the target's copy, and — for a live line — the row that is the audit
		/// record of it. Main thread only.
		/// </summary>
		/// <param name="sender">The sender, or null for a pumped line (already persisted, no sender to answer).</param>
		/// <param name="msg">The whisper as received.</param>
		/// <param name="target">The target, resident here.</param>
		/// <param name="targetName">The target name as the sender typed it (normalised); persisted as its address, see <see cref="ChatTellAddress"/>.</param>
		/// <param name="trimmed">The whisper body.</param>
		private void DeliverLocalTell(IPlayerCharacter sender, ChatBroadcast msg, IPlayerCharacter target, string targetName, string trimmed)
		{
			if (sender != null)
			{
				// The name short-circuit above catches this too; an ID is the one that cannot be spelled two ways.
				if (target.ID == sender.ID)
				{
					Server.NetworkWrapper.Broadcast(sender.Owner, new ChatBroadcast()
					{
						Channel = msg.Channel,
						SenderID = msg.SenderID,
						Text = ChatHelper.TELL_ERROR_MESSAGE_SELF + " ",
					}, true, Channel.Reliable);
					return;
				}

				Server.NetworkWrapper.Broadcast(sender.Owner, new ChatBroadcast()
				{
					Channel = msg.Channel,
					SenderID = target.ID,
					Text = ChatHelper.TELL_RELAYED + " " + trimmed,
				}, true, Channel.Reliable);
			}

			Server.NetworkWrapper.Broadcast(target.Owner, new ChatBroadcast()
			{
				Channel = msg.Channel,
				SenderID = msg.SenderID,
				Text = trimmed,
			}, true, Channel.Reliable);

			// Only live player messages: a pumped line is already a row.
			if (sender != null)
			{
				EnqueuePersist(sender.ID, sender.CharacterName, sender.Account, sender.WorldServerID, msg.Channel, ChatTellAddress.FormatLine(targetName, trimmed), msg.ReceivedUtcTicks);
			}
		}

		/// <summary>
		/// Asynchronously resolves the target character by name, marshals Broadcasts to the main thread,
		/// and persists the chat message on success (unless called from the message pump).
		/// </summary>
		/// <param name="senderConn">Sender connection for relay/status responses.</param>
		/// <param name="senderID">Sender character identifier.</param>
		/// <param name="channel">Chat channel to broadcast.</param>
		/// <param name="targetName">Target character name.</param>
		/// <param name="trimmed">Message body without tell target prefix.</param>
		/// <param name="characterName">Sender character name used for persistence.</param>
		/// <param name="accountName">Sender account name used for persistence.</param>
		/// <param name="worldServerId">Sender world server identifier.</param>
		/// <param name="receivedTicks">UTC ticks when the server received the message, for legal audit persistence.</param>
		/// <returns>Asynchronous tell chat processing task.</returns>
		private async Task OnTellChatAsync(NetworkConnection senderConn, long senderID, ChatChannel channel, string targetName, string trimmed, string characterName, string accountName, long worldServerId, bool persist, long receivedTicks)
		{
			try
			{
				if (!TryGetDbService(out ICharacterService characterService))
				{
					return;
				}

				// Look up target character by name
				DatabaseResult<CharacterData?> result = await characterService.FetchAsync(targetName);

				/* A name that does not exist answers exactly as a name that is offline.
				 *
				 * The two used to be distinguishable: an offline character produced
				 * FISHMMO_TARGET_OFFLINE and a nonexistent one produced nothing at all. That
				 * turns /tell into a character-name oracle — a script can walk a name list and
				 * learn which characters exist on the shard, with no rate limit beyond ordinary
				 * chat throttling and nothing recorded anywhere. Character names are the handle
				 * players are known by and are worth as little as possible to enumerate.
				 *
				 * A database error is deliberately reported the same way rather than surfaced,
				 * for the same reason: the sender learns "not delivered", never why. The operator
				 * does learn why — a fault answered as "offline" is otherwise invisible. A name that
				 * fails validation is the sender's typing, not a fault, and is not logged: a player
				 * could otherwise write to the server log at chat rate. */
				if (!result.IsSuccess && result.ErrorCode != DatabaseErrorCodes.ValidationError)
				{
					await Log.Warning("ChatSystem", $"OnTellChatAsync could not look up tell target '{targetName}'; answered as offline: [{result.ErrorCode}] {result.ErrorMessage}");
				}

				bool resolved = result.IsSuccess && result.Data.HasValue && result.Data.Value.ID > 0;
				if (!resolved)
				{
					TryEnqueueMainThread(() =>
					{
						if (senderConn == null || !senderConn.IsActive)
						{
							return;
						}
						Server.NetworkWrapper.Broadcast(senderConn, new ChatBroadcast()
						{
							Channel = channel,
							SenderID = senderID,
							Text = ChatHelper.TARGET_OFFLINE + " " + targetName,
						}, true, Channel.Reliable);
					});
					return;
				}

				CharacterData targetData = result.Data.Value;
				long targetID = targetData.ID;
				bool online = targetData.Online;

				// Marshal Broadcasts to main thread
				TryEnqueueMainThread(() =>
				{
					// if the sender exists then we can send a return message if the target character is valid
					if (senderConn != null && senderConn.IsActive)
					{
						// are we messaging ourself?
						if (senderID == targetID)
						{
							Server.NetworkWrapper.Broadcast(senderConn, new ChatBroadcast()
							{
								Channel = channel,
								SenderID = senderID,
								Text = ChatHelper.TELL_ERROR_MESSAGE_SELF + " ",
							}, true, Channel.Reliable);
							return;
						}
						else if (!online)
						{
							// if the target character is not online
							Server.NetworkWrapper.Broadcast(senderConn, new ChatBroadcast()
							{
								Channel = channel,
								SenderID = senderID,
								Text = ChatHelper.TARGET_OFFLINE + " " + targetName,
							}, true, Channel.Reliable);
							return;
						}
						else if (targetID > 0)
						{
							Server.NetworkWrapper.Broadcast(senderConn, new ChatBroadcast()
							{
								Channel = channel,
								SenderID = targetID,
								Text = ChatHelper.TELL_RELAYED + " " + trimmed,
							}, true, Channel.Reliable);
						}
					}

					if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData))
					{
						// if the target character is on this server we send them the message
						if (mappingData.CharactersByID.TryGetValue(targetID, out IPlayerCharacter targetCharacter))
						{
							Server.NetworkWrapper.Broadcast(targetCharacter.Owner, new ChatBroadcast()
							{
								Channel = channel,
								SenderID = senderID,
								Text = trimmed,
							}, true, Channel.Reliable);
						}
					}
				});

				// Only persist for live player messages — pump-sourced messages are already persisted.
				if (persist)
				{
					// Enqueue for batch DB persistence instead of per-message async write.
					/* Addressed as it was typed, quoted when the name has a space: the target's own
					 * scene server finds the row by this address (ChatService.BuildPumpFilterSql). */
					EnqueuePersist(senderID, characterName, accountName, worldServerId, channel, ChatTellAddress.FormatLine(targetName, trimmed), receivedTicks);
				}
			}
			catch (Exception ex)
			{
				await Log.Error("ChatSystem", $"Error in OnTellChatAsync (SenderID={senderID}, Target='{targetName}'): {ex}");
			}
		}
	}
}