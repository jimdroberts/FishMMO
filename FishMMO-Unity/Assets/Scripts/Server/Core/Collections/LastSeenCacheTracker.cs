using System;
using System.Collections.Generic;

namespace FishMMO.Server.Core.Collections
{
	/// <summary>
	/// Queue/index tracker for key-value caches whose entries expire by last-seen timestamp.
	/// Optimized for high-frequency touch and bounded head-first TTL sweeps.
	/// </summary>
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
		public LastSeenCacheTracker(IEqualityComparer<TKey> comparer = null)
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
		public bool TryGetAndTouch(TKey key, DateTime nowUtc, out TValue value)
		{
			lock (gate)
			{
				if (!values.TryGetValue(key, out CacheValue current))
				{
					value = default;
					return false;
				}

				current = new CacheValue(current.Value, nowUtc);
				values[key] = current;

				if (nodes.TryGetValue(key, out LinkedListNode<QueueNode> oldNode))
				{
					queue.Remove(oldNode);
				}

				nodes[key] = queue.AddLast(new QueueNode(key, nowUtc));
				value = current.Value;
				return true;
			}
		}

		/// <summary>
		/// Inserts or updates a cached value and sets its last-seen timestamp.
		/// </summary>
		public void Upsert(TKey key, TValue value, DateTime nowUtc)
		{
			lock (gate)
			{
				values[key] = new CacheValue(value, nowUtc);

				if (nodes.TryGetValue(key, out LinkedListNode<QueueNode> oldNode))
				{
					queue.Remove(oldNode);
				}

				nodes[key] = queue.AddLast(new QueueNode(key, nowUtc));
			}
		}

		/// <summary>
		/// Removes a cached key if present.
		/// </summary>
		public void Remove(TKey key)
		{
			lock (gate)
			{
				values.Remove(key);
				if (nodes.TryGetValue(key, out LinkedListNode<QueueNode> node))
				{
					nodes.Remove(key);
					queue.Remove(node);
				}
			}
		}

		/// <summary>
		/// Sweeps entries whose last-seen exceeds the provided TTL.
		/// </summary>
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

					if (!values.TryGetValue(queued.Key, out CacheValue current))
					{
						queue.RemoveFirst();
						nodes.Remove(queued.Key);
						continue;
					}

					if (current.LastSeenUtc != queued.LastSeenUtc)
					{
						// Stale queue node after touch/upsert refresh.
						queue.RemoveFirst();
						continue;
					}

					if ((nowUtc - current.LastSeenUtc) < ttl)
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
			public readonly DateTime LastSeenUtc;

			public QueueNode(TKey key, DateTime lastSeenUtc)
			{
				Key = key;
				LastSeenUtc = lastSeenUtc;
			}
		}

		private readonly struct CacheValue
		{
			public readonly TValue Value;
			public readonly DateTime LastSeenUtc;

			public CacheValue(TValue value, DateTime lastSeenUtc)
			{
				Value = value;
				LastSeenUtc = lastSeenUtc;
			}
		}
	}
}