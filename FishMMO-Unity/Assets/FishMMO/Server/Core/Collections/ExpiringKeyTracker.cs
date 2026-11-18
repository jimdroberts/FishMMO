using System;
using System.Collections.Generic;

namespace FishMMO.Server.Core.Collections
{
	/// <summary>
	/// Queue/index tracker for expiring keyed entries.
	/// Uses head-first sweeps to avoid full dictionary enumeration under heavy load.
	/// </summary>
	/// <typeparam name="TKey">Tracker key type.</typeparam>
	public sealed class ExpiringKeyTracker<TKey>
	{
		private readonly object gate = new object();
		private readonly Dictionary<TKey, DateTime> nextAllowedUtc;
		private readonly LinkedList<ExpiryQueueNode> expiryQueue = new LinkedList<ExpiryQueueNode>();
		private readonly Dictionary<TKey, LinkedListNode<ExpiryQueueNode>> queueNodes;

		/// <summary>
		/// Initializes a new tracker with an optional key comparer.
		/// </summary>
		public ExpiringKeyTracker(IEqualityComparer<TKey> comparer = null)
		{
			nextAllowedUtc = comparer == null
				? new Dictionary<TKey, DateTime>()
				: new Dictionary<TKey, DateTime>(comparer);

			queueNodes = comparer == null
				? new Dictionary<TKey, LinkedListNode<ExpiryQueueNode>>()
				: new Dictionary<TKey, LinkedListNode<ExpiryQueueNode>>(comparer);
		}

		/// <summary>
		/// Current number of tracked keys.
		/// </summary>
		public int Count
		{
			get
			{
				lock (gate)
				{
					return nextAllowedUtc.Count;
				}
			}
		}

		/// <summary>
		/// Clears all tracked entries.
		/// </summary>
		public void Clear()
		{
			lock (gate)
			{
				nextAllowedUtc.Clear();
				expiryQueue.Clear();
				queueNodes.Clear();
			}
		}

		/// <summary>
		/// Attempts to begin a debounce/rate-limit window for a key.
		/// </summary>
		/// <param name="key">Tracker key.</param>
		/// <param name="nowUtc">Current UTC timestamp.</param>
		/// <param name="duration">Window duration.</param>
		/// <returns><c>true</c> if allowed now; otherwise <c>false</c>.</returns>
		public bool TryBegin(TKey key, DateTime nowUtc, TimeSpan duration)
		{
			if (duration <= TimeSpan.Zero)
			{
				return true;
			}

			lock (gate)
			{
				if (nextAllowedUtc.TryGetValue(key, out DateTime nextAllowed) && nextAllowed > nowUtc)
				{
					return false;
				}

				DateTime expiresUtc = nowUtc.Add(duration);
				nextAllowedUtc[key] = expiresUtc;

				if (queueNodes.TryGetValue(key, out LinkedListNode<ExpiryQueueNode> existingNode))
				{
					expiryQueue.Remove(existingNode);
				}

				queueNodes[key] = expiryQueue.AddLast(new ExpiryQueueNode(key, expiresUtc));
				return true;
			}
		}

		/// <summary>
		/// Attempts to begin a debounce/rate-limit window for a key, timed on
		/// <see cref="MonotonicClock"/>.
		/// </summary>
		/// <param name="key">Tracker key.</param>
		/// <param name="nowSeconds">Current <see cref="MonotonicClock.NowSeconds"/> reading.</param>
		/// <param name="duration">Window duration.</param>
		/// <returns><c>true</c> if allowed now; otherwise <c>false</c>.</returns>
		/// <remarks>
		/// A debounce is a duration, and on <c>DateTime.UtcNow</c> a host clock stepped back an hour
		/// held every recently seen key inside its window for that hour: an account refused for three
		/// seconds was refused for sixty minutes. The tracker only ever compares the instants it is
		/// given with one another, so the monotonic reading is carried in the same field, offset from
		/// an arbitrary origin.
		/// <para>
		/// One tracker, one clock. Driving an instance through both this overload and the
		/// <see cref="DateTime"/> one compares unrelated numbers; every tracker in the codebase uses
		/// exactly one of them.
		/// </para>
		/// </remarks>
		public bool TryBegin(TKey key, double nowSeconds, TimeSpan duration)
		{
			return TryBegin(key, FromMonotonic(nowSeconds), duration);
		}

		/// <summary>
		/// Sweeps expired keys, for a tracker timed on <see cref="MonotonicClock"/>. See
		/// <see cref="TryBegin(TKey, double, TimeSpan)"/>.
		/// </summary>
		/// <param name="nowSeconds">Current <see cref="MonotonicClock.NowSeconds"/> reading.</param>
		/// <param name="maxScan">Maximum queue nodes to inspect this sweep.</param>
		/// <param name="maxRemove">Maximum keys to remove this sweep.</param>
		/// <returns>Number of entries removed.</returns>
		public int SweepExpired(double nowSeconds, int maxScan, int maxRemove)
		{
			return SweepExpired(FromMonotonic(nowSeconds), maxScan, maxRemove);
		}

		/// <summary>
		/// Carries a <see cref="MonotonicClock"/> reading in a <see cref="DateTime"/> so the two
		/// overloads share one implementation. See <see cref="MonotonicInstant"/>.
		/// </summary>
		private static DateTime FromMonotonic(double seconds) => MonotonicInstant.From(seconds);

		/// <summary>
		/// Removes a key from the tracker if present. Useful for cancelling a debounce
		/// window when the operation it was guarding has already failed and the caller
		/// wants to allow an immediate retry rather than make the user wait out the window.
		/// </summary>
		/// <param name="key">Tracker key to remove.</param>
		/// <returns><c>true</c> if the key existed and was removed; otherwise <c>false</c>.</returns>
		public bool Remove(TKey key)
		{
			lock (gate)
			{
				if (!nextAllowedUtc.Remove(key))
				{
					return false;
				}

				if (queueNodes.TryGetValue(key, out LinkedListNode<ExpiryQueueNode> node))
				{
					expiryQueue.Remove(node);
					queueNodes.Remove(key);
				}
				return true;
			}
		}

		/// <summary>
		/// Sweeps expired keys with bounded scan and removal limits.
		/// </summary>
		/// <param name="nowUtc">Current UTC timestamp.</param>
		/// <param name="maxScan">Maximum queue nodes to inspect this sweep.</param>
		/// <param name="maxRemove">Maximum keys to remove this sweep.</param>
		/// <returns>Number of entries removed.</returns>
		public int SweepExpired(DateTime nowUtc, int maxScan, int maxRemove)
		{
			if (maxScan <= 0 || maxRemove <= 0)
			{
				return 0;
			}

			lock (gate)
			{
				int scanned = 0;
				int removed = 0;

				while (scanned < maxScan && removed < maxRemove)
				{
					LinkedListNode<ExpiryQueueNode> head = expiryQueue.First;
					if (head == null)
					{
						break;
					}

					scanned++;
					ExpiryQueueNode queued = head.Value;

					if (!nextAllowedUtc.TryGetValue(queued.Key, out DateTime currentExpiry))
					{
						expiryQueue.RemoveFirst();
						queueNodes.Remove(queued.Key);
						continue;
					}

					if (currentExpiry != queued.ExpiresUtc)
					{
						// Stale queued node after refresh.
						expiryQueue.RemoveFirst();
						continue;
					}

					if (currentExpiry > nowUtc)
					{
						break;
					}

					nextAllowedUtc.Remove(queued.Key);
					queueNodes.Remove(queued.Key);
					expiryQueue.RemoveFirst();
					removed++;
				}

				return removed;
			}
		}

		private readonly struct ExpiryQueueNode
		{
			public readonly TKey Key;
			public readonly DateTime ExpiresUtc;

			public ExpiryQueueNode(TKey key, DateTime expiresUtc)
			{
				Key = key;
				ExpiresUtc = expiresUtc;
			}
		}
	}
}