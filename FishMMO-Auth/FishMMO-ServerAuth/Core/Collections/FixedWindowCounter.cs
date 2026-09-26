using System;
using System.Collections.Generic;

namespace FishMMO.Auth.Core.Collections
{
	/// <summary>
	/// Per-key event counter over a fixed window that opens at the key's first event, with
	/// expiry swept head-first in the order windows opened.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The shape behind every "N failures within T of the first one" lockout and every "at most
	/// N per T" burst limiter here: a key's window opens at its first event, counts events until
	/// it is <see cref="Window"/> old, and then closes — the next event opens a fresh window. A
	/// window never moves once opened, so the order windows opened in is exactly the order they
	/// expire in, and that is the order the internal list keeps.
	/// </para>
	/// <para>
	/// <b>Why not a dictionary swept from its head.</b> The trackers this replaces were
	/// <c>ConcurrentDictionary</c> instances swept by enumerating at most N entries per pass. An
	/// enumeration always starts at the same place, so once the first N entries were live the
	/// sweep never reached anything behind them: expired entries leaked until a capacity cap was
	/// hit, after which new keys stopped being tracked at all and the lockout they implement
	/// silently switched off. Here a sweep looks only at the oldest window, removes it if it has
	/// closed, and stops at the first one that has not — O(removed + 1), and it always reaches
	/// the oldest entry first.
	/// </para>
	/// <para>
	/// An expired window that the sweep has not reached yet is never counted: every read treats
	/// it as absent and every write replaces it, so the sweep only reclaims memory and has no
	/// effect on any answer.
	/// </para>
	/// <para>
	/// <b>Time is monotonic seconds</b> — a <c>MonotonicClock.NowSeconds</c> reading, from the
	/// authenticator's clock or the game server's. A window is a local duration, and a wall clock
	/// stepped by NTP would close every window at once or hold them all open. One counter must
	/// only ever be given readings from one clock.
	/// </para>
	/// <para>
	/// Thread-safe. Every operation takes one short lock; nothing is allocated except when a key
	/// opens a window.
	/// </para>
	/// </remarks>
	/// <typeparam name="TKey">Counted key type.</typeparam>
	public sealed class FixedWindowCounter<TKey> where TKey : notnull
	{
		private readonly object gate = new object();
		private readonly Dictionary<TKey, LinkedListNode<Entry>> entries;
		private readonly LinkedList<Entry> windowsByOpening = new LinkedList<Entry>();
		private readonly TimeSpan window;
		private readonly double windowSeconds;

		/// <summary>
		/// Initializes a counter whose windows last <paramref name="window"/>.
		/// </summary>
		/// <param name="window">How long a window stays open after its first event. Must be positive.</param>
		/// <param name="comparer">Optional key comparer.</param>
		public FixedWindowCounter(TimeSpan window, IEqualityComparer<TKey>? comparer = null)
		{
			if (window <= TimeSpan.Zero)
			{
				throw new ArgumentOutOfRangeException(nameof(window), "The window must be positive.");
			}
			this.window = window;
			windowSeconds = window.TotalSeconds;
			entries = comparer == null
				? new Dictionary<TKey, LinkedListNode<Entry>>()
				: new Dictionary<TKey, LinkedListNode<Entry>>(comparer);
		}

		/// <summary>How long a window stays open after its first event.</summary>
		public TimeSpan Window => window;

		/// <summary>
		/// Keys currently held, including closed windows the sweep has not reached yet.
		/// </summary>
		public int Count
		{
			get
			{
				lock (gate)
				{
					return entries.Count;
				}
			}
		}

		/// <summary>
		/// Counts one event for <paramref name="key"/>, opening a fresh window when none is open.
		/// </summary>
		/// <param name="key">Counted key.</param>
		/// <param name="nowSeconds">Current monotonic time in seconds.</param>
		/// <param name="maxKeys">
		/// Capacity for keys with no open window. A key that already has one is always counted,
		/// so a lockout already under way keeps accruing at capacity; only a new key is refused.
		/// </param>
		/// <returns>The count in the key's window after this event, or 0 when the key was refused for capacity.</returns>
		public int Increment(TKey key, double nowSeconds, int maxKeys = int.MaxValue)
		{
			lock (gate)
			{
				if (TryGetOpen(key, nowSeconds, out LinkedListNode<Entry> node))
				{
					node.Value.Count++;
					return node.Value.Count;
				}

				if (!HasRoomLocked(nowSeconds, maxKeys))
				{
					return 0;
				}

				Open(key, nowSeconds);
				return 1;
			}
		}

		/// <summary>
		/// Counts one event for <paramref name="key"/> only while its window holds fewer than
		/// <paramref name="limit"/> events.
		/// </summary>
		/// <remarks>
		/// A refused event does not touch the window — it neither counts nor extends it — so a
		/// caller hammering a full window waits out the same window as everyone else.
		/// </remarks>
		/// <param name="key">Counted key.</param>
		/// <param name="nowSeconds">Current monotonic time in seconds.</param>
		/// <param name="limit">Events allowed per window.</param>
		/// <param name="maxKeys">Capacity for keys with no open window; see <see cref="Increment"/>.</param>
		/// <returns><c>true</c> when the event was counted; <c>false</c> when the window is full or the key was refused for capacity.</returns>
		public bool TryIncrement(TKey key, double nowSeconds, int limit, int maxKeys = int.MaxValue)
		{
			if (limit <= 0)
			{
				return false;
			}

			lock (gate)
			{
				if (TryGetOpen(key, nowSeconds, out LinkedListNode<Entry> node))
				{
					if (node.Value.Count >= limit)
					{
						return false;
					}
					node.Value.Count++;
					return true;
				}

				if (!HasRoomLocked(nowSeconds, maxKeys))
				{
					return false;
				}

				Open(key, nowSeconds);
				return true;
			}
		}

		/// <summary>
		/// Events counted in <paramref name="key"/>'s open window, or 0 when it has none.
		/// </summary>
		/// <remarks>A closed window found on the way is removed.</remarks>
		public int GetCount(TKey key, double nowSeconds)
		{
			lock (gate)
			{
				return TryGetOpen(key, nowSeconds, out LinkedListNode<Entry> node) ? node.Value.Count : 0;
			}
		}

		/// <summary>
		/// Seconds until <paramref name="key"/>'s open window closes, or 0 when it has none.
		/// </summary>
		/// <remarks>
		/// For a lockout this is how long the refusal still has to run, which is what a player told
		/// they are locked out needs to know. A closed window found on the way is removed.
		/// </remarks>
		public double SecondsUntilClose(TKey key, double nowSeconds)
		{
			lock (gate)
			{
				if (!TryGetOpen(key, nowSeconds, out LinkedListNode<Entry> node))
				{
					return 0d;
				}
				return Math.Max(0d, node.Value.OpenedSeconds + windowSeconds - nowSeconds);
			}
		}

		/// <summary>Drops <paramref name="key"/>'s window, open or not.</summary>
		/// <returns><c>true</c> when the key was held.</returns>
		public bool Remove(TKey key)
		{
			lock (gate)
			{
				if (!entries.TryGetValue(key, out LinkedListNode<Entry> node))
				{
					return false;
				}
				entries.Remove(key);
				windowsByOpening.Remove(node);
				return true;
			}
		}

		/// <summary>Drops every window.</summary>
		public void Clear()
		{
			lock (gate)
			{
				entries.Clear();
				windowsByOpening.Clear();
			}
		}

		/// <summary>
		/// Removes closed windows, oldest first, stopping at the first one still open.
		/// </summary>
		/// <param name="nowSeconds">Current monotonic time in seconds.</param>
		/// <param name="maxRemove">Most windows to remove in this call.</param>
		/// <returns>Windows removed.</returns>
		public int SweepExpired(double nowSeconds, int maxRemove)
		{
			if (maxRemove <= 0)
			{
				return 0;
			}

			lock (gate)
			{
				return SweepLocked(nowSeconds, maxRemove);
			}
		}

		/// <summary>
		/// Whether a window opened at <paramref name="openedSeconds"/> is still open at <paramref name="nowSeconds"/>.
		/// </summary>
		/// <remarks>
		/// The single rule every read, write and sweep uses. A window is open for exactly
		/// <see cref="Window"/>: closed at <c>opened + window</c>, not a tick after it.
		/// </remarks>
		public bool IsOpen(double openedSeconds, double nowSeconds) => nowSeconds - openedSeconds < windowSeconds;

		private bool TryGetOpen(TKey key, double nowSeconds, out LinkedListNode<Entry> node)
		{
			if (!entries.TryGetValue(key, out node!))
			{
				return false;
			}

			if (IsOpen(node.Value.OpenedSeconds, nowSeconds))
			{
				return true;
			}

			// Closed but not yet swept: reclaim it now so the caller sees the key as absent.
			entries.Remove(key);
			windowsByOpening.Remove(node);
			node = null!;
			return false;
		}

		private bool HasRoomLocked(double nowSeconds, int maxKeys)
		{
			if (entries.Count < maxKeys)
			{
				return true;
			}

			// At capacity: closed windows the periodic sweep has not reached yet are not live
			// keys, so reclaim them before refusing anyone.
			SweepLocked(nowSeconds, int.MaxValue);
			return entries.Count < maxKeys;
		}

		private void Open(TKey key, double nowSeconds)
		{
			LinkedListNode<Entry> node = windowsByOpening.AddLast(new Entry(key, nowSeconds));
			entries[key] = node;
		}

		private int SweepLocked(double nowSeconds, int maxRemove)
		{
			int removed = 0;
			while (removed < maxRemove)
			{
				LinkedListNode<Entry>? head = windowsByOpening.First;
				if (head == null || IsOpen(head.Value.OpenedSeconds, nowSeconds))
				{
					break;
				}
				windowsByOpening.RemoveFirst();
				entries.Remove(head.Value.Key);
				removed++;
			}
			return removed;
		}

		private sealed class Entry
		{
			public readonly TKey Key;
			public readonly double OpenedSeconds;
			public int Count;

			public Entry(TKey key, double openedSeconds)
			{
				Key = key;
				OpenedSeconds = openedSeconds;
				Count = 1;
			}
		}
	}
}
