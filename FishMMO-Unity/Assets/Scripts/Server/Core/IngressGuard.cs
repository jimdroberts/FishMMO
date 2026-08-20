using System;
using System.Collections.Concurrent;

namespace FishMMO.Server.Core
{
	/// <summary>
	/// Reusable per-connection, per-operation debounce and in-flight guard.
	/// Eliminates duplicated ingress-guard boilerplate across server systems.
	/// Thread-safe: backed by ConcurrentDictionary for cross-thread access.
	/// </summary>
	public sealed class IngressGuard
	{
		private const int MaxTrackerEntries = 10000;

		private readonly ConcurrentDictionary<long, DateTime> nextAllowedUtcByKey = new ConcurrentDictionary<long, DateTime>();

		/// <summary>
		/// Keys with an operation currently in progress, mapped to when it was acquired.
		/// </summary>
		/// <remarks>
		/// The timestamp exists only so a marker whose <see cref="End"/> was somehow never
		/// reached can eventually be reclaimed. It is deliberately not the debounce timestamp:
		/// the sweep used to drop the in-flight marker together with the debounce entry, which
		/// meant any operation still running once its debounce entry aged out — a database stall
		/// is enough — silently lost its lock, letting a duplicate start while the first was
		/// still going, after which the first one's <see cref="End"/> released the second's
		/// marker instead of its own.
		/// </remarks>
		private readonly ConcurrentDictionary<long, DateTime> inFlightSinceUtcByKey = new ConcurrentDictionary<long, DateTime>();

		/// <summary>
		/// Age at which an in-flight marker is presumed leaked rather than merely slow.
		/// </summary>
		/// <remarks>
		/// Every caller releases in a <c>finally</c>, so this should never fire; it exists so a
		/// bug on one path cannot lock an operation out for the life of the process. Far beyond
		/// any legitimate request, so a slow one is never mistaken for a leaked one.
		/// </remarks>
		private static readonly TimeSpan InFlightStaleAfter = TimeSpan.FromMinutes(5);

		private DateTime nextSweepUtc;

		/// <summary>
		/// Initializes a new instance of the <see cref="IngressGuard"/> class.
		/// </summary>
		public IngressGuard()
		{
			nextSweepUtc = DateTime.UtcNow;
		}

		/// <summary>
		/// Attempts to acquire the ingress guard for the given connection and operation.
		/// Returns false if debounce is active, the tracker is saturated, or an operation is already in-flight.
		/// Debounce timestamp is only updated AFTER a successful in-flight acquisition,
		/// preventing unintended sliding-window extension on rejected requests.
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

			if (nextAllowedUtcByKey.Count >= MaxTrackerEntries)
			{
				guardKey = 0;
				return false;
			}

			DateTime nowUtc = DateTime.UtcNow;

			// Optional global per-connection rate limit (operation 0 key)
			if (globalRateMilliseconds > 0)
			{
				long globalKey = (long)connectionId << 16;
				if (nextAllowedUtcByKey.TryGetValue(globalKey, out DateTime globalNext) && nowUtc < globalNext)
				{
					return false;
				}
			}

			// Per-operation debounce check
			if (nextAllowedUtcByKey.TryGetValue(guardKey, out DateTime nextAllowed) && nowUtc < nextAllowed)
			{
				return false;
			}

			// In-flight lock — only update timestamps after successful acquisition
			if (!inFlightSinceUtcByKey.TryAdd(guardKey, nowUtc))
			{
				return false;
			}

			nextAllowedUtcByKey[guardKey] = nowUtc.AddMilliseconds(debounceMilliseconds);

			if (globalRateMilliseconds > 0)
			{
				long globalKey = (long)connectionId << 16;
				nextAllowedUtcByKey[globalKey] = nowUtc.AddMilliseconds(globalRateMilliseconds);
			}

			return true;
		}

		/// <summary>
		/// Releases the in-flight lock for the given guard key. Call in a finally block.
		/// </summary>
		public void End(long guardKey)
		{
			inFlightSinceUtcByKey.TryRemove(guardKey, out _);
		}

		/// <summary>
		/// Bounded sweep of stale ingress entries. Call once per frame or at a configured interval.
		/// </summary>
		/// <param name="sweepIntervalSeconds">Minimum seconds between sweep passes.</param>
		/// <param name="entryTtlSeconds">Entries older than this are considered stale.</param>
		/// <param name="maxRemovals">Maximum entries to remove per sweep pass.</param>
		public void Sweep(float sweepIntervalSeconds, float entryTtlSeconds, int maxRemovals)
		{
			DateTime nowUtc = DateTime.UtcNow;
			if (nowUtc < nextSweepUtc)
			{
				return;
			}

			nextSweepUtc = nowUtc.AddSeconds(sweepIntervalSeconds);
			DateTime staleBefore = nowUtc.AddSeconds(-entryTtlSeconds);
			int removed = 0;

			foreach (var kvp in nextAllowedUtcByKey)
			{
				if (removed >= maxRemovals)
				{
					break;
				}

				if (kvp.Value > staleBefore)
				{
					continue;
				}

				// An operation that is still running keeps its debounce entry. Dropping it would
				// let the next request past the debounce check while the first is unfinished,
				// and the entry is reclaimed on the following pass once End() has run anyway.
				if (inFlightSinceUtcByKey.ContainsKey(kvp.Key))
				{
					continue;
				}

				if (nextAllowedUtcByKey.TryRemove(kvp.Key, out _))
				{
					removed++;
				}
			}

			// Reclaim markers that were never released. See InFlightStaleAfter — this is a
			// backstop against a missing End(), not a timeout on legitimate work.
			DateTime inFlightStaleBefore = nowUtc - InFlightStaleAfter;
			foreach (var kvp in inFlightSinceUtcByKey)
			{
				if (kvp.Value <= inFlightStaleBefore)
				{
					inFlightSinceUtcByKey.TryRemove(kvp.Key, out _);
				}
			}
		}

		/// <summary>
		/// Clears all tracked entries. Safe to call during shutdown.
		/// </summary>
		public void Clear()
		{
			nextAllowedUtcByKey.Clear();
			inFlightSinceUtcByKey.Clear();
			nextSweepUtc = DateTime.UtcNow;
		}
	}
}