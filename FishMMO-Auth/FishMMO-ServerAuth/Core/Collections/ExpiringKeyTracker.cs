using System;
using System.Collections.Generic;

namespace FishMMO.Auth.Core.Collections
{
	/// <summary>
	/// Queue/index tracker for expiring keyed entries.
	/// Uses head-first sweeps to avoid full dictionary enumeration under heavy load.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Time is monotonic seconds</b> — a <c>MonotonicClock.NowSeconds</c> reading from the
	/// authenticator's clock, never <c>DateTime.UtcNow</c>. A debounce is a duration, and on the
	/// host's wall clock a step backwards held every recently seen key inside its window for the
	/// size of the step (an account refused for two seconds was refused for an hour), while a step
	/// forward opened every window at once. The tracker only compares the readings it is given
	/// with one another, so the clock's origin does not matter.
	/// </para>
	/// <para>
	/// There is deliberately no <see cref="DateTime"/> overload. The game server's copy of this
	/// class still carries one for callers outside authentication, and one tracker driven through
	/// both compared unrelated numbers; this copy's callers are all in the authenticator and all on
	/// the one clock, so the choice cannot be made wrongly here.
	/// </para>
	/// </remarks>
	/// <typeparam name="TKey">Tracker key type.</typeparam>
	public sealed class ExpiringKeyTracker<TKey>
	{
		private readonly object gate = new object();
		private readonly Dictionary<TKey, double> nextAllowedSeconds;
		private readonly LinkedList<ExpiryQueueNode> expiryQueue = new LinkedList<ExpiryQueueNode>();
		private readonly Dictionary<TKey, LinkedListNode<ExpiryQueueNode>> queueNodes;

		/// <summary>
		/// Initializes a new tracker with an optional key comparer.
		/// </summary>
		public ExpiringKeyTracker(IEqualityComparer<TKey>? comparer = null)
		{
			nextAllowedSeconds = comparer == null
				? new Dictionary<TKey, double>()
				: new Dictionary<TKey, double>(comparer);

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
					return nextAllowedSeconds.Count;
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
				nextAllowedSeconds.Clear();
				expiryQueue.Clear();
				queueNodes.Clear();
			}
		}

		/// <summary>
		/// Attempts to begin a debounce/rate-limit window for a key.
		/// </summary>
		/// <param name="key">Tracker key.</param>
		/// <param name="nowSeconds">Current monotonic time in seconds.</param>
		/// <param name="duration">Window duration.</param>
		/// <returns><c>true</c> if allowed now; otherwise <c>false</c>.</returns>
		public bool TryBegin(TKey key, double nowSeconds, TimeSpan duration)
		{
			if (duration <= TimeSpan.Zero)
			{
				return true;
			}

			lock (gate)
			{
				if (nextAllowedSeconds.TryGetValue(key, out double nextAllowed) && nextAllowed > nowSeconds)
				{
					return false;
				}

				double expiresSeconds = nowSeconds + duration.TotalSeconds;
				nextAllowedSeconds[key] = expiresSeconds;

				if (queueNodes.TryGetValue(key, out LinkedListNode<ExpiryQueueNode>? existingNode))
				{
					expiryQueue.Remove(existingNode);
				}

				queueNodes[key] = expiryQueue.AddLast(new ExpiryQueueNode(key, expiresSeconds));
				return true;
			}
		}

		/// <summary>
		/// Sweeps expired keys with bounded scan and removal limits.
		/// </summary>
		/// <param name="nowSeconds">Current monotonic time in seconds.</param>
		/// <param name="maxScan">Maximum queue nodes to inspect this sweep.</param>
		/// <param name="maxRemove">Maximum keys to remove this sweep.</param>
		/// <returns>Number of entries removed.</returns>
		public int SweepExpired(double nowSeconds, int maxScan, int maxRemove)
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
					LinkedListNode<ExpiryQueueNode>? head = expiryQueue.First;
					if (head == null)
					{
						break;
					}

					scanned++;
					ExpiryQueueNode queued = head.Value;

					if (!nextAllowedSeconds.TryGetValue(queued.Key, out double currentExpiry))
					{
						expiryQueue.RemoveFirst();
						queueNodes.Remove(queued.Key);
						continue;
					}

					if (currentExpiry != queued.ExpiresSeconds)
					{
						// Stale queued node after refresh.
						expiryQueue.RemoveFirst();
						continue;
					}

					if (currentExpiry > nowSeconds)
					{
						break;
					}

					nextAllowedSeconds.Remove(queued.Key);
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
			public readonly double ExpiresSeconds;

			public ExpiryQueueNode(TKey key, double expiresSeconds)
			{
				Key = key;
				ExpiresSeconds = expiresSeconds;
			}
		}
	}
}
