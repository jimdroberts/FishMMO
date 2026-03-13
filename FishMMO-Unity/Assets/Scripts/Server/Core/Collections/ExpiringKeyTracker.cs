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