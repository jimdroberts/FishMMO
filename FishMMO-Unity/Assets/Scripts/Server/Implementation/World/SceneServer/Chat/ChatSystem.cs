using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Manages all chat functionality including public, party, guild, whisper, and system messages with rate limiting and spam protection.
	/// Game logic and Broadcasts run synchronously on the main thread.
	/// Database operations are fire-and-forget async to avoid blocking the main thread.
	/// Results from async DB queries that require main-thread Broadcasts are marshalled via IChatSystemMainThreadQueueData.
	/// </summary>
	[CreateAssetMenu(fileName = "ChatSystem", menuName = "FishMMO/Server/SceneServer/Chat System", order = 1)]
	[RequiresDataContainer(typeof(ChatSystemRuntimeData))]
	[RequiresDataContainer(typeof(ChatSystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public class ChatSystem : ServerBehaviour, IChatSystem
	{
		/// <summary>
		/// Maximum number of queued main-thread actions processed per frame.
		/// This time-slices queue draining to avoid frame spikes.
		/// </summary>
		[Header("Main Thread Dispatch")]
		[Tooltip("Max chat-system actions drained from main-thread queue per frame")]
		[SerializeField] private int maxMainThreadActionsPerFrame = 100;

		/// <summary>
		/// Internal message rate limit tracker.
		/// </summary>
		[SerializeField]
		[Tooltip("The server chat rate limit in milliseconds. This should be equal to the clients UIChat.messageRateLimit")]
		private float messageRateLimit = 500.0f;
		/// <summary>
		/// Maximum allowed chat message length.
		/// </summary>
		[SerializeField]
		[Tooltip("Maximum allowed chat message length.")]
		private int maxMessageLength = 128;

		/// <summary>
		/// If true, allows repeat messages from clients without spam filtering.
		/// </summary>
		public bool AllowRepeatMessages = false;
		/// <summary>
		/// The server chat rate limit in milliseconds. Should match the client's UIChat.messageRateLimit.
		/// </summary>
		public float MessageRateLimit => messageRateLimit;
		/// <summary>
		/// Maximum allowed chat message length.
		/// </summary>
		public int MaxMessageLength => maxMessageLength;
		/// <summary>
		/// The server chat message pump rate limit in seconds.
		/// </summary>
		[Tooltip("The server chat message pump rate limit in seconds.")]
		public float MessagePumpRate = 2.0f;
		/// <summary>
		/// Number of chat messages to fetch per database poll.
		/// </summary>
		public int MessageFetchCount = 20;

		/// <summary>
		/// Initializes the chat system, registering broadcast handlers and chat helper commands.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("ChatSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.DataContainerRegistry.TryGet<IChatSystemMainThreadQueueData>(out _))
			{
				Log.Error("ChatSystem", "Failed to initialize: IChatSystemMainThreadQueueData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			// Chat helper commands
			ChatHelper.InitializeOnce(GetChannelCommand);

			// Network broadcasts
			Server.NetworkWrapper.RegisterBroadcast<ChatBroadcast>(OnServerChatBroadcastReceived, true);

			// Periodic callbacks
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.RegisterPeriodicCallback(MessagePumpRate, OnPeriodicMessagePump);
			}

			maxMainThreadActionsPerFrame = Mathf.Max(1, maxMainThreadActionsPerFrame);

			Log.Debug("ChatSystem", $"Initialized (MessagePumpRate={MessagePumpRate}s, FetchCount={MessageFetchCount})");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Cleans up the chat system, unregistering broadcast handlers and chat helper commands.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("ChatSystem", "OnDeinitialize: Server is null");
				return;
			}

			// Drain any remaining queued main-thread actions
			DrainMainThreadQueue(drainAll: true);

			// Network broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<ChatBroadcast>(OnServerChatBroadcastReceived);

			// Periodic callbacks
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicMessagePump);
			}
		}

		/// <summary>
		/// Drains queued main-thread actions from the IChatSystemMainThreadQueueData container.
		/// Called from OnLateUpdate and OnDeinitialize.
		/// </summary>
		private void DrainMainThreadQueue(bool drainAll)
		{
			if (Server?.DataContainerRegistry.TryGet<IChatSystemMainThreadQueueData>(out var queueData) == true)
			{
				if (drainAll)
				{
					queueData.Drain();
				}
				else
				{
					queueData.Drain(maxMainThreadActionsPerFrame);
				}
			}
		}

		/// <summary>
		/// Enqueues an action to be executed on the main thread.
		/// </summary>
		/// <param name="action">The action to enqueue.</param>
		private void EnqueueMainThread(Action action)
		{
			if (Server?.DataContainerRegistry.TryGet<IChatSystemMainThreadQueueData>(out var queueData) == true)
			{
				queueData.Enqueue(action);
			}
		}

		/// <summary>
		/// Drains the main-thread queue each frame.
		/// </summary>
		public override void OnLateUpdate(float deltaTime)
		{
			DrainMainThreadQueue(drainAll: false);
		}

		/// <summary>
		/// Periodic callback that fetches and processes chat messages from the database asynchronously.
		/// </summary>
		/// <param name="deltaTime">Delta time parameter (unused).</param>
		private void OnPeriodicMessagePump(float deltaTime)
		{
			if (Initialized &&
				Server.ServerState == ConnectionState.Started &&
				Server.DataContainerRegistry.TryGet(out IChatSystemRuntimeData runtimeData))
			{
				lock (runtimeData)
				{
					if (runtimeData.MessagePumpInFlight != 0)
					{
						return;
					}

					runtimeData.MessagePumpInFlight = 1;
				}

				if (!TryEnqueueAsyncWork(() => FetchAndProcessChatMessagesAsync()))
				{
					lock (runtimeData)
					{
						runtimeData.MessagePumpInFlight = 0;
					}
				}
			}
		}

		/// <summary>
		/// Asynchronously fetches new chat messages from the database and marshals processing to the main thread.
		/// </summary>
		/// <returns>Asynchronous fetch-and-process task.</returns>
		private async Task FetchAndProcessChatMessagesAsync()
		{
			try
			{
				if (!Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> _))
				{
					return;
				}
				if (!Server.DataContainerRegistry.TryGet(out IChatSystemRuntimeData data))
				{
					return;
				}
				if (Server.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<IChatService>(out var chatService))
				{
					return;
				}

				long sceneServerID = Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeData) ? runtimeData.ID : 0;

				// Capture fetch state from main-thread data container
				DateTime lastFetchTime = data.LastFetchTime;
				long lastFetchPosition = data.LastFetchPosition;
				int fetchCount = MessageFetchCount;

				// Async DB fetch on background thread
				DatabaseResult<List<ChatData>> result = await chatService.FetchAsync(lastFetchTime, lastFetchPosition, fetchCount, sceneServerID);

				if (!result.IsSuccess || result.Data == null || result.Data.Count < 1)
				{
					return;
				}

				List<ChatData> messages = result.Data;
				ChatData latest = messages[messages.Count - 1];

				// Marshal processing to main thread — Broadcasts must run on main thread
				EnqueueMainThread(() =>
				{
					// Update fetch state on main thread
					if (Server.DataContainerRegistry.TryGet(out IChatSystemRuntimeData mainData))
					{
						mainData.LastFetchPosition = latest.ID;
						mainData.LastFetchTime = latest.TimeCreated;
					}

					ProcessChatMessages(messages);
				});
			}
			catch (Exception ex)
			{
				await Log.Error("ChatSystem", $"Error fetching chat messages: {ex}");
			}
			finally
			{
				if (Server.DataContainerRegistry.TryGet(out IChatSystemRuntimeData runtimeData))
				{
					lock (runtimeData)
					{
						runtimeData.MessagePumpInFlight = 0;
					}
				}
			}
		}

		/// <summary>
		/// Processes a list of chat messages, broadcasting them to appropriate channels and clients.
		/// Must be called on the main thread.
		/// </summary>
		/// <param name="messages">List of chat message data to process.</param>
		private void ProcessChatMessages(List<ChatData> messages)
		{
			if (messages == null || messages.Count < 1)
			{
				return;
			}
			foreach (ChatData message in messages)
			{
				ChatChannel channel = (ChatChannel)message.Channel;
				if (channel == ChatChannel.Discord)
				{
					OnSendDiscordMessage(message.WorldServerID, message.SceneServerID, message.Message);
				}
				else if (ChatHelper.ChatChannelCommands.TryGetValue(channel, out ChatCommandDetails sayCommand))
				{
					sayCommand.Func?.Invoke(null, new ChatBroadcast()
					{
						Channel = channel,
						Text = message.Message,
					});
				}
			}
		}

		/// <summary>
		/// Gets the chat command handler for a specific chat channel.
		/// </summary>
		/// <param name="channel">Chat channel to get the command for.</param>
		/// <returns>Chat command delegate for the channel.</returns>
		public ChatCommand GetChannelCommand(ChatChannel channel)
		{
			switch (channel)
			{
				case ChatChannel.World: return OnWorldChat;
				case ChatChannel.Region: return OnRegionChat;
				case ChatChannel.Party: return OnPartyChat;
				case ChatChannel.Guild: return OnGuildChat;
				case ChatChannel.Tell: return OnTellChat;
				case ChatChannel.Trade: return OnTradeChat;
				case ChatChannel.Say: return OnSayChat;
				// ChatChannel.System is Server->Client only. We never parse system messages locally.
				default: return OnSayChat;
			}
		}

		/// <summary>
		/// Handles incoming chat broadcast from a character, validates and processes the message.
		/// </summary>
		/// <param name="conn">Network connection of the sender.</param>
		/// <param name="msg">Chat broadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnServerChatBroadcastReceived(NetworkConnection conn, ChatBroadcast msg, Channel channel)
		{
			if (conn == null)
			{
				return;
			}

			if (conn.FirstObject != null)
			{
				IPlayerCharacter sender = conn.FirstObject.GetComponent<IPlayerCharacter>();
				ProcessNewChatMessage(conn, sender, msg);
			}
			else
			{
				conn.Kick(FishNet.Managing.Server.KickReason.UnexpectedProblem);
			}
		}

		/// <summary>
		/// Parses and processes a new chat message received from a connection, including validation, rate limiting, spam filtering, and command handling.
		/// </summary>
		/// <param name="conn">Network connection of the sender.</param>
		/// <param name="sender">Player character sending the message.</param>
		/// <param name="msg">Chat broadcast message.</param>
		private void ProcessNewChatMessage(NetworkConnection conn, IPlayerCharacter sender, ChatBroadcast msg)
		{
			// validate message length
			if (sender == null ||
				string.IsNullOrWhiteSpace(msg.Text) ||
				msg.Text.Length > maxMessageLength)
			{
				conn.Kick(FishNet.Managing.Server.KickReason.ExploitExcessiveData);
				return;
			}

			// we rate limit client chat, the message is ignored
			if (MessageRateLimit > 0)
			{
				if (sender.NextChatMessageTime > DateTime.UtcNow)
				{
					return;
				}
				sender.NextChatMessageTime = DateTime.UtcNow.AddMilliseconds(MessageRateLimit);
			}
			// we spam limit client chat, the message is ignored
			if (!AllowRepeatMessages)
			{
				if (!string.IsNullOrWhiteSpace(sender.LastChatMessage) &&
					sender.LastChatMessage.Equals(msg.Text))
				{
					return;
				}
				sender.LastChatMessage = msg.Text;
			}

			// remove Rich Text Tags if any exist
			msg.Text = ChatHelper.Sanitize(msg.Text);

			string cmd = ChatHelper.GetCommandAndTrim(ref msg.Text);

			// commands are handled differently from chat commands
			if (ChatHelper.TryParseCommand(cmd, sender, msg))
			{
				return;
			}

			// the text is empty
			if (msg.Text.Length < 1)
			{
				return;
			}

			if (ChatHelper.TryParseChatCommand(cmd, out ChatCommandDetails commandDetails))
			{
				msg.SenderID = sender.ID;
				msg.Channel = commandDetails.Channel;

				switch (msg.Channel)
				{
					case ChatChannel.Guild:
						if (!sender.TryGet(out IGuildController guildController) ||
							guildController.ID < 1)
						{
							return;
						}

						// add the senders guild ID
						msg.Text = guildController.ID + " " + msg.Text;
						break;
					case ChatChannel.Party:
						if (!sender.TryGet(out IPartyController partyController) ||
							partyController.ID < 1)
						{
							return;
						}

						// add the senders party ID
						msg.Text = partyController.ID + " " + msg.Text;
						break;
					case ChatChannel.Trade:
					case ChatChannel.World:
						// add the senders world id
						msg.Text = sender.WorldServerID + " " + msg.Text;
						break;
					default:
						break;
				}

				if (commandDetails.Func.Invoke(sender, msg))
				{
					// write the parsed message to the database (fire-and-forget async)
					TryEnqueueAsyncWork(() => PersistChatMessageAsync(sender.ID, sender.CharacterName, sender.Account, sender.WorldServerID, msg.Channel, msg.Text), sender.ID);
				}
			}
		}

		/// <summary>
		/// Asynchronously persists a chat message to the database.
		/// </summary>
		/// <param name="characterId">The character ID of the sender.</param>
		/// <param name="characterName">The character name of the sender.</param>
		/// <param name="accountName">The account name of the sender.</param>
		/// <param name="worldServerId">The world server ID.</param>
		/// <param name="channel">The chat channel.</param>
		/// <param name="message">The message text.</param>
		private async Task PersistChatMessageAsync(long characterId, string characterName, string accountName, long worldServerId, ChatChannel channel, string message)
		{
			try
			{
				if (Server.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<IChatService>(out var chatService))
				{
					return;
				}

				long sceneServerID = Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeData) ? runtimeData.ID : 0;

				await chatService.PersistAsync(
					characterId,
					characterName ?? string.Empty,
					accountName ?? string.Empty,
					worldServerId,
					sceneServerID,
					(FishMMO.Database.Data.Enums.ChatChannel)(byte)channel,
					message,
					DateTime.UtcNow);
			}
			catch (Exception ex)
			{
				await Log.Error("ChatSystem", $"Error persisting chat message (CharID={characterId}): {ex}");
			}
		}

		/// <summary>
		/// Handles world chat messages, broadcasting to all characters in the specified world.
		/// </summary>
		/// <param name="sender">Player character sending the message.</param>
		/// <param name="msg">Chat broadcast message.</param>
		/// <returns>True if message was broadcast, false otherwise.</returns>
		public bool OnWorldChat(IPlayerCharacter sender, ChatBroadcast msg)
		{
			// get the world ID
			string wid = ChatHelper.GetWordAndTrimmed(msg.Text, out string trimmed);
			if (string.IsNullOrWhiteSpace(wid) || !long.TryParse(wid, out long worldID))
			{
				// no worldID in the message
				return false;
			}

			ChatBroadcast newMsg = new ChatBroadcast()
			{
				Channel = msg.Channel,
				SenderID = msg.SenderID,
				Text = trimmed,
			};

			if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData) &&
				mappingData.CharactersByWorld.TryGetValue(worldID, out var characters))
			{
				// send to all world characters
				foreach (IPlayerCharacter character in new List<IPlayerCharacter>(characters.Values))
				{
					Server.NetworkWrapper.Broadcast(character.Owner, newMsg, true, Channel.Reliable);
				}
			}
			return true;
		}

		/// <summary>
		/// Handles region chat messages, broadcasting to all connections in the sender's scene.
		/// </summary>
		/// <param name="sender">Player character sending the message.</param>
		/// <param name="msg">Chat broadcast message.</param>
		/// <returns>False to prevent message from being written to the database.</returns>
		public bool OnRegionChat(IPlayerCharacter sender, ChatBroadcast msg)
		{
			if (sender == null)
			{
				return false;
			}
			// get the senders observed scene
			UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(sender.SceneName);
			if (scene.IsValid() &&
				Server.NetworkWrapper.NetworkManager != null &&
				Server.NetworkWrapper.NetworkManager.SceneManager != null)
			{
				if (Server.NetworkWrapper.NetworkManager.SceneManager.SceneConnections.TryGetValue(scene, out HashSet<NetworkConnection> connections))
				{
					foreach (NetworkConnection connection in connections)
					{
						Server.NetworkWrapper.Broadcast(connection, msg, true, Channel.Reliable);
					}
				}
			}
			return false; // we return false here so the message is not written to the database
		}

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
			if (Server.Database?.ServiceRegistry == null)
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

			TryEnqueueAsyncWork(() => OnPartyChatAsync(partyID, senderID, channel, trimmed, senderID, characterName, accountName, worldServerID), senderID);
			return false; // suppress synchronous save — async path handles it
		}

		/// <summary>
		/// Asynchronously fetches party members from the database, marshals Broadcasts to the main thread,
		/// and persists the chat message on success.
		/// </summary>
		/// <param name="partyID">Party identifier used to resolve recipients.</param>
		/// <param name="senderID">Sender character identifier.</param>
		/// <param name="channel">Chat channel to broadcast.</param>
		/// <param name="trimmed">Message body without command prefix/party token.</param>
		/// <param name="characterId">Sender character identifier used for persistence.</param>
		/// <param name="characterName">Sender character name used for persistence.</param>
		/// <param name="accountName">Sender account name used for persistence.</param>
		/// <param name="worldServerId">Sender world server identifier.</param>
		/// <returns>Asynchronous party chat processing task.</returns>
		private async Task OnPartyChatAsync(long partyID, long senderID, ChatChannel channel, string trimmed, long characterId, string characterName, string accountName, long worldServerId)
		{
			try
			{
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var partyService))
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
				EnqueueMainThread(() =>
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

				// Persist the chat message
				await PersistChatMessageAsync(characterId, characterName, accountName, worldServerId, channel, trimmed);
			}
			catch (Exception ex)
			{
				await Log.Error("ChatSystem", $"Error in OnPartyChatAsync: {ex}");
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
			if (Server.Database?.ServiceRegistry == null)
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

			TryEnqueueAsyncWork(() => OnGuildChatAsync(guildID, senderID, channel, trimmed, senderID, characterName, accountName, worldServerID), senderID);
			return false; // suppress synchronous save — async path handles it
		}

		/// <summary>
		/// Asynchronously fetches guild members from the database, marshals Broadcasts to the main thread,
		/// and persists the chat message on success.
		/// </summary>
		/// <param name="guildID">Guild identifier used to resolve recipients.</param>
		/// <param name="senderID">Sender character identifier.</param>
		/// <param name="channel">Chat channel to broadcast.</param>
		/// <param name="trimmed">Message body without command prefix/guild token.</param>
		/// <param name="characterId">Sender character identifier used for persistence.</param>
		/// <param name="characterName">Sender character name used for persistence.</param>
		/// <param name="accountName">Sender account name used for persistence.</param>
		/// <param name="worldServerId">Sender world server identifier.</param>
		/// <returns>Asynchronous guild chat processing task.</returns>
		private async Task OnGuildChatAsync(long guildID, long senderID, ChatChannel channel, string trimmed, long characterId, string characterName, string accountName, long worldServerId)
		{
			try
			{
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterGuildService>(out var guildService))
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
				EnqueueMainThread(() =>
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

				// Persist the chat message
				await PersistChatMessageAsync(characterId, characterName, accountName, worldServerId, channel, trimmed);
			}
			catch (Exception ex)
			{
				await Log.Error("ChatSystem", $"Error in OnGuildChatAsync: {ex}");
			}
		}

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

			if (Server.Database?.ServiceRegistry == null)
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

			TryEnqueueAsyncWork(() => OnTellChatAsync(senderConn, senderID, channel, targetName, trimmed, characterName, accountName, worldServerID), senderID);
			return false; // suppress synchronous save — async path handles it
		}

		/// <summary>
		/// Asynchronously resolves the target character by name, marshals Broadcasts to the main thread,
		/// and persists the chat message on success.
		/// </summary>
		/// <param name="senderConn">Sender connection for relay/status responses.</param>
		/// <param name="senderID">Sender character identifier.</param>
		/// <param name="channel">Chat channel to broadcast.</param>
		/// <param name="targetName">Target character name.</param>
		/// <param name="trimmed">Message body without tell target prefix.</param>
		/// <param name="characterName">Sender character name used for persistence.</param>
		/// <param name="accountName">Sender account name used for persistence.</param>
		/// <param name="worldServerId">Sender world server identifier.</param>
		/// <returns>Asynchronous tell chat processing task.</returns>
		private async Task OnTellChatAsync(NetworkConnection senderConn, long senderID, ChatChannel channel, string targetName, string trimmed, string characterName, string accountName, long worldServerId)
		{
			try
			{
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
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
				EnqueueMainThread(() =>
				{
					// if the sender exists then we can send a return message if the target character is valid
					if (senderConn != null)
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

				// Persist the chat message
				await PersistChatMessageAsync(senderID, characterName, accountName, worldServerId, channel, trimmed);
			}
			catch (Exception ex)
			{
				await Log.Error("ChatSystem", $"Error in OnTellChatAsync: {ex}");
			}
		}

		/// <summary>
		/// Handles trade chat messages, broadcasting to all characters in the specified world.
		/// </summary>
		/// <param name="sender">Player character sending the message.</param>
		/// <param name="msg">Chat broadcast message.</param>
		/// <returns>True if message was broadcast, false otherwise.</returns>
		public bool OnTradeChat(IPlayerCharacter sender, ChatBroadcast msg)
		{
			// get the world ID
			string wid = ChatHelper.GetWordAndTrimmed(msg.Text, out string trimmed);
			if (string.IsNullOrWhiteSpace(wid) || !long.TryParse(wid, out long worldID))
			{
				// no worldID in the message
				return false;
			}

			if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData))
			{
				ChatBroadcast newMsg = new ChatBroadcast()
				{
					Channel = msg.Channel,
					SenderID = msg.SenderID,
					Text = trimmed,
				};
				if (mappingData.CharactersByWorld.TryGetValue(worldID, out var characters))
				{
					// send to all world characters
					foreach (IPlayerCharacter character in new List<IPlayerCharacter>(characters.Values))
					{
						Server.NetworkWrapper.Broadcast(character.Owner, newMsg, true, Channel.Reliable);
					}
				}
				return true;
			}
			return false;
		}

		/// <summary>
		/// Handles say (local) chat messages, broadcasting to all observers of the sender.
		/// </summary>
		/// <param name="sender">Player character sending the message.</param>
		/// <param name="msg">Chat broadcast message.</param>
		/// <returns>False to prevent message from being written to the database.</returns>
		public bool OnSayChat(IPlayerCharacter sender, ChatBroadcast msg)
		{
			if (sender != null && sender.Observers != null)
			{
				// get the senders observed characters and send them the chat message
				foreach (NetworkConnection obsConnection in sender.Observers)
				{
					Server.NetworkWrapper.Broadcast(obsConnection, msg, true, Channel.Reliable);
				}
			}
			return false; // we return false here so the message is not written to the database
		}

		/// <summary>
		/// Allows the server to send system messages to the connection.
		/// </summary>
		/// <param name="conn">Network connection to send the system message to.</param>
		/// <param name="message">System message text.</param>
		public void OnSendSystemMessage(NetworkConnection conn, string message)
		{
			if (conn == null)
				return;

			ChatBroadcast msg = new ChatBroadcast()
			{
				Channel = ChatChannel.System,
				Text = message,
			};

			Server.NetworkWrapper.Broadcast(conn, msg, true, Channel.Reliable);
		}

		/// <summary>
		/// Allows the server to send Discord messages to a specific world server.
		/// </summary>
		/// <param name="worldServerID">World server ID to send the message to.</param>
		/// <param name="sceneServerID">Scene server ID (for context).</param>
		/// <param name="message">Discord message text.</param>
		public void OnSendDiscordMessage(long worldServerID, long sceneServerID, string message)
		{
			ChatBroadcast newMsg = new ChatBroadcast()
			{
				Channel = ChatChannel.Discord,
				SenderID = 0,
				Text = message,
			};

			if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData) &&
				mappingData.CharactersByWorld.TryGetValue(worldServerID, out var characters))
			{
				// send to all world characters
				foreach (IPlayerCharacter character in new List<IPlayerCharacter>(characters.Values))
				{
					Server.NetworkWrapper.Broadcast(character.Owner, newMsg, true, Channel.Reliable);
				}
			}
		}

		/// <summary>
		/// Enqueues an async work item to the centralized async worker for controlled execution.
		/// Returns false when the queue is unavailable or rejected due to backpressure.
		/// </summary>
		/// <param name="work">Asynchronous work delegate to queue.</param>
		/// <param name="entityKey">Optional entity key for ordered execution.</param>
		/// <param name="callerName">Optional caller name used for diagnostics.</param>
		/// <returns>True if work was accepted by the queue; otherwise false.</returns>
		private bool TryEnqueueAsyncWork(Func<Task> work, long entityKey = 0, [CallerMemberName] string callerName = null)
		{
			if (Server?.DataContainerRegistry.TryGet<IAsyncWorkerData>(out var asyncWorker) == true)
			{
				if (entityKey != 0)
				{
					if (asyncWorker.Enqueue(work, entityKey, callerName))
					{
						return true;
					}

					Log.Warning("ChatSystem", $"{callerName}: Async worker queue rejected work (entityKey={entityKey}).");
					return false;
				}
				else
				{
					if (asyncWorker.Enqueue(work, callerName))
					{
						return true;
					}

					Log.Warning("ChatSystem", $"{callerName}: Async worker queue rejected work.");
					return false;
				}
			}

			Log.Warning("ChatSystem", $"{callerName}: IAsyncWorkerData unavailable; work was not enqueued.");
			return false;
		}
	}
}