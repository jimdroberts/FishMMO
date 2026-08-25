using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Logging;
using FishMMO.Server.Core;
using UnityEngine;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Centralized async work queue.
	///
	/// Replaces fire-and-forget <c>_ = SomeAsync(...)</c> across all server systems with a
	/// bounded, backpressure-aware pool that runs work items concurrently while preserving
	/// FIFO order between items that share an entity key.
	///
	/// Design:
	/// <list type="bullet">
	///   <item>Work runs concurrently, capped by <see cref="maxConcurrency"/>. A slow item delays only the items ordered behind it.</item>
	///   <item>Items sharing an <c>entityKey</c> run in the order they were enqueued, one at a time.</item>
	///   <item>Bounded admission: <see cref="Enqueue(Func{Task}, string)"/> returns false once <see cref="maxOutstandingItems"/> items are accepted but unfinished.</item>
	///   <item>Nothing ever executes on the calling thread — see <see cref="DispatchUnordered"/>.</item>
	/// </list>
	///
	/// Usage:
	/// <code>
	/// // Unordered — runs as soon as a concurrency slot is free:
	/// asyncWorkerData.Enqueue(() => PersistInventoryAsync(dto));
	///
	/// // Ordered — this character's items run in enqueue order, one at a time:
	/// asyncWorkerData.Enqueue(() => SaveCharacterAsync(charData), characterID);
	/// </code>
	///
	/// Systems declare dependency via:
	/// <c>[RequiresDataContainer(typeof(AsyncWorkerData))]</c>
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why this is not a set of sequential worker loops.</b> It used to be: N channels, one
	/// long-lived loop each, <c>await item.Work()</c> one item at a time. That made every worker
	/// a head-of-line queue — a single item that waited on something slow stalled every unrelated
	/// item routed to the same channel, for as long as it took. The waits are real and they are
	/// long: <c>RunOnMainThreadAsync</c> blocks for up to 30 seconds when the main-thread queue is
	/// not draining, <c>ClaimCharacterSessionAsync</c> backs off across five attempts, and any
	/// database call can stall. With eight loops it took eight such items to halt every save,
	/// session release, scene status write and routing decision on the process at once — while the
	/// thread pool sat idle, because none of those items were using a thread. They were awaiting.
	/// </para>
	/// <para>
	/// Concurrency is what this pool should be bounding, not parallelism, so it bounds it directly
	/// with a semaphore and lets the thread pool schedule. Ordering is the one thing the loops
	/// genuinely provided, and it is preserved exactly where it was promised — per entity key —
	/// rather than as a side effect of which channel an item hashed to.
	/// </para>
	/// <para>
	/// <b>Pool sizing.</b> <see cref="maxConcurrency"/> is deliberately well under the database
	/// connection pool (<c>AppSettings.MaxPoolSize</c>, 100 by default). A work item holds at most
	/// one connection at a time, so this caps connection demand from the pool with headroom left
	/// for the synchronous shutdown flush and the health checks, which do not come through here.
	/// </para>
	/// </remarks>
	public class AsyncWorkerData : RuntimeDataContainer, IAsyncWorkerData
	{
		/// <summary>
		/// Maximum work items executing at once.
		/// </summary>
		/// <remarks>
		/// This is a concurrency limit, not a thread count. Items are asynchronous and spend most
		/// of their life awaiting, so this can comfortably exceed the number of cores; what it must
		/// not exceed is the database connection pool. See the pool-sizing note on the class.
		/// </remarks>
		private int maxConcurrency = 32;

		/// <summary>
		/// Maximum items accepted but not yet finished, across the whole pool.
		/// </summary>
		/// <remarks>
		/// The backpressure threshold. Reaching it makes <c>Enqueue</c> return false, which every
		/// caller already handles — by telling the client the server is busy, by queuing the work
		/// for retry, or by abandoning a cycle that the next one will repeat.
		/// </remarks>
		private int maxOutstandingItems = 16384;

		/// <summary>Caps how many items may run at once. See <see cref="maxConcurrency"/>.</summary>
		private SemaphoreSlim concurrencyGate;

		/// <summary>
		/// Per-entity-key ordering lanes. A key is present only while it has work outstanding.
		/// </summary>
		private ConcurrentDictionary<long, OrderedLane> lanes;

		/// <summary>Items accepted but not yet finished. Backs both backpressure and <see cref="PendingCount"/>.</summary>
		private int outstandingCount;

		/// <summary>Items currently executing, i.e. holding a concurrency slot.</summary>
		private int runningCount;

		/// <summary>Total count of completed work items, including ones that threw.</summary>
		private long completedCount;

		/// <summary>False once the pool is shutting down, so no further work is accepted.</summary>
		private volatile bool accepting;

		/// <summary>How long <see cref="OnDeinitialize"/> waits for in-flight work to finish.</summary>
		/// <remarks>
		/// Matches the deadline the worker-loop implementation used. Items still running when it
		/// expires are abandoned at process exit; the character system does its own bounded
		/// synchronous flush for the state that must not be lost.
		/// </remarks>
		private const int DrainTimeoutMilliseconds = 3000;

		/// <summary>Polling interval while waiting for the drain.</summary>
		private const int DrainPollMilliseconds = 25;

		/// <inheritdoc/>
		/// <remarks>
		/// Accepted but not yet started. Items that are running are reported by neither this nor
		/// <see cref="CompletedCount"/> — they are in flight.
		/// </remarks>
		public int PendingCount
		{
			get
			{
				int queued = Volatile.Read(ref outstandingCount) - Volatile.Read(ref runningCount);
				return queued > 0 ? queued : 0;
			}
		}

		/// <inheritdoc/>
		public long CompletedCount => Interlocked.Read(ref completedCount);

		/// <summary>
		/// Prepares the concurrency gate and ordering lanes.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			maxConcurrency = Mathf.Max(1, maxConcurrency);
			maxOutstandingItems = Mathf.Max(maxConcurrency, maxOutstandingItems);

			concurrencyGate = new SemaphoreSlim(maxConcurrency, maxConcurrency);
			lanes = new ConcurrentDictionary<long, OrderedLane>();
			Volatile.Write(ref outstandingCount, 0);
			Volatile.Write(ref runningCount, 0);
			accepting = true;

			_ = Log.Debug("AsyncWorkerData", $"Initialized (MaxConcurrency={maxConcurrency}, MaxOutstanding={maxOutstandingItems})");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <inheritdoc/>
		public bool Enqueue(Func<Task> work, string callerName = null)
		{
			if (!TryAccept(work, callerName, out AsyncWorkItem item))
			{
				return false;
			}

			DispatchUnordered(item);
			return true;
		}

		/// <inheritdoc/>
		public bool Enqueue(Func<Task> work, long entityKey, string callerName = null)
		{
			/* Key 0 means "no ordering requirement", not "the entity whose id is zero".
			 *
			 * The hashing implementation this replaces mapped it to a single channel, so every
			 * caller that passed a default id — and there are several — was quietly serialized
			 * onto one worker with every other such caller. Treating it as unordered is what the
			 * callers meant. */
			if (entityKey == 0)
			{
				return Enqueue(work, callerName);
			}

			if (!TryAccept(work, callerName, out AsyncWorkItem item))
			{
				return false;
			}

			DispatchOrdered(entityKey, item);
			return true;
		}

		/// <summary>
		/// Admits one item if the pool is running and has room, reserving its slot.
		/// </summary>
		/// <param name="work">Work being admitted.</param>
		/// <param name="callerName">Caller identifier, for diagnostics.</param>
		/// <param name="item">The admitted item.</param>
		/// <returns><c>false</c> when the pool is stopped, the work is null, or the queue is full.</returns>
		private bool TryAccept(Func<Task> work, string callerName, out AsyncWorkItem item)
		{
			item = default;

			if (work == null || !accepting || concurrencyGate == null || lanes == null)
			{
				return false;
			}

			// Reserved before dispatch so two concurrent enqueues cannot both pass the cap.
			if (Interlocked.Increment(ref outstandingCount) > maxOutstandingItems)
			{
				Interlocked.Decrement(ref outstandingCount);
				return false;
			}

			item = new AsyncWorkItem(work, callerName);
			return true;
		}

		/// <summary>
		/// Starts an item with no ordering requirement.
		/// </summary>
		/// <remarks>
		/// <c>Task.Run</c> rather than simply calling the async method. Awaiting the concurrency
		/// gate completes synchronously whenever a slot is free, so the work would otherwise run
		/// inline, on the calling thread, up to its first real await — and callers enqueue from the
		/// Unity main thread. Getting the work off that thread is the entire purpose of this class.
		/// </remarks>
		private void DispatchUnordered(AsyncWorkItem item)
		{
			_ = Task.Run(() => RunAsync(item));
		}

		/// <summary>
		/// Appends an item to its entity's ordering lane.
		/// </summary>
		/// <remarks>
		/// The lane is a chain of continuations: each item runs after the previous one for the same
		/// key has finished, so the FIFO guarantee the entity-keyed overload promises is preserved
		/// while different keys proceed independently.
		/// <para>
		/// <c>ContinueWith</c> on <see cref="TaskScheduler.Default"/> without
		/// <c>ExecuteSynchronously</c>, for the reason given on <see cref="DispatchUnordered"/>: an
		/// already-completed predecessor must not cause the work to run inline on the enqueuing
		/// thread — which here would additionally run it while holding the lane lock.
		/// </para>
		/// </remarks>
		/// <param name="entityKey">Ordering key. Never 0; see <see cref="Enqueue(Func{Task}, long, string)"/>.</param>
		/// <param name="item">Item to append.</param>
		private void DispatchOrdered(long entityKey, AsyncWorkItem item)
		{
			while (true)
			{
				OrderedLane lane = lanes.GetOrAdd(entityKey, _ => new OrderedLane());

				lock (lane.Gate)
				{
					/* The lane was retired by its last item between the lookup above and this lock,
					 * so it is no longer the lane for this key and anything appended to it would run
					 * unordered with respect to whatever comes next. Take a fresh one. */
					if (lane.Retired)
					{
						continue;
					}

					lane.Outstanding++;
					lane.Tail = lane.Tail.ContinueWith(
						_ => RunLaneItemAsync(entityKey, lane, item),
						CancellationToken.None,
						TaskContinuationOptions.DenyChildAttach,
						TaskScheduler.Default).Unwrap();
					return;
				}
			}
		}

		/// <summary>
		/// Runs one lane item, then retires the lane if it was the last.
		/// </summary>
		private async Task RunLaneItemAsync(long entityKey, OrderedLane lane, AsyncWorkItem item)
		{
			try
			{
				await RunAsync(item).ConfigureAwait(false);
			}
			finally
			{
				lock (lane.Gate)
				{
					if (--lane.Outstanding == 0)
					{
						// Retire under the lock so an appender either sees the flag and takes a
						// fresh lane, or gets in first and keeps this one alive.
						lane.Retired = true;
						lanes.TryRemove(entityKey, out _);
					}
				}
			}
		}

		/// <summary>
		/// Acquires a concurrency slot, runs the item, and accounts for it.
		/// </summary>
		/// <remarks>
		/// Never throws: a work item that fails must not fault the lane chain it belongs to, or
		/// every later item for that entity would be skipped.
		/// </remarks>
		private async Task RunAsync(AsyncWorkItem item)
		{
			try
			{
				/* Snapshotted, because teardown drops the field while items may still be in
				 * flight. Reading it twice would let an item acquire a slot and then fail to
				 * release it, or throw a NullReferenceException that reads as work failing. */
				SemaphoreSlim gate = concurrencyGate;
				if (gate == null)
				{
					// The pool is gone; this item is being abandoned with the rest.
					return;
				}

				await gate.WaitAsync().ConfigureAwait(false);

				Interlocked.Increment(ref runningCount);
				try
				{
					Task running = item.Work();
					if (running != null)
					{
						await running.ConfigureAwait(false);
					}
				}
				finally
				{
					Interlocked.Decrement(ref runningCount);
					gate.Release();
				}
			}
			catch (ObjectDisposedException)
			{
				// The gate went away under a concurrent teardown. Nothing to report.
			}
			catch (Exception ex)
			{
				string caller = item.CallerName ?? "unknown";
				await Log.Error("AsyncWorkerData", $"Async work from '{caller}' failed: {ex}").ConfigureAwait(false);
			}
			finally
			{
				Interlocked.Decrement(ref outstandingCount);
				Interlocked.Increment(ref completedCount);
			}
		}

		/// <summary>
		/// Stops accepting new work. Everything already accepted is left to run.
		/// </summary>
		/// <remarks>
		/// The channel-based implementation discarded the queue here, and this looked like a
		/// faithful port of that — but the contract was self-defeating. <c>Clear</c> has exactly one
		/// caller, <c>RuntimeDataContainerRegistry.DeinitializeAll</c>, which invokes it
		/// immediately before <see cref="OnDeinitialize"/>. So "discard everything pending" only
		/// ever ran during shutdown, and what is pending during shutdown is precisely the work that
		/// must not be lost: the saves and <em>session releases</em> that the behaviours enqueued as
		/// they tore down moments earlier.
		/// <para>
		/// A dropped release is not a local loss. The character stays Online in the database until
		/// its lease expires, so after a restart the player is refused by every scene server for the
		/// next two minutes. Combat-logout bodies are the common case: <c>FinalizeAllCombatLingers</c>
		/// hands each one's save and release to this pool and then removes its token from
		/// <c>SessionTokens</c>, so the synchronous shutdown flush no longer covers them — this pool
		/// is the only thing that can release them.
		/// </para>
		/// <para>
		/// Refusing new work still does the useful half: nothing further piles up while the process
		/// is going down, and <see cref="OnDeinitialize"/>'s bounded wait flushes the backlog.
		/// </para>
		/// </remarks>
		public override void Clear()
		{
			accepting = false;
		}

		/// <summary>
		/// Stops accepting work and waits, bounded, for what is still running.
		/// </summary>
		protected override void OnDeinitialize()
		{
			accepting = false;

			if (concurrencyGate == null)
			{
				lanes = null;
				return;
			}

			/* Bounded, and on the main thread, exactly as the worker-loop implementation's
			 * Task.WaitAll was. Process exit must not wait on an item that never completes.
			 *
			 * Clamped to what is left of the shutdown budget, because this wait is part of the same
			 * teardown the budget is sizing. The budget exists to keep the whole of teardown inside
			 * a supervisor's stop timeout — 8s against Docker's 10s default — and a drain that
			 * ignored it would add three seconds on top of a figure already chosen to fit. An
			 * exhausted budget yields 0 and this does not wait at all, which is the right answer:
			 * anything still running is about to be killed with the process either way.
			 *
			 * Note that items awaiting a main-thread dispatch cannot finish here — the main thread
			 * is this one, sleeping — so they will always cost the full remaining wait. That is why
			 * the cap matters rather than being theoretical. */
			int budget = UnitySyncOverAsync.ClampToShutdownBudget(DrainTimeoutMilliseconds);
			int waited = 0;
			while (budget > 0 && Volatile.Read(ref outstandingCount) > 0 && waited < budget)
			{
				Thread.Sleep(DrainPollMilliseconds);
				waited += DrainPollMilliseconds;
			}

			int remaining = Volatile.Read(ref outstandingCount);
			_ = Log.Debug("AsyncWorkerData", $"Deinitialized (Completed={CompletedCount}, Remaining={remaining})");

			/* Not disposed. A work item that outlived the drain is still holding, or about to
			 * acquire, this gate; disposing it would turn an abandoned item into an
			 * ObjectDisposedException storm on the way out. It is unreferenced after this and
			 * collected with everything else. */
			concurrencyGate = null;
			lanes?.Clear();
			lanes = null;
		}

		/// <summary>
		/// One entity's ordering chain. Present in <see cref="lanes"/> only while it has work.
		/// </summary>
		private sealed class OrderedLane
		{
			/// <summary>Guards <see cref="Tail"/>, <see cref="Outstanding"/> and <see cref="Retired"/>.</summary>
			public readonly object Gate = new object();

			/// <summary>The most recently appended item's task; the next item continues from it.</summary>
			public Task Tail = Task.CompletedTask;

			/// <summary>Items appended to this lane that have not finished.</summary>
			public int Outstanding;

			/// <summary>Set when the lane emptied and was removed, so a late appender takes a fresh one.</summary>
			public bool Retired;
		}

		/// <summary>
		/// Represents a single unit of async work.
		/// </summary>
		private readonly struct AsyncWorkItem
		{
			/// <summary>The work to run.</summary>
			public readonly Func<Task> Work;

			/// <summary>Caller identifier, for diagnostics.</summary>
			public readonly string CallerName;

			public AsyncWorkItem(Func<Task> work, string callerName)
			{
				Work = work;
				CallerName = callerName;
			}
		}
	}
}
