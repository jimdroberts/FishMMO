using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FishMMO.Server.Core.Collections
{
	/// <summary>
	/// A thread-safe, fixed-TTL cache of asynchronous reads in which every caller asking for the
	/// same key while a read is in flight shares that one read.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="TimedCache{TKey,TValue}"/> answers "is there a fresh value"; on a miss every
	/// caller goes and reads for itself. For a value many players ask for at once — the first page
	/// of a leaderboard the moment a season ends, say — that is N identical database reads where
	/// one would do. Here the first caller on a miss starts the read and every caller behind it,
	/// until the read completes and then until its TTL runs out, is handed the same task.
	/// </para>
	/// <para>
	/// <b>Freshness is measured from when the read STARTED.</b> A value read for 2 s then kept for
	/// a 60 s TTL is served until 60 s after it began, not 62 — the data is as old as the moment
	/// the database was asked, whatever the read cost.
	/// </para>
	/// <para>
	/// <b>Failures are never cached.</b> A read that throws, or whose value the caller's
	/// <c>keep</c> predicate rejects, is removed before its task completes, so every caller
	/// sharing it sees that one failure and the next caller after starts a fresh read. A shard
	/// whose database blipped for a second must not serve "unavailable" for the next minute.
	/// </para>
	/// <para>
	/// The read itself runs outside the lock; the lock only guards the table. Continuations run
	/// asynchronously, so completing a read never runs a waiting caller's code on the reading
	/// thread while it still holds anything.
	/// </para>
	/// </remarks>
	/// <typeparam name="TKey">Cache key type.</typeparam>
	/// <typeparam name="TValue">Cached value type.</typeparam>
	public sealed class SingleFlightCache<TKey, TValue>
	{
		private readonly object gate = new object();
		private readonly Dictionary<TKey, Entry> entries;
		private readonly LinkedList<QueueNode> queue = new LinkedList<QueueNode>();
		private readonly Func<DateTime> utcNow;

		/// <summary>
		/// Initializes a new cache.
		/// </summary>
		/// <param name="utcNow">Clock; defaults to <see cref="DateTime.UtcNow"/>. Injected by tests.</param>
		/// <param name="comparer">Optional key comparer.</param>
		public SingleFlightCache(Func<DateTime> utcNow = null, IEqualityComparer<TKey> comparer = null)
		{
			this.utcNow = utcNow ?? (() => DateTime.UtcNow);
			entries = comparer == null
				? new Dictionary<TKey, Entry>()
				: new Dictionary<TKey, Entry>(comparer);
		}

		/// <summary>Entries currently held, in flight or completed.</summary>
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
		/// Returns the cached value for <paramref name="key"/>, joins a read already in flight for
		/// it, or starts one.
		/// </summary>
		/// <param name="key">Cache key.</param>
		/// <param name="ttl">How long a completed read is served, from when it started.</param>
		/// <param name="fetch">The read. Called at most once per miss, outside the lock.</param>
		/// <param name="keep">
		/// Optional: whether a completed value may be cached. A rejected value is still returned to
		/// every caller that shared the read; it is simply not served to anyone after.
		/// </param>
		/// <returns>The value, or the read's exception.</returns>
		public Task<TValue> GetOrFetchAsync(TKey key, TimeSpan ttl, Func<Task<TValue>> fetch, Func<TValue, bool> keep = null)
		{
			if (fetch == null)
			{
				throw new ArgumentNullException(nameof(fetch));
			}

			Entry entry;
			lock (gate)
			{
				DateTime now = utcNow();
				if (entries.TryGetValue(key, out entry) && IsServable(entry, now, ttl))
				{
					return entry.Completion.Task;
				}

				entry = new Entry(now);
				entries[key] = entry;
				queue.AddLast(new QueueNode(key, entry));
			}

			_ = FillAsync(key, entry, fetch, keep);
			return entry.Completion.Task;
		}

		/// <summary>
		/// Drops the entry for one key. A read in flight still completes for the callers already
		/// sharing it; the next caller starts a new one.
		/// </summary>
		public void Invalidate(TKey key)
		{
			lock (gate)
			{
				entries.Remove(key);
			}
		}

		/// <summary>Drops every entry, with the same in-flight rule as <see cref="Invalidate"/>.</summary>
		public void Clear()
		{
			lock (gate)
			{
				entries.Clear();
				queue.Clear();
			}
		}

		/// <summary>
		/// Removes completed entries older than <paramref name="ttl"/>, oldest first, bounded.
		/// </summary>
		/// <remarks>
		/// Stops at the first entry that is still fresh or still in flight: entries are queued in
		/// start order, so everything behind it started later. A read in flight is always bounded
		/// by the database command timeout, so it cannot hold the sweep up for long.
		/// </remarks>
		/// <param name="ttl">Entries whose read started longer ago than this are removed.</param>
		/// <param name="maxScan">Maximum queue nodes to inspect.</param>
		/// <param name="maxRemove">Maximum entries to remove.</param>
		/// <returns>Entries removed.</returns>
		public int SweepExpired(TimeSpan ttl, int maxScan, int maxRemove)
		{
			if (maxScan <= 0 || maxRemove <= 0)
			{
				return 0;
			}

			lock (gate)
			{
				DateTime now = utcNow();
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
					QueueNode node = head.Value;

					// Superseded, invalidated or failed: the table no longer holds this node's entry.
					if (!entries.TryGetValue(node.Key, out Entry current) || !ReferenceEquals(current, node.Entry))
					{
						queue.RemoveFirst();
						continue;
					}

					if (!current.Completion.Task.IsCompleted || now - current.StartedAtUtc < ttl)
					{
						break;
					}

					entries.Remove(node.Key);
					queue.RemoveFirst();
					removed++;
				}

				return removed;
			}
		}

		/// <summary>
		/// In flight: join it. Completed successfully and young enough: serve it. Anything else —
		/// expired, or a failure whose removal has not run yet — is a miss.
		/// </summary>
		private static bool IsServable(Entry entry, DateTime now, TimeSpan ttl)
		{
			Task<TValue> task = entry.Completion.Task;
			if (!task.IsCompleted)
			{
				return true;
			}
			return task.Status == TaskStatus.RanToCompletion && now - entry.StartedAtUtc < ttl;
		}

		private async Task FillAsync(TKey key, Entry entry, Func<Task<TValue>> fetch, Func<TValue, bool> keep)
		{
			TValue value;
			try
			{
				value = await fetch().ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				// Removed BEFORE the task faults, so a caller that retries on the failure misses.
				Remove(key, entry);
				entry.Completion.TrySetException(ex);
				return;
			}

			if (keep != null)
			{
				bool kept;
				try
				{
					kept = keep(value);
				}
				catch (Exception ex)
				{
					Remove(key, entry);
					entry.Completion.TrySetException(ex);
					return;
				}
				if (!kept)
				{
					Remove(key, entry);
				}
			}
			entry.Completion.TrySetResult(value);
		}

		/// <summary>Removes the entry only if it is still the one in the table.</summary>
		private void Remove(TKey key, Entry entry)
		{
			lock (gate)
			{
				if (entries.TryGetValue(key, out Entry current) && ReferenceEquals(current, entry))
				{
					entries.Remove(key);
				}
			}
		}

		private sealed class Entry
		{
			public readonly DateTime StartedAtUtc;
			public readonly TaskCompletionSource<TValue> Completion =
				new TaskCompletionSource<TValue>(TaskCreationOptions.RunContinuationsAsynchronously);

			public Entry(DateTime startedAtUtc)
			{
				StartedAtUtc = startedAtUtc;
			}
		}

		private readonly struct QueueNode
		{
			public readonly TKey Key;
			public readonly Entry Entry;

			public QueueNode(TKey key, Entry entry)
			{
				Key = key;
				Entry = entry;
			}
		}
	}
}
