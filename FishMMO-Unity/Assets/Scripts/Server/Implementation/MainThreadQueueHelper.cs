using System;
using FishNet.Connection;
using FishMMO.Server.Core;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Shared utility for main-thread queue drain and enqueue operations.
	/// Eliminates copy-paste boilerplate across server systems (D1).
	/// </summary>
	public static class MainThreadQueueHelper
	{
		/// <summary>
		/// Drains the main-thread queue, processing up to maxActions (or all if drainAll is true).
		/// </summary>
		/// <typeparam name="TQueue">The specific IMainThreadQueueData subtype for this system.</typeparam>
		/// <param name="server">Server instance (null-safe).</param>
		/// <param name="maxActions">Maximum actions to drain per call.</param>
		/// <param name="drainAll">If true, drains all queued actions regardless of maxActions.</param>
		public static void Drain<TQueue>(
			IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server,
			int maxActions,
			bool drainAll)
			where TQueue : class, IMainThreadQueueData
		{
			if (server?.DataContainerRegistry.TryGet<TQueue>(out var queueData) == true)
			{
				if (drainAll)
				{
					queueData.Drain();
				}
				else
				{
					queueData.Drain(maxActions);
				}
			}
		}

		/// <summary>
		/// Enqueues an action on the main-thread queue. Returns false if the queue is at capacity or unavailable.
		/// </summary>
		/// <typeparam name="TQueue">The specific IMainThreadQueueData subtype for this system.</typeparam>
		/// <param name="server">Server instance (null-safe).</param>
		/// <param name="action">The action to enqueue.</param>
		/// <returns>True if enqueued successfully; false if queue full or unavailable.</returns>
		public static bool TryEnqueue<TQueue>(
			IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server,
			Action action)
			where TQueue : class, IMainThreadQueueData
		{
			if (server?.DataContainerRegistry.TryGet<TQueue>(out var queueData) == true)
			{
				return queueData.TryEnqueue(action);
			}
			return false;
		}
	}
}
