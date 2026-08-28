using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Merges the combat events one character receives within a tick into a bounded set.
	/// </summary>
	/// <remarks>
	/// <para>
	/// One <see cref="CombatEventBroadcast"/> per hit is fine for a duel and a flood for an area
	/// effect: twenty projectiles landing on one creature in one tick would be twenty messages to
	/// every observer of that creature, and every one of them would spawn its own label on top of
	/// the last. Hits from the same source, of the same kind and damage type, are therefore summed
	/// into one entry; what a client shows for them is one number, which is also what a player
	/// can actually read.
	/// </para>
	/// <para>
	/// Bounded at <see cref="MaxEntries"/> distinct entries per flush. Past the bound, further
	/// hits are folded into an anonymous entry (source 0) for their kind and type so the total is
	/// still right and the message count is not. Pure C# so the merge rules are unit tested.
	/// </para>
	/// </remarks>
	public sealed class CombatEventCoalescer
	{
		/// <summary>Distinct (source, kind, type) entries kept per flush before folding.</summary>
		public const int MaxEntries = 8;

		/// <summary>One merged entry.</summary>
		public struct Entry
		{
			/// <summary>NetworkObject id of the source, or 0 when unknown or folded.</summary>
			public int SourceObjectID;
			/// <summary>What happened.</summary>
			public CombatEventKind Kind;
			/// <summary>Damage template id for damage, 0 otherwise.</summary>
			public int DamageTemplateID;
			/// <summary>Summed amount.</summary>
			public int Amount;
		}

		private readonly List<Entry> entries = new List<Entry>(MaxEntries);

		/// <summary>Number of merged entries waiting to be flushed.</summary>
		public int Count => entries.Count;

		/// <summary>
		/// Records one event, merging it into an existing entry where the key matches.
		/// </summary>
		/// <param name="sourceObjectID">NetworkObject id of the source, or 0.</param>
		/// <param name="kind">What happened.</param>
		/// <param name="damageTemplateID">Damage template id, or 0 for a heal.</param>
		/// <param name="amount">The amount. Non-positive amounts are ignored: they produce no label.</param>
		public void Add(int sourceObjectID, CombatEventKind kind, int damageTemplateID, int amount)
		{
			if (amount <= 0)
			{
				return;
			}

			// Heals carry no damage type; normalising it here keeps the merge key honest.
			if (kind != CombatEventKind.Damage)
			{
				damageTemplateID = 0;
			}

			int index = IndexOf(sourceObjectID, kind, damageTemplateID);
			if (index < 0 && entries.Count >= MaxEntries)
			{
				/* Full. Fold into the anonymous bucket for this kind and type so the total still
				 * reaches the client. The bucket itself may need a slot; if even that is refused
				 * the oldest entry absorbs the hit, which keeps the sum correct at the cost of
				 * attributing it to the wrong source — the least visible thing to get wrong. */
				sourceObjectID = 0;
				index = IndexOf(0, kind, damageTemplateID);
				if (index < 0)
				{
					index = 0;
				}
			}

			if (index >= 0)
			{
				Entry merged = entries[index];
				merged.Amount = ClampedAdd(merged.Amount, amount);
				entries[index] = merged;
				return;
			}

			entries.Add(new Entry()
			{
				SourceObjectID = sourceObjectID,
				Kind = kind,
				DamageTemplateID = damageTemplateID,
				Amount = amount,
			});
		}

		/// <summary>
		/// Moves every merged entry into <paramref name="output"/> and clears this coalescer.
		/// </summary>
		/// <param name="output">Receives the entries; not cleared first.</param>
		public void Flush(List<Entry> output)
		{
			if (output != null)
			{
				output.AddRange(entries);
			}
			entries.Clear();
		}

		/// <summary>Drops everything without reporting it.</summary>
		public void Clear()
		{
			entries.Clear();
		}

		private int IndexOf(int sourceObjectID, CombatEventKind kind, int damageTemplateID)
		{
			for (int i = 0; i < entries.Count; ++i)
			{
				Entry e = entries[i];
				if (e.SourceObjectID == sourceObjectID && e.Kind == kind && e.DamageTemplateID == damageTemplateID)
				{
					return i;
				}
			}
			return -1;
		}

		private static int ClampedAdd(int a, int b)
		{
			long sum = (long)a + b;
			return sum > int.MaxValue ? int.MaxValue : (int)sum;
		}
	}
}
