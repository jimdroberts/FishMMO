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
		/// Initializes a new pending chat persist entry for batch DB persistence.
		/// </summary>
		/// <param name="characterId">The character identifier.</param>
		/// <param name="characterName">The character name.</param>
		/// <param name="accountName">The account name.</param>
		/// <param name="worldServerId">The world server identifier.</param>
		/// <param name="channel">The chat channel.</param>
		/// <param name="message">The chat message content.</param>
		/// <param name="receivedTicks">UTC timestamp in ticks when the message was received.</param>
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