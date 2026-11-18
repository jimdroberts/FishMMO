using System;
using System.Collections.Generic;

namespace FishMMO.Server.Core
{
	/// <summary>
	/// Reusable per-connection, per-operation debounce and in-flight guard.
	/// Eliminates duplicated ingress-guard boilerplate across server systems.
	/// Thread-safe: every operation takes one short lock, so <see cref="End"/> may be called from
	/// the worker that finished the operation.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Clock.</b> Debounce windows and marker ages are local durations, so they are timed on
	/// <see cref="MonotonicClock"/>. On the wall clock a step backwards held every debounce window
	/// closed for the length of the step — every guarded request from every player refused — and
	/// a step forwards lifted them all at once.
	/// </para>
	/// <para>
	/// <b>Expiry.</b> Debounce entries are kept in the order they were last set, and so are
	/// in-flight markers, so both sweeps read from the oldest end and stop at the first entry that
	/// is not due. The previous sweep enumerated the whole dictionary from its head on every pass
	/// and removed at most a capped number, and the in-flight backstop enumerated every marker on
	/// every pass.
	/// </para>
	/// <para>
	/// <b>Capacity.</b> A debounce entry whose window has closed decides nothing — a request
	/// against it is allowed exactly as if it were absent — and is only kept so a busy key is not
	/// reallocated on every request. So when the tracker is full, closed entries are reclaimed on
	/// the spot, and only a key that would need a new entry while <see cref="MaxTrackerEntries"/>
	/// windows are all genuinely open is refused. It used to refuse every request from everyone
	/// once the count reached the cap, which ordinary traffic reaches on a busy scene well before
	/// the periodic sweep catches up.
	/// </para>
	/// </remarks>
	public sealed class IngressGuard
	{
		/// <summary>
		/// Debounce entries beyond which a request that needs a NEW entry is refused — and only
		/// once every closed entry has been reclaimed. A memory bound against a flood, not a
		/// traffic limit.
		/// </summary>
		private const int MaxTrackerEntries = 10000;

		/// <summary>
		/// Age at which an in-flight marker is presumed leaked rather than merely slow.
		/// </summary>
		/// <remarks>
		/// Every caller releases in a <c>finally</c>, so this should never fire; it exists so a
		/// bug on one path cannot lock an operation out for the life of the process. Far beyond
		/// any legitimate request, so a slow one is never mistaken for a leaked one.
		/// </remarks>
		private const double InFlightStaleAfterSeconds = 5 * 60;

		/// <summary>One key's debounce state.</summary>
		private sealed class DebounceEntry
		{
			public readonly long Key;
			/// <summary>Monotonic seconds before which a new request for this key is refused.</summary>
			public double NextAllowedSeconds;

			public DebounceEntry(long key)
			{
				Key = key;
			}
		}

		/// <summary>One operation in progress.</summary>
		private sealed class InFlightMarker
		{
			public readonly long Key;
			/// <summary>Monotonic seconds at which it was acquired.</summary>
			public readonly double SinceSeconds;

			public InFlightMarker(long key, double sinceSeconds)
			{
				Key = key;
				SinceSeconds = sinceSeconds;
			}
		}

		private readonly object gate = new object();
		private readonly Func<double> clock;

		/// <summary>Debounce entries by key. Guarded by <see cref="gate"/>.</summary>
		private readonly Dictionary<long, LinkedListNode<DebounceEntry>> debounceByKey = new Dictionary<long, LinkedListNode<DebounceEntry>>();

		/// <summary>
		/// The same entries in the order they were last set, oldest first. Guarded by <see cref="gate"/>.
		/// </summary>
		/// <remarks>
		/// An entry is due for removal once its window closed more than the caller's TTL ago. Every
		/// window is its set time plus that operation's debounce, so across operations with
		/// different debounces this order is not exactly the due order: an entry with a long
		/// debounce at the head can hold back shorter ones behind it by at most the difference.
		/// That only delays reclaiming memory — a closed window decides nothing either way — and
		/// the debounces in use are all seconds or less.
		/// </remarks>
		private readonly LinkedList<DebounceEntry> debounceBySetTime = new LinkedList<DebounceEntry>();

		/// <summary>
		/// Keys with an operation currently in progress. Guarded by <see cref="gate"/>.
		/// </summary>
		/// <remarks>
		/// The acquisition time exists only so a marker whose <see cref="End"/> was somehow never
		/// reached can eventually be reclaimed. It is deliberately not the debounce timestamp:
		/// the sweep used to drop the in-flight marker together with the debounce entry, which
		/// meant any operation still running once its debounce entry aged out — a database stall
		/// is enough — silently lost its lock, letting a duplicate start while the first was
		/// still going, after which the first one's <see cref="End"/> released the second's
		/// marker instead of its own.
		/// </remarks>
		private readonly Dictionary<long, LinkedListNode<InFlightMarker>> inFlightByKey = new Dictionary<long, LinkedListNode<InFlightMarker>>();

