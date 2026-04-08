using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
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
	// Group-based async channel handlers: Party and Guild.
	public partial class ChatSystem
	{
		/// <summary>
		/// Handles party chat messages, querying party members asynchronously from the database
		/// and marshalling Broadcasts back to the main thread. Returns false to suppress the
		/// synchronous DB save — the async path handles persistence when broadcast succeeds.
		/// </summary>
		/// <param name="sender">Player character sending the message.</param>
		/// <param name="msg">Chat broadcast message.</param>
		/// <returns>False — persistence is handled inside the async path.</returns>
		public bool OnPartyChat(IPlayerCharacter sender, ChatBroadcast msg)
		{
			if (Server?.Database?.ServiceRegistry == null)
			{
				return false;
			}

			// get the party ID
			string gid = ChatHelper.GetWordAndTrimmed(msg.Text, out string trimmed);
			if (string.IsNullOrWhiteSpace(gid) || !long.TryParse(gid, out long partyID))
			{
				// no partyID in the message
				return false;
			}

			// Capture immutable data for the async path
			long senderID = msg.SenderID;
			ChatChannel channel = msg.Channel;
			string characterName = sender?.CharacterName ?? string.Empty;
			string accountName = sender?.Account ?? string.Empty;
			long worldServerID = sender != null ? sender.WorldServerID : 0;

			// Capture the receive timestamp ticks from the broadcast struct (stamped at the network boundary).
			long receivedTicks = msg.ReceivedUtcTicks;

			bool persist = sender != null;
			EnqueuePersistence(() => OnPartyChatAsync(partyID, senderID, channel, trimmed, senderID, characterName, accountName, worldServerID, persist, receivedTicks), senderID);
			return false; // suppress synchronous save — async path handles it
		}

		/// <summary>
		/// Asynchronously fetches party members from the database, marshals Broadcasts to the main thread,
		/// and persists the chat message on success (unless called from the message pump).
		/// </summary>
		/// <param name="partyID">Party identifier used to resolve recipients.</param>
		/// <param name="senderID">Sender character identifier.</param>
		/// <param name="channel">Chat channel to broadcast.</param>
		/// <param name="trimmed">Message body without command prefix/party token.</param>
		/// <param name="characterId">Sender character identifier used for persistence.</param>
		/// <param name="characterName">Sender character name used for persistence.</param>
		/// <param name="accountName">Sender account name used for persistence.</param>
		/// <param name="worldServerId">Sender world server identifier.</param>
		/// <param name="receivedTicks">UTC ticks when the server received the message, for legal audit persistence.</param>
		/// <returns>Asynchronous party chat processing task.</returns>
		private async Task OnPartyChatAsync(long partyID, long senderID, ChatChannel channel, string trimmed, long characterId, string characterName, string accountName, long worldServerId, bool persist, long receivedTicks)
		{
			try
			{
				if (!TryGetDbService(out ICharacterPartyService partyService))
				{
					return;
				}

				DatabaseResult<IReadOnlyList<CharacterPartyData>> result = await partyService.FetchManyAsync(partyID);
				if (!result.IsSuccess || result.Data == null || result.Data.Count < 1)
				{
					return;
				}

				IReadOnlyList<CharacterPartyData> members = result.Data;

				// Marshal Broadcasts to main thread
				TryEnqueueMainThread(() =>
				{
					if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData))
					{
						return;
					}

					ChatBroadcast newMsg = new ChatBroadcast()
					{
						Channel = channel,
						SenderID = senderID,
						Text = trimmed,
					};

					foreach (CharacterPartyData member in members)
					{
						if (mappingData.CharactersByID.TryGetValue(member.CharacterID, out IPlayerCharacter character))
						{
							// broadcast to party member...
							Server.NetworkWrapper.Broadcast(character.Owner, newMsg, true, Channel.Reliable);
						}
					}
				});

				// Only persist for live player messages — pump-sourced messages are already persisted.
				if (persist)
				{
					// Enqueue for batch DB persistence instead of per-message async write.
					EnqueuePersist(characterId, characterName, accountName, worldServerId, channel, partyID + " " + trimmed, receivedTicks);
				}
			}
			catch (Exception ex)
			{
				await Log.Error("ChatSystem", $"Error in OnPartyChatAsync (PartyID={partyID}, SenderID={senderID}): {ex}");
			}
		}

		/// <summary>
		/// Handles guild chat messages, querying guild members asynchronously from the database
		/// and marshalling Broadcasts back to the main thread. Returns false to suppress the
		/// synchronous DB save — the async path handles persistence when broadcast succeeds.
		/// </summary>
		/// <param name="sender">Player character sending the message.</param>
		/// <param name="msg">Chat broadcast message.</param>
		/// <returns>False — persistence is handled inside the async path.</returns>
		public bool OnGuildChat(IPlayerCharacter sender, ChatBroadcast msg)
		{
			if (Server?.Database?.ServiceRegistry == null)
			{
				return false;
			}

			// get the guild ID
			string gid = ChatHelper.GetWordAndTrimmed(msg.Text, out string trimmed);
			if (string.IsNullOrWhiteSpace(gid) || !long.TryParse(gid, out long guildID))
			{
				// no guildID in the message
				return false;
			}

			// Capture immutable data for the async path
			long senderID = msg.SenderID;
			ChatChannel channel = msg.Channel;
			string characterName = sender?.CharacterName ?? string.Empty;
			string accountName = sender?.Account ?? string.Empty;
			long worldServerID = sender != null ? sender.WorldServerID : 0;

			// Capture the receive timestamp ticks from the broadcast struct (stamped at the network boundary).
			long receivedTicks = msg.ReceivedUtcTicks;

			bool persist = sender != null;
			EnqueuePersistence(() => OnGuildChatAsync(guildID, senderID, channel, trimmed, senderID, characterName, accountName, worldServerID, persist, receivedTicks), senderID);
			return false; // suppress synchronous save — async path handles it
		}

		/// <summary>
		/// Asynchronously fetches guild members from the database, marshals Broadcasts to the main thread,
		/// and persists the chat message on success (unless called from the message pump).
		/// </summary>
		/// <param name="guildID">Guild identifier used to resolve recipients.</param>
		/// <param name="senderID">Sender character identifier.</param>
		/// <param name="channel">Chat channel to broadcast.</param>
		/// <param name="trimmed">Message body without command prefix/guild token.</param>
		/// <param name="characterId">Sender character identifier used for persistence.</param>
		/// <param name="characterName">Sender character name used for persistence.</param>
		/// <param name="accountName">Sender account name used for persistence.</param>
		/// <param name="worldServerId">Sender world server identifier.</param>
		/// <param name="receivedTicks">UTC ticks when the server received the message, for legal audit persistence.</param>
		/// <returns>Asynchronous guild chat processing task.</returns>
		private async Task OnGuildChatAsync(long guildID, long senderID, ChatChannel channel, string trimmed, long characterId, string characterName, string accountName, long worldServerId, bool persist, long receivedTicks)
		{
			try
			{
				if (!TryGetDbService(out ICharacterGuildService guildService))
				{
					return;
				}

				DatabaseResult<IReadOnlyList<CharacterGuildData>> result = await guildService.FetchManyAsync(guildID);
				if (!result.IsSuccess || result.Data == null || result.Data.Count < 1)
				{
					return;
				}

				IReadOnlyList<CharacterGuildData> members = result.Data;

				// Marshal Broadcasts to main thread
				TryEnqueueMainThread(() =>
				{
					if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData))
					{
						return;
					}

					ChatBroadcast newMsg = new ChatBroadcast()
					{
						Channel = channel,
						SenderID = senderID,
						Text = trimmed,
					};

					foreach (CharacterGuildData member in members)
					{
						if (mappingData.CharactersByID.TryGetValue(member.CharacterID, out IPlayerCharacter character))
						{
							// broadcast to guild member...
							Server.NetworkWrapper.Broadcast(character.Owner, newMsg, true, Channel.Reliable);
						}
					}
				});

				// Only persist for live player messages — pump-sourced messages are already persisted.
				if (persist)
				{
					// Enqueue for batch DB persistence instead of per-message async write.
					EnqueuePersist(characterId, characterName, accountName, worldServerId, channel, guildID + " " + trimmed, receivedTicks);
				}
			}
			catch (Exception ex)
			{
				await Log.Error("ChatSystem", $"Error in OnGuildChatAsync (GuildID={guildID}, SenderID={senderID}): {ex}");
			}
		}
	}
}