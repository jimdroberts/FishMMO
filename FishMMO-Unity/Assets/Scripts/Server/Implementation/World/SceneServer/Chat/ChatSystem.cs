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
using FishMMO.Auth.Core;
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
		/// Maximum time the shutdown flush blocks the main thread waiting on the database.
		/// Shorter than the 30s startup default: a stalled database must not hold process exit
		/// open, and losing the tail of the chat log is preferable to a hung shutdown.
		/// </summary>
		private const int persistFlushTimeoutMs = 10_000;

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

			// A refused privileged command is a security event; ChatHelper is engine-agnostic
			// shared code with no server to log against, so it reports and this records.
			ChatHelper.OnCommandRefused += ChatHelper_OnCommandRefused;

			// Network broadcasts
			Server.NetworkWrapper.RegisterBroadcast<ChatBroadcast>(OnServerChatBroadcastReceived, true);

			// Periodic callbacks
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.RegisterPeriodicCallback(MessagePumpRate, OnPeriodicMessagePump);
				periodicSystem.RegisterPeriodicCallback(persistFlushIntervalSeconds, OnPeriodicPersistFlush);
				periodicSystem.RegisterPeriodicCallback(outboundBatchIntervalSeconds, OnPeriodicOutboundFlush);
			}

			/* Clamp the inspector value to the wire contract.
			 *
			 * ChatBroadcast.MaxTextLength is the length the broadcast documents itself as
			 * carrying, and until now nothing read it — it was a constant with no callers while
			 * the real limit was whatever this serialized field happened to say. Raising the
			 * field above the constant would have let messages through that the client's own
			 * ParseLocalMessage silently discards, which looks exactly like the server dropping
			 * chat at random. */
			maxMessageLength = Mathf.Clamp(maxMessageLength, 1, ChatBroadcast.MaxTextLength);

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

			ChatHelper.OnCommandRefused -= ChatHelper_OnCommandRefused;

			/* Drop the static channel registration so the next run rebuilds it.
			 *
			 * ChatHelper's maps are static and hold delegates bound to this ScriptableObject.
			 * They outlive a play-session restart in the editor while this object does not, and
			 * InitializeOnce's latch meant the second session kept the first session's handlers.
			 * Individual slash commands are removed by the systems that registered them. */
			ChatHelper.ResetChannelCommands();

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

				/* Resolve the sender from the server's own connection map, not from
				 * conn.FirstObject.
				 *
				 * ConnectionCharacters is populated by the character load pipeline from the
				 * database row, so the character it yields — and in particular its AccessLevel —
				 * is server-authoritative by construction. FirstObject is whatever NetworkObject
				 * happens to be first on the connection, and IPlayerCharacter.ReadPayload
				 * deserializes AccessLevel straight off the wire. That payload is only ever read
				 * server-side on FishNet's predicted-spawn path, which this project disables
				 * globally (_allowPredictedSpawning is false on all three server scenes) and
				 * which additionally requires a PredictedSpawn component no prefab here has — so
				 * it is unreachable today. It is one project setting away from not being, and
				 * this path now decides whether /admin commands run.
				 *
				 * The map is populated at the same moment the object is spawned, so nothing is
				 * lost by preferring it. */
				IPlayerCharacter sender = null;
				if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var chatCharMapping))
				{
					chatCharMapping.ConnectionCharacters.TryGetValue(entry.Connection, out sender);
				}

				if (sender != null)
				{
					ProcessNewChatMessage(entry.Connection, sender, entry.Message);
				}
				else
				{
					/* No resident character is a race, not an exploit, so drop the message
					 * instead of the player.
					 *
					 * The queue is drained a frame or more after the message arrives, and the
					 * character is removed from the connection map while its connection stays up
					 * on every scene transfer, bind-point respawn and channel switch. A chat line
					 * sent moments before one of those therefore dequeues with nothing left to
					 * attribute it to — and kicking for it disconnected a player mid-transfer,
					 * with no notice, for typing. Anything genuinely trying to talk without a
					 * character is refused just as effectively by discarding what it sends. */
					Log.Debug("ChatSystem", $"Dropping chat from connection {entry.Connection.ClientId}: no resident character.");
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
		/// Records a character attempting a slash command above its access level.
		/// </summary>
		/// <remarks>
		/// Logged at warning rather than debug. The command layer deliberately gives the sender
		/// no feedback — an unprivileged player must not be able to discover which command names
		/// exist by watching how the server responds — so this log line is the only trace that
		/// somebody is probing the admin commands.
		/// </remarks>
		/// <param name="sender">Character that ran the command.</param>
		/// <param name="command">Command it tried to run, including the leading slash.</param>
		/// <param name="required">Access level the command requires.</param>
		private void ChatHelper_OnCommandRefused(IPlayerCharacter sender, string command, AccessLevel required)
		{
			if (sender == null)
			{
				return;
			}

			Log.Warning("ChatSystem",
				$"Character '{sender.CharacterName}' ({sender.ID}, account '{sender.Account}') attempted '{command}' " +
				$"which requires {required}; the character has {sender.AccessLevel}. The command was ignored.");
		}

		/// <summary>
		/// Parses and processes a new chat message received from a connection, including validation, rate limiting, spam filtering, and command handling.
		/// </summary>
		/// <param name="conn">Network connection of the sender.</param>
		/// <param name="sender">Player character sending the message.</param>
		/// <param name="msg">Chat broadcast message (ReceivedUtcTicks already stamped at the network boundary).</param>
		private void ProcessNewChatMessage(NetworkConnection conn, IPlayerCharacter sender, ChatBroadcast msg)
		{
			if (sender == null || msg.Text == null)
			{
				return;
			}

			/* Length is enforced HERE, at the network boundary, and by truncation rather than by
			 * disconnecting.
			 *
			 * ChatBroadcast.MaxTextLength was the documented limit and nothing ever read it — the
			 * only length test in the pipeline was the kick this replaces, and the client's own
			 * cap is advisory because a client is free not to apply it. maxMessageLength is now
			 * clamped to MaxTextLength at initialisation, so this is the single authoritative cap.
			 *
			 * Capping BEFORE sanitising also bounds the work the regex passes can be made to do:
			 * a hostile client cannot hand the sanitiser a megabyte to chew through on the main
			 * thread.
			 *
			 * That kick was also a live false positive. The client sanitises before
			 * sending, so a player typing "<b>" sent an empty string — and empty text hit
			 * `IsNullOrWhiteSpace` here and disconnected them with ExploitExcessiveData for
			 * typing three ordinary characters. Nothing is gained by kicking for either case:
			 * an over-long message is truncated and an empty one is dropped, which refuses the
			 * exploit just as completely without punishing the honest client that triggers the
			 * same condition. Flood protection, which is the case a kick is actually right for,
			 * is unchanged and lives in OnServerChatBroadcastReceived. */
			if (msg.Text.Length > maxMessageLength)
			{
				msg.Text = msg.Text.Substring(0, maxMessageLength);
			}

			/* Sanitise everything, unconditionally.
			 *
			 * This used to run only when the text contained '<', which covered rich-text tags and
			 * nothing else. Three things get through a '<' test:
			 *   - FISHMMO_ control codes. The client matches these on the first word of a message
			 *     and renders them specially, so "/tell Bob FISHMMO_TELL_RELAYED you owe me gold"
			 *     appeared in Bob's log as a whisper Bob had apparently sent to someone else.
			 *   - Newlines, which turn one chat row into as many lines as the sender likes.
			 *   - U+202E and friends, which reverse the rendering of the rest of the line.
			 * See ChatSanitizer for what each pass does and why the order is what it is. */
			msg.Text = ChatHelper.SanitizeIncoming(msg.Text, maxMessageLength);

			if (string.IsNullOrWhiteSpace(msg.Text))
			{
				// Nothing survived cleaning (or nothing was sent). Drop it silently.
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
			/* The duplicate filter is for chat, not for commands.
			 *
			 * It ran before the command was parsed, so repeating any slash command was silently
			 * dropped — and repeating one is the normal thing to do, because the first attempt is
			 * often refused for a reason that then goes away. "/leaveinstance" while still in
			 * combat, then again once combat ends, was swallowed; so was a second
			 * "/admin stopshutdown" after the first went unacknowledged. A command that appears
			 * to do nothing invites exactly the repeat this filter then ate.
			 *
			 * Applied below instead, on the paths that reach actual chat. */
			bool startsWithSlash = msg.Text.Length > 0 && msg.Text[0] == '/';

			if (!AllowRepeatMessages && !startsWithSlash)
			{
				if (!string.IsNullOrWhiteSpace(sender.LastChatMessage) &&
					sender.LastChatMessage.Equals(msg.Text))
				{
					return;
				}
				sender.LastChatMessage = msg.Text;
			}

			// Text was sanitised at the top of this method, before the rate limit and the
			// duplicate filter, so both of those now compare the text that will actually be sent.

			string cmd = ChatHelper.GetCommandAndTrim(ref msg.Text);

			// commands are handled differently from chat commands
			if (ChatHelper.TryParseCommand(cmd, sender, msg))
			{
				return;
			}

			/* Not a registered command after all, so the duplicate filter still applies. A
			 * channel prefix such as "/w" arrives here with the prefix already stripped, so the
			 * comparison is against the message body — which is what a player repeating
			 * themselves actually repeats. */
			if (!AllowRepeatMessages && startsWithSlash)
			{
				if (!string.IsNullOrWhiteSpace(sender.LastChatMessage) &&
					sender.LastChatMessage.Equals(msg.Text))
				{
					return;
				}
				sender.LastChatMessage = msg.Text;
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

					/* One unwritable row used to cost the whole batch — up to
					 * maxPersistBatchSize (2000) messages, because PersistBatchAsync is a single
					 * AddRange + SaveChanges and the entries were already dequeued by the time it
					 * failed. The chat log is kept for audit, so a single malformed row silently
					 * erasing two thousand others is the worst possible failure mode.
					 *
					 * Retry by bisection: halve the batch until the failing rows are isolated,
					 * and lose only those. A transient failure (database down) fails every half,
					 * which is why the recursion is depth-capped — see
					 * PersistWithIsolationAsync. */
					var isolation = new List<(long, string, string, long, long, FishMMO.Database.Data.Enums.ChatChannel, string, DateTime)>(batch);
					await PersistWithIsolationAsync(chatService, isolation, 0);
				}
			}
			catch (Exception ex)
			{
				await Log.Error("ChatSystem", $"Error in FlushPersistQueueAsync: {ex}");
			}
		}

		/// <summary>
		/// Maximum bisection depth used to isolate a failing row inside a persist batch.
		/// </summary>
		/// <remarks>
		/// Bounds the error path. A row-specific failure is found in log2(batch) halvings, but a
		/// database that is simply unavailable fails every half, and without a cap that turns one
		/// failed flush into 2 * batchSize more doomed round-trips. At depth 6 a 2000-message
		/// batch is narrowed to blocks of about 32 and the remainder is dropped with a log line —
		/// enough granularity that a poison row costs tens of messages rather than thousands,
		/// while a dead database costs at most 126 extra attempts before the flush gives up.
		/// </remarks>
		private const int maxPersistIsolationDepth = 6;

		/// <summary>
		/// Re-attempts a failed persist batch by halving it, so that only the entries that
		/// genuinely cannot be written are lost.
		/// </summary>
		/// <param name="chatService">Chat persistence service.</param>
		/// <param name="entries">Entries from a batch whose write failed.</param>
		/// <param name="depth">Current bisection depth; recursion stops at <see cref="maxPersistIsolationDepth"/>.</param>
		private async Task PersistWithIsolationAsync(
			IChatService chatService,
			List<(long, string, string, long, long, FishMMO.Database.Data.Enums.ChatChannel, string, DateTime)> entries,
			int depth)
		{
			if (entries == null || entries.Count < 1)
			{
				return;
			}

			// Shutdown takes priority over salvaging the log.
			if (Server == null ||
				Server.ServerState != ConnectionState.Started ||
				(Server.DataContainerRegistry.TryGet(out IChatSystemRuntimeData runtimeData) && runtimeData.IsShuttingDown))
			{
				return;
			}

			if (entries.Count == 1 || depth >= maxPersistIsolationDepth)
			{
				DatabaseResult leaf = await chatService.PersistBatchAsync(entries);
				if (!leaf.IsSuccess)
				{
					await Log.Warning("ChatSystem",
						$"Discarding {entries.Count} chat message(s) that could not be persisted: {leaf.ErrorCode} - {leaf.ErrorMessage}");
				}
				return;
			}

			int half = entries.Count / 2;
			var first = entries.GetRange(0, half);
			var second = entries.GetRange(half, entries.Count - half);

			DatabaseResult firstResult = await chatService.PersistBatchAsync(first);
			if (!firstResult.IsSuccess)
			{
				await PersistWithIsolationAsync(chatService, first, depth + 1);
			}

			DatabaseResult secondResult = await chatService.PersistBatchAsync(second);
			if (!secondResult.IsSuccess)
			{
				await PersistWithIsolationAsync(chatService, second, depth + 1);
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
					// Hand the write a private copy. PersistBatchBuffer is shared and is cleared
					// (then nulled) by ChatSystemRuntimeData.OnDeinitialize immediately after this
					// method returns — if the write is still reading it on a worker thread after a
					// timeout, that is a torn read of a List<T> being mutated concurrently.
					var pending = new List<(long, string, string, long, long, FishMMO.Database.Data.Enums.ChatChannel, string, DateTime)>(batch);

					// Shutdown flush runs on the Unity main thread: never block on the raw EF task
					// (same deadlock as the KEK load). A timeout here drops the batch, so log it.
					if (UnitySyncOverAsync.TryRun(
						cancellationToken => chatService.PersistBatchAsync(pending, cancellationToken: cancellationToken),
						out DatabaseResult result,
						persistFlushTimeoutMs))
					{
						if (!result.IsSuccess)
						{
							Log.Warning("ChatSystem", $"FlushPersistQueueSync DB error ({pending.Count} messages): {result.ErrorCode} - {result.ErrorMessage}");
						}
					}
					else
					{
						Log.Warning("ChatSystem", $"FlushPersistQueueSync timed out after {persistFlushTimeoutMs}ms; {pending.Count} message(s) were not persisted.");
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
			/* Discord is an untrusted source and this is the boundary it crosses into the game.
			 *
			 * The row this came from was written by the Discord bot straight from a Discord
			 * message: the author's display name — which that user chooses, and which can be
			 * changed to anything at any time — followed by whatever they typed. It was
			 * broadcast from here verbatim, arrived at a client that renders it into a Label,
			 * and (in the client) was explicitly exempted from tab filtering, so no player could
			 * turn it off. A Discord display name of "<size=500>" was therefore a remote
			 * client-side attack on every player in the world, launched from outside the game
			 * with no account needed.
			 *
			 * The bot now sanitises on the way in as well. This is not redundant with that: the
			 * chat table is shared state that other tools write to, and the server must not
			 * assume a row in it came from a version of the bot that cleans. Sanitising on both
			 * sides of the database is the whole point of a trust boundary.
			 *
			 * The cap also matters. The bridge's own limit was 500 characters while the client
			 * discards anything over ChatBroadcast.MaxTextLength, so long Discord messages
			 * vanished silently instead of arriving truncated. */
			string relayText = ChatHelper.SanitizeIncoming(message, maxMessageLength);
			if (string.IsNullOrWhiteSpace(relayText))
			{
				return;
			}

			ChatBroadcast newMsg = new ChatBroadcast()
			{
				Channel = ChatChannel.Discord,
				SenderID = 0,
				Text = relayText,
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