		/// <summary>In-flight markers in acquisition order, oldest first. Guarded by <see cref="gate"/>.</summary>
		private readonly LinkedList<InFlightMarker> inFlightByAge = new LinkedList<InFlightMarker>();

		/// <summary>Monotonic seconds before which <see cref="Sweep"/> does nothing. Guarded by <see cref="gate"/>.</summary>
		private double nextSweepSeconds;

		/// <summary>
		/// Initializes a new instance of the <see cref="IngressGuard"/> class.
		/// </summary>
		public IngressGuard() : this(() => MonotonicClock.NowSeconds)
		{
		}

		/// <summary>
		/// Initializes a guard on the given clock. Tests drive time by hand through this.
		/// </summary>
		/// <param name="nowSeconds">Monotonic clock in seconds.</param>
		internal IngressGuard(Func<double> nowSeconds)
		{
			clock = nowSeconds ?? throw new ArgumentNullException(nameof(nowSeconds));
			nextSweepSeconds = clock();
		}

		/// <summary>
		/// Attempts to acquire the ingress guard for the given connection and operation.
		/// Returns false if debounce is active, an operation is already in-flight, or — only
		/// under a flood — the tracker is full of open debounce windows and this key would need
		/// a new one. Debounce timestamps are only updated AFTER a successful in-flight
		/// acquisition, preventing unintended sliding-window extension on rejected requests.
		/// </summary>
		/// <param name="connectionId">Network connection identifier.</param>
		/// <param name="operation">Operation code (cast your enum to byte).</param>
		/// <param name="debounceMilliseconds">Minimum milliseconds between requests for this key.</param>
		/// <param name="guardKey">Output guard key to pass to End() in a finally block.</param>
		/// <param name="globalRateMilliseconds">Optional global per-connection rate (0 = disabled). Uses operation 0 key.</param>
		/// <returns>True if the guard was acquired; false if the request should be rejected.</returns>
		public bool TryBegin(int connectionId, byte operation, int debounceMilliseconds, out long guardKey, int globalRateMilliseconds = 0)
		{
			guardKey = ((long)connectionId << 16) | operation;
			long globalKey = (long)connectionId << 16;

			lock (gate)
			{
				double now = clock();

				// Optional global per-connection rate limit (operation 0 key)
				LinkedListNode<DebounceEntry> globalNode = null;
				if (globalRateMilliseconds > 0 &&
					debounceByKey.TryGetValue(globalKey, out globalNode) &&
					now < globalNode.Value.NextAllowedSeconds)
				{
					return false;
				}

				// Per-operation debounce check
				if (debounceByKey.TryGetValue(guardKey, out LinkedListNode<DebounceEntry> node) &&
					now < node.Value.NextAllowedSeconds)
				{
					return false;
				}

				// In-flight lock — checked before anything is recorded.
				if (inFlightByKey.ContainsKey(guardKey))
				{
					return false;
				}

				// Capacity applies only to entries this request would add.
				int newEntries = node == null ? 1 : 0;
				if (globalRateMilliseconds > 0 && globalNode == null && globalKey != guardKey)
				{
					newEntries++;
				}
				if (newEntries > 0 && debounceByKey.Count + newEntries > MaxTrackerEntries)
				{
					ReclaimClosedLocked(now);
					if (debounceByKey.Count + newEntries > MaxTrackerEntries)
					{
						guardKey = 0;
						return false;
					}
				}

				inFlightByKey[guardKey] = inFlightByAge.AddLast(new InFlightMarker(guardKey, now));

				SetDebounceLocked(guardKey, node, now + debounceMilliseconds / 1000.0);
				if (globalRateMilliseconds > 0)
				{
					// Looked up again: when the operation code is 0 the global key IS the guard
					// key, and the global rate then overwrites the per-operation window, as it
					// always has.
					debounceByKey.TryGetValue(globalKey, out globalNode);
					SetDebounceLocked(globalKey, globalNode, now + globalRateMilliseconds / 1000.0);
				}

				return true;
			}
		}

		/// <summary>
		/// Releases the in-flight lock for the given guard key. Call in a finally block.
		/// </summary>
		public void End(long guardKey)
		{
			lock (gate)
			{
				if (inFlightByKey.TryGetValue(guardKey, out LinkedListNode<InFlightMarker> marker))
				{
					inFlightByKey.Remove(guardKey);
					inFlightByAge.Remove(marker);
				}
			}
		}

		/// <summary>
		/// Bounded sweep of stale ingress entries. Call once per frame or at a configured interval.
		/// </summary>
		/// <remarks>
		/// Reads from the oldest end and stops at the first entry not yet due, so a pass costs one
		/// comparison plus one per entry it removes. An entry whose operation is still running
		/// keeps its debounce entry — dropping it would let the next request past the debounce
		/// check while the first is unfinished — and goes to the back to be looked at again.
		/// </remarks>
		/// <param name="sweepIntervalSeconds">Minimum seconds between sweep passes.</param>
		/// <param name="entryTtlSeconds">Entries whose window closed longer ago than this are stale.</param>
		/// <param name="maxRemovals">Maximum entries to remove per sweep pass; the rest stay at the head for the next.</param>
		public void Sweep(float sweepIntervalSeconds, float entryTtlSeconds, int maxRemovals)
		{
			lock (gate)
			{
				double now = clock();
				if (now < nextSweepSeconds)
				{
					return;
				}
				nextSweepSeconds = now + sweepIntervalSeconds;

				double staleBefore = now - entryTtlSeconds;
				int removed = 0;
				// Bounds the pass if every due entry at the head is still in flight and rotates.
				int visitsLeft = debounceByKey.Count;
				while (removed < maxRemovals && visitsLeft-- > 0)
				{
					LinkedListNode<DebounceEntry> head = debounceBySetTime.First;
					if (head == null || head.Value.NextAllowedSeconds > staleBefore)
					{
						break;
					}

					debounceBySetTime.RemoveFirst();
					if (inFlightByKey.ContainsKey(head.Value.Key))
					{
						// Still running: keep the entry, and look again once End() has run.
						debounceBySetTime.AddLast(head);
						continue;
					}

					debounceByKey.Remove(head.Value.Key);
					removed++;
				}

				// Reclaim markers that were never released. See InFlightStaleAfterSeconds — this
				// is a backstop against a missing End(), not a timeout on legitimate work.
				while (inFlightByAge.First != null && now - inFlightByAge.First.Value.SinceSeconds >= InFlightStaleAfterSeconds)
				{
					InFlightMarker leaked = inFlightByAge.First.Value;
					inFlightByAge.RemoveFirst();
					inFlightByKey.Remove(leaked.Key);
				}
			}
		}

		/// <summary>
		/// Clears all tracked entries. Safe to call during shutdown.
		/// </summary>
		public void Clear()
		{
			lock (gate)
			{
				debounceByKey.Clear();
				debounceBySetTime.Clear();
				inFlightByKey.Clear();
				inFlightByAge.Clear();
				nextSweepSeconds = clock();
			}
		}

		/// <summary>Debounce entries currently held, closed or not. For tests and diagnostics.</summary>
		internal int TrackedEntryCount
		{
			get
			{
				lock (gate)
				{
					return debounceByKey.Count;
				}
			}
		}

		/// <summary>
		/// Sets <paramref name="key"/>'s window to close at <paramref name="nextAllowedSeconds"/>
		/// and moves it to the newest end. Caller holds <see cref="gate"/>.
		/// </summary>
		private void SetDebounceLocked(long key, LinkedListNode<DebounceEntry> node, double nextAllowedSeconds)
		{
			if (node == null)
			{
				node = debounceBySetTime.AddLast(new DebounceEntry(key));
				debounceByKey[key] = node;
			}
			else
			{
				debounceBySetTime.Remove(node);
				debounceBySetTime.AddLast(node);
			}
			node.Value.NextAllowedSeconds = nextAllowedSeconds;
		}

		/// <summary>
		/// Removes every debounce entry whose window has closed and whose operation is not
		/// running. Decision-neutral: a closed window allows a request exactly as a missing one
		/// does. A full walk, run only when the tracker is at capacity. Caller holds <see cref="gate"/>.
		/// </summary>
		private void ReclaimClosedLocked(double now)
		{
			LinkedListNode<DebounceEntry> node = debounceBySetTime.First;
			while (node != null)
			{
				LinkedListNode<DebounceEntry> next = node.Next;
				if (node.Value.NextAllowedSeconds <= now && !inFlightByKey.ContainsKey(node.Value.Key))
				{
					debounceBySetTime.Remove(node);
					debounceByKey.Remove(node.Value.Key);
				}
				node = next;
			}
		}
	}
}
