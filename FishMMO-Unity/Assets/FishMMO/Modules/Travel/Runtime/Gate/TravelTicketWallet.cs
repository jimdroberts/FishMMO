using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The travel tickets a client carries between scene servers (issue #241): kept for the client process's lifetime until each
	/// expires, per character, and presented on every scene server the character loads into (<see cref="GateController"/>).
	/// </summary>
	/// <remarks>
	/// Not saved: a crashed client loses its tickets, which costs only the way home through a lit Gate Stone. The bytes are
	/// opaque here; only a server can read and verify them.
	/// </remarks>
	public static class TravelTicketWallet
	{
		/// <summary>Most tickets kept per character; the oldest goes first.</summary>
		public const int MaxPerCharacter = 6;

		private struct Entry
		{
			public byte[] Bytes;
			public double ExpiresAt;
		}

		private static readonly Dictionary<long, List<Entry>> byCharacter = new Dictionary<long, List<Entry>>();

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			byCharacter.Clear();
		}

		/// <summary>Keeps <paramref name="ticket"/> for <paramref name="characterID"/> for <paramref name="seconds"/>.</summary>
		public static void Keep(long characterID, byte[] ticket, float seconds)
		{
			if (ticket == null || ticket.Length == 0 || ticket.Length > TravelTicket.MaxBytes || seconds <= 0.0f)
			{
				return;
			}
			if (!byCharacter.TryGetValue(characterID, out List<Entry> list))
			{
				list = new List<Entry>();
				byCharacter[characterID] = list;
			}
			Prune(list, Time.realtimeSinceStartupAsDouble);
			while (list.Count >= MaxPerCharacter)
			{
				list.RemoveAt(0);
			}
			list.Add(new Entry() { Bytes = ticket, ExpiresAt = Time.realtimeSinceStartupAsDouble + seconds });
		}

		/// <summary>The unexpired tickets of <paramref name="characterID"/>, oldest first.</summary>
		public static List<byte[]> Live(long characterID)
		{
			var result = new List<byte[]>();
			if (byCharacter.TryGetValue(characterID, out List<Entry> list))
			{
				Prune(list, Time.realtimeSinceStartupAsDouble);
				for (int i = 0; i < list.Count; ++i)
				{
					result.Add(list[i].Bytes);
				}
			}
			return result;
		}

		private static void Prune(List<Entry> list, double now)
		{
			list.RemoveAll(e => e.ExpiresAt < now);
		}
	}
}
