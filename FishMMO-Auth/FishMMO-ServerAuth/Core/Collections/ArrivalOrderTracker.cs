using System;
using System.Collections.Generic;

namespace FishMMO.Auth.Core.Collections
{
	/// <summary>
	/// Queue/index tracker that preserves first-seen ordering with O(1) add/remove by key.
	/// Useful for TTL sweeps that should process oldest entries first.
	/// </summary>
	/// <remarks>
	/// First-seen times are monotonic seconds (a <c>MonotonicClock.NowSeconds</c> reading), never
	/// <c>DateTime.UtcNow</c>: the account manager's backstop sweep ages entries by them, and on the
	/// wall clock a step forward purged every connection mid-sign-in at once while a step back held
	/// abandoned ones for the size of the step. A caller that only wants the order (the login queue)
	/// passes <c>default</c> and ignores the value.
	/// </remarks>
	/// <typeparam name="TKey">Tracked key type.</typeparam>
	public sealed class ArrivalOrderTracker<TKey>
	{
		private readonly object gate = new object();
		private readonly LinkedList<ArrivalEntry<TKey>> queue = new LinkedList<ArrivalEntry<TKey>>();
		private readonly Dictionary<TKey, LinkedListNode<ArrivalEntry<TKey>>> nodes;

		/// <summary>
		/// Initializes a new tracker with an optional key comparer.
		/// </summary>
		public ArrivalOrderTracker(IEqualityComparer<TKey>? comparer = null)
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
		/// <param name="key">The key to track.</param>
		/// <param name="firstSeenSeconds">The monotonic time, in seconds, to record as first-seen.</param>
		public void TrackIfMissing(TKey key, double firstSeenSeconds)
		{
			lock (gate)
			{
				if (nodes.ContainsKey(key))
				{
					return;
				}

				nodes[key] = queue.AddLast(new ArrivalEntry<TKey>(key, firstSeenSeconds));
			}
		}

		/// <summary>
		/// Removes a tracked key if present. O(1) via Dictionary→LinkedListNode lookup.
		/// </summary>
		/// <param name="key">The key to remove.</param>
		/// <returns><c>true</c> if the key was found and removed; otherwise, <c>false</c>.</returns>
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
		/// <param name="key">The oldest key, if one exists.</param>
		/// <param name="firstSeenSeconds">The first-seen monotonic time of the oldest key.</param>
		/// <returns><c>true</c> if a key was found; otherwise, <c>false</c>.</returns>
		public bool TryPeekOldest(out TKey key, out double firstSeenSeconds)
		{
			lock (gate)
			{
				LinkedListNode<ArrivalEntry<TKey>> head = queue.First;
				if (head == null)
				{
					key = default!;
					firstSeenSeconds = default;
					return false;
				}

				key = head.Value.Key;
				firstSeenSeconds = head.Value.FirstSeenSeconds;
				return true;
			}
		}

		/// <summary>
		/// Removes and returns the oldest tracked key.
		/// </summary>
		/// <param name="key">The removed key, if one existed.</param>
		/// <param name="firstSeenSeconds">The first-seen monotonic time of the removed key.</param>
		/// <returns><c>true</c> if a key was removed; otherwise, <c>false</c>.</returns>
		public bool PopOldest(out TKey key, out double firstSeenSeconds)
		{
			lock (gate)
			{
				LinkedListNode<ArrivalEntry<TKey>> head = queue.First;
				if (head == null)
				{
					key = default!;
					firstSeenSeconds = default;
					return false;
				}

				key = head.Value.Key;
				firstSeenSeconds = head.Value.FirstSeenSeconds;
				queue.RemoveFirst();
				nodes.Remove(key);
				return true;
			}
		}

		/// <summary>
		/// Returns <c>true</c> if <paramref name="key"/> is currently tracked.
		/// O(1) via dictionary lookup.
		/// </summary>
		public bool Contains(TKey key)
		{
			lock (gate)
			{
				return nodes.ContainsKey(key);
			}
		}

		/// <summary>
		/// Gets the 1-based position of <paramref name="key"/> in the queue.
		/// Returns 0 if the key is not tracked.
		/// O(1) via dictionary+LinkedListNode traversal is NOT possible
		/// (LinkedListNode has no index).  This does an O(N) linear scan.
		/// Callers that need per-entry positions for the entire queue
		/// should use <see cref="ForEachInOrder"/> instead.
		/// </summary>
		public int GetPosition(TKey key)
		{
			lock (gate)
			{
				if (!nodes.TryGetValue(key, out LinkedListNode<ArrivalEntry<TKey>> target))
					return 0;
				int pos = 1;
				for (var node = queue.First; node != null; node = node.Next)
				{
					if (node == target) return pos;
					pos++;
				}
				return 0; // Should not reach here — node was in dictionary but not in list
			}
		}

		/// <summary>
		/// Invokes <paramref name="action"/> for each tracked key in FIFO order.
		/// The callback receives the key, its first-seen monotonic time, and its
		/// 1-based position.  All work is done under the internal lock so the
		/// callback should be fast and must not call back into the tracker.
		/// Returns the total number of entries processed.
		/// </summary>
		/// <param name="action">Callback invoked for each entry: (key, firstSeenSeconds, position).</param>
		/// <returns>The total number of entries iterated.</returns>
		public int ForEachInOrder(Action<TKey, double, int> action)
		{
			lock (gate)
			{
				int pos = 1;
				for (var node = queue.First; node != null; node = node.Next, pos++)
				{
					action(node.Value.Key, node.Value.FirstSeenSeconds, pos);
				}
				return pos - 1;
			}
		}

		private readonly struct ArrivalEntry<T>
		{
			public readonly T Key;
			public readonly double FirstSeenSeconds;

			public ArrivalEntry(T key, double firstSeenSeconds)
			{
				Key = key;
				FirstSeenSeconds = firstSeenSeconds;
			}
		}
	}
}