using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using ChatService = FishMMO.Database.Npgsql.Services.ChatService;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
using FishMMO.Shared;
using FishMMO.Auth.Core;
using FishMMO.Logging;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The core chat intake: receives every line a player sends, rate-gates, sanitises and queues it, runs slash commands
	/// (with /help and the command audit), applies the stored mute and the flood mute, and sends system messages.
	/// Conversation (the channels, cross-server delivery, persistence and the chat window) is the Chat module's
	/// <c>ChatChannelSystem</c>, reached through <see cref="IChatLineHandler"/> .
	/// Game logic and broadcasts run on the main thread; the command audit's database writes run on the async worker.
	/// </summary>
	[CreateAssetMenu(fileName = "ChatSystem", menuName = "FishMMO/Server/SceneServer/Chat System", order = 1)]
	[RequiresDataContainer(typeof(ChatSystemRuntimeData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public partial class ChatSystem : ServerBehaviour, IChatSystem
	{

		/// <summary>
		/// Maximum number of inbound chat messages dequeued from the lock-free
		/// incoming queue and processed per frame. Prevents network spikes
		/// from monopolising the main thread.
		/// </summary>
		[Tooltip("Max incoming chat messages processed from the lock-free queue per frame")]
		[SerializeField] private int maxIncomingChatsPerFrame = 500;

		/// <summary>
		/// Hard cap on the incoming chat queue size. A message arriving while the queue already
		/// has this many entries is dropped, and the drops are logged.
		/// </summary>
		/// <remarks>
		/// Dropped, not kicked. Every sender is charged its rate tokens before its message is
		/// queued (see <see cref="ChatRateGate"/>), so no one sender can fill the queue; reaching
		/// the cap means many senders within their limits at once — a main-thread hitch, not an
		/// attack — and the connection that happens to arrive next is no more guilty than the rest.
		/// </remarks>
		[Tooltip("Maximum pending incoming chat messages; beyond it new messages are dropped (memory bound)")]
		[SerializeField] private int maxIncomingQueueSize = 10000;

		/// <summary>
		/// Messages dropped at the incoming cap since the last log line. Main thread only.
		/// </summary>
		private int incomingOverflowDrops;

		/// <summary>
		/// <see cref="MonotonicClock.NowTicks"/> before which another incoming-cap log line is not
		/// written. Main thread only.
		/// </summary>
		private long nextIncomingOverflowLogTicks;

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
		/// Maximum allowed chat message length.
		/// </summary>
		[SerializeField]
		[Tooltip("Maximum allowed chat message length.")]
		private int maxMessageLength = 128;
		/// <summary>
		/// The server chat rate limit in milliseconds. Should match the client's UIChat.messageRateLimit.
		/// </summary>
		public float MessageRateLimit => messageRateLimit;
		/// <summary>
		/// Maximum allowed chat message length.
		/// </summary>
		public int MaxMessageLength => maxMessageLength;

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

			if (!Server.DataContainerRegistry.TryGet<IChatSystemRuntimeData>(out var runtimeData))
			{
				Log.Error("ChatSystem", "Failed to initialize: IChatSystemRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			// A refused privileged command is a security event; ChatHelper is engine-agnostic
			// shared code with no server to log against, so it reports and this records.
			ChatHelper.OnCommandRefused += ChatHelper_OnCommandRefused;

			/* Every elevated command, run or refused, lands in the same audit log the Control
			 * Panel writes to. See ChatSystem.Audit.cs. */
			ChatHelper.OnCommandRefused += ChatHelper_OnElevatedCommandRefused;
			ChatHelper.OnElevatedCommand += ChatHelper_OnElevatedCommand;

			/* /help lists the commands the caller may run, decided here from their own access
			 * level. Player level, so not audited. See ChatSystem.Help.cs. */
			RegisterHelpCommand();

			// Network broadcasts
			Server.NetworkWrapper.RegisterBroadcast<ChatBroadcast>(OnServerChatBroadcastReceived, true);

			// Periodic callbacks
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.RegisterPeriodicCallback(FloodMuteSweepIntervalSeconds, OnPeriodicFloodMuteSweep);
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

			maxIncomingChatsPerFrame = Mathf.Max(1, maxIncomingChatsPerFrame);
			maxIncomingQueueSize = Mathf.Max(100, maxIncomingQueueSize);
			chatTokenBucketCapacity = Mathf.Max(1, chatTokenBucketCapacity);
			chatTokenRefillRate = Mathf.Max(0.01f, chatTokenRefillRate);

			incomingOverflowDrops = 0;
			nextIncomingOverflowLogTicks = 0;

			// Flood mute settings and state. See ChatSystem.FloodMute.cs.
			InitializeFloodMute();

			Log.Debug("ChatSystem", "Initialized (intake, commands and system messages; conversation is the Chat module's)");
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

			// Drain any remaining incoming chat queue entries
			DrainIncomingChatQueue(drainAll: true);


			// Network broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<ChatBroadcast>(OnServerChatBroadcastReceived);

			ChatHelper.OnCommandRefused -= ChatHelper_OnCommandRefused;
			ChatHelper.OnCommandRefused -= ChatHelper_OnElevatedCommandRefused;
			ChatHelper.OnElevatedCommand -= ChatHelper_OnElevatedCommand;

			UnregisterHelpCommand();

			// Periodic callbacks
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicFloodMuteSweep);
			}

			floodMuteStates.Clear();
		}

		/// <summary>
		/// Each frame: drain the lock-free incoming chat queue, then the main-thread action queue.
		/// </summary>
		protected override void OnUpdate(float deltaTime)
		{
			DrainIncomingChatQueue(drainAll: false);
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

			// Enqueue into the incoming queue for time-sliced draining on the main thread.
			// This decouples the network callback from game-logic processing,
			// preventing network spikes from freezing gameplay.
			if (!Server.DataContainerRegistry.TryGet(out IChatSystemRuntimeData runtimeData) ||
				runtimeData.IncomingChatQueue == null)
			{
				return;
			}

			/* The sender is charged BEFORE the message is queued. See ChatRateGate.
			 *
			 * The sender is resolved from the server's own connection map for the same reason the
			 * drain resolves it there (see DrainIncomingChatQueue). A connection with no resident
			 * character has nothing to charge, and nothing it sends would be processed anyway. */
			if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var chatCharMapping) ||
				!chatCharMapping.ConnectionCharacters.TryGetValue(conn, out IPlayerCharacter sender) ||
				sender == null)
			{
				return;
			}

			/* The rate is charged on the monotonic clock, not on ReceivedUtcTicks. That stamp is the
			 * legal record of when the server received the line, a wall-clock instant that is
			 * persisted; the bucket and the minimum gap are local durations. Charged on the wall
			 * clock, a host stepped back refused every sender's next line for the size of the step,
			 * and each refusal counted towards a flood mute. */
			long rateTicks = MonotonicClock.NowTicks;
			if (!TryChargeChatRate(sender, rateTicks))
			{
				/* Throttled: dropped, as the bucket always did — but counted, and too many refusals
				 * in a short window mute the sender and tell them why. See ChatFloodMute. */
				RecordChatRateRefusal(conn, sender);
				return;
			}

			// Memory bound: O(1) atomic counter rather than ConcurrentQueue.Count, which is O(N).
			int newSize = runtimeData.IncrementIncomingQueueSize();
			if (newSize > maxIncomingQueueSize)
			{
				runtimeData.DecrementIncomingQueueSize();
				RecordIncomingOverflow(rateTicks);
				return;
			}

			runtimeData.IncomingChatQueue.Enqueue((conn, msg));
		}

		/// <summary>
		/// Charges one message against the sender's chat rate. See <see cref="ChatRateGate"/>.
		/// </summary>
		/// <param name="sender">The sending character, whose rate fields are read and written back.</param>
		/// <param name="nowTicks"><see cref="MonotonicClock.NowTicks"/> as the message arrived.</param>
		/// <returns>True if the message may be queued.</returns>
		private bool TryChargeChatRate(IPlayerCharacter sender, long nowTicks)
		{
			var state = new ChatRateGate.State
			{
				Tokens = sender.Component<ChatState>().Tokens,
				LastRefillTicks = sender.Component<ChatState>().LastRefillTicks,
				IsFull = sender.Component<ChatState>().IsFull,
				NextMessageTicks = sender.Component<ChatState>().NextMessageTicks,
			};

			bool allowed = ChatRateGate.TryCharge(ref state, nowTicks, chatTokenBucketCapacity, chatTokenRefillRate, MessageRateLimit);

			sender.Component<ChatState>().Tokens = state.Tokens;
			sender.Component<ChatState>().LastRefillTicks = state.LastRefillTicks;
			sender.Component<ChatState>().IsFull = state.IsFull;
			sender.Component<ChatState>().NextMessageTicks = state.NextMessageTicks;
			return allowed;
		}

		/// <summary>
		/// Counts a message dropped at the incoming cap and logs the count at most every ten seconds.
		/// </summary>
		/// <param name="nowTicks"><see cref="MonotonicClock.NowTicks"/> now.</param>
		private void RecordIncomingOverflow(long nowTicks)
		{
			++incomingOverflowDrops;
			if (nowTicks < nextIncomingOverflowLogTicks)
			{
				return;
			}
			nextIncomingOverflowLogTicks = nowTicks + TimeSpan.TicksPerSecond * 10;
			Log.Warning("ChatSystem",
				$"The incoming chat queue is at its cap of {maxIncomingQueueSize}; dropped {incomingOverflowDrops} message(s) since the last report. " +
				$"The main thread is draining chat slower than players are sending it within their rate limits.");
			incomingOverflowDrops = 0;
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
			 * same condition. Flood protection lives in OnServerChatBroadcastReceived, which charges
			 * each sender's rate before anything is queued. */
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

			/* The token bucket and the minimum gap were charged when the message was queued, in
			 * OnServerChatBroadcastReceived; see ChatRateGate. */

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
			// Text was sanitised at the top of this method, before the rate limit and the
			// duplicate filter, so both of those now compare the text that will actually be sent.

			string cmd = ChatHelper.GetCommandAndTrim(ref msg.Text);

			// commands are handled differently from chat commands
			if (ChatHelper.TryParseCommand(cmd, sender, msg))
			{
				return;
			}

			/* A mute silences chat and nothing else, so it is tested here: after commands, before any
			 * channel. A muted player must still be able to /report, /helpme and read /tickets — the
			 * support channel is how a mute is appealed — and every channel prefix, /w and /tell and /g
			 * alike, is chat that arrives past this point and is refused. The state is held in memory,
			 * so the hot path is one comparison. */
			if (sender.Component<ChatState>().MutedUntilTicks > receivedTicks)
			{
				OnSendSystemMessage(conn, ChatMutePolicy.DescribeForPlayer(sender.Component<ChatState>().MutedUntilTicks, sender.Component<ChatState>().MuteReason, receivedTicks));
				return;
			}

			/* The flood mute, at the same point for the same reason: it silences chat, not commands.
			 * It is held apart from the stored mute above; see ChatSystem.FloodMute.cs. */
			if (TryRefuseFloodMuted(conn, sender))
			{
				return;
			}

			/* What is left is conversation: a channel word ("/w", "/g", …) or plain text. Routing it to its channel,
			 * the duplicate filter, delivery across servers and persistence are the Chat module's; without
			 * that module there is no conversation, and the line goes nowhere. Commands, the mutes and system messages
			 * above are the core's. */
			ResolveChatLineHandler()?.HandleChatLine(conn, sender, msg, cmd, receivedTicks, maxMessageLength);
		}

		/// <summary>
		/// The conversation handler (the Chat module's channel system), or null when that module is not installed. Looked up
		/// per line rather than kept: module systems initialise after the scene's own and shut down before them.
		/// </summary>
		private IChatLineHandler ResolveChatLineHandler()
		{
			return Server?.BehaviourRegistry != null && Server.BehaviourRegistry.TryGet(out IChatLineHandler handler) ? handler : null;
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
	}
}