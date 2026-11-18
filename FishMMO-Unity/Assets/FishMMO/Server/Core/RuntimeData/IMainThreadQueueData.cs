using System;

namespace FishMMO.Server.Core
{
	/// <summary>
	/// Runtime data container interface for a thread-safe main-thread action queue.
	/// Systems use this to marshal network operations (Broadcast, Kick, Disconnect)
	/// from async worker threads back to the main Unity thread.
	/// </summary>
	public interface IMainThreadQueueData : IRuntimeDataContainer
	{
		/// <summary>
		/// Thread-safe attempt to enqueue an action to the main Unity thread.
		/// Called from async worker threads.
		/// </summary>
		/// <param name="action">The action to execute on the main thread.</param>
		/// <returns><c>true</c> if the action was enqueued; <c>false</c> if the queue is at capacity.</returns>
		bool TryEnqueue(Action action);

		/// <summary>
		/// Drains all queued actions, executing them on the calling thread.
		/// Must be called from the main Unity thread (e.g., in OnLateUpdate).
		/// Copies all queued actions under lock, then invokes them outside the lock
		/// to minimize lock hold time and avoid re-entrancy issues.
		/// </summary>
		void Drain();

		/// <summary>
		/// Drains up to <paramref name="maxActions"/> queued actions, executing them on the calling thread.
		/// Must be called from the main Unity thread (e.g., in OnLateUpdate).
		/// </summary>
		/// <param name="maxActions">Maximum number of actions to execute in this call. Values &lt;= 0 execute none.</param>
		/// <returns>Number of actions executed.</returns>
		int Drain(int maxActions);
	}
}
