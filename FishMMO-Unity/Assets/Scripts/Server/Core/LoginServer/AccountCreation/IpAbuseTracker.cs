using System;
using System.Collections.Generic;

namespace FishMMO.Server.Core.LoginServer
{
	/// <summary>
	/// Per-IP account-creation abuse state: the rate limit on creation attempts and the failure
	/// count that blocks an IP, in one record per IP, expired head-first in activity order.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The rules, in one place.</b> An IP may begin a creation attempt no more often than the
	/// rate limit allows. Failures count against it; at the failure threshold it is blocked, and a
	/// blocked or rate-limited attempt changes nothing — so a blocked IP cannot keep its own block
	/// alive by hammering it. An IP's record, and with it any block, lapses once the retention
	/// period (the configured block duration) has passed since its last accepted attempt or
	/// recorded failure. A success clears the failure count.
	/// </para>
	/// <para>
	/// <b>What this replaced.</b> Two ConcurrentDictionaries — last attempt per IP and failures
	/// per IP — swept once a minute by enumerating a few hundred entries from each dictionary's
	/// head. Enumeration always starts in the same place, so once those entries were live the
	/// sweep never reached anything behind them: roughly 128 entries a minute were reclaimed and
	/// the rest leaked until the failure tracker reached its 50,000-entry cap, after which new
	/// IPs' failures went untracked and the IP block stopped working. A failure recorded by the
	/// verification path, which creates no attempt record, was also dropped by whichever sweep
	/// first reached it — anywhere from under a minute to never — instead of lasting the block
	/// duration. Here records are kept in the order of their last activity, so the oldest is
	/// always at the head and a sweep stops at the first live one.
	/// </para>
	/// <para>
	/// <b>Time is monotonic seconds</b> (<see cref="MonotonicClock.NowSeconds"/>): the rate limit
	/// and the block are local durations, and on the wall clock an NTP step would lift every block
	/// at once or hold them all for the length of the step.
	/// </para>
	/// <para>
	/// Thread-safe: ingress runs on the main thread and failures are recorded from async
	/// workers. Every operation takes one short lock.
	/// </para>
	/// </remarks>
	public sealed class IpAbuseTracker
	{
		/// <summary>What <see cref="TryBeginAttempt"/> decided.</summary>
		public enum AttemptVerdict
		{
			/// <summary>The attempt may proceed; it has been recorded.</summary>
			Accepted = 0,
			/// <summary>Too soon after this IP's last accepted attempt. Nothing was recorded.</summary>
			RateLimited = 1,
			/// <summary>The IP is blocked for failures. Nothing was recorded.</summary>
			Blocked = 2,
		}

		private sealed class Record
		{
			public readonly string Ip;
			/// <summary>Last accepted creation attempt (monotonic seconds), or null when there has been none.</summary>
			public double? LastAttemptSeconds;
			/// <summary>Failures since the record opened or its last success.</summary>
			public int Failures;
			/// <summary>Last accepted attempt or recorded failure (monotonic seconds); retention counts from here.</summary>
			public double LastActivitySeconds;

			public Record(string ip, double nowSeconds)
			{
				Ip = ip;
				LastActivitySeconds = nowSeconds;
			}
		}

		private readonly object gate = new object();
		private readonly Dictionary<string, LinkedListNode<Record>> records;
		private readonly LinkedList<Record> byActivity = new LinkedList<Record>();
		private int failingCount;

		/// <summary>
		/// Initializes an empty tracker. IPs are compared ordinally: callers pass them normalized.
		/// </summary>
		public IpAbuseTracker()
		{
			records = new Dictionary<string, LinkedListNode<Record>>(StringComparer.Ordinal);
		}

		/// <summary>IPs currently held, including any past retention that no sweep has reached yet.</summary>
		public int Count
		{
			get
			{
				lock (gate)
				{
					return records.Count;
				}
			}
		}

		/// <summary>IPs currently held with at least one failure.</summary>
		public int FailingCount
		{
			get
			{
				lock (gate)
				{
					return failingCount;
				}
			}
		}

		/// <summary>
		/// Decides whether <paramref name="ip"/> may begin a creation attempt now, and records it
		/// when it may.
		/// </summary>
		/// <param name="ip">Normalized client IP.</param>
		/// <param name="nowSeconds">Current monotonic time in seconds.</param>
		/// <param name="rateLimit">Minimum time between accepted attempts from one IP.</param>
		/// <param name="maxFailures">Failures at which the IP is blocked.</param>
		/// <param name="retention">How long a record lasts after its last activity (the block duration).</param>
		public AttemptVerdict TryBeginAttempt(string ip, double nowSeconds, TimeSpan rateLimit, int maxFailures, TimeSpan retention)
		{
			if (string.IsNullOrEmpty(ip))
			{
				return AttemptVerdict.Accepted;
			}

			lock (gate)
			{
				LinkedListNode<Record> node = GetLiveLocked(ip, nowSeconds, retention);
				if (node != null)
				{
					Record record = node.Value;
					if (IsBlocked(record.Failures, maxFailures))
					{
						return AttemptVerdict.Blocked;
					}
					if (record.LastAttemptSeconds.HasValue && nowSeconds - record.LastAttemptSeconds.Value < rateLimit.TotalSeconds)
					{
						return AttemptVerdict.RateLimited;
					}
					record.LastAttemptSeconds = nowSeconds;
					TouchLocked(node, nowSeconds);
					return AttemptVerdict.Accepted;
				}

				Record created = new Record(ip, nowSeconds) { LastAttemptSeconds = nowSeconds };
				records[ip] = byActivity.AddLast(created);
				return AttemptVerdict.Accepted;
			}
		}

		/// <summary>Whether <paramref name="ip"/> is currently blocked for failures.</summary>
		public bool IsBlocked(string ip, double nowSeconds, int maxFailures, TimeSpan retention)
		{
			if (string.IsNullOrEmpty(ip))
			{
				return false;
			}

			lock (gate)
			{
				LinkedListNode<Record> node = GetLiveLocked(ip, nowSeconds, retention);
				return node != null && IsBlocked(node.Value.Failures, maxFailures);
			}
		}

		/// <summary>
		/// Records a failure against <paramref name="ip"/>.
		/// </summary>
		/// <param name="ip">Normalized client IP.</param>
		/// <param name="nowSeconds">Current monotonic time in seconds.</param>
		/// <param name="retention">How long a record lasts after its last activity.</param>
		/// <param name="maxFailingIps">
		/// Most IPs that may hold failures at once. An IP that already has failures is always
		/// counted; only a new one is refused at the cap.
		/// </param>
		/// <returns>
		/// <c>false</c> when the IP could not be tracked because the cap was reached. Callers fail
		/// closed on that: ignoring the failure instead would let an attacker who filled the
		/// tracker stay just under the block threshold indefinitely.
		/// </returns>
		public bool TryRecordFailure(string ip, double nowSeconds, TimeSpan retention, int maxFailingIps)
		{
			if (string.IsNullOrEmpty(ip))
			{
				return true;
			}

			lock (gate)
			{
				LinkedListNode<Record> node = GetLiveLocked(ip, nowSeconds, retention);
				bool newlyFailing = node == null || node.Value.Failures == 0;
				if (newlyFailing && failingCount >= maxFailingIps)
				{
					// Reclaim records past retention before refusing: they are not live failures.
					SweepLocked(nowSeconds, retention, int.MaxValue);
					node = GetLiveLocked(ip, nowSeconds, retention);
					if (failingCount >= maxFailingIps)
					{
						return false;
					}
				}

				if (node == null)
				{
					node = byActivity.AddLast(new Record(ip, nowSeconds));
					records[ip] = node;
				}
				else
				{
					TouchLocked(node, nowSeconds);
				}

				if (node.Value.Failures == 0)
				{
					failingCount++;
				}
				node.Value.Failures++;
				return true;
			}
		}

		/// <summary>
		/// Clears <paramref name="ip"/>'s failure count after a success. The attempt record stays,
		/// so the rate limit still applies.
		/// </summary>
		public void ClearFailures(string ip)
		{
			if (string.IsNullOrEmpty(ip))
			{
				return;
			}

			lock (gate)
			{
				if (records.TryGetValue(ip, out LinkedListNode<Record> node) && node.Value.Failures > 0)
				{
					node.Value.Failures = 0;
					failingCount--;
				}
			}
		}

		/// <summary>
		/// Removes records past retention, oldest activity first, stopping at the first live one.
		/// </summary>
		/// <returns>Records removed.</returns>
		public int SweepExpired(double nowSeconds, TimeSpan retention, int maxRemove)
		{
			if (maxRemove <= 0)
			{
				return 0;
			}

			lock (gate)
			{
				return SweepLocked(nowSeconds, retention, maxRemove);
			}
		}

		/// <summary>Drops every record.</summary>
		public void Clear()
		{
			lock (gate)
			{
				records.Clear();
				byActivity.Clear();
				failingCount = 0;
			}
		}

		/// <summary>The block rule: blocked at <paramref name="maxFailures"/> failures or more.</summary>
		public static bool IsBlocked(int failures, int maxFailures) => failures >= Math.Max(1, maxFailures);

		/// <summary>Whether a record last active at <paramref name="lastActivitySeconds"/> has lapsed at <paramref name="nowSeconds"/>.</summary>
		public static bool IsLapsed(double lastActivitySeconds, double nowSeconds, TimeSpan retention) => nowSeconds - lastActivitySeconds >= retention.TotalSeconds;

		private LinkedListNode<Record> GetLiveLocked(string ip, double nowSeconds, TimeSpan retention)
		{
			if (!records.TryGetValue(ip, out LinkedListNode<Record> node))
			{
				return null;
			}
			if (!IsLapsed(node.Value.LastActivitySeconds, nowSeconds, retention))
			{
				return node;
			}

			// Lapsed but not yet swept: it no longer exists as far as any rule is concerned.
			RemoveLocked(node);
			return null;
		}

		private void TouchLocked(LinkedListNode<Record> node, double nowSeconds)
		{
			node.Value.LastActivitySeconds = nowSeconds;
			byActivity.Remove(node);
			byActivity.AddLast(node);
		}

		private void RemoveLocked(LinkedListNode<Record> node)
		{
			if (node.Value.Failures > 0)
			{
				failingCount--;
			}
			records.Remove(node.Value.Ip);
			byActivity.Remove(node);
		}

		private int SweepLocked(double nowSeconds, TimeSpan retention, int maxRemove)
		{
			int removed = 0;
			while (removed < maxRemove)
			{
				LinkedListNode<Record> head = byActivity.First;
				if (head == null || !IsLapsed(head.Value.LastActivitySeconds, nowSeconds, retention))
				{
					break;
				}
				RemoveLocked(head);
				removed++;
			}
			return removed;
		}
	}
}
