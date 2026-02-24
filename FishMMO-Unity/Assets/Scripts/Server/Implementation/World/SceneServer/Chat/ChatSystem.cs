using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;
using System;
using System.Collections.Generic;
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
	public partial class ChatSystem : ServerBehaviour, IChatSystem
	{
		/// <summary>
		/// Maximum additional characters a channel ID prefix (e.g., guild/party/world ID + space) can add to a message.
		/// </summary>
		private const int MaxChannelIdPrefixLength = 22;

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
		[SerializeField] private bool allowRepeatMessages = false;
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
		[SerializeField] private float messagePumpRate = 2.0f;
		/// <summary>
		/// Number of chat messages to fetch per database poll.
		/// </summary>
		[SerializeField] private int messageFetchCount = 20;
		/// <summary>
		/// If true, allows repeat messages from clients without spam filtering.
		/// </summary>
		public bool AllowRepeatMessages => allowRepeatMessages;
		/// <summary>
		/// The server chat message pump rate limit in seconds.
		/// </summary>
		public float MessagePumpRate => messagePumpRate;
		/// <summary>
		/// Number of chat messages to fetch per database poll.
		/// </summary>
		public int MessageFetchCount => messageFetchCount;

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

			if (!Server.DataContainerRegistry.TryGet<IChatSystemRuntimeData>(out var runtimeData))
			{
				Log.Error("ChatSystem", "Failed to initialize: IChatSystemRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			// Channel → handler dictionary (replaces switch in GetChannelCommand — OCP fix)
			runtimeData.ChannelCommandMap = new Dictionary<ChatChannel, ChatCommand>
			{
				{ ChatChannel.World, OnWorldChat },
				{ ChatChannel.Region, OnRegionChat },
				{ ChatChannel.Party, OnPartyChat },
				{ ChatChannel.Guild, OnGuildChat },
				{ ChatChannel.Tell, OnTellChat },
				{ ChatChannel.Trade, OnTradeChat },
				{ ChatChannel.Say, OnSayChat },
			};

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
			DrainMainThreadQueue<IChatSystemMainThreadQueueData>(maxMainThreadActionsPerFrame, drainAll);
		}

		/// <summary>
		/// Enqueues an action to be executed on the main thread.
		/// </summary>
		/// <param name="action">The action to enqueue.</param>
		private bool TryEnqueueMainThread(Action action)
		{
			return TryEnqueueMainThread<IChatSystemMainThreadQueueData>(action);
		}

		/// <summary>
		/// Clears the MessagePumpInFlight flag. Null-safe for shutdown scenarios.
		/// </summary>
		private void ClearMessagePumpFlag()
		{
			if (Server?.DataContainerRegistry.TryGet(out IChatSystemRuntimeData runtimeData) == true)
			{
				runtimeData.EndMessagePump();
			}
		}

		/// <summary>
		/// Drains the main-thread queue each frame.
		/// </summary>
		protected override void OnUpdate(float deltaTime)
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
				Server != null &&
				Server.ServerState == ConnectionState.Started &&
				Server.DataContainerRegistry.TryGet(out IChatSystemRuntimeData runtimeData))
			{
				if (!runtimeData.TryBeginMessagePump())
				{
					return;
				}

				if (!TryEnqueueAsyncWork(() => FetchAndProcessChatMessagesAsync()))
				{
					runtimeData.EndMessagePump();
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
				if (Server?.Database?.ServiceRegistry == null)
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
				if (!TryEnqueueMainThread(() =>
				{
					try
					{
						// Update fetch state on main thread
						if (Server?.DataContainerRegistry.TryGet(out IChatSystemRuntimeData mainData) == true)
						{
							mainData.LastFetchPosition = latest.ID;
							mainData.LastFetchTime = latest.TimeCreated;
						}

						ProcessChatMessages(messages);
					}
					finally
					{
						// C2 fix: clear pump flag AFTER cursor update, preventing re-fetch of same rows.
						ClearMessagePumpFlag();
					}
				}))
				{
					// Enqueue failed — clear flag immediately so pump can retry next interval.
					ClearMessagePumpFlag();
				}
			}
			catch (Exception ex)
			{
				await Log.Error("ChatSystem", $"Error fetching chat messages: {ex}");
				// C3 fix: null-conditional on Server to prevent NRE during shutdown.
				ClearMessagePumpFlag();
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
						SenderID = message.CharacterID,
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
			if (Server?.DataContainerRegistry.TryGet(out IChatSystemRuntimeData runtimeData) == true &&
				runtimeData.ChannelCommandMap != null &&
				runtimeData.ChannelCommandMap.TryGetValue(channel, out ChatCommand command))
			{
				return command;
			}
			// ChatChannel.System is Server->Client only. We never parse system messages locally.
			return OnSayChat;
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

				// Enforce maximum persistable message length after channel ID prepend.
				// A long ID + space adds up to 21 chars; cap at maxMessageLength + MaxChannelIdPrefixLength for safety.
				if (msg.Text.Length > maxMessageLength + MaxChannelIdPrefixLength)
				{
					msg.Text = msg.Text.Substring(0, maxMessageLength + MaxChannelIdPrefixLength);
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
				if (Server?.Database?.ServiceRegistry == null)
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
				mappingData.CharactersByWorld.TryGetValue(worldServerID, out var characters) &&
				Server.DataContainerRegistry.TryGet<IChatSystemRuntimeData>(out var chatData))
			{
				// Defensive copy into reusable buffer: Broadcast may trigger a disconnect callback that modifies the collection.
				var buffer = chatData.CharacterBroadcastBuffer;
				buffer.Clear();
				buffer.AddRange(characters.Values);
				for (int i = 0; i < buffer.Count; i++)
				{
					Server.NetworkWrapper.Broadcast(buffer[i].Owner, newMsg, true, Channel.Reliable);
				}
			}
		}
	}
}