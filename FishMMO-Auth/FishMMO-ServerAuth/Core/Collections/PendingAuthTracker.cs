using System;
using System.Collections.Generic;
using FishMMO.Auth.Implementation;

namespace FishMMO.Auth.Core.Collections
{
	/// <summary>
	/// Connections that have completed their handshake and not yet authenticated, each in a
	/// <see cref="PendingAuthPhase"/>, with expiry swept head-first in the order they fall due.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Two lists, each in deadline order.</b> An authenticating entry falls due
	/// <see cref="PendingAuthRules.ProgressTtlSeconds"/> after its last progress, the same TTL for
	/// every entry, so the order of last progress is the order of expiry; a progress report moves
	/// its entry to the tail. An entry awaiting its second factor falls due one window after its
	/// prompt, again the same window for every entry, so the order of prompts is the order of
	/// expiry; a prompt moves its entry to the tail. The two durations differ, so the two kinds
	/// cannot share one list and stay ordered — they did share one, as a single TTL, which is why
	/// the two-factor prompt had fifteen seconds. <see cref="SweepOverdue"/> reads only the two
	/// heads and stops at the first entry of each that is still in time: O(removed + 1) whatever
	/// the number pending.
	/// </para>
	/// <para>
	/// The window is applied when the sweep reads it, not stamped on each entry, so changing
	/// <see cref="TwoFactorWindowSeconds"/> moves every prompt's deadline together and the list
	/// stays in order.
	/// </para>
	/// <para>
	/// <b>Time is monotonic seconds</b>, passed in by the caller: on the wall clock a step backwards
	/// stopped every entry from falling due until the clock caught up and the pending cap filled
	/// with connections nobody would purge. Stamps never go backwards either — a caller that read
	/// the clock just before another thread did can reach the lock second, and its stamp is raised
	/// to the latest one so the lists cannot fall out of order.
	/// </para>
	/// <para>
	/// Thread-safe. Every operation takes one short lock and calls nothing outside this class, so
	/// the lock can be taken under no other and never waits on disconnect or database work. Entries
	/// are keyed by client ID; operations that act for a specific connection check it is the same
	/// connection, so a late call for a connection whose ID was recycled cannot touch its successor.
	/// </para>
	/// </remarks>
	/// <typeparam name="TConnection">The connection type.</typeparam>
	public sealed class PendingAuthTracker<TConnection>
	{
		private readonly object gate = new object();
		private readonly Dictionary<int, LinkedListNode<Entry>> entries = new Dictionary<int, LinkedListNode<Entry>>();
		private readonly LinkedList<Entry> authenticating = new LinkedList<Entry>();
		private readonly LinkedList<Entry> awaitingTwoFactor = new LinkedList<Entry>();
		private readonly double progressTtlSeconds;
		private readonly double authenticatingCapSeconds;
		private double twoFactorWindowSeconds;
		private double latestStampSeconds = double.MinValue;

		/// <summary>
		/// Initializes an empty tracker.
		/// </summary>
		/// <param name="progressTtlSeconds">See <see cref="PendingAuthRules.ProgressTtlSeconds"/>. Must be positive.</param>
		/// <param name="authenticatingCapSeconds">See <see cref="PendingAuthRules.AuthenticatingCapSeconds"/>. Must be positive.</param>
		/// <param name="twoFactorWindowSeconds">Seconds a player has to answer one two-factor prompt. Must be positive.</param>
		public PendingAuthTracker(double progressTtlSeconds, double authenticatingCapSeconds, double twoFactorWindowSeconds)
		{
			if (!(progressTtlSeconds > 0)) throw new ArgumentOutOfRangeException(nameof(progressTtlSeconds), "Must be positive.");
			if (!(authenticatingCapSeconds > 0)) throw new ArgumentOutOfRangeException(nameof(authenticatingCapSeconds), "Must be positive.");
			if (!(twoFactorWindowSeconds > 0)) throw new ArgumentOutOfRangeException(nameof(twoFactorWindowSeconds), "Must be positive.");
			this.progressTtlSeconds = progressTtlSeconds;
			this.authenticatingCapSeconds = authenticatingCapSeconds;
			this.twoFactorWindowSeconds = twoFactorWindowSeconds;
		}

		/// <summary>
		/// Seconds a player has to answer one two-factor prompt. Takes effect for every prompt at
		/// once, including those already on screen.
		/// </summary>
		public double TwoFactorWindowSeconds
		{
			get
			{
				lock (gate)
				{
					return twoFactorWindowSeconds;
				}
			}
			set
			{
				if (!(value > 0)) throw new ArgumentOutOfRangeException(nameof(value), "Must be positive.");
				lock (gate)
				{
					twoFactorWindowSeconds = value;
				}
			}
		}

		/// <summary>Connections currently pending, in either phase.</summary>
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
		/// Starts tracking a connection whose handshake has just completed, in
		/// <see cref="PendingAuthPhase.Authenticating"/>.
		/// </summary>
		/// <remarks>
		/// A client ID already tracked is restarted from scratch with the new connection: a fresh
		/// handshake on it already holds a slot, so the capacity does not apply.
		/// </remarks>
		/// <param name="clientId">The connection's client ID.</param>
		/// <param name="connection">The connection.</param>
		/// <param name="nowSeconds">Current monotonic time in seconds.</param>
		/// <param name="capacity">Most connections that may be authenticating at once (see <see cref="PendingAuthRules.AdmitsNewPending"/>).</param>
		/// <returns><c>false</c> when the cap or the ceiling is reached.</returns>
		public bool TryStart(int clientId, TConnection connection, double nowSeconds, int capacity)
		{
			lock (gate)
			{
				if (entries.TryGetValue(clientId, out LinkedListNode<Entry>? existing))
				{
					existing.List?.Remove(existing);
					entries.Remove(clientId);
				}
				else if (!PendingAuthRules.AdmitsNewPending(authenticating.Count, entries.Count, capacity))
				{
					// The cap counts machine work only; prompts have a looser ceiling. See the rule.
					return false;
				}

				double stamp = Stamp(nowSeconds);
				entries[clientId] = authenticating.AddLast(new Entry(clientId, connection, stamp));
				return true;
			}
		}

		/// <summary>
		/// Records progress on an authenticating connection, restarting its progress TTL.
		/// </summary>
		/// <remarks>
		/// Ignored for a connection that is not tracked, that has been authenticating for
		/// <see cref="PendingAuthRules.AuthenticatingCapSeconds"/>, or that is awaiting its second
		/// factor (see <see cref="PendingAuthRules.MayExtend"/>).
		/// </remarks>
		/// <param name="clientId">The connection's client ID.</param>
		/// <param name="nowSeconds">Current monotonic time in seconds.</param>
		/// <returns><c>true</c> when the TTL was restarted.</returns>
		public bool ReportProgress(int clientId, double nowSeconds)
		{
			lock (gate)
			{
				if (!entries.TryGetValue(clientId, out LinkedListNode<Entry>? node))
				{
					return false;
				}

				Entry entry = node.Value;
				double stamp = Stamp(nowSeconds);
				if (!PendingAuthRules.MayExtend(entry.Phase, entry.PhaseStartedSeconds, stamp, authenticatingCapSeconds))
				{
					return false;
				}

				entry.LastProgressSeconds = stamp;
				authenticating.Remove(node);
				authenticating.AddLast(node);
				return true;
			}
		}

		/// <summary>
		/// Puts a connection on a two-factor prompt: <see cref="PendingAuthPhase.AwaitingTwoFactor"/>
		/// with a full window starting now.
		/// </summary>
		/// <remarks>
		/// Called for every prompt, the first and each re-prompt after a counted wrong code, so each
		/// one gets the whole window.
		/// </remarks>
		/// <param name="clientId">The connection's client ID.</param>
		/// <param name="connection">The connection being prompted.</param>
		/// <param name="nowSeconds">Current monotonic time in seconds.</param>
		/// <returns><c>false</c> when the connection is not tracked (it is being dropped).</returns>
		public bool BeginAwaitingTwoFactor(int clientId, TConnection connection, double nowSeconds)
		{
			lock (gate)
			{
				if (!TryGetSame(clientId, connection, out LinkedListNode<Entry> node))
				{
					return false;
				}

				double stamp = Stamp(nowSeconds);
				Entry entry = node.Value;
				entry.Phase = PendingAuthPhase.AwaitingTwoFactor;
				entry.PhaseStartedSeconds = stamp;
				entry.LastProgressSeconds = stamp;
				node.List?.Remove(node);
				awaitingTwoFactor.AddLast(node);
				return true;
			}
		}

		/// <summary>
		/// Starts a fresh <see cref="PendingAuthPhase.Authenticating"/> phase for a connection, as
		/// when the player's two-factor code arrives and the servers begin checking it.
		/// </summary>
		/// <param name="clientId">The connection's client ID.</param>
		/// <param name="connection">The connection.</param>
		/// <param name="nowSeconds">Current monotonic time in seconds.</param>
		/// <returns><c>false</c> when the connection is not tracked.</returns>
		public bool ResumeAuthenticating(int clientId, TConnection connection, double nowSeconds)
		{
			lock (gate)
			{
				if (!TryGetSame(clientId, connection, out LinkedListNode<Entry> node))
				{
					return false;
				}

				double stamp = Stamp(nowSeconds);
				Entry entry = node.Value;
				entry.Phase = PendingAuthPhase.Authenticating;
				entry.PhaseStartedSeconds = stamp;
				entry.LastProgressSeconds = stamp;
				node.List?.Remove(node);
				authenticating.AddLast(node);
				return true;
			}
		}

		/// <summary>
		/// The phase a connection is in, when it is pending.
		/// </summary>
		/// <param name="clientId">The connection's client ID.</param>
		/// <param name="connection">The connection; an entry for a different one under the same ID does not count.</param>
		/// <param name="phase">Its phase, when tracked.</param>
		/// <returns><c>true</c> when <paramref name="connection"/> is pending.</returns>
		public bool TryGetPhase(int clientId, TConnection connection, out PendingAuthPhase phase)
		{
			lock (gate)
			{
				if (TryGetSame(clientId, connection, out LinkedListNode<Entry> node))
				{
					phase = node.Value.Phase;
					return true;
				}
				phase = default;
				return false;
			}
		}

		/// <summary>Stops tracking a client ID, whichever connection holds it.</summary>
		/// <returns><c>true</c> when it was tracked.</returns>
		public bool Remove(int clientId)
		{
			lock (gate)
			{
				if (!entries.TryGetValue(clientId, out LinkedListNode<Entry>? node))
				{
					return false;
				}
				entries.Remove(clientId);
				node.List?.Remove(node);
				return true;
			}
		}

		/// <summary>Stops tracking <paramref name="connection"/>, leaving a successor on a recycled ID alone.</summary>
		/// <returns><c>true</c> when it was tracked.</returns>
		public bool Remove(int clientId, TConnection connection)
		{
			lock (gate)
			{
				if (!TryGetSame(clientId, connection, out LinkedListNode<Entry> node))
				{
					return false;
				}
				entries.Remove(clientId);
				node.List?.Remove(node);
				return true;
			}
		}

		/// <summary>Stops tracking everything.</summary>
		public void Clear()
		{
			lock (gate)
			{
				entries.Clear();
				authenticating.Clear();
				awaitingTwoFactor.Clear();
			}
		}

		/// <summary>
		/// Takes overdue connections out of tracking, oldest first, stopping at the first entry of
		/// each phase that is still in time.
		/// </summary>
		/// <remarks>
		/// Each connection comes back with the phase it ran out of time in, because the two mean
		/// different things to the player. A stalled exchange is dropped without a word, as any
		/// other stall is; an unanswered two-factor prompt is a person who was asked for something
		/// and is owed an answer saying the question has closed (<c>TwoFactorExpired</c>).
		/// </remarks>
		/// <param name="nowSeconds">Current monotonic time in seconds.</param>
		/// <param name="maxRemovals">Most connections to take in this call; the rest stay at the heads.</param>
		/// <param name="removed">
		/// Receives the connections taken and the phase each was in, for the caller to answer, purge
		/// and disconnect outside the lock.
		/// </param>
		/// <returns>Connections taken.</returns>
		public int SweepOverdue(double nowSeconds, int maxRemovals, List<(TConnection Connection, PendingAuthPhase Phase)> removed)
		{
			if (maxRemovals <= 0 || removed == null)
			{
				return 0;
			}

			int taken = 0;
			lock (gate)
			{
				while (taken < maxRemovals)
				{
					LinkedListNode<Entry>? head = authenticating.First;
					if (head != null && IsOverdueLocked(head.Value, nowSeconds))
					{
						Take(head, removed);
						taken++;
						continue;
					}

					head = awaitingTwoFactor.First;
					if (head != null && IsOverdueLocked(head.Value, nowSeconds))
					{
						Take(head, removed);
						taken++;
						continue;
					}

					break;
				}
			}
			return taken;
		}

		private bool IsOverdueLocked(Entry entry, double nowSeconds) =>
			PendingAuthRules.IsOverdue(entry.Phase, entry.PhaseStartedSeconds, entry.LastProgressSeconds, nowSeconds,
				progressTtlSeconds, twoFactorWindowSeconds);

		private void Take(LinkedListNode<Entry> node, List<(TConnection Connection, PendingAuthPhase Phase)> removed)
		{
			node.List!.Remove(node);
			entries.Remove(node.Value.ClientId);
			removed.Add((node.Value.Connection, node.Value.Phase));
		}

		private bool TryGetSame(int clientId, TConnection connection, out LinkedListNode<Entry> node)
		{
			if (entries.TryGetValue(clientId, out node!) && ConnectionIdentity.Same(node.Value.Connection, connection))
			{
				return true;
			}
			node = null!;
			return false;
		}

		/// <summary>The time to stamp an entry with: <paramref name="nowSeconds"/>, never earlier than any stamp before it.</summary>
		private double Stamp(double nowSeconds)
		{
			if (nowSeconds > latestStampSeconds)
			{
				latestStampSeconds = nowSeconds;
			}
			return latestStampSeconds;
		}

		private sealed class Entry
		{
			public readonly int ClientId;
			public readonly TConnection Connection;
			public PendingAuthPhase Phase;
			/// <summary>When the current phase began: tracking start, a code's arrival, or the prompt.</summary>
			public double PhaseStartedSeconds;
			/// <summary>Last progress report, or the phase start.</summary>
			public double LastProgressSeconds;

			public Entry(int clientId, TConnection connection, double nowSeconds)
			{
				ClientId = clientId;
				Connection = connection;
				Phase = PendingAuthPhase.Authenticating;
				PhaseStartedSeconds = nowSeconds;
				LastProgressSeconds = nowSeconds;
			}
		}
	}
}
