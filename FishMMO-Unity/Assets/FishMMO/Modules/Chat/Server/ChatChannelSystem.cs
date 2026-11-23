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
using FishMMO.Logging;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The Chat module's conversation: routes the lines the core chat intake accepted (<see cref="IChatLineHandler"/>) to
	/// their channels (say, region, world, trade, tell, party, guild, team), delivers them across servers through the
	/// database pump, persists them in batches and sends world chat in batches. Split from <c>ChatSystem</c> with the
	/// module; the intake, slash commands, mutes and system messages stay in the core's <c>ChatSystem</c>.
	/// </summary>
	[CreateAssetMenu(fileName = "ChatChannelSystem", menuName = "FishMMO/Server/SceneServer/Chat Channel System", order = 1)]
	[RequiresDataContainer(typeof(ChatChannelRuntimeData))]
	[RequiresDataContainer(typeof(ChatSystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public partial class ChatChannelSystem : ServerBehaviour, IChatLineHandler, IChatHelper
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
		/// How long a chat message the database refused with a transient failure keeps being retried.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A transient failure means the database was unreachable, not that anything was wrong with
		/// the rows, so the answer is to try the same rows again later — not to bisect a batch that
		/// every half of will fail equally, and then discard it. The chat log is kept for audit, and
		/// an outage used to cost it every message sent while the outage lasted.
		/// </para>
		/// <para>
		/// Bounded by age rather than by attempt count because the message already carries the one
		/// clock that matters — when the server received it. A message still unwritten after this
		/// long is discarded with an error, which caps the retry queue at this window's worth of
		/// chat however long the database stays down. Late is not free, either: a whisper, party or
		/// guild line reaches players on OTHER scene servers only once its row is written, so a
		/// retried message is delivered there as late as it was written.
		/// </para>
		/// </remarks>
		[Tooltip("Seconds a chat message refused by a transient database failure is retried before it is discarded")]
		[SerializeField] private float persistRetryWindowSeconds = 300.0f;

		/// <summary>
		/// Non-zero while an asynchronous persist flush is in flight. See <see cref="OnPeriodicPersistFlush"/>.
		/// </summary>
		private int persistFlushInFlight;

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
		/// Prevents unbounded memory growth in <see cref="IChatChannelRuntimeData.OutboundWorldBroadcastBuffer"/>.
		/// </summary>
		[Tooltip("Maximum buffered World/Trade messages per world (oldest dropped when exceeded)")]
		[SerializeField] private int maxBufferedWorldMessages = 200;

		/// <summary>
		/// If true, allows repeat messages from clients without spam filtering.
		/// </summary>
		[SerializeField] private bool allowRepeatMessages = false;
		/// <summary>
		/// The server chat message pump rate limit in seconds.
		/// </summary>
		[Tooltip("The server chat message pump rate limit in seconds.")]
		[SerializeField] private float messagePumpRate = 2.0f;

		/// <summary>
		/// Rows per page of a pump read.
		/// </summary>
		/// <remarks>
		/// Replaces <c>messageFetchCount</c> (20), which was the WHOLE of a read: one page of the
		/// entire shard's chat every two seconds, however far behind the pump was. Above about
		/// ten persisted lines a second across all servers the backlog grew without limit and
		/// whispers arrived minutes late (hot-path audit H6). A read now keeps going while its
		/// pages come back full, and only rows relevant to this server count against it.
		/// </remarks>
		[Tooltip("Rows per page of a chat pump read. Pages are read while they come back full, up to the page limit.")]
		[SerializeField] private int messagePumpPageSize = 100;

		/// <summary>
		/// Most pages one pump read may walk, bounding a read's work when the pump has fallen behind.
		/// </summary>
		/// <remarks>
		/// At the defaults a read takes up to 1,000 relevant rows, 500 a second at a two-second
		/// pump. A read that stops at the limit carries on from where it stopped on the next pump.
		/// </remarks>
		[Tooltip("Most pages one chat pump read may walk before leaving the rest for the next pump.")]
		[SerializeField] private int messagePumpMaxPages = 10;

		/// <summary>
		/// If true, allows repeat messages from clients without spam filtering.
		/// </summary>
		public bool AllowRepeatMessages => allowRepeatMessages;
		/// <summary>
		/// The server chat message pump rate limit in seconds.
		/// </summary>
		public float MessagePumpRate => messagePumpRate;

		/// <summary>
		/// Fetch failures of the chat pump. Touched only by the one pump in flight, which the
		/// in-flight flag serialises, so never by two threads at once.
		/// </summary>
		private readonly RepeatingFaultLog pumpFetchFaults = new RepeatingFaultLog("ChatSystem", "Chat message pump fetch");

		/// <summary>
		/// Ticks of the database clock at the start of a FIRST pump read whose rows could not be
		/// handed to the main thread, or 0. Written by the worker, taken by the next query build.
		/// See <see cref="ChatPumpCursor.PinFloor"/>.
		/// </summary>
		private long pendingPumpFloorTicks;

		/// <summary>
		/// Failures delivering pumped rows on the main thread.
		/// </summary>
		private readonly RepeatingFaultLog pumpDeliveryFaults = new RepeatingFaultLog("ChatSystem", "Chat message pump delivery");

		/// <summary>
		/// Monotonic seconds for <see cref="RepeatingFaultLog"/>, safe off the main thread.
		/// </summary>
		private static double MonotonicSeconds => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;

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
			if (Server?.DataContainerRegistry.TryGet(out IChatChannelRuntimeData runtimeData) == true)
			{
				runtimeData.EndMessagePump();
			}
		}

		/// <summary>
		/// Periodic callback that starts one read of the cross-server chat pump.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The pump is the only way a whisper, party or guild line reaches a player on a different
		/// scene server from its sender, and the way World, Trade and Discord lines reach every
		/// scene server but the sender's. Each read asks the database for the rows relevant to
		/// somebody this server hosts, starting a commit window behind what it has settled and
		/// skipping by ID what it has already handled. See <see cref="ChatPumpCursor"/> for why it
		/// is a window rather than a cursor, and <c>ChatService.FetchPumpAsync</c> for the query.
		/// </para>
		/// <para>
		/// What is relevant is read from the server's own maps HERE, on the main thread, and the
		/// read carries a snapshot of it: the maps are not safe to touch from the worker.
		/// </para>
		/// </remarks>
		/// <param name="deltaTime">Elapsed seconds (unused).</param>
		private void OnPeriodicMessagePump(float deltaTime)
		{
			if (!Initialized ||
				Server == null ||
				Server.ServerState != ConnectionState.Started ||
				!Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> _) ||
				!Server.DataContainerRegistry.TryGet(out IChatChannelRuntimeData runtimeData))
			{
				return;
			}

			if (!runtimeData.TryBeginMessagePump())
			{
				return;
			}

			bool handedOff = false;
			try
			{
				ChatPumpQuery query = BuildPumpQuery(runtimeData);
				handedOff = TryEnqueueAsyncWork(() => FetchAndProcessChatMessagesAsync(query));
			}
			catch (Exception ex)
			{
				pumpDeliveryFaults.Report(ex, MonotonicSeconds);
			}
			finally
			{
				// A read that never started must not leave the pump believing one is in flight.
				if (!handedOff)
				{
					runtimeData.EndMessagePump();
				}
			}
		}

		/// <summary>
		/// Snapshots what this server hosts into a pump query, and records the keys on the cursor.
		/// Main thread only.
		/// </summary>
		/// <param name="runtimeData">The chat runtime data holding the cursor.</param>
		/// <returns>The query for one read.</returns>
		private ChatPumpQuery BuildPumpQuery(IChatChannelRuntimeData runtimeData)
		{
			var keys = new HashSet<ChatPumpKey>();
			var worldIds = new List<long>();
			var partyIds = new List<long>();
			var guildIds = new List<long>();
			var names = new List<string>();

			if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData))
			{
				foreach (KeyValuePair<long, Dictionary<long, IPlayerCharacter>> world in mappingData.CharactersByWorld)
				{
					if (world.Value != null && world.Value.Count > 0)
					{
						worldIds.Add(world.Key);
						keys.Add(ChatPumpKey.World(world.Key));
					}
				}

				// The only possible tell targets here; a whisper to anybody else is another server's.
				foreach (string nameLowerCase in mappingData.CharactersByLowerCaseName.Keys)
				{
					if (!string.IsNullOrEmpty(nameLowerCase))
					{
						names.Add(nameLowerCase);
						keys.Add(ChatPumpKey.Tell(nameLowerCase));
					}
				}
			}

			if (Server.DataContainerRegistry.TryGet<IPartyCharacterMappingData>(out var partyData))
			{
				foreach (KeyValuePair<long, HashSet<long>> party in partyData.PartyCharacterTracker)
				{
					if (party.Key > 0 && party.Value != null && party.Value.Count > 0)
					{
						partyIds.Add(party.Key);
						keys.Add(ChatPumpKey.Party(party.Key));
					}
				}
			}

			if (Server.DataContainerRegistry.TryGet<IGuildCharacterMappingData>(out var guildData))
			{
				foreach (KeyValuePair<long, HashSet<long>> guild in guildData.GuildCharacterTracker)
				{
					if (guild.Key > 0 && guild.Value != null && guild.Value.Count > 0)
					{
						guildIds.Add(guild.Key);
						keys.Add(ChatPumpKey.Guild(guild.Key));
					}
				}
			}

			ChatPumpCursor cursor = runtimeData.PumpCursor;
			long lostFirstReadTicks = System.Threading.Interlocked.Exchange(ref pendingPumpFloorTicks, 0);
			if (lostFirstReadTicks != 0)
			{
				cursor.PinFloor(new DateTime(lostFirstReadTicks, DateTimeKind.Utc));
			}
			cursor.BeginRead(keys);

			return new ChatPumpQuery
			{
				FromUtc = cursor.Watermark,
				ExcludeIds = cursor.SnapshotSeenIds(),
				SceneServerId = Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var sceneRuntimeData) ? sceneRuntimeData.ID : 0,
				WorldServerIds = worldIds.ToArray(),
				PartyIds = partyIds.ToArray(),
				GuildIds = guildIds.ToArray(),
				TellTargetsLowerCase = names.ToArray(),
				PageSize = messagePumpPageSize,
				MaxPages = messagePumpMaxPages,
			};
		}

		/// <summary>
		/// Runs one pump read on the async worker and hands the rows to the main thread.
		/// </summary>
		/// <param name="query">The read, snapshotted on the main thread.</param>
		/// <returns>Asynchronous fetch-and-process task.</returns>
		private async Task FetchAndProcessChatMessagesAsync(ChatPumpQuery query)
		{
			bool handedOffToMainThread = false;
			try
			{
				// Shutdown guard: abort if server is no longer running.
				if (Server == null || Server.ServerState != ConnectionState.Started)
				{
					return;
				}
				if (!TryGetDbService(out IChatService chatService))
				{
					return;
				}

				DatabaseResult<ChatPumpPage> result = await chatService.FetchPumpAsync(query);

				/* A failed read leaves the cursor where it was, so nothing is skipped — the next pump
				 * asks again. It is still reported: this pump is the only way a whisper, party or
				 * guild line reaches a player on another scene server, and an outage of it is
				 * otherwise invisible until players notice. */
				if (!result.IsSuccess || result.Data == null)
				{
					pumpFetchFaults.Report(new InvalidOperationException($"[{result.ErrorCode}] {result.ErrorMessage}"), MonotonicSeconds);
					return;
				}
				pumpFetchFaults.ReportSuccess();

				ChatPumpPage page = result.Data;

				/* Handed over even when it holds no rows: the cursor still has to learn where the
				 * database clock stood, or an idle server's window would never advance and every
				 * read would scan further back than the last. */
				if (TryEnqueueMainThread(() =>
				{
					try
					{
						ApplyPumpPage(page);
					}
					finally
					{
						// Cleared AFTER the cursor moved, so the next read starts from where this one settled.
						ClearMessagePumpFlag();
					}
				}))
				{
					handedOffToMainThread = true;
				}
				else if (!query.FromUtc.HasValue)
				{
					/* A first read whose rows never reach the cursor would leave no record of where it
					 * started, and the next read would begin at a later "now". Keep its start so the
					 * next query covers the span. A later read needs nothing: its watermark has not
					 * moved, so the next read covers the same rows again. */
					System.Threading.Interlocked.CompareExchange(ref pendingPumpFloorTicks, page.ReadStartedUtc.Ticks, 0);
				}
			}
			catch (Exception ex)
			{
				pumpFetchFaults.Report(ex, MonotonicSeconds);
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
		/// Delivers the rows of one pump read that the cursor admits, then settles the cursor.
		/// Must be called on the main thread.
		/// </summary>
		/// <param name="page">The read.</param>
		private void ApplyPumpPage(ChatPumpPage page)
		{
			if (page == null ||
				Server == null ||
				!Server.DataContainerRegistry.TryGet(out IChatChannelRuntimeData runtimeData) ||
				runtimeData.PumpCursor == null)
			{
				return;
			}

			ChatPumpCursor cursor = runtimeData.PumpCursor;
			DateTime? lastRowTime = null;
			bool rowFailed = false;
			try
			{
				List<ChatData> messages = page.Messages;
				for (int i = 0; messages != null && i < messages.Count; i++)
				{
					ChatData message = messages[i];
					lastRowTime = message.TimeCreated;

					if (!cursor.Admit(message.ID, message.TimeCreated, PumpKeyOf(message)))
					{
						continue;
					}

					/* One row that throws costs that row, not the read: it is already recorded as
					 * handled, so it is not retried forever either. */
					try
					{
						ProcessChatMessage(message);
					}
					catch (Exception ex)
					{
						rowFailed = true;
						pumpDeliveryFaults.Report(ex, MonotonicSeconds);
					}
				}
			}
			finally
			{
				cursor.CompleteRead(page.ReadStartedUtc, page.Drained, lastRowTime);
			}
			if (!rowFailed)
			{
				pumpDeliveryFaults.ReportSuccess();
			}
		}

		/// <summary>
		/// The relevance key of a pumped row, parsed exactly as the pump's SQL matched it.
		/// </summary>
		/// <param name="message">The row.</param>
		/// <returns>The key, or a <see cref="ChatPumpKeyKind.None"/> key for a row with no parsable address.</returns>
		private static ChatPumpKey PumpKeyOf(ChatData message)
		{
			switch ((ChatChannel)message.Channel)
			{
				case ChatChannel.World:
				case ChatChannel.Trade:
				case ChatChannel.Discord:
					return ChatPumpKey.World(message.WorldServerID);
				case ChatChannel.Party:
					return long.TryParse(ChatPumpKey.FirstWord(message.Message), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long partyID)
						? ChatPumpKey.Party(partyID)
						: default;
				case ChatChannel.Guild:
					return long.TryParse(ChatPumpKey.FirstWord(message.Message), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long guildID)
						? ChatPumpKey.Guild(guildID)
						: default;
				case ChatChannel.Tell:
					/* The address, not the first word: a name with a space is written quoted, and
					 * the SQL extracts it the same way (ChatService.BuildPumpFilterSql). */
					return ChatTellAddress.TryParseAddress(message.Message, out string targetName)
						? ChatPumpKey.Tell(targetName)
						: default;
				default:
					return default;
			}
		}

		/// <summary>
		/// Routes one pumped row through its channel's handler with no sender.
		/// Must be called on the main thread.
		/// </summary>
		/// <param name="message">Chat message data to process.</param>
		private void ProcessChatMessage(ChatData message)
		{
			ChatChannel channel = (ChatChannel)message.Channel;
			if (channel == ChatChannel.Discord)
			{
				OnSendDiscordMessage(message.WorldServerID, message.SceneServerID, message.Message);
			}
			else if (ChatHelper.ChatChannelCommands.TryGetValue(channel, out ChatCommandDetails sayCommand))
			{
				// sender is intentionally null for pump-sourced messages: handlers use null to
				// suppress persistence (already persisted) and skip sender-specific replies.
				sayCommand.Func?.Invoke(null, new ChatBroadcast()
				{
					Channel = channel,
					SenderID = message.CharacterID,
					Text = message.Message,
				});
			}
		}

		/// <summary>
		/// Gets the chat command handler for a specific chat channel.
		/// </summary>
		/// <param name="channel">Chat channel to get the command for.</param>
		/// <returns>Chat command delegate for the channel.</returns>
		public ChatCommand GetChannelCommand(ChatChannel channel)
		{
			if (Server?.DataContainerRegistry.TryGet(out IChatChannelRuntimeData runtimeData) == true &&
				runtimeData.ChannelCommandMap != null &&
				runtimeData.ChannelCommandMap.TryGetValue(channel, out ChatCommand command))
			{
				return command;
			}
			// ChatChannel.System is Server->Client only. We never parse system messages locally.
			return OnSayChat;
		}

		/// <summary>
		/// Enqueues a chat message for batch DB persistence.
		/// Thread-safe — can be called from main thread or async paths.
		/// </summary>
		private void EnqueuePersist(long characterId, string characterName, string accountName, long worldServerId, ChatChannel channel, string message, long receivedTicks)
		{
			if (Server?.DataContainerRegistry.TryGet(out IChatChannelRuntimeData runtimeData) == true &&
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
		/// <remarks>
		/// <para>
		/// <b>One flush at a time.</b> This fires every <see cref="persistFlushIntervalSeconds"/>
		/// (0.1s by default) and the async worker runs unordered items concurrently, so without the
		/// in-flight flag any flush slower than the interval — a 2000-row write under load, or a
		/// failed batch being bisected — had the next one start beside it. They shared
		/// <see cref="IChatChannelRuntimeData.PersistBatchBuffer"/>: the second cleared and refilled
		/// the list while the first's write was still reading it, so a flush could write another
		/// flush's rows (duplicating them and losing its own), throw on an index that had gone, or
		/// hand its failure path a list that was no longer its batch. The chat log is kept for
		/// audit, and it was being silently corrupted in exactly the busy moments worth auditing.
		/// </para>
		/// <para>
		/// A private list per flush would have fixed only the corruption. Overlapping writes also
		/// commit out of order — each is stamped when its INSERT runs, not when it commits — and a
		/// slow flush committing after a faster, later one is exactly what the other scene servers'
		/// pumps must then absorb inside their commit window. They do now (see
		/// <see cref="ChatPumpCursor"/>), for every writer on the shard; serialising this server's
		/// own flushes still keeps its rows in the order they were said.
		/// </para>
		/// </remarks>
		private void OnPeriodicPersistFlush(float deltaTime)
		{
			if (!Initialized ||
				Server == null ||
				Server.ServerState != ConnectionState.Started)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet(out IChatChannelRuntimeData runtimeData) ||
				runtimeData.PendingPersistQueue == null ||
				runtimeData.PendingPersistQueue.IsEmpty)
			{
				return;
			}

			// The flush in flight takes whatever has queued since, up to its batch size, next time.
			if (Interlocked.CompareExchange(ref persistFlushInFlight, 1, 0) != 0)
			{
				return;
			}

			if (!TryEnqueueAsyncWork(() => FlushPersistQueueAsync()))
			{
				Interlocked.Exchange(ref persistFlushInFlight, 0);
				Log.Error("ChatSystem", "Failed to enqueue chat persist flush. Messages remain queued for next cycle.");
			}
		}

		/// <summary>
		/// Drains the <see cref="IChatChannelRuntimeData.PendingPersistQueue"/> and writes all entries
		/// to the database in a single batch via <c>PersistBatchAsync</c>.
		/// Runs on a background thread via the async worker, one at a time — see
		/// <see cref="OnPeriodicPersistFlush"/>, which took the in-flight flag this releases.
		/// </summary>
		private async Task FlushPersistQueueAsync()
		{
			try
			{
				if (Server == null || Server.ServerState != ConnectionState.Started)
				{
					return;
				}
				if (!Server.DataContainerRegistry.TryGet(out IChatChannelRuntimeData runtimeData) ||
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

				/* A list of this flush's own, not the shared PersistBatchBuffer.
				 *
				 * The write reads the list after its first await, and a retry reads it again. The
				 * in-flight flag keeps a second async flush away from it, but not the synchronous
				 * shutdown flush, which drains into that buffer on the main thread — and the runtime
				 * data clears and nulls the buffer immediately afterwards, whether or not a write is
				 * still reading it. The buffer is left to the shutdown path alone. */
				var batch = new List<(long, string, string, long, long, FishMMO.Database.Data.Enums.ChatChannel, string, DateTime)>();
				// Each line's first-queued time, index for index with the batch: the retry window is
				// measured on it, and it cannot ride the database tuple.
				var queuedAt = new List<double>();

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
					queuedAt.Add(entry.QueuedAtSeconds);
				}

				if (batch.Count < 1)
				{
					return;
				}

				// All-or-nothing: IChatService.PersistBatchAsync writes the whole list in one
				// transaction, so a retry after a failure cannot duplicate rows.
				long writeStarted = System.Diagnostics.Stopwatch.GetTimestamp();
				DatabaseResult result = await chatService.PersistBatchAsync(batch);
				double writeSeconds = (System.Diagnostics.Stopwatch.GetTimestamp() - writeStarted) / (double)System.Diagnostics.Stopwatch.Frequency;

				/* The other scene servers' pumps read a commit window behind their progress (see
				 * ChatService.PumpCommitWindowSeconds): a row that commits later than that after its
				 * stamp can be behind every reader already, and is then never delivered off this
				 * server — though it is still in the log. This call's wall time bounds stamp to
				 * commit from above, so exceeding the window is the one case worth an operator's
				 * attention. Retries inside the call restamp, so this can only over-report. */
				if (result.IsSuccess && writeSeconds > ChatService.PumpCommitWindowSeconds)
				{
					await Log.Warning("ChatSystem",
						$"Persisting {batch.Count} chat message(s) took {writeSeconds:0.0}s, longer than the {ChatService.PumpCommitWindowSeconds:0}s pump commit window. " +
						"Whispers, party and guild lines in it may not have reached players on other scene servers.");
				}

				if (!result.IsSuccess)
				{
					await Log.Warning("ChatSystem", $"FlushPersistQueueAsync DB error ({batch.Count} messages): [{result.ErrorCode}] {result.ErrorMessage}");

					/* One unwritable row used to cost the whole batch — up to
					 * maxPersistBatchSize (2000) messages, because PersistBatchAsync is a single
					 * AddRange + SaveChanges and the entries were already dequeued by the time it
					 * failed. The chat log is kept for audit, so a single malformed row silently
					 * erasing two thousand others is the worst possible failure mode.
					 *
					 * What happens next depends on WHY it failed. A transient failure (the database
					 * unreachable) says nothing about the rows, so they go back on the queue — see
					 * persistRetryWindowSeconds. Anything else is a row the database will not take,
					 * so the batch is bisected until the failing rows are isolated and only those
					 * are lost — see PersistWithIsolationAsync. */
					await PersistWithIsolationAsync(chatService, batch, queuedAt, result, 0);
				}
			}
			catch (Exception ex)
			{
				await Log.Error("ChatSystem", $"Error in FlushPersistQueueAsync: {ex}");
			}
			finally
			{
				Interlocked.Exchange(ref persistFlushInFlight, 0);
			}
		}

		/// <summary>
		/// Maximum bisection depth used to isolate a failing row inside a persist batch.
		/// </summary>
		/// <remarks>
		/// Bounds the error path. A row-specific failure is found in log2(batch) halvings, and
		/// without a cap a batch full of bad rows turns one failed flush into 2 * batchSize more
		/// round-trips. At depth 6 a 2000-message batch is narrowed to blocks of about 32 and a
		/// block still failing there is dropped with a log line — enough granularity that a poison
		/// row costs tens of messages rather than thousands, at a cost of at most 126 extra
		/// attempts. A database that is simply unavailable no longer reaches this at all: its
		/// failure is transient, and a transient failure is re-queued instead of bisected.
		/// </remarks>
		private const int maxPersistIsolationDepth = 6;

		/// <summary>
		/// Settles a persist batch whose write failed: re-queues it when the failure was transient,
		/// otherwise halves it so that only the entries that genuinely cannot be written are lost.
		/// </summary>
		/// <param name="chatService">Chat persistence service.</param>
		/// <param name="entries">Entries whose write just failed.</param>
		/// <param name="queuedAt">Each entry's first-queued <see cref="MonotonicClock"/> seconds, index for index.</param>
		/// <param name="failure">The failure that write returned.</param>
		/// <param name="depth">Current bisection depth; recursion stops at <see cref="maxPersistIsolationDepth"/>.</param>
		private async Task PersistWithIsolationAsync(
			IChatService chatService,
			List<(long, string, string, long, long, FishMMO.Database.Data.Enums.ChatChannel, string, DateTime)> entries,
			List<double> queuedAt,
			DatabaseResult failure,
			int depth)
		{
			if (entries == null || entries.Count < 1)
			{
				return;
			}

			// Shutdown takes priority over salvaging the log.
			if (Server == null ||
				Server.ServerState != ConnectionState.Started ||
				(Server.DataContainerRegistry.TryGet(out IChatChannelRuntimeData runtimeData) && runtimeData.IsShuttingDown))
			{
				await Log.Warning("ChatSystem",
					$"Discarding {entries.Count} chat message(s) that could not be persisted before shutdown: [{failure.ErrorCode}] {failure.ErrorMessage}");
				return;
			}

			if (failure.IsTransient)
			{
				await RequeueForPersistRetryAsync(entries, queuedAt, failure);
				return;
			}

			/* This write has already failed, so a leaf is settled from the failure in hand rather
			 * than by asking the database the same question again. */
			if (entries.Count == 1 || depth >= maxPersistIsolationDepth)
			{
				await Log.Warning("ChatSystem",
					$"Discarding {entries.Count} chat message(s) that could not be persisted: [{failure.ErrorCode}] {failure.ErrorMessage}");
				return;
			}

			int half = entries.Count / 2;
			var first = entries.GetRange(0, half);
			var second = entries.GetRange(half, entries.Count - half);
			var firstQueuedAt = queuedAt.GetRange(0, half);
			var secondQueuedAt = queuedAt.GetRange(half, queuedAt.Count - half);

			DatabaseResult firstResult = await chatService.PersistBatchAsync(first);
			if (!firstResult.IsSuccess)
			{
				await PersistWithIsolationAsync(chatService, first, firstQueuedAt, firstResult, depth + 1);
			}

			DatabaseResult secondResult = await chatService.PersistBatchAsync(second);
			if (!secondResult.IsSuccess)
			{
				await PersistWithIsolationAsync(chatService, second, secondQueuedAt, secondResult, depth + 1);
			}
		}

		/// <summary>
		/// Puts entries refused by a transient database failure back on the persist queue, and
		/// discards those that have been waiting longer than <see cref="persistRetryWindowSeconds"/>.
		/// </summary>
		/// <remarks>
		/// Re-queued at the back rather than the front, which a concurrent queue cannot do anyway.
		/// Nothing depends on the order: every row carries the time the server received it, and
		/// that — not its position in the table — is the time the log records.
		/// </remarks>
		/// <param name="entries">Entries whose write failed transiently.</param>
		/// <param name="failure">The failure, for the log line.</param>
		private async Task RequeueForPersistRetryAsync(
			List<(long characterId, string characterName, string accountName, long worldServerId, long sceneServerId, FishMMO.Database.Data.Enums.ChatChannel channel, string message, DateTime serverReceivedTime)> entries,
			List<double> queuedAt,
			DatabaseResult failure)
		{
			if (Server?.DataContainerRegistry.TryGet(out IChatChannelRuntimeData runtimeData) != true ||
				runtimeData.PendingPersistQueue == null)
			{
				await Log.Error("ChatSystem",
					$"Discarding {entries.Count} chat message(s): the database was unavailable and the persist queue is gone. [{failure.ErrorCode}] {failure.ErrorMessage}");
				return;
			}

			// Measured from when each line was first queued, on the monotonic clock: the receipt stamp is
			// wall time, and a host clock step would discard or keep a whole backlog at once.
			double oldestRetained = MonotonicClock.NowSeconds - persistRetryWindowSeconds;
			int expired = 0;

			for (int i = 0; i < entries.Count; ++i)
			{
				var entry = entries[i];
				if (queuedAt[i] < oldestRetained)
				{
					++expired;
					continue;
				}

				runtimeData.PendingPersistQueue.Enqueue(new PendingChatPersist(
					entry.characterId,
					entry.characterName,
					entry.accountName,
					entry.worldServerId,
					(ChatChannel)(byte)entry.channel,
					entry.message,
					entry.serverReceivedTime.Ticks,
					queuedAt[i]));
			}

			if (expired > 0)
			{
				await Log.Error("ChatSystem",
					$"Discarding {expired} chat message(s) the database has refused for longer than {persistRetryWindowSeconds:0}s: [{failure.ErrorCode}] {failure.ErrorMessage}");
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
				if (!Server.DataContainerRegistry.TryGet(out IChatChannelRuntimeData runtimeData) ||
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
			if (!Server.DataContainerRegistry.TryGet(out IChatChannelRuntimeData runtimeData))
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

			/* One recipient set per world, and each message serialised once for all of them. This
			 * looped a single-connection send over every character in the world for every
			 * message, serialising the same line once per player (hot-path audit S1). */
			HashSet<NetworkConnection> recipients = runtimeData.ConnectionBroadcastSet;
			if (recipients == null)
			{
				return;
			}

			for (int k = 0; k < keyBuffer.Count; k++)
			{
				long worldID = keyBuffer[k];
				if (!buffer.TryGetValue(worldID, out var messages) || messages.Count < 1)
				{
					continue;
				}

				// Cap messages sent per flush to prevent huge network bursts.
				int sendCount = Math.Min(messages.Count, maxOutboundBatchSize);

				if (FillWorldRecipients(mappingData, worldID, recipients))
				{
					try
					{
						for (int m = 0; m < sendCount; m++)
						{
							Server.NetworkWrapper.Broadcast(recipients, messages[m], true, Channel.Reliable);
						}
					}
					finally
					{
						recipients.Clear();
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
			string relayText = ChatHelper.SanitizeIncoming(message, CoreMaxMessageLength);
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

			BroadcastToWorld(worldServerID, newMsg);
		}


		/// <summary>The core chat intake's message length cap (<see cref="IChatSystem.MaxMessageLength"/>), or the wire limit without it.</summary>
		private int CoreMaxMessageLength =>
			Server?.BehaviourRegistry != null && Server.BehaviourRegistry.TryGet(out IChatSystem chat)
				? chat.MaxMessageLength
				: ChatBroadcast.MaxTextLength;
		/// <summary>
		/// Registers the channel handlers and the periodic pump, persistence and outbound flushes.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("ChatChannelSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}
			if (!Server.DataContainerRegistry.TryGet<IChatSystemMainThreadQueueData>(out _))
			{
				Log.Error("ChatChannelSystem", "Failed to initialize: IChatSystemMainThreadQueueData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}
			if (!Server.DataContainerRegistry.TryGet<IChatChannelRuntimeData>(out var runtimeData))
			{
				Log.Error("ChatChannelSystem", "Failed to initialize: IChatChannelRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			// Channel → handler dictionary.
			runtimeData.ChannelCommandMap = new Dictionary<ChatChannel, ChatCommand>
			{
				{ ChatChannel.World, OnWorldChat },
				{ ChatChannel.Region, OnRegionChat },
				{ ChatChannel.Party, OnPartyChat },
				{ ChatChannel.Guild, OnGuildChat },
				{ ChatChannel.Tell, OnTellChat },
				{ ChatChannel.Trade, OnTradeChat },
				{ ChatChannel.Say, OnSayChat },
				{ ChatChannel.Team, OnTeamChat },
			};

			// The channel words ("/w", "/g", …) the core's command registry resolves to these handlers.
			ChatHelper.InitializeOnce(GetChannelCommand);

			// Party-addressed server signals (Call to Arms) ride this system's party rows.
			PartySignals.Poster = PostPartySignal;

			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.RegisterPeriodicCallback(MessagePumpRate, OnPeriodicMessagePump);
				periodicSystem.RegisterPeriodicCallback(persistFlushIntervalSeconds, OnPeriodicPersistFlush);
				periodicSystem.RegisterPeriodicCallback(outboundBatchIntervalSeconds, OnPeriodicOutboundFlush);
			}

			maxMainThreadActionsPerFrame = Mathf.Max(1, maxMainThreadActionsPerFrame);
			persistFlushIntervalSeconds = Mathf.Max(0.01f, persistFlushIntervalSeconds);
			maxPersistBatchSize = Mathf.Max(1, maxPersistBatchSize);
			persistRetryWindowSeconds = Mathf.Max(0.0f, persistRetryWindowSeconds);
			outboundBatchIntervalSeconds = Mathf.Max(0.01f, outboundBatchIntervalSeconds);
			maxOutboundBatchSize = Mathf.Max(1, maxOutboundBatchSize);
			maxBufferedWorldMessages = Mathf.Max(1, maxBufferedWorldMessages);
			messagePumpPageSize = Mathf.Clamp(messagePumpPageSize, 1, ChatService.MaxPumpPageSize);
			messagePumpMaxPages = Mathf.Clamp(messagePumpMaxPages, 1, ChatService.MaxPumpPages);

			/* Cleared here rather than trusted. This object outlives a play session in the editor,
			 * and a flag left set by one that was torn down mid-flush would stop the next from ever
			 * persisting a line of chat. */
			Interlocked.Exchange(ref persistFlushInFlight, 0);

			Log.Debug("ChatChannelSystem", $"Initialized (MessagePumpRate={MessagePumpRate}s, PumpPageSize={messagePumpPageSize}, PumpMaxPages={messagePumpMaxPages})");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Flushes what is still buffered or queued, then unregisters the channel handlers and the periodic callbacks.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("ChatChannelSystem", "OnDeinitialize: Server is null");
				return;
			}

			if (PartySignals.Poster == (Func<IPlayerCharacter, long, string, bool>)PostPartySignal)
			{
				PartySignals.Poster = null;
			}

			// Flush any remaining outbound World/Trade broadcast buffers.
			FlushOutboundBroadcastBuffers();

			// Signal shutdown so async flush paths exit early and don't race with the sync flush.
			if (Server.DataContainerRegistry.TryGet(out IChatChannelRuntimeData shutdownData))
			{
				shutdownData.IsShuttingDown = true;
			}

			// Flush any remaining pending persist entries before shutdown.
			FlushPersistQueueSync();

			// Drain any remaining queued main-thread actions
			DrainMainThreadQueue(drainAll: true);

			/* Drop the static channel registration so the next run rebuilds it.
			 *
			 * ChatHelper's maps are static and hold delegates bound to this ScriptableObject.
			 * They outlive a play-session restart in the editor while this object does not, and
			 * InitializeOnce's latch meant the second session kept the first session's handlers. */
			ChatHelper.ResetChannelCommands();

			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicMessagePump);
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicPersistFlush);
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicOutboundFlush);
			}
		}

		/// <inheritdoc />
		protected override void OnUpdate(float deltaTime)
		{
			DrainMainThreadQueue(drainAll: false);
		}

		/// <summary>
		/// Routes one line the core intake accepted to its channel and queues it for persistence. Moved from the tail of
		/// <c>ChatSystem.ProcessNewChatMessage</c>.
		/// </summary>
		public void HandleChatLine(NetworkConnection conn, IPlayerCharacter sender, ChatBroadcast msg, string command, long receivedTicks, int maxMessageLength)
		{
			if (!Initialized || Server == null || sender == null || msg.Text == null)
			{
				return;
			}

			/* The duplicate filter is for chat, not for commands, so it applies here, past the command
			 * registry: repeating a slash command is normal (the first attempt is often refused for a reason
			 * that then goes away). A channel prefix such as "/w" arrives with the prefix already stripped,
			 * so the comparison is against the message body, which is what a player repeating themselves
			 * actually repeats. */
			if (!AllowRepeatMessages)
			{
				if (!string.IsNullOrWhiteSpace(sender.Component<ChatState>().LastMessage) &&
					sender.Component<ChatState>().LastMessage.Equals(msg.Text))
				{
					return;
				}
				sender.Component<ChatState>().LastMessage = msg.Text;
			}

			// the text is empty
			if (msg.Text.Length < 1)
			{
				return;
			}

			if (ChatHelper.TryParseChatCommand(command, out ChatCommandDetails commandDetails))
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
	}
}
