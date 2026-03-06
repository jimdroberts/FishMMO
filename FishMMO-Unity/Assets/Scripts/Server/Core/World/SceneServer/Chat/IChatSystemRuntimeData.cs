using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using FishNet.Connection;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime data container for chat message synchronization state.
	/// Tracks database polling position for chat message pump.
	/// </summary>
	public interface IChatSystemRuntimeData : IRuntimeDataContainer
	{
		/// <summary>
		/// Timestamp of the last successful database fetch for chat messages.
		/// </summary>
		DateTime LastFetchTime { get; set; }

		/// <summary>
		/// Position (ID) of the last fetched chat message in the database.
		/// </summary>
		long LastFetchPosition { get; set; }

		/// <summary>
		/// Reusable scratch list for character broadcast iteration, avoiding per-message allocation.
		/// Only used from the main thread.
		/// </summary>
		List<IPlayerCharacter> CharacterBroadcastBuffer { get; }

		/// <summary>
		/// Reusable scratch list for connection broadcast iteration, avoiding per-message allocation.
		/// Only used from the main thread.
		/// </summary>
		List<NetworkConnection> ConnectionBroadcastBuffer { get; }

		/// <summary>
		/// Atomic in-flight flag for the periodic message pump.
		/// 0 = idle, 1 = running.
		/// </summary>
		bool TryBeginMessagePump();
		void EndMessagePump();

		/// <summary>
		/// Maps each <see cref="ChatChannel"/> to its corresponding handler delegate.
		/// Populated during initialization; used for OCP-compliant channel dispatch.
		/// </summary>
		Dictionary<ChatChannel, ChatCommand> ChannelCommandMap { get; set; }

		/// <summary>
		/// Lock-free queue for incoming chat broadcasts from the network layer.
		/// The networking callback enqueues stamped messages; the main-thread OnUpdate
		/// drains up to N per frame for processing. Prevents network spikes from freezing gameplay.
		/// </summary>
		ConcurrentQueue<(NetworkConnection Connection, ChatBroadcast Message)> IncomingChatQueue { get; }

		/// <summary>
		/// Atomically increments the incoming queue size counter and returns the new value.
		/// O(1) replacement for ConcurrentQueue.Count (which is O(N)).
		/// </summary>
		int IncrementIncomingQueueSize();

		/// <summary>
		/// Atomically decrements the incoming queue size counter and returns the new value.
		/// Called on the main thread after each successful TryDequeue.
		/// </summary>
		int DecrementIncomingQueueSize();

		/// <summary>
		/// Current approximate size of the incoming queue (via atomic counter, O(1)).
		/// </summary>
		int IncomingQueueSize { get; }

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
		public readonly long CharacterId;
		public readonly string CharacterName;
		public readonly string AccountName;
		public readonly long WorldServerId;
		public readonly ChatChannel Channel;
		public readonly string Message;
		public readonly long ReceivedTicks;

		public PendingChatPersist(long characterId, string characterName, string accountName,
			long worldServerId, ChatChannel channel, string message, long receivedTicks)
		{
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