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
using FishMMO.Shared.Core;
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
		/// Maximum number of inbound chat messages dequeued from the lock-free
		/// incoming queue and processed per frame. Prevents network spikes
		/// from monopolising the main thread.
		/// </summary>
		[Tooltip("Max incoming chat messages processed from the lock-free queue per frame")]
		[SerializeField] private int maxIncomingChatsPerFrame = 500;

		/// <summary>
		/// Hard cap on the incoming chat queue size. If a client enqueues a message
		/// while the queue already has this many entries, the connection is kicked
		/// to prevent memory exhaustion from a flood attack.
		/// </summary>
		[Tooltip("Maximum pending incoming chat messages before the sender is kicked (DoS protection)")]
		[SerializeField] private int maxIncomingQueueSize = 10000;

		/// <summary>
		/// Internal message rate limit tracker.
		/// </summary>
		[SerializeField]
		[Tooltip("The server chat rate limit in milliseconds. This should be equal to the clients UIChat.messageRateLimit")]
		private float messageRateLimit = 500.0f;

		/// <summary>
		/// Token bucket capacity — maximum burst of messages a player can send before throttling.
		/// </summary>
		[Header("Token Bucket Anti-Spam")]
		[Tooltip("Maximum number of chat tokens a player can accumulate (burst capacity)")]
		[SerializeField] private int chatTokenBucketCapacity = 5;

		/// <summary>
		/// Tokens refilled per second. At the default 1.0, a player regains one message
		/// permit per second after exhausting the burst capacity.
		/// </summary>
		[Tooltip("Tokens refilled per second (1.0 = one message permit per second)")]
		[SerializeField] private float chatTokenRefillRate = 1.0f;

		/// <summary>
		/// Interval in seconds between batch DB persistence flushes.
		/// Messages are queued in a lock-free queue and flushed periodically
		/// to reduce per-message DB round-trips.
		/// </summary>
		[Header("Batch DB Persistence")]
		[Tooltip("Seconds between batch DB persistence flushes")]
		[SerializeField] private float persistFlushIntervalSeconds = 0.1f;

		/// <summary>
		/// Maximum number of messages drained from the persist queue per flush.
		/// Caps the DB batch size to prevent a single flush from spiking the database
		/// when tens of thousands of messages have accumulated (e.g., after a stall).
		/// Overflow stays in the queue for the next flush cycle.
		/// </summary>
		[Tooltip("Maximum messages written to the database per flush (overflow carries to next flush)")]
		[SerializeField] private int maxPersistBatchSize = 2000;

		/// <summary>
		/// Interval in seconds between outbound World/Trade broadcast flushes.
		/// Buffered messages are batched per-world and sent in a single burst,
		/// reducing per-message network overhead for large channels.
		/// </summary>
		[Header("Outbound Broadcast Batching")]
		[Tooltip("Seconds between outbound World/Trade broadcast flushes (0.05 = 50ms)")]
		[SerializeField] private float outboundBatchIntervalSeconds = 0.05f;

		/// <summary>
		/// Maximum number of buffered World/Trade messages sent to each recipient per flush.
		/// Prevents a single flush from generating excessive network traffic.
		/// </summary>
		[Tooltip("Maximum buffered World/Trade messages sent per recipient per flush")]
		[SerializeField] private int maxOutboundBatchSize = 10;

		/// <summary>
		/// Hard cap on the total number of buffered World/Trade messages per world ID.
		/// If a flush stalls or drains too slowly, oldest messages are dropped once this limit is hit.
		/// Prevents unbounded memory growth in <see cref="IChatSystemRuntimeData.OutboundWorldBroadcastBuffer"/>.
		/// </summary>
		[Tooltip("Maximum buffered World/Trade messages per world (oldest dropped when exceeded)")]
		[SerializeField] private int maxBufferedWorldMessages = 200;
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
				periodicSystem.RegisterPeriodicCallback(persistFlushIntervalSeconds, OnPeriodicPersistFlush);
				periodicSystem.RegisterPeriodicCallback(outboundBatchIntervalSeconds, OnPeriodicOutboundFlush);
			}

			maxMainThreadActionsPerFrame = Mathf.Max(1, maxMainThreadActionsPerFrame);
			maxIncomingChatsPerFrame = Mathf.Max(1, maxIncomingChatsPerFrame);
			maxIncomingQueueSize = Mathf.Max(100, maxIncomingQueueSize);
			chatTokenBucketCapacity = Mathf.Max(1, chatTokenBucketCapacity);
			chatTokenRefillRate = Mathf.Max(0.01f, chatTokenRefillRate);
			persistFlushIntervalSeconds = Mathf.Max(0.01f, persistFlushIntervalSeconds);
			maxPersistBatchSize = Mathf.Max(1, maxPersistBatchSize);
			outboundBatchIntervalSeconds = Mathf.Max(0.01f, outboundBatchIntervalSeconds);
			maxOutboundBatchSize = Mathf.Max(1, maxOutboundBatchSize);
			maxBufferedWorldMessages = Mathf.Max(1, maxBufferedWorldMessages);

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

			// Flush any remaining outbound World/Trade broadcast buffers.
			FlushOutboundBroadcastBuffers();

			// Signal shutdown so async flush paths exit early and don't race with the sync flush.
			if (Server.DataContainerRegistry.TryGet(out IChatSystemRuntimeData shutdownData))
			{
				shutdownData.IsShuttingDown = true;
			}

			// Flush any remaining pending persist entries before shutdown.
			FlushPersistQueueSync();

			// Drain any remaining incoming chat queue entries
			DrainIncomingChatQueue(drainAll: true);

			// Drain any remaining queued main-thread actions
			DrainMainThreadQueue(drainAll: true);

			// Network broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<ChatBroadcast>(OnServerChatBroadcastReceived);

			// Periodic callbacks
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicMessagePump);
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicPersistFlush);
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicOutboundFlush);
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
		/// Each frame: drain the lock-free incoming chat queue, then the main-thread action queue.
		/// </summary>
		protected override void OnUpdate(float deltaTime)
		{
			DrainIncomingChatQueue(drainAll: false);
			DrainMainThreadQueue(drainAll: false);
		}

		/// <summary>
		/// Drains up to <see cref="maxIncomingChatsPerFrame"/> entries from the lock-free
		/// <see cref="IChatSystemRuntimeData.IncomingChatQueue"/> and passes each to
		/// <see cref="ProcessNewChatMessage"/>. When <paramref name="drainAll"/> is true
		/// (shutdown), all remaining entries are processed.
		/// </summary>
		private void DrainIncomingChatQueue(bool drainAll)
		{
			if (!Server.DataContainerRegistry.TryGet(out IChatSystemRuntimeData runtimeData))
			{
				return;
			}

			var queue = runtimeData.IncomingChatQueue;
			if (queue == null)
			{
				return;
			}

			int budget = drainAll ? int.MaxValue : maxIncomingChatsPerFrame;
			int processed = 0;

			while (processed < budget && queue.TryDequeue(out var entry))
			{
				runtimeData.DecrementIncomingQueueSize();
				processed++;

				// Connection may have disconnected between enqueue and dequeue — skip stale entries.
				if (entry.Connection == null || !entry.Connection.IsActive)
				{
					continue;
				}

				if (entry.Connection.FirstObject != null)
				{
					IPlayerCharacter sender = entry.Connection.FirstObject.GetComponent<IPlayerCharacter>();
					ProcessNewChatMessage(entry.Connection, sender, entry.Message);
				}
				else
				{
					entry.Connection.Kick(FishNet.Managing.Server.KickReason.UnexpectedProblem);
				}
			}
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
			bool handedOffToMainThread = false;
			try
			{
				// Shutdown guard: abort if server is no longer running.
				if (Server == null || Server.ServerState != ConnectionState.Started)
				{
					return;
				}
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
				if (TryEnqueueMainThread(() =>
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
						// Clear pump flag AFTER cursor update, preventing re-fetch of same rows.
						ClearMessagePumpFlag();
					}
				}))
				{
					handedOffToMainThread = true;
				}
			}
			catch (Exception ex)
			{
				await Log.Error("ChatSystem", $"Error fetching chat messages: {ex}");
			}
			finally
			{
				// Safety net: if the main-thread callback didn't take ownership of the flag,
				// clear it here so the pump doesn't deadlock on early returns or failures.
				if (!handedOffToMainThread)
				{
					ClearMessagePumpFlag();
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
					// sender is intentionally null for pump-sourced messages:
					// async handlers use null to suppress persistence (already persisted)
					// and skip sender-specific operations (connection relay, etc.).
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

			// Stamp the exact server receipt time for legal audit / subpoena compliance.
			// Travels inside the struct so no shared mutable state between same-frame messages.
			msg.ReceivedUtcTicks = DateTime.UtcNow.Ticks;

			// Enqueue into the lock-free incoming queue for main-thread draining.
			// This decouples the network callback from game-logic processing,
			// preventing network spikes from freezing gameplay.
			if (Server.DataContainerRegistry.TryGet(out IChatSystemRuntimeData runtimeData) &&
				runtimeData.IncomingChatQueue != null)
			{
				// DoS protection: atomically increment the counter (O(1)) and check
				// against the hard cap. If over limit, decrement back and kick.
				// Replaces ConcurrentQueue.Count which is O(N).
				int newSize = runtimeData.IncrementIncomingQueueSize();
				if (newSize > maxIncomingQueueSize)
				{
					runtimeData.DecrementIncomingQueueSize();
					conn.Kick(FishNet.Managing.Server.KickReason.ExploitExcessiveData);
					return;
				}

				runtimeData.IncomingChatQueue.Enqueue((conn, msg));
			}
		}

		/// <summary>
		/// Parses and processes a new chat message received from a connection, including validation, rate limiting, spam filtering, and command handling.
		/// </summary>
		/// <param name="conn">Network connection of the sender.</param>
		/// <param name="sender">Player character sending the message.</param>
		/// <param name="msg">Chat broadcast message (ReceivedUtcTicks already stamped at the network boundary).</param>
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

			// Prevent unloaded characters from chatting. Dead players can still send messages.
			if (!sender.IsFlagged(CharacterFlags.IsLoaded))
				return;

			// Use ticks directly from the broadcast struct — avoids DateTime allocation per message.
			long receivedTicks = msg.ReceivedUtcTicks;

			// --- Token Bucket Anti-Spam ---
			// Refill tokens based on elapsed time since last refill, then consume one.
			// If the bucket is empty the message is silently dropped.
			if (chatTokenBucketCapacity > 0 && chatTokenRefillRate > 0f)
			{
				double elapsedSeconds = (receivedTicks - sender.ChatTokenLastRefillTicks) / (double)TimeSpan.TicksPerSecond;
				if (elapsedSeconds > 0)
				{
					if (sender.IsChatTokensFull)
					{
						sender.ChatTokens = chatTokenBucketCapacity;
						sender.IsChatTokensFull = false;
					}
					else
					{
						sender.ChatTokens += elapsedSeconds * chatTokenRefillRate;
						if (sender.ChatTokens > chatTokenBucketCapacity)
						{
							sender.ChatTokens = chatTokenBucketCapacity;
						}
					}
					sender.ChatTokenLastRefillTicks = receivedTicks;
				}

				if (sender.ChatTokens < 1.0)
				{
					return; // throttled — bucket empty
				}

				sender.ChatTokens -= 1.0;
			}

			// Legacy per-message cooldown (kept as secondary gate alongside the token bucket).
			if (MessageRateLimit > 0)
			{
				if (sender.NextChatMessageTicks > receivedTicks)
				{
					return;
				}
				sender.NextChatMessageTicks = receivedTicks + (long)(MessageRateLimit * TimeSpan.TicksPerMillisecond);
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

			// Remove Rich Text Tags only if any might exist (avoids allocation when text is clean).
			if (msg.Text.IndexOf('<') >= 0)
			{
				msg.Text = ChatHelper.Sanitize(msg.Text);
			}

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
					// Enqueue for batch DB persistence instead of per-message async write.
					EnqueuePersist(sender.ID, sender.CharacterName, sender.Account, sender.WorldServerID, msg.Channel, msg.Text, receivedTicks);
				}
			}
		}

		/// <summary>
		/// Enqueues a chat message for batch DB persistence.
		/// Thread-safe — can be called from main thread or async paths.
		/// </summary>
		private void EnqueuePersist(long characterId, string characterName, string accountName, long worldServerId, ChatChannel channel, string message, long receivedTicks)
		{
			if (Server?.DataContainerRegistry.TryGet(out IChatSystemRuntimeData runtimeData) == true &&
				runtimeData.PendingPersistQueue != null)
			{
				runtimeData.PendingPersistQueue.Enqueue(new PendingChatPersist(
					characterId,
					characterName ?? string.Empty,
					accountName ?? string.Empty,
					worldServerId,
					channel,
					message,
					receivedTicks));
			}
		}

		/// <summary>
		/// Periodic callback that dispatches <see cref="FlushPersistQueueAsync"/> onto the async worker.
		/// </summary>
		private void OnPeriodicPersistFlush(float deltaTime)
		{
			if (!Initialized ||
				Server == null ||
				Server.ServerState != ConnectionState.Started)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet(out IChatSystemRuntimeData runtimeData) ||
				runtimeData.PendingPersistQueue == null ||
				runtimeData.PendingPersistQueue.IsEmpty)
			{
				return;
			}

			if (!TryEnqueueAsyncWork(() => FlushPersistQueueAsync()))
			{
				Log.Error("ChatSystem", "Failed to enqueue chat persist flush. Messages remain queued for next cycle.");
			}
		}

		/// <summary>
		/// Drains the <see cref="IChatSystemRuntimeData.PendingPersistQueue"/> and writes all entries
		/// to the database in a single batch via <c>PersistBatchAsync</c>.
		/// Runs on a background thread via the async worker.
		/// </summary>
		private async Task FlushPersistQueueAsync()
		{
			try
			{
				if (Server == null || Server.ServerState != ConnectionState.Started)
				{
					return;
				}
				if (!Server.DataContainerRegistry.TryGet(out IChatSystemRuntimeData runtimeData) ||
					runtimeData.PendingPersistQueue == null)
				{
					return;
				}

				// Shutdown race guard: the sync flush in OnDeinitialize owns the queue now.
				if (runtimeData.IsShuttingDown)
				{
					return;
				}

				if (Server.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<IChatService>(out var chatService))
				{
					return;
				}

				long sceneServerID = Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var sceneRuntimeData)
					? sceneRuntimeData.ID : 0;

				// Reuse the persistent batch buffer to avoid per-flush allocation.
				var batch = runtimeData.PersistBatchBuffer;
				batch.Clear();

				while (batch.Count < maxPersistBatchSize && runtimeData.PendingPersistQueue.TryDequeue(out PendingChatPersist entry))
				{
					batch.Add((
						entry.CharacterId,
						entry.CharacterName,
						entry.AccountName,
						entry.WorldServerId,
						sceneServerID,
						(FishMMO.Database.Data.Enums.ChatChannel)(byte)entry.Channel,
						entry.Message,
						new DateTime(entry.ReceivedTicks, DateTimeKind.Utc)));
				}

				if (batch.Count < 1)
				{
					return;
				}

				DatabaseResult result = await chatService.PersistBatchAsync(batch);
				if (!result.IsSuccess)
				{
					await Log.Warning("ChatSystem", $"FlushPersistQueueAsync DB error ({batch.Count} messages): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("ChatSystem", $"Error in FlushPersistQueueAsync: {ex}");
			}
		}

		/// <summary>
		/// Synchronous shutdown helper: drains the persist queue into a blocking batch write.
		/// Called from <see cref="OnDeinitialize"/> to flush any remaining messages before the
		/// server shuts down.
		/// </summary>
		private void FlushPersistQueueSync()
		{
			try
			{
				if (!Server.DataContainerRegistry.TryGet(out IChatSystemRuntimeData runtimeData) ||
					runtimeData.PendingPersistQueue == null ||
					runtimeData.PendingPersistQueue.IsEmpty)
				{
					return;
				}
				if (Server.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<IChatService>(out var chatService))
				{
					return;
				}

				long sceneServerID = Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var sceneRuntimeData)
					? sceneRuntimeData.ID : 0;

				// Reuse the persistent batch buffer to avoid shutdown allocation.
				var batch = runtimeData.PersistBatchBuffer;
				batch.Clear();

				while (runtimeData.PendingPersistQueue.TryDequeue(out PendingChatPersist entry))
				{
					batch.Add((
						entry.CharacterId,
						entry.CharacterName,
						entry.AccountName,
						entry.WorldServerId,
						sceneServerID,
						(FishMMO.Database.Data.Enums.ChatChannel)(byte)entry.Channel,
						entry.Message,
						new DateTime(entry.ReceivedTicks, DateTimeKind.Utc)));
				}

				if (batch.Count > 0)
				{
					DatabaseResult result = chatService.PersistBatchAsync(batch).GetAwaiter().GetResult();
					if (!result.IsSuccess)
					{
						Log.Warning("ChatSystem", $"FlushPersistQueueSync DB error ({batch.Count} messages): {result.ErrorCode} - {result.ErrorMessage}");
					}
				}
			}
			catch (Exception ex)
			{
				Log.Error("ChatSystem", $"Error in FlushPersistQueueSync: {ex}");
			}
		}

		/// <summary>
		/// Periodic callback that flushes buffered World/Trade outbound broadcasts.
		/// Called from the periodic update system at <see cref="outboundBatchIntervalSeconds"/>.
		/// </summary>
		private void OnPeriodicOutboundFlush(float deltaTime)
		{
			if (!Initialized ||
				Server == null ||
				Server.ServerState != ConnectionState.Started)
			{
				return;
			}

			FlushOutboundBroadcastBuffers();
		}

		/// <summary>
		/// Flushes all buffered outbound World/Trade broadcasts to their recipients.
		/// For each world ID, sends up to <see cref="maxOutboundBatchSize"/> messages per recipient per flush.
		/// Must be called on the main thread.
		/// </summary>
		private void FlushOutboundBroadcastBuffers()
		{
			if (!Server.DataContainerRegistry.TryGet(out IChatSystemRuntimeData runtimeData))
			{
				return;
			}

			var buffer = runtimeData.OutboundWorldBroadcastBuffer;
			if (buffer == null || buffer.Count < 1)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData))
			{
				return;
			}

			// Collect keys into reusable buffer to avoid modifying dictionary during iteration.
			var keyBuffer = runtimeData.OutboundWorldFlushKeyBuffer;
			keyBuffer.Clear();
			foreach (var kvp in buffer)
			{
				keyBuffer.Add(kvp.Key);
			}

			var characterBroadcastBuffer = runtimeData.CharacterBroadcastBuffer;

			for (int k = 0; k < keyBuffer.Count; k++)
			{
				long worldID = keyBuffer[k];
				if (!buffer.TryGetValue(worldID, out var messages) || messages.Count < 1)
				{
					continue;
				}

				// Cap messages sent per flush to prevent huge network bursts.
				int sendCount = Math.Min(messages.Count, maxOutboundBatchSize);

				if (mappingData.CharactersByWorld.TryGetValue(worldID, out var characters))
				{
					// Defensive copy into reusable buffer.
					// Manual loop avoids boxing the Dictionary.ValueCollection struct enumerator
					// that AddRange(IEnumerable<T>) would cause.
					characterBroadcastBuffer.Clear();
					foreach (var character in characters.Values)
					{
						characterBroadcastBuffer.Add(character);
					}

					for (int m = 0; m < sendCount; m++)
					{
						for (int c = 0; c < characterBroadcastBuffer.Count; c++)
						{
							Server.NetworkWrapper.Broadcast(characterBroadcastBuffer[c].Owner, messages[m], true, Channel.Reliable);
						}
					}
				}

				// Remove sent messages; keep overflow for next flush.
				if (sendCount >= messages.Count)
				{
					messages.Clear();
				}
				else
				{
					messages.RemoveRange(0, sendCount);
				}
			}

			// Remove empty world entries to keep dictionary tidy.
			for (int k = keyBuffer.Count - 1; k >= 0; k--)
			{
				long worldID = keyBuffer[k];
				if (buffer.TryGetValue(worldID, out var remaining) && remaining.Count < 1)
				{
					buffer.Remove(worldID);
				}
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
				// Manual loop avoids boxing the Dictionary.ValueCollection struct enumerator.
				var buffer = chatData.CharacterBroadcastBuffer;
				buffer.Clear();
				foreach (var character in characters.Values)
				{
					buffer.Add(character);
				}
				for (int i = 0; i < buffer.Count; i++)
				{
					Server.NetworkWrapper.Broadcast(buffer[i].Owner, newMsg, true, Channel.Reliable);
				}
			}
		}
	}
}