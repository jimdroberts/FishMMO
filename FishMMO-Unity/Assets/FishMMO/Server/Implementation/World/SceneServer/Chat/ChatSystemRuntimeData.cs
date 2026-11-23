using System.Collections.Concurrent;
using System.Threading;
using FishNet.Connection;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The core chat intake's runtime data: the lock-free queue of incoming lines and its size. The conversation state
	/// lives in the Chat module (<c>ChatChannelRuntimeData</c>).
	/// </summary>
	public class ChatSystemRuntimeData : RuntimeDataContainer, IChatSystemRuntimeData
	{
		/// <inheritdoc />
		public ConcurrentQueue<(NetworkConnection Connection, ChatBroadcast Message)> IncomingChatQueue { get; private set; }

		private int incomingQueueSize;

		/// <inheritdoc />
		public int IncomingQueueSize => Volatile.Read(ref incomingQueueSize);

		/// <inheritdoc />
		public int IncrementIncomingQueueSize() => Interlocked.Increment(ref incomingQueueSize);

		/// <inheritdoc />
		public int DecrementIncomingQueueSize() => Interlocked.Decrement(ref incomingQueueSize);

		/// <inheritdoc />
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			IncomingChatQueue = new ConcurrentQueue<(NetworkConnection, ChatBroadcast)>();
			Interlocked.Exchange(ref incomingQueueSize, 0);
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <inheritdoc />
		public override void Clear()
		{
			// Drained instead of nulled: the network thread may be enqueueing.
			if (IncomingChatQueue != null)
			{
				while (IncomingChatQueue.TryDequeue(out _)) { }
			}
			Interlocked.Exchange(ref incomingQueueSize, 0);
		}

		/// <inheritdoc />
		protected override void OnDeinitialize()
		{
			Clear();
			IncomingChatQueue = null;
		}
	}
}
