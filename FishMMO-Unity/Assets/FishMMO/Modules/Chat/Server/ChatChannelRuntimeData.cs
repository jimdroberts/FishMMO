using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using FishNet.Connection;
using ChatService = FishMMO.Database.Npgsql.Services.ChatService;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The Chat module's channel system state: the cross-server pump, the channel handlers, persistence and the batched
	/// outbound world messages. Split from the core chat intake's data with the module.
	/// </summary>
	public class ChatChannelRuntimeData : RuntimeDataContainer, IChatChannelRuntimeData
	{
		/// <inheritdoc />
		public ChatPumpCursor PumpCursor { get; private set; }

		/// <inheritdoc />
		public HashSet<NetworkConnection> ConnectionBroadcastSet { get; private set; }

		/// <inheritdoc />
		public Dictionary<Shared.ChatChannel, ChatCommand> ChannelCommandMap { get; set; }

		/// <inheritdoc />
		public ConcurrentQueue<PendingChatPersist> PendingPersistQueue { get; private set; }

		/// <inheritdoc />
		public List<(long characterId, string characterName, string accountName, long worldServerId, long sceneServerId, FishMMO.Database.Data.Enums.ChatChannel channel, string message, DateTime serverReceivedTime)> PersistBatchBuffer { get; private set; }

		/// <inheritdoc />
		public bool IsShuttingDown { get; set; }

		/// <inheritdoc />
		public Dictionary<long, List<ChatBroadcast>> OutboundWorldBroadcastBuffer { get; private set; }

		/// <inheritdoc />
		public List<long> OutboundWorldFlushKeyBuffer { get; private set; }

		private int messagePumpInFlight;

		/// <inheritdoc />
		public bool TryBeginMessagePump()
		{
			return Interlocked.CompareExchange(ref messagePumpInFlight, 1, 0) == 0;
		}

		/// <inheritdoc />
		public void EndMessagePump()
		{
			Interlocked.Exchange(ref messagePumpInFlight, 0);
		}

		/// <inheritdoc />
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			/* The window is the writer's contract, not a tuning knob of the reader's: see
			 * ChatService.PumpCommitWindowSeconds. */
			PumpCursor = new ChatPumpCursor(TimeSpan.FromSeconds(ChatService.PumpCommitWindowSeconds));
			ConnectionBroadcastSet = new HashSet<NetworkConnection>();
			ChannelCommandMap = new Dictionary<Shared.ChatChannel, ChatCommand>();
			PendingPersistQueue = new ConcurrentQueue<PendingChatPersist>();
			PersistBatchBuffer = new List<(long, string, string, long, long, FishMMO.Database.Data.Enums.ChatChannel, string, DateTime)>();
			IsShuttingDown = false;
			OutboundWorldBroadcastBuffer = new Dictionary<long, List<ChatBroadcast>>();
			OutboundWorldFlushKeyBuffer = new List<long>();
			Interlocked.Exchange(ref messagePumpInFlight, 0);
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <inheritdoc />
		public override void Clear()
		{
			PumpCursor?.Reset();
			ConnectionBroadcastSet?.Clear();
			ChannelCommandMap?.Clear();
			// Drained instead of nulled: an async flush may be reading it.
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

		/// <inheritdoc />
		protected override void OnDeinitialize()
		{
			Clear();
			PumpCursor = null;
			ConnectionBroadcastSet = null;
			ChannelCommandMap = null;
			PendingPersistQueue = null;
			PersistBatchBuffer = null;
			OutboundWorldBroadcastBuffer = null;
			OutboundWorldFlushKeyBuffer = null;
		}
	}
}
