using System;
using System.Threading.Tasks;

namespace FishMMO.Server.Core
{
	/// <summary>
	/// Runtime data container interface for a centralized async work queue.
	/// Provides bounded, backpressure-aware enqueueing of async work items,
	/// executed concurrently under a configurable concurrency limit.
	///
	/// Systems use this instead of fire-and-forget <c>_ = SomeAsync(...)</c>
	/// to bound how much work is in flight at once and to get ordering
	/// guarantees for entity-keyed work.
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
		/// Work items sharing the same entityKey are guaranteed to execute in FIFO order,
		/// one at a time; items with different keys proceed independently.
		/// Returns true if the item was accepted, false if the queue is full (backpressure).
		/// </summary>
		/// <remarks>
		/// An <paramref name="entityKey"/> of 0 means "no ordering requirement" and is treated
		/// exactly like the unkeyed overload. It is not an entity whose id happens to be zero, and
		/// callers that pass a default id are not asking to be serialized with each other.
		/// </remarks>
		/// <param name="work">The async work to execute.</param>
		/// <param name="entityKey">Entity identifier for ordering (e.g., characterID), or 0 for none.</param>
		/// <param name="callerName">Optional caller identifier for diagnostics.</param>
		/// <returns>True if enqueued successfully.</returns>
		bool Enqueue(Func<Task> work, long entityKey, string callerName = null);

		/// <summary>
		/// Current number of items accepted but not yet started.
		/// Useful for monitoring and diagnostics.
		/// </summary>
		int PendingCount { get; }

		/// <summary>
		/// Total number of work items processed since startup.
		/// </summary>
		long CompletedCount { get; }
	}
}