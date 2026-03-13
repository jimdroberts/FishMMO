using System;
using System.Collections.Generic;

namespace FishMMO.Server.Core.Collections
{
	/// <summary>
	/// Queue/index tracker that preserves first-seen ordering with O(1) add/remove by key.
	/// Useful for TTL sweeps that should process oldest entries first.
	/// </summary>
	/// <typeparam name="TKey">Tracked key type.</typeparam>
	public sealed class ArrivalOrderTracker<TKey>
	{
		private readonly object gate = new object();
		private readonly LinkedList<ArrivalEntry<TKey>> queue = new LinkedList<ArrivalEntry<TKey>>();
		private readonly Dictionary<TKey, LinkedListNode<ArrivalEntry<TKey>>> nodes;

		/// <summary>
		/// Initializes a new tracker with an optional key comparer.
		/// </summary>
		public ArrivalOrderTracker(IEqualityComparer<TKey> comparer = null)
		{
			nodes = comparer == null
				? new Dictionary<TKey, LinkedListNode<ArrivalEntry<TKey>>>()
				: new Dictionary<TKey, LinkedListNode<ArrivalEntry<TKey>>>(comparer);
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
					return queue.Count;
				}
			}
		}

		/// <summary>
		/// Clears all tracked keys.
		/// </summary>
		public void Clear()
		{
			lock (gate)
			{
				queue.Clear();
				nodes.Clear();
			}
		}

		/// <summary>
		/// Adds a key only if it is not already tracked.
		/// </summary>
		public void TrackIfMissing(TKey key, DateTime firstSeenUtc)
		{
			lock (gate)
			{
				if (nodes.ContainsKey(key))
				{
					return;
				}

				nodes[key] = queue.AddLast(new ArrivalEntry<TKey>(key, firstSeenUtc));
			}
		}

		/// <summary>
		/// Removes a tracked key if present. O(1) via Dictionary→LinkedListNode lookup;
		/// <see cref="LinkedList{T}.Remove(LinkedListNode{T})"/> is itself O(1).
		/// </summary>
		public bool Remove(TKey key)
		{
			lock (gate)
			{
				if (!nodes.TryGetValue(key, out LinkedListNode<ArrivalEntry<TKey>> node))
				{
					return false;
				}

				nodes.Remove(key);
				if (node.List != null)
				{
					queue.Remove(node);
				}
				return true;
			}
		}

		/// <summary>
		/// Gets the oldest tracked key without removing it.
		/// </summary>
		public bool TryPeekOldest(out TKey key, out DateTime firstSeenUtc)
		{
			lock (gate)
			{
				LinkedListNode<ArrivalEntry<TKey>> head = queue.First;
				if (head == null)
				{
					key = default;
					firstSeenUtc = default;
					return false;
				}

				key = head.Value.Key;
				firstSeenUtc = head.Value.FirstSeenUtc;
				return true;
			}
		}

		/// <summary>
		/// Removes and returns the oldest tracked key.
		/// </summary>
		public bool PopOldest(out TKey key, out DateTime firstSeenUtc)
		{
			lock (gate)
			{
				LinkedListNode<ArrivalEntry<TKey>> head = queue.First;
				if (head == null)
				{
					key = default;
					firstSeenUtc = default;
					return false;
				}

				key = head.Value.Key;
				firstSeenUtc = head.Value.FirstSeenUtc;
				queue.RemoveFirst();
				nodes.Remove(key);
				return true;
			}
		}

		private readonly struct ArrivalEntry<T>
		{
			public readonly T Key;
			public readonly DateTime FirstSeenUtc;

			public ArrivalEntry(T key, DateTime firstSeenUtc)
			{
				Key = key;
				FirstSeenUtc = firstSeenUtc;
			}
		}
	}
}