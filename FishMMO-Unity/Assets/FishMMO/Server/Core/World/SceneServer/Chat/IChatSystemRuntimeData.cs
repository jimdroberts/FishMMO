using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using FishNet.Connection;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime data of the core chat intake: the lock-free queue of incoming lines and its size. The conversation state
	/// (the cross-server pump, channel map, persistence and outbound buffers) is the Chat module's
	/// (<c>IChatChannelRuntimeData</c>).
	/// </summary>
	public interface IChatSystemRuntimeData : IRuntimeDataContainer
	{
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
	}
}
