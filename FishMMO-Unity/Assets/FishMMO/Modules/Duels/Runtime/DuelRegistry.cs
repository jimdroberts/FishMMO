using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Who is duelling whom, and whether the fight has started: the one fact the harm rule, the alliance and the
	/// finishing blow need, held the same way on the server and on each duellist's client.
	/// </summary>
	/// <remarks>
	/// The server publishes a pair when a duel is agreed and marks it live when the countdown ends; each duellist's client
	/// publishes the same from the duel's own messages, so its predicted hits, its targeting and its finishing-blow clamp
	/// agree with the server. Keyed by character id; an entry exists for both sides of a duel. Main thread only.
	/// </remarks>
	public static class DuelRegistry
	{
		private sealed class Entry
		{
			public long DuelID;
			public long First;
			public long Second;
			public bool Live;
		}

		private static readonly Dictionary<long, Entry> byCharacter = new Dictionary<long, Entry>();

		/// <summary>
		/// A duellist's blow left the other on their last point of health (the harm rule spared it): winner, loser. Raised
		/// wherever the blow is applied; the server ends the duel on it.
		/// </summary>
		public static event Action<long, long> OnFinishingBlow;

		/// <summary>Characters in a duel (both sides counted).</summary>
		public static int Count => byCharacter.Count;

		/// <summary>Records a duel between two characters, replacing any either was in.</summary>
		public static void Publish(long duelID, long first, long second, bool live)
		{
			if (first <= 0 || second <= 0 || first == second)
			{
				return;
			}
			RemoveCharacter(first);
			RemoveCharacter(second);
			Entry entry = new Entry { DuelID = duelID, First = first, Second = second, Live = live };
			byCharacter[first] = entry;
			byCharacter[second] = entry;
		}

		/// <summary>Starts or stops the fight of a duel.</summary>
		public static void SetLive(long duelID, bool live)
		{
			foreach (Entry entry in byCharacter.Values)
			{
				if (entry.DuelID == duelID)
				{
					entry.Live = live;
					return;
				}
			}
		}

		/// <summary>Forgets a duel.</summary>
		public static void Remove(long duelID)
		{
			long first = 0, second = 0;
			foreach (Entry entry in byCharacter.Values)
			{
				if (entry.DuelID == duelID)
				{
					first = entry.First;
					second = entry.Second;
					break;
				}
			}
			if (first != 0)
			{
				byCharacter.Remove(first);
				byCharacter.Remove(second);
			}
		}

		/// <summary>Forgets the duel a character is in, if any.</summary>
		public static void RemoveCharacter(long characterID)
		{
			if (byCharacter.TryGetValue(characterID, out Entry entry))
			{
				byCharacter.Remove(entry.First);
				byCharacter.Remove(entry.Second);
			}
		}

		/// <summary>Forgets every duel. For server restarts, tests and the client leaving the world.</summary>
		public static void Clear()
		{
			byCharacter.Clear();
		}

		/// <summary>The opponent of a character in a duel, and whether they are fighting yet.</summary>
		public static bool TryGetOpponent(long characterID, out long opponentID, out bool live)
		{
			if (characterID > 0 && byCharacter.TryGetValue(characterID, out Entry entry))
			{
				opponentID = entry.First == characterID ? entry.Second : entry.First;
				live = entry.Live;
				return true;
			}
			opponentID = 0;
			live = false;
			return false;
		}

		/// <summary>Whether two characters are each other's duel opponents, and whether that duel is being fought.</summary>
		public static bool ArePaired(long a, long b, out bool live)
		{
			if (TryGetOpponent(a, out long opponent, out live) && opponent == b)
			{
				return true;
			}
			live = false;
			return false;
		}

		/// <summary>Raises <see cref="OnFinishingBlow"/>.</summary>
		internal static void RaiseFinishingBlow(long winnerID, long loserID)
		{
			OnFinishingBlow?.Invoke(winnerID, loserID);
		}
	}
}
