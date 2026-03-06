using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using FishNet.Connection;
using FishMMO.Database.Data.Enums;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
using FishMMO.Shared;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Runtime data container for chat message synchronization state.
	/// Manages chat database polling state separately from ChatSystem logic.
	/// </summary>
	public class ChatSystemRuntimeData : RuntimeDataContainer, IChatSystemRuntimeData
	{
		/// <summary>
		/// Timestamp of the last successful database fetch for chat messages.
		/// </summary>
		public DateTime LastFetchTime { get; set; }

		/// <summary>
		/// Position (ID) of the last fetched chat message in the database.
		/// </summary>
		public long LastFetchPosition { get; set; }

		/// <inheritdoc/>
		public List<IPlayerCharacter> CharacterBroadcastBuffer { get; private set; }

		/// <inheritdoc/>
		public List<NetworkConnection> ConnectionBroadcastBuffer { get; private set; }

		/// <inheritdoc/>
		public Dictionary<Shared.ChatChannel, ChatCommand> ChannelCommandMap { get; set; }

		/// <inheritdoc/>
		public ConcurrentQueue<(NetworkConnection Connection, ChatBroadcast Message)> IncomingChatQueue { get; private set; }

		private int incomingQueueSize;

		/// <inheritdoc/>
		public int IncomingQueueSize => Volatile.Read(ref incomingQueueSize);

		/// <inheritdoc/>
		public int IncrementIncomingQueueSize() => Interlocked.Increment(ref incomingQueueSize);

		/// <inheritdoc/>
		public int DecrementIncomingQueueSize() => Interlocked.Decrement(ref incomingQueueSize);

		/// <inheritdoc/>
		public ConcurrentQueue<PendingChatPersist> PendingPersistQueue { get; private set; }

		/// <inheritdoc/>
		public List<(long characterId, string characterName, string accountName, long worldServerId, long sceneServerId, FishMMO.Database.Data.Enums.ChatChannel channel, string message, DateTime serverReceivedTime)> PersistBatchBuffer { get; private set; }

		/// <inheritdoc/>
		public bool IsShuttingDown { get; set; }

		/// <inheritdoc/>
		public Dictionary<long, List<ChatBroadcast>> OutboundWorldBroadcastBuffer { get; private set; }

		/// <inheritdoc/>
		public List<long> OutboundWorldFlushKeyBuffer { get; private set; }

		private int messagePumpInFlight;

		/// <inheritdoc/>
		public bool TryBeginMessagePump()
		{
			return Interlocked.CompareExchange(ref messagePumpInFlight, 1, 0) == 0;
		}

		/// <inheritdoc/>
		public void EndMessagePump()
		{
			Interlocked.Exchange(ref messagePumpInFlight, 0);
		}

		/// <summary>
		/// Initializes the chat message queue data container.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			LastFetchTime = DateTime.UtcNow;
			LastFetchPosition = 0;
			CharacterBroadcastBuffer = new List<IPlayerCharacter>();
			ConnectionBroadcastBuffer = new List<NetworkConnection>();
			ChannelCommandMap = new Dictionary<Shared.ChatChannel, ChatCommand>();
			IncomingChatQueue = new ConcurrentQueue<(NetworkConnection, ChatBroadcast)>();
			Interlocked.Exchange(ref incomingQueueSize, 0);
			PendingPersistQueue = new ConcurrentQueue<PendingChatPersist>();
			PersistBatchBuffer = new List<(long, string, string, long, long, FishMMO.Database.Data.Enums.ChatChannel, string, DateTime)>();
			IsShuttingDown = false;
			OutboundWorldBroadcastBuffer = new Dictionary<long, List<ChatBroadcast>>();
			OutboundWorldFlushKeyBuffer = new List<long>();
			Interlocked.Exchange(ref messagePumpInFlight, 0);
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears the chat message queue state.
		/// </summary>
		public override void Clear()
		{
			LastFetchTime = DateTime.UtcNow;
			LastFetchPosition = 0;
			CharacterBroadcastBuffer?.Clear();
			ConnectionBroadcastBuffer?.Clear();
			ChannelCommandMap?.Clear();
			// ConcurrentQueues: drain instead of nulling — they may be in use from another thread.
			if (IncomingChatQueue != null)
			{
				while (IncomingChatQueue.TryDequeue(out _)) { }
			}
			Interlocked.Exchange(ref incomingQueueSize, 0);
			if (PendingPersistQueue != null)
			{
				while (PendingPersistQueue.TryDequeue(out _)) { }
			}
			PersistBatchBuffer?.Clear();
			IsShuttingDown = false;
			OutboundWorldBroadcastBuffer?.Clear();
			OutboundWorldFlushKeyBuffer?.Clear();
			Interlocked.Exchange(ref messagePumpInFlight, 0);
		}

		/// <summary>
		/// Deinitializes the chat message queue data container.
		/// </summary>
		protected override void OnDeinitialize()
		{
			Clear();
			CharacterBroadcastBuffer = null;
			ConnectionBroadcastBuffer = null;
			ChannelCommandMap = null;
			IncomingChatQueue = null;
			PendingPersistQueue = null;
			PersistBatchBuffer = null;
			OutboundWorldBroadcastBuffer = null;
			OutboundWorldFlushKeyBuffer = null;
		}
	}
}