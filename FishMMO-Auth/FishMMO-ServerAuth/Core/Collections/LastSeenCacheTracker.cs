using System;
using System.Collections.Generic;

namespace FishMMO.Auth.Core.Collections
{
	/// <summary>
	/// Queue/index tracker for key-value caches whose entries expire by last-seen timestamp.
	/// Optimized for high-frequency touch and bounded head-first TTL sweeps.
	/// </summary>
	/// <remarks>
	/// <b>Time is monotonic seconds</b> — a <c>MonotonicClock.NowSeconds</c> reading from the
	/// authenticator's clock. A TTL is a duration: on the wall clock a step backwards kept every
	/// entry fresh for the size of the step, and a step forward expired the whole cache at once. The
	/// head-first sweep also depends on last-seen stamps arriving in order, which a stepped wall
	/// clock breaks and a monotonic one does not. See <see cref="ExpiringKeyTracker{TKey}"/> for why
	/// there is no <see cref="DateTime"/> overload.
	/// </remarks>
	/// <typeparam name="TKey">Cache key type.</typeparam>
	/// <typeparam name="TValue">Cache value type.</typeparam>
	public sealed class LastSeenCacheTracker<TKey, TValue>
	{
		private readonly object gate = new object();
		private readonly Dictionary<TKey, CacheValue> values;
		private readonly LinkedList<QueueNode> queue = new LinkedList<QueueNode>();
		private readonly Dictionary<TKey, LinkedListNode<QueueNode>> nodes;

		/// <summary>
		/// Initializes a new tracker with an optional key comparer.
		/// </summary>
		public LastSeenCacheTracker(IEqualityComparer<TKey>? comparer = null)
		{
			values = comparer == null
				? new Dictionary<TKey, CacheValue>()
				: new Dictionary<TKey, CacheValue>(comparer);

			nodes = comparer == null
				? new Dictionary<TKey, LinkedListNode<QueueNode>>()
				: new Dictionary<TKey, LinkedListNode<QueueNode>>(comparer);
		}

		/// <summary>
		/// Clears all cached entries.
		/// </summary>
		public void Clear()
		{
			lock (gate)
			{
				values.Clear();
				queue.Clear();
				nodes.Clear();
			}
		}

		/// <summary>
		/// Tries to get a cached value and refreshes its last-seen timestamp.
		/// </summary>
		/// <param name="key">The cache key to look up.</param>
		/// <param name="nowSeconds">Current monotonic time in seconds; becomes the entry's last-seen time.</param>
		/// <param name="value">The cached value if found; otherwise, <c>default</c>.</param>
		/// <returns><c>true</c> if the key was found; otherwise, <c>false</c>.</returns>
		public bool TryGetAndTouch(TKey key, double nowSeconds, out TValue value)
		{
			lock (gate)
			{
				if (!values.TryGetValue(key, out CacheValue current))
				{
					value = default!;
					return false;
				}

				current = new CacheValue(current.Value, nowSeconds);
				values[key] = current;

				if (nodes.TryGetValue(key, out LinkedListNode<QueueNode>? oldNode))
				{
					queue.Remove(oldNode);
				}

				nodes[key] = queue.AddLast(new QueueNode(key, nowSeconds));
				value = current.Value;
				return true;
			}
		}

		/// <summary>
		/// Inserts or updates a cached value and sets its last-seen timestamp.
		/// </summary>
		/// <param name="key">The cache key.</param>
		/// <param name="value">The value to store.</param>
		/// <param name="nowSeconds">Current monotonic time in seconds, used as the last-seen time.</param>
		public void Upsert(TKey key, TValue value, double nowSeconds)
		{
			lock (gate)
			{
				values[key] = new CacheValue(value, nowSeconds);

				if (nodes.TryGetValue(key, out LinkedListNode<QueueNode>? oldNode))
				{
					queue.Remove(oldNode);
				}

				nodes[key] = queue.AddLast(new QueueNode(key, nowSeconds));
			}
		}

		/// <summary>
		/// Removes a cached key if present.
		/// </summary>
		/// <param name="key">The cache key to remove.</param>
		public void Remove(TKey key)
		{
			lock (gate)
			{
				values.Remove(key);
				if (nodes.TryGetValue(key, out LinkedListNode<QueueNode>? node))
				{
					nodes.Remove(key);
					queue.Remove(node);
				}
			}
		}

		/// <summary>
		/// Sweeps entries whose last-seen exceeds the provided TTL.
		/// </summary>
		/// <param name="nowSeconds">Current monotonic time in seconds.</param>
		/// <param name="ttl">Maximum age before an entry is swept.</param>
		/// <param name="maxScan">Maximum number of entries to inspect per sweep.</param>
		/// <param name="maxRemove">Maximum number of entries to remove per sweep.</param>
		/// <returns>Number of entries removed.</returns>
		public int SweepExpired(double nowSeconds, TimeSpan ttl, int maxScan, int maxRemove)
		{
			if (ttl <= TimeSpan.Zero || maxScan <= 0 || maxRemove <= 0)
			{
				return 0;
			}

			double ttlSeconds = ttl.TotalSeconds;
			lock (gate)
			{
				int scanned = 0;
				int removed = 0;

				while (scanned < maxScan && removed < maxRemove)
				{
					LinkedListNode<QueueNode>? head = queue.First;
					if (head == null)
					{
						break;
					}

					scanned++;
					QueueNode queued = head.Value;

					if (!values.TryGetValue(queued.Key, out CacheValue current))
					{
						queue.RemoveFirst();
						nodes.Remove(queued.Key);
						continue;
					}

					if (current.LastSeenSeconds != queued.LastSeenSeconds)
					{
						// Stale queue node after touch/upsert refresh.
						queue.RemoveFirst();
						continue;
					}

					if ((nowSeconds - current.LastSeenSeconds) < ttlSeconds)
					{
						break;
					}

					values.Remove(queued.Key);
					nodes.Remove(queued.Key);
					queue.RemoveFirst();
					removed++;
				}

				return removed;
			}
		}

		private readonly struct QueueNode
		{
			public readonly TKey Key;
			public readonly double LastSeenSeconds;

			public QueueNode(TKey key, double lastSeenSeconds)
			{
				Key = key;
				LastSeenSeconds = lastSeenSeconds;
			}
		}

		private readonly struct CacheValue
		{
			public readonly TValue Value;
			public readonly double LastSeenSeconds;

			public CacheValue(TValue value, double lastSeenSeconds)
			{
				Value = value;
				LastSeenSeconds = lastSeenSeconds;
			}
		}
	}
}
