using System;
using System.Collections.Generic;

namespace FishMMO.Server.Core.Collections
{
	/// <summary>
	/// A thread-safe, write-through TTL cache where entries expire after a fixed duration
	/// from when they were stored. Reads do NOT extend the lifetime (unlike
	/// <see cref="LastSeenCacheTracker{TKey,TValue}"/>).
	/// <para>
	/// Supports bounded head-first <see cref="SweepExpired"/> for memory management.
	/// </para>
	/// <para>
	/// Ages are measured on <see cref="MonotonicClock"/>, not the host's wall clock. A TTL is a
	/// duration, and on <c>DateTime.UtcNow</c> a clock stepped back an hour kept every entry
	/// "fresh" for that hour: the world server's available-scene cache served the same instance
	/// list, populations and all, long after the instances had filled or gone.
	/// </para>
	/// </summary>
	/// <typeparam name="TKey">Cache key type.</typeparam>
	/// <typeparam name="TValue">Cache value type.</typeparam>
	public sealed class TimedCache<TKey, TValue>
	{
		private readonly object gate = new object();
		private readonly Dictionary<TKey, CacheEntry> entries;
		private readonly LinkedList<QueueNode> queue = new LinkedList<QueueNode>();
		private readonly Dictionary<TKey, LinkedListNode<QueueNode>> nodes;

		/// <summary>
		/// Initializes a new cache with an optional key comparer.
		/// </summary>
		public TimedCache(IEqualityComparer<TKey> comparer = null)
		{
			entries = comparer == null
				? new Dictionary<TKey, CacheEntry>()
				: new Dictionary<TKey, CacheEntry>(comparer);

			nodes = comparer == null
				? new Dictionary<TKey, LinkedListNode<QueueNode>>()
				: new Dictionary<TKey, LinkedListNode<QueueNode>>(comparer);
		}

		/// <summary>
		/// Tries to retrieve a cached value that was stored within the specified TTL.
		/// Does NOT refresh the stored timestamp — entries always expire relative to
		/// the time they were written via <see cref="Set"/>.
		/// </summary>
		/// <param name="key">Cache key.</param>
		/// <param name="ttl">Maximum age for the entry to be considered valid.</param>
		/// <param name="value">The cached value if found and still valid.</param>
		/// <returns><c>true</c> if a valid (non-expired) entry was found.</returns>
		public bool TryGet(TKey key, TimeSpan ttl, out TValue value)
		{
			lock (gate)
			{
				if (entries.TryGetValue(key, out CacheEntry entry) &&
					MonotonicClock.NowSeconds - entry.StoredAt < ttl.TotalSeconds)
				{
					value = entry.Value;
					return true;
				}
				value = default;
				return false;
			}
		}

		/// <summary>
		/// Stores or overwrites a value, stamped with the current <see cref="MonotonicClock"/> reading.
		/// </summary>
		/// <param name="key">Cache key.</param>
		/// <param name="value">Value to cache.</param>
		public void Set(TKey key, TValue value)
		{
			double now = MonotonicClock.NowSeconds;
			lock (gate)
			{
				entries[key] = new CacheEntry(value, now);

				if (nodes.TryGetValue(key, out LinkedListNode<QueueNode> oldNode))
				{
					queue.Remove(oldNode);
				}
				nodes[key] = queue.AddLast(new QueueNode(key, now));
			}
		}

		/// <summary>
		/// Removes a single cached entry.
		/// </summary>
		/// <param name="key">Cache key to invalidate.</param>
		public void Invalidate(TKey key)
		{
			lock (gate)
			{
				entries.Remove(key);
				if (nodes.TryGetValue(key, out LinkedListNode<QueueNode> node))
				{
					nodes.Remove(key);
					queue.Remove(node);
				}
			}
		}

		/// <summary>
		/// Clears all cached entries.
		/// </summary>
		public void Clear()
		{
			lock (gate)
			{
				entries.Clear();
				queue.Clear();
				nodes.Clear();
			}
		}

		/// <summary>
		/// Sweeps entries older than the specified TTL using bounded head-first traversal.
		/// </summary>
		/// <param name="ttl">Entries older than this duration are eligible for removal.</param>
		/// <param name="maxScan">Maximum queue nodes to inspect this sweep.</param>
		/// <param name="maxRemove">Maximum entries to remove this sweep.</param>
		/// <returns>Number of entries removed.</returns>
		/// <remarks>
		/// Reads the clock itself rather than taking a "now": the entries were stamped by
		/// <see cref="Set"/> on <see cref="MonotonicClock"/>, and a caller-supplied instant from any
		/// other clock would compare two unrelated numbers.
		/// </remarks>
		public int SweepExpired(TimeSpan ttl, int maxScan, int maxRemove)
		{
			if (ttl <= TimeSpan.Zero || maxScan <= 0 || maxRemove <= 0)
			{
				return 0;
			}

			double now = MonotonicClock.NowSeconds;
			double ttlSeconds = ttl.TotalSeconds;

			lock (gate)
			{
				int scanned = 0;
				int removed = 0;

				while (scanned < maxScan && removed < maxRemove)
				{
					LinkedListNode<QueueNode> head = queue.First;
					if (head == null)
					{
						break;
					}

					scanned++;
					QueueNode queued = head.Value;

					// If the entry was re-Set with a newer timestamp, the queue node is stale — discard it.
					if (entries.TryGetValue(queued.Key, out CacheEntry entry) &&
						entry.StoredAt != queued.StoredAt)
					{
						queue.RemoveFirst();
						nodes.Remove(queued.Key);
						continue;
					}

					// Oldest non-stale entry is still fresh — stop.
					if (entries.ContainsKey(queued.Key) && now - entry.StoredAt < ttlSeconds)
					{
						break;
					}

					entries.Remove(queued.Key);
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
			/// <summary>When the entry was stored, on <see cref="MonotonicClock"/>.</summary>
			public readonly double StoredAt;

			public QueueNode(TKey key, double storedAt)
			{
				Key = key;
				StoredAt = storedAt;
			}
		}

		private readonly struct CacheEntry
		{
			public readonly TValue Value;
			/// <summary>When the entry was stored, on <see cref="MonotonicClock"/>.</summary>
			public readonly double StoredAt;

			public CacheEntry(TValue value, double storedAt)
			{
				Value = value;
				StoredAt = storedAt;
			}
		}
	}
}