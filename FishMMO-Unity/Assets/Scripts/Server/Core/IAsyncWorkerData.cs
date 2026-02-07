using System;
using System.Threading.Tasks;

namespace FishMMO.Server.Core
{
	/// <summary>
	/// Runtime data container interface for a centralized async work queue.
	/// Provides bounded, backpressure-aware enqueueing of async work items
	/// that are processed by a configurable number of worker loops.
	///
	/// Systems use this instead of fire-and-forget <c>_ = SomeAsync(...)</c>
	/// to control thread pool utilization and provide ordering guarantees
	/// for entity-keyed work.
	/// </summary>
	public interface IAsyncWorkerData : IRuntimeDataContainer
	{
		/// <summary>
		/// Enqueue an async work item for processing.
		/// Returns true if the item was accepted, false if the queue is full (backpressure).
		/// </summary>
		/// <param name="work">The async work to execute.</param>
		/// <param name="callerName">Optional caller identifier for diagnostics.</param>
		/// <returns>True if enqueued successfully.</returns>
		bool Enqueue(Func<Task> work, string callerName = null);

		/// <summary>
		/// Enqueue an async work item with an entity key for ordered processing.
		/// Work items sharing the same entityKey are guaranteed to execute in FIFO order
		/// (routed to the same worker via consistent hashing).
		/// Returns true if the item was accepted, false if the queue is full (backpressure).
		/// </summary>
		/// <param name="work">The async work to execute.</param>
		/// <param name="entityKey">Entity identifier for ordering (e.g., characterID).</param>
		/// <param name="callerName">Optional caller identifier for diagnostics.</param>
		/// <returns>True if enqueued successfully.</returns>
		bool Enqueue(Func<Task> work, long entityKey, string callerName = null);

		/// <summary>
		/// Current number of items waiting in the queue(s).
		/// Useful for monitoring and diagnostics.
		/// </summary>
		int PendingCount { get; }

		/// <summary>
		/// Total number of work items processed since startup.
		/// </summary>
		long CompletedCount { get; }
	}
}