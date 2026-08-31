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
	/// <para>
	/// <b>Each entry counts its hits as well as summing them.</b> Merging is the right answer for
	/// display and the wrong one for the caster's predicted numbers, which are drawn one per hit and
	/// settled one per report. See <see cref="Entry.Occurrences"/>.
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

			/// <summary>
			/// How many separate hits were summed into <see cref="Amount"/>.
			/// </summary>
			/// <remarks>
			/// Merging is right for display and wrong for confirmation: the caster's client drew one
			/// predicted label per hit, so the report has to say how many predictions it settles or
			/// the ones it does not settle grey themselves out as denied. See
			/// <c>CombatEventBroadcast.Occurrences</c>.
			/// </remarks>
			public int Occurrences;
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
			// Periodic damage keeps its type — the client colours DoT numbers by it.
			if (kind != CombatEventKind.Damage && kind != CombatEventKind.PeriodicDamage)
			{
				damageTemplateID = 0;
			}

			int index = IndexOf(sourceObjectID, kind, damageTemplateID);
			if (index < 0 && entries.Count >= MaxEntries)
			{
				/* Full. Fold into the anonymous bucket for this kind and type so the total still
				 * reaches the client; failing that, any same-kind-and-type entry (wrong source —
				 * the least visible thing to get wrong), then any same-kind entry (wrong colour,
				 * but damage stays damage). Never an entry of a DIFFERENT kind: entry 0 used to
				 * absorb the overflow whatever it was, so a raid victim's ninth damage stream
				 * inflated a heal number, and its Occurrences then settled the wrong kind's
				 * predictions on the caster's client. If no same-kind host exists the hit is
				 * dropped — under this load a missing number reads better than damage displayed
				 * as healing. */
				sourceObjectID = 0;
				index = IndexOf(0, kind, damageTemplateID);
				if (index < 0)
				{
					index = IndexOfKindAndType(kind, damageTemplateID);
				}
				if (index < 0)
				{
					index = IndexOfKind(kind);
				}
				if (index < 0)
				{
					return;
				}
			}

			if (index >= 0)
			{
				Entry merged = entries[index];
				merged.Amount = ClampedAdd(merged.Amount, amount);
				/* Counted even when the fold above redirected this hit into the anonymous bucket or
				 * into the oldest entry. The count exists to settle the caster's predictions, and the
				 * caster predicted the hit whichever entry ends up carrying its amount — losing the
				 * count would grey out a landed hit, which is worse than attributing it oddly. */
				merged.Occurrences = ClampedAdd(merged.Occurrences, 1);
				entries[index] = merged;
				return;
			}

			entries.Add(new Entry()
			{
				SourceObjectID = sourceObjectID,
				Kind = kind,
				DamageTemplateID = damageTemplateID,
				Amount = amount,
				Occurrences = 1,
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

		/// <summary>First entry of the given kind and damage type, any source. Overflow fallback.</summary>
		private int IndexOfKindAndType(CombatEventKind kind, int damageTemplateID)
		{
			for (int i = 0; i < entries.Count; ++i)
			{
				if (entries[i].Kind == kind && entries[i].DamageTemplateID == damageTemplateID)
				{
					return i;
				}
			}
			return -1;
		}

		/// <summary>First entry of the given kind, any type or source. Last-resort overflow fallback.</summary>
		private int IndexOfKind(CombatEventKind kind)
		{
			for (int i = 0; i < entries.Count; ++i)
			{
				if (entries[i].Kind == kind)
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
