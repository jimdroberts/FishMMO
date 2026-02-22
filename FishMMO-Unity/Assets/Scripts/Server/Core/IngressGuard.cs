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
		private readonly ConcurrentDictionary<long, byte> inFlightByKey = new ConcurrentDictionary<long, byte>();
		private DateTime nextSweepUtc;

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
			if (!inFlightByKey.TryAdd(guardKey, 0))
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
			inFlightByKey.TryRemove(guardKey, out _);
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

				if (kvp.Value <= staleBefore && nextAllowedUtcByKey.TryRemove(kvp.Key, out _))
				{
					inFlightByKey.TryRemove(kvp.Key, out _);
					removed++;
				}
			}
		}

		/// <summary>
		/// Clears all tracked entries. Safe to call during shutdown.
		/// </summary>
		public void Clear()
		{
			nextAllowedUtcByKey.Clear();
			inFlightByKey.Clear();
			nextSweepUtc = DateTime.UtcNow;
		}
	}
}
