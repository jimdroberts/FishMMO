using System;
using System.Collections.Generic;
using FishMMO.Server.Core.Collections;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Lookups in flight, keyed by what is being looked up, each with every requester waiting on it.
	/// Main thread only.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A second request joins the first.</b> The naming system used to mark a key in flight with
	/// a bare flag, so a second connection asking for the same ID while the first fetch ran was
	/// simply dropped — no fetch of its own, and no share of the first one's answer (hot-path audit
	/// M18). Every requester now waits on the one fetch, and the answer goes to all of them.
	/// </para>
	/// <para>
	/// <b>A lookup that never answers is abandoned, not trusted forever.</b> The answer comes back
	/// through the main-thread queue, which can refuse it when full. A key whose fetch has been out
	/// longer than the stale bound is treated as lost: the next request for it starts a new fetch
	/// and inherits the waiters, and <see cref="SweepStale"/> drops one nobody asks for again.
	/// </para>
	/// <para>
	/// Pure bookkeeping, generic over the waiter so the rule can be pinned without a connection.
	/// </para>
	/// </remarks>
	/// <typeparam name="TKey">What is being looked up.</typeparam>
	/// <typeparam name="TWaiter">Who is waiting for the answer.</typeparam>
	public sealed class InFlightLookupTable<TKey, TWaiter>
	{
		/// <summary>
		/// What <see cref="Join"/> decided.
		/// </summary>
		public enum JoinResult
		{
			/// <summary>No live lookup for the key: the caller must start the fetch.</summary>
			Started,
			/// <summary>A lookup is already out; the waiter will receive its answer.</summary>
			Joined,
			/// <summary>The table or the key's waiter list is full; the request is not tracked.</summary>
			Refused,
		}

		private sealed class Entry
		{
			public DateTime StartedUtc;
			public readonly List<TWaiter> Waiters = new List<TWaiter>(2);
		}

		private readonly Dictionary<TKey, Entry> entries;
		private readonly IEqualityComparer<TWaiter> waiterComparer;
		private readonly List<TKey> keyScratch = new List<TKey>();

		/// <summary>Most keys in flight at once.</summary>
		public int MaxEntries { get; }

		/// <summary>Most requesters waiting on one key.</summary>
		public int MaxWaitersPerEntry { get; }

		/// <summary>Keys currently in flight.</summary>
		public int Count => entries.Count;

		/// <summary>
		/// Creates a table.
		/// </summary>
		/// <param name="maxEntries">Most keys in flight at once.</param>
		/// <param name="maxWaitersPerEntry">Most requesters waiting on one key.</param>
		/// <param name="keyComparer">Key comparer, or null for the default.</param>
		/// <param name="waiterComparer">Waiter comparer, or null for the default.</param>
		public InFlightLookupTable(int maxEntries, int maxWaitersPerEntry, IEqualityComparer<TKey> keyComparer = null, IEqualityComparer<TWaiter> waiterComparer = null)
		{
			MaxEntries = Math.Max(1, maxEntries);
			MaxWaitersPerEntry = Math.Max(1, maxWaitersPerEntry);
			entries = keyComparer == null ? new Dictionary<TKey, Entry>() : new Dictionary<TKey, Entry>(keyComparer);
			this.waiterComparer = waiterComparer ?? EqualityComparer<TWaiter>.Default;
		}

		/// <summary>
		/// Adds a requester to the lookup for a key, and says whether the caller must start the fetch.
		/// </summary>
		/// <param name="key">What is being looked up.</param>
		/// <param name="waiter">Who is asking. Asking twice waits once.</param>
		/// <param name="nowUtc">Now.</param>
		/// <param name="staleAfter">How long a fetch may be out before it is presumed lost.</param>
		/// <returns>
		/// <see cref="JoinResult.Started"/> when there was no live lookup (a stale one is restarted,
		/// keeping its waiters), <see cref="JoinResult.Joined"/> when one is out, and
		/// <see cref="JoinResult.Refused"/> when the table is full.
		/// </returns>
		public JoinResult Join(TKey key, TWaiter waiter, DateTime nowUtc, TimeSpan staleAfter)
		{
			if (entries.TryGetValue(key, out Entry entry))
			{
				bool stale = nowUtc - entry.StartedUtc >= staleAfter;
				if (!Contains(entry, waiter))
				{
					if (entry.Waiters.Count >= MaxWaitersPerEntry)
					{
						return stale ? Restart(entry, nowUtc) : JoinResult.Refused;
					}
					entry.Waiters.Add(waiter);
				}
				return stale ? Restart(entry, nowUtc) : JoinResult.Joined;
			}

			if (entries.Count >= MaxEntries)
			{
				return JoinResult.Refused;
			}

			entry = new Entry { StartedUtc = nowUtc };
			entry.Waiters.Add(waiter);
			entries.Add(key, entry);
			return JoinResult.Started;
		}

		/// <summary>
		/// <see cref="Join(TKey, TWaiter, DateTime, TimeSpan)"/> for a table timed on
		/// <see cref="MonotonicClock"/>.
		/// </summary>
		/// <param name="key">What is being looked up.</param>
		/// <param name="waiter">Who is asking.</param>
		/// <param name="nowSeconds">Current <see cref="MonotonicClock.NowSeconds"/> reading.</param>
		/// <param name="staleAfter">How long a fetch may be out before it is presumed lost.</param>
		/// <returns>As <see cref="Join(TKey, TWaiter, DateTime, TimeSpan)"/>.</returns>
		/// <remarks>
		/// The stale bound is a local duration: on the wall clock a host stepped forward presumed
		/// every fetch in flight lost at once and started each again. One table, one clock: see
		/// <see cref="MonotonicInstant"/>.
		/// </remarks>
		public JoinResult Join(TKey key, TWaiter waiter, double nowSeconds, TimeSpan staleAfter) =>
			Join(key, waiter, MonotonicInstant.From(nowSeconds), staleAfter);

		/// <summary>
		/// Ends the lookup for a key and hands back everyone who was waiting on it.
		/// </summary>
		/// <param name="key">The key whose fetch finished (answered or not).</param>
		/// <param name="waiters">Receives the waiters. Not cleared first.</param>
		/// <returns>True if the key was in flight.</returns>
		public bool TryComplete(TKey key, List<TWaiter> waiters)
		{
			if (!entries.TryGetValue(key, out Entry entry))
			{
				return false;
			}
			entries.Remove(key);
			waiters?.AddRange(entry.Waiters);
			return true;
		}

		/// <summary>
		/// Drops lookups out longer than <paramref name="staleAfter"/>.
		/// </summary>
		/// <returns>The number dropped.</returns>
		public int SweepStale(DateTime nowUtc, TimeSpan staleAfter)
		{
			keyScratch.Clear();
			foreach (KeyValuePair<TKey, Entry> pair in entries)
			{
				if (nowUtc - pair.Value.StartedUtc >= staleAfter)
				{
					keyScratch.Add(pair.Key);
				}
			}
			for (int i = 0; i < keyScratch.Count; i++)
			{
				entries.Remove(keyScratch[i]);
			}
			int dropped = keyScratch.Count;
			keyScratch.Clear();
			return dropped;
		}

		/// <summary>
		/// <see cref="SweepStale(DateTime, TimeSpan)"/> for a table timed on <see cref="MonotonicClock"/>.
		/// </summary>
		/// <param name="nowSeconds">Current <see cref="MonotonicClock.NowSeconds"/> reading.</param>
		/// <param name="staleAfter">How long a fetch may be out before it is presumed lost.</param>
		/// <returns>The number dropped.</returns>
		public int SweepStale(double nowSeconds, TimeSpan staleAfter) =>
			SweepStale(MonotonicInstant.From(nowSeconds), staleAfter);

		/// <summary>Forgets everything.</summary>
		public void Clear()
		{
			entries.Clear();
		}

		private bool Contains(Entry entry, TWaiter waiter)
		{
			for (int i = 0; i < entry.Waiters.Count; i++)
			{
				if (waiterComparer.Equals(entry.Waiters[i], waiter))
				{
					return true;
				}
			}
			return false;
		}

		private static JoinResult Restart(Entry entry, DateTime nowUtc)
		{
			entry.StartedUtc = nowUtc;
			return JoinResult.Started;
		}
	}

	/// <summary>
	/// One connection's naming request budget: a token bucket.
	/// </summary>
	/// <remarks>
	/// Replaces a 75 ms per-connection drop window, which answered the first request of any burst
	/// and silently dropped the rest: opening a 100-member roster sent 100 requests in one frame and
	/// 99 were thrown away (hot-path audit M18). A bucket admits a burst up to its capacity and then
	/// the refill rate, which bounds the database work a connection can cause just as firmly.
	/// </remarks>
	public struct NamingRequestBucket
	{
		/// <summary>Tokens available.</summary>
		public double Tokens;

		/// <summary>
		/// Ticks of the last refill, on whichever clock the caller keeps the bucket on. The naming
		/// system keeps it on <see cref="MonotonicClock.NowTicks"/>: a refill rate is a local
		/// duration.
		/// </summary>
		public long LastRefillTicks;

		/// <summary>
		/// A bucket for a connection seen for the first time: full.
		/// </summary>
		public static NamingRequestBucket Full(int capacity, long nowTicks)
		{
			return new NamingRequestBucket { Tokens = Math.Max(0, capacity), LastRefillTicks = nowTicks };
		}

		/// <summary>
		/// Refills for the time elapsed and takes one token.
		/// </summary>
		/// <param name="nowTicks">Ticks now, on the bucket's clock.</param>
		/// <param name="capacity">Burst capacity. Zero or less admits everything.</param>
		/// <param name="refillPerSecond">Tokens returned per second.</param>
		/// <returns>True if the request is admitted.</returns>
		public bool TryTake(long nowTicks, int capacity, double refillPerSecond)
		{
			return TryTakeUpTo(nowTicks, capacity, refillPerSecond, 1) == 1;
		}

		/// <summary>
		/// Refills for the time elapsed and takes as many of <paramref name="requested"/> tokens as
		/// the bucket holds.
		/// </summary>
		/// <remarks>
		/// A batched name request is charged one token per ID it carries, the same as that many
		/// single requests. A batch larger than what is left is answered in part — the leading
		/// IDs, up to the tokens available — rather than refused whole: the client asks again for
		/// the rest, and a partial answer is what the same IDs sent one by one would have got.
		/// </remarks>
		/// <param name="nowTicks">Ticks now, on the bucket's clock.</param>
		/// <param name="capacity">Burst capacity. Zero or less admits everything.</param>
		/// <param name="refillPerSecond">Tokens returned per second.</param>
		/// <param name="requested">Tokens wanted.</param>
		/// <returns>The number admitted, from zero to <paramref name="requested"/>.</returns>
		public int TryTakeUpTo(long nowTicks, int capacity, double refillPerSecond, int requested)
		{
			if (requested <= 0)
			{
				return 0;
			}
			if (capacity <= 0)
			{
				return requested;
			}

			double elapsedSeconds = (nowTicks - LastRefillTicks) / (double)TimeSpan.TicksPerSecond;
			if (elapsedSeconds > 0.0)
			{
				Tokens = Math.Min(capacity, Tokens + elapsedSeconds * Math.Max(0.0, refillPerSecond));
				LastRefillTicks = nowTicks;
			}

			int admitted = (int)Math.Min(requested, Math.Floor(Math.Max(0.0, Tokens)));
			Tokens -= admitted;
			return admitted;
		}
	}
}
