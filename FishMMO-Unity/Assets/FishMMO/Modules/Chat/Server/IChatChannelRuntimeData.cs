using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using FishNet.Connection;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Runtime data of the Chat module's channel system: where the cross-server pump has got to, the channel handlers,
	/// the persistence queue and the batched outbound world messages. Split from the core chat intake's data with the
	/// module.
	/// </summary>
	public interface IChatChannelRuntimeData : IRuntimeDataContainer
	{
		/// <summary>
		/// Where the cross-server chat pump has got to: its trailing window, the rows it has
		/// already handled in it, and when each relevance key became relevant here.
		/// Only used from the main thread.
		/// </summary>
		ChatPumpCursor PumpCursor { get; }

		/// <summary>
		/// Reusable recipient set for one multicast chat broadcast, so a line bound for many
		/// players is serialised once instead of once per recipient. Filled, sent and cleared
		/// within one call; never kept. Only used from the main thread.
		/// </summary>
		HashSet<NetworkConnection> ConnectionBroadcastSet { get; }

		/// <summary>
		/// Atomically transitions the message pump from idle to in-flight.
		/// Returns true if this call won the race; false if a pump is already in flight.
		/// </summary>
		bool TryBeginMessagePump();

		/// <summary>
		/// Atomically transitions the message pump from in-flight back to idle.
		/// </summary>
		void EndMessagePump();

		/// <summary>
		/// Maps each <see cref="ChatChannel"/> to its corresponding handler delegate.
		/// Populated during initialization; used for OCP-compliant channel dispatch.
		/// </summary>
		Dictionary<ChatChannel, ChatCommand> ChannelCommandMap { get; set; }

		/// <summary>
		/// Lock-free queue for chat messages pending batch DB persistence.
		/// Channel handlers enqueue persistence entries; a periodic callback flushes them
		/// to the database in batches via PersistBatchAsync. Reduces per-message DB writes
		/// from O(N) to ~O(N/batchSize) round-trips.
		/// </summary>
		ConcurrentQueue<PendingChatPersist> PendingPersistQueue { get; }

		/// <summary>
		/// Reusable scratch list for batch DB persistence, avoiding per-flush allocation.
		/// Cleared and reused each flush cycle. Only used from a single flush path (async worker or sync shutdown).
		/// </summary>
		List<(long characterId, string characterName, string accountName, long worldServerId, long sceneServerId, FishMMO.Database.Data.Enums.ChatChannel channel, string message, DateTime serverReceivedTime)> PersistBatchBuffer { get; }

		/// <summary>
		/// Set to true when the server begins its shutdown sequence.
		/// Async flush paths check this flag and exit early to prevent
		/// duplicate writes against the synchronous shutdown flush.
		/// </summary>
		bool IsShuttingDown { get; set; }

		/// <summary>
		/// Accumulates outbound world/trade broadcasts per world ID for batched sending.
		/// Flushed on a periodic callback to reduce per-message network overhead.
		/// Main-thread only.
		/// </summary>
		Dictionary<long, List<ChatBroadcast>> OutboundWorldBroadcastBuffer { get; }

		/// <summary>
		/// Reusable scratch list for outbound world broadcast flush, avoiding per-flush allocation.
		/// Main-thread only.
		/// </summary>
		List<long> OutboundWorldFlushKeyBuffer { get; }
	}

	/// <summary>
	/// Immutable struct for a chat message pending batch DB persistence.
	/// Captured on the main or async thread, consumed on the periodic flush background thread.
	/// Stores the receive timestamp as ticks to avoid DateTime allocation on the hot path;
	/// converted to DateTime only at the DB boundary.
	/// </summary>
	public readonly struct PendingChatPersist
	{
		/// <summary>
		/// The character identifier associated with this chat message.
		/// </summary>
		public readonly long CharacterId;

		/// <summary>
		/// The character name associated with this chat message.
		/// </summary>
		public readonly string CharacterName;

		/// <summary>
		/// The account name associated with this chat message.
		/// </summary>
		public readonly string AccountName;

		/// <summary>
		/// The world server identifier that received this chat message.
		/// </summary>
		public readonly long WorldServerId;

		/// <summary>
		/// The chat channel this message was sent to.
		/// </summary>
		public readonly ChatChannel Channel;

		/// <summary>
		/// The chat message content.
		/// </summary>
		public readonly string Message;

		/// <summary>
		/// UTC timestamp in ticks when the message was received by the server.
		/// </summary>
		public readonly long ReceivedTicks;

		/// <summary>
		/// <see cref="MonotonicClock"/> seconds when the line was first queued. A retry keeps it, so
		/// the persist-retry window is measured on a clock a host clock step cannot move.
		/// </summary>
		public readonly double QueuedAtSeconds;

		/// <summary>
		/// Initializes a new pending chat persist entry for batch DB persistence.
		/// </summary>
		/// <param name="characterId">The character identifier.</param>
		/// <param name="characterName">The character name.</param>
		/// <param name="accountName">The account name.</param>
		/// <param name="worldServerId">The world server identifier.</param>
		/// <param name="channel">The chat channel.</param>
		/// <param name="message">The chat message content.</param>
		/// <param name="receivedTicks">UTC timestamp in ticks when the message was received.</param>
		/// <param name="queuedAtSeconds">When the line was first queued; NaN (the default) means now.</param>
		public PendingChatPersist(long characterId, string characterName, string accountName,
			long worldServerId, ChatChannel channel, string message, long receivedTicks, double queuedAtSeconds = double.NaN)
		{
			QueuedAtSeconds = double.IsNaN(queuedAtSeconds) ? MonotonicClock.NowSeconds : queuedAtSeconds;
			CharacterId = characterId;
			CharacterName = characterName;
			AccountName = accountName;
			WorldServerId = worldServerId;
			Channel = channel;
			Message = message;
			ReceivedTicks = receivedTicks;
		}
	}
}
