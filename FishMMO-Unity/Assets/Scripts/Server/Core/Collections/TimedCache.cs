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
					(DateTime.UtcNow - entry.StoredAtUtc) < ttl)
				{
					value = entry.Value;
					return true;
				}
				value = default;
				return false;
			}
		}

		/// <summary>
		/// Stores or overwrites a value with the current UTC timestamp.
		/// </summary>
		/// <param name="key">Cache key.</param>
		/// <param name="value">Value to cache.</param>
		public void Set(TKey key, TValue value)
		{
			DateTime now = DateTime.UtcNow;
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
		/// <param name="nowUtc">Current UTC timestamp.</param>
		/// <param name="ttl">Entries older than this duration are eligible for removal.</param>
		/// <param name="maxScan">Maximum queue nodes to inspect this sweep.</param>
		/// <param name="maxRemove">Maximum entries to remove this sweep.</param>
		/// <returns>Number of entries removed.</returns>
		public int SweepExpired(DateTime nowUtc, TimeSpan ttl, int maxScan, int maxRemove)
		{
			if (ttl <= TimeSpan.Zero || maxScan <= 0 || maxRemove <= 0)
			{
				return 0;
			}

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
						entry.StoredAtUtc != queued.StoredAtUtc)
					{
						queue.RemoveFirst();
						nodes.Remove(queued.Key);
						continue;
					}

					// Oldest non-stale entry is still fresh — stop.
					if (entries.ContainsKey(queued.Key) && (nowUtc - entry.StoredAtUtc) < ttl)
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
			public readonly DateTime StoredAtUtc;

			public QueueNode(TKey key, DateTime storedAtUtc)
			{
				Key = key;
				StoredAtUtc = storedAtUtc;
			}
		}

		private readonly struct CacheEntry
		{
			public readonly TValue Value;
			public readonly DateTime StoredAtUtc;

			public CacheEntry(TValue value, DateTime storedAtUtc)
			{
				Value = value;
				StoredAtUtc = storedAtUtc;
			}
		}
	}
}