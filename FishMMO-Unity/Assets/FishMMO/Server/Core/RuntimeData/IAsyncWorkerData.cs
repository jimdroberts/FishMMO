using System;
using System.Threading.Tasks;

namespace FishMMO.Server.Core
{
	/// <summary>
	/// What <see cref="IAsyncWorkerData.EnqueueRequired"/> did with a work item.
	/// </summary>
	public enum AsyncWorkAdmission : byte
	{
		/// <summary>Admitted within the backpressure threshold.</summary>
		Admitted = 0,

		/// <summary>Admitted past the backpressure threshold. It will run, behind the backlog.</summary>
		AdmittedOverCapacity = 1,

		/// <summary>Not admitted: the pool is not running. The caller must run the work some other way.</summary>
		Refused = 2,
	}

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
		/// Admits work that must not be refused for being over the backpressure threshold — a write
		/// whose in-memory state has already been committed, so dropping it would lose data.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Past the threshold the item is still admitted, still runs under the same concurrency cap as
		/// everything else, and still keeps its place in its entity's order. What it does not do is
		/// run immediately: it waits for a slot like any other item.
		/// </para>
		/// <para>
		/// This replaces the caller-side fallback that ran over-threshold persistence on the thread
		/// pool directly. That fallback was outside the concurrency cap, so a database stall that
		/// filled the queue turned into an unbounded number of concurrent writes against the
		/// connection pool — which then timed out, filed repairs, and queued more writes — and it
		/// broke the per-entity order the incremental item writes rely on, at exactly the moment
		/// the server was busiest.
		/// </para>
		/// <para>
		/// Items admitted over the threshold still count towards it, so ordinary
		/// <see cref="Enqueue(Func{Task}, string)"/> callers keep seeing backpressure until the
		/// backlog drains.
		/// </para>
		/// </remarks>
		/// <param name="work">The async work to execute.</param>
		/// <param name="entityKey">Entity identifier for ordering, or 0 for none.</param>
		/// <param name="callerName">Optional caller identifier for diagnostics.</param>
		/// <returns>
		/// Whether the work was admitted, and whether it was over the threshold. Refused only when the
		/// pool is not running at all (not yet initialised, or shutting down).
		/// </returns>
		AsyncWorkAdmission EnqueueRequired(Func<Task> work, long entityKey, string callerName = null);

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