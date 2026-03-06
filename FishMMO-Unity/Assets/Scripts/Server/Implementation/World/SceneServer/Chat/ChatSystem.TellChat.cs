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
		/// Handles tell (private) chat messages. Queries the target character asynchronously from the database,
		/// marshals Broadcasts to the main thread, and persists the message on success.
		/// Returns false to suppress the synchronous DB save — the async path handles persistence.
		/// </summary>
		/// <param name="sender">Player character sending the message.</param>
		/// <param name="msg">Chat broadcast message.</param>
		/// <returns>False — persistence is handled inside the async path.</returns>
		public bool OnTellChat(IPlayerCharacter sender, ChatBroadcast msg)
		{
			// get the target
			string targetName = ChatHelper.GetWordAndTrimmed(msg.Text, out string trimmed);
			if (string.IsNullOrWhiteSpace(targetName))
			{
				// no target in the tell message
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
			TryEnqueueAsyncWork(() => OnTellChatAsync(senderConn, senderID, channel, targetName, trimmed, characterName, accountName, worldServerID, persist, receivedTicks), senderID);
			return false; // suppress synchronous save — async path handles it
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
				if (!result.IsSuccess || !result.Data.HasValue)
				{
					return;
				}

				CharacterData targetData = result.Data.Value;
				long targetID = targetData.ID;
				bool online = targetData.Online;

				// did we find the ID?
				if (targetID < 1)
				{
					return;
				}

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
					EnqueuePersist(senderID, characterName, accountName, worldServerId, channel, targetName + " " + trimmed, receivedTicks);
				}
			}
			catch (Exception ex)
			{
				await Log.Error("ChatSystem", $"Error in OnTellChatAsync (SenderID={senderID}, Target='{targetName}'): {ex}");
			}
		}
	}
}