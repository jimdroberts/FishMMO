using System.Collections.Generic;

namespace FishMMO.Client
{
	/// <summary>
	/// Tracks which slots of one item container have a request outstanding with the server.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The item panels submit a request and then do nothing until the server's echo arrives. That
	/// leaves a window — one round trip, which on a bad connection is a long time — in which the
	/// slot still looks exactly as it did, so a second click submits the same operation again. Two
	/// equips of the same inventory slot, or two deposits of the same stack, is not a cosmetic
	/// problem: the server processes both, and what the second one does depends entirely on what
	/// the first one left behind.
	/// </para>
	/// <para>
	/// Each outstanding slot gets its own <see cref="PendingReplyGuard"/> — the same watchdog the
	/// four login panels use — so a request whose reply never arrives hands the slot back instead
	/// of leaving it dead for the rest of the session. The reply can go missing for reasons the
	/// client cannot see: a handler that returned before broadcasting, a dropped packet on an
	/// unreliable path, a scene handover. The guard does not care which.
	/// </para>
	/// <para>
	/// Guards are keyed by slot and kept after release rather than removed, because a player
	/// working out of one bag drags the same handful of slots over and over and re-allocating a
	/// guard per click would churn for no reason. <see cref="Count"/> therefore counts guards, not
	/// pending slots; <see cref="HasAnyPending"/> is the question worth asking.
	/// </para>
	/// </remarks>
	public sealed class ItemSlotPendingSet
	{
		/// <summary>
		/// How long an item operation may go unanswered before the slot is handed back.
		/// </summary>
		/// <remarks>
		/// Much shorter than <see cref="PendingReplyGuard.DefaultTimeoutSeconds"/>, and
		/// deliberately so. The login panels wait on a database round trip that a human has
		/// already been told to expect; an item click is a reflex, and a slot that stays locked
		/// for half a minute after a dropped reply reads as the game being broken. Still far
		/// longer than any healthy round trip, so a merely slow server is never mistaken for a
		/// silent one.
		/// </remarks>
		public const float ItemOperationTimeoutSeconds = 8.0f;

		/// <summary>Guards keyed by slot index. A guard may exist and not be pending.</summary>
		private readonly Dictionary<int, PendingReplyGuard> guards = new Dictionary<int, PendingReplyGuard>();

		/// <summary>Reused between <see cref="CollectExpired"/> calls so the tick allocates nothing.</summary>
		private readonly List<int> expired = new List<int>();

		/// <summary>Number of slots that currently have a request outstanding.</summary>
		public int PendingCount { get; private set; }

		/// <summary>True while any slot in this container is waiting on the server.</summary>
		public bool HasAnyPending => PendingCount > 0;

		/// <summary>
		/// Reports whether a request is outstanding on <paramref name="slot"/>.
		/// </summary>
		/// <param name="slot">Container slot index.</param>
		/// <returns>True while the slot is waiting on a server reply.</returns>
		public bool IsPending(int slot)
		{
			return guards.TryGetValue(slot, out PendingReplyGuard guard) && guard.IsPending;
		}

		/// <summary>
		/// Claims <paramref name="slot"/> for a request that is about to be sent.
		/// </summary>
		/// <remarks>
		/// This is the double-submit guard itself: it returns false when the slot already has a
		/// request in flight, and the caller must then send nothing at all. Returning false rather
		/// than replacing the existing wait matters — replacing it would reset the watchdog on
		/// every click, so a player clicking a stuck slot repeatedly would never let it time out.
		/// </remarks>
		/// <param name="slot">Container slot index.</param>
		/// <param name="timeoutSeconds">Seconds to wait for the reply.</param>
		/// <returns>True when the slot was free and is now claimed.</returns>
		public bool TryBegin(int slot, float timeoutSeconds = ItemOperationTimeoutSeconds)
		{
			if (slot < 0)
			{
				return false;
			}

			if (!guards.TryGetValue(slot, out PendingReplyGuard guard))
			{
				guard = new PendingReplyGuard();
				guards[slot] = guard;
			}

			if (guard.IsPending)
			{
				return false;
			}

			guard.Begin(timeoutSeconds);
			++PendingCount;
			return true;
		}

		/// <summary>
		/// Ends the wait on <paramref name="slot"/>.
		/// </summary>
		/// <param name="slot">Container slot index.</param>
		/// <returns>True when the slot had been waiting, so the caller knows to repaint it.</returns>
		public bool Release(int slot)
		{
			if (!guards.TryGetValue(slot, out PendingReplyGuard guard) || !guard.IsPending)
			{
				return false;
			}

			guard.Clear();
			--PendingCount;
			return true;
		}

		/// <summary>
		/// Ends every outstanding wait, reporting the slots that were released.
		/// </summary>
		/// <remarks>
		/// Used by every teardown path — panel close, character change, quit to login, destroy.
		/// A lock that outlives the panel it belongs to is worse than no lock at all: the slot is
		/// rebuilt from the container looking normal, but the set still refuses the next click.
		/// </remarks>
		/// <param name="releasedInto">Receives the released slot indices. May be null.</param>
		public void ReleaseAll(List<int> releasedInto = null)
		{
			if (PendingCount < 1)
			{
				return;
			}

			foreach (KeyValuePair<int, PendingReplyGuard> pair in guards)
			{
				if (!pair.Value.IsPending)
				{
					continue;
				}
				pair.Value.Clear();
				releasedInto?.Add(pair.Key);
			}
			PendingCount = 0;
		}

		/// <summary>
		/// Collects the slots whose wait has just expired.
		/// </summary>
		/// <remarks>
		/// <see cref="PendingReplyGuard.HasExpired"/> is self-clearing and reports true exactly
		/// once per wait, so this can be driven straight from a per-frame tick and each slot is
		/// handed back once. The returned list is reused; copy it if you need to keep it.
		/// </remarks>
		/// <returns>Slots released by timeout on this call. Empty when none expired.</returns>
		public List<int> CollectExpired()
		{
			expired.Clear();

			if (PendingCount < 1)
			{
				return expired;
			}

			foreach (KeyValuePair<int, PendingReplyGuard> pair in guards)
			{
				if (pair.Value.HasExpired())
				{
					expired.Add(pair.Key);
				}
			}

			PendingCount -= expired.Count;
			if (PendingCount < 0)
			{
				PendingCount = 0;
			}
			return expired;
		}
	}
}
