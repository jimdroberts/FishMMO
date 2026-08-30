using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// What kind of thing contributed a modifier to an attribute.
	/// </summary>
	/// <remarks>
	/// The kind is half of a <see cref="ModifierSource"/> key. It exists because the id spaces do not
	/// share a namespace: an item's database id and a buff's template id are both small positive
	/// numbers and would otherwise collide, so item 7 and buff 7 would overwrite each other's
	/// contribution to the same attribute.
	/// </remarks>
	public enum ModifierSourceKind : byte
	{
		/// <summary>
		/// The bucket <see cref="CharacterAttribute.AddModifier"/> writes into.
		/// </summary>
		/// <remarks>
		/// Deliberately named for what it is. Nothing in the shipped call graph writes here any more —
		/// every real contributor names itself — and anything that appears in this bucket is a
		/// contribution nobody can remove except by negating it, which is the failure the ledger
		/// exists to end. It survives so that the escape hatch is visible rather than absent.
		/// </remarks>
		Unattributed = 0,

		/// <summary>
		/// The server's total, installed wholesale by the reconcile, the spawn payload or the
		/// attribute broadcast.
		/// </summary>
		/// <remarks>
		/// This is the residual: the difference between what the server says the total is and what
		/// this peer's own attributed sources add up to. On an observer it is the entire modifier,
		/// because an observer applies no sources of its own. On the server it is normally zero,
		/// because the server IS the authority and its ledger is fully attributed.
		/// </remarks>
		Authoritative = 1,

		/// <summary>
		/// An equipped item, keyed by <see cref="Item.ID"/> — its row in <c>character_item</c>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The item's real, durable identity, which survives a move between slots, a move between
		/// containers and a relog. It used to be a process-local counter (<c>Item.InstanceID</c>)
		/// because the database could not supply an id that identified an item: rows lived in three
		/// tables keyed <c>(character_id, slot)</c>, each with its own identity sequence, so a row id
		/// named a slot and the same number named three different items. The single
		/// <c>character_item</c> table removed that, and with it the need for a second identity.
		/// </para>
		/// <para>
		/// <b>Zero is a real key here, and a transient one.</b> An item created at runtime has no
		/// identity until its first persist returns one, and it can be equipped in the meantime. The
		/// contribution it writes under key zero is re-keyed by <c>Item.AssignPersistentID</c> when
		/// the id arrives — released under the old key and restated under the new one — so nothing
		/// is stranded and the character's totals do not move.
		/// </para>
		/// </remarks>
		Item = 2,

		/// <summary>A buff, keyed by its template id — which is also its instance key in the buff container.</summary>
		Buff = 3,

		/// <summary>
		/// Dungeon difficulty scaling applied to an NPC, keyed by the attribute template the scaling
		/// entry names — or zero for the sheet-wide resource multiplier, which names none.
		/// </summary>
		/// <remarks>
		/// The id is what keeps the two halves of a difficulty definition separate.
		/// <c>EnemyResourceMultiplier</c> applies to every resource and takes id zero;
		/// <c>EnemyAttributeScalars</c> names one template each and takes that template's id. Sharing
		/// one key made the named entry REPLACE the sheet-wide one, so a resource singled out for
		/// extra scaling silently lost the group multiplier instead of compounding with it.
		/// </remarks>
		DungeonScaling = 4,

		/// <summary>
		/// An NPC's authored attribute bonus roll, keyed by the template it names AND its position
		/// in the authored list.
		/// </summary>
		/// <remarks>
		/// <c>AttributeBonuses</c> is an authored LIST and nothing stops it naming the same template
		/// twice — a designer splitting a roll into a flat part and a scalar part, say. The template
		/// alone is not enough to tell those two apart: they produce the same key, the second
		/// overwrites the first, and half the roll silently disappears. The list index is what
		/// separates them. See <see cref="ModifierSource.NpcBonus"/> for how the two are packed.
		/// </remarks>
		NpcBonus = 5,

		/// <summary>A region the character is standing in, keyed by the region's instance id.</summary>
		Region = 6,
	}

	/// <summary>
	/// Identifies one contributor to a <see cref="CharacterAttribute"/>'s external modifier.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why attribution exists at all.</b> <c>ExternalModifier</c> used to be a bare accumulator:
	/// every contributor called <c>AddModifier(delta)</c> and nothing recorded who had added what. A
	/// contribution could therefore never be REMOVED, only negated — and a negation is only correct
	/// while the negating code knows exactly what was added and is certain it ran once. Neither is
	/// guaranteed: <c>ItemGenerator</c> subtracted a stored value that could have drifted, the buff
	/// templates reversed themselves through a running tick counter, NPC dungeon scaling had no
	/// reversal at all, and <c>ApplyRegionAttributeAction</c> had to be cut down to resources because
	/// a region bonus had no owner and nothing reversed it on leaving, dying, disconnecting inside
	/// the region or changing scene.
	/// </para>
	/// <para>
	/// <b>It never travels.</b> No source id is on the wire, and none needs to be. Which peer applies
	/// which source is already a settled invariant: the server owns every buff and item instance; the
	/// owner runs the same buff simulation (<c>SimulatesBuffEffects</c>) and the same equip callbacks,
	/// so it keys its own entries from ids it already read; and an observer applies nothing at all, so
	/// its ledger is a single <see cref="ModifierSourceKind.Authoritative"/> entry. The reconcile and
	/// the payload carry exactly the bytes they carried before.
	/// </para>
	/// <para>
	/// <b>Keys are cheap and comparisons are linear.</b> An attribute carries a handful of sources at
	/// most, so the ledger is a short list walked with a struct compare rather than a dictionary —
	/// no hashing, and nothing allocated until an attribute actually gains a source.
	/// </para>
	/// </remarks>
	public readonly struct ModifierSource : IEquatable<ModifierSource>
	{
		/// <summary>What kind of contributor this is.</summary>
		public readonly ModifierSourceKind Kind;

		/// <summary>
		/// Which one, within its kind. Zero for kinds that can only have one instance per attribute.
		/// </summary>
		public readonly long Id;

		public ModifierSource(ModifierSourceKind kind, long id = 0)
		{
			Kind = kind;
			Id = id;
		}

		/// <summary>The bucket for an unattributed <see cref="CharacterAttribute.AddModifier"/>.</summary>
		public static ModifierSource Unattributed => new ModifierSource(ModifierSourceKind.Unattributed);

		/// <summary>The server's residual total.</summary>
		public static ModifierSource Authoritative => new ModifierSource(ModifierSourceKind.Authoritative);

		/// <summary>An equipped item's generated bonuses.</summary>
		public static ModifierSource Item(long instanceID) => new ModifierSource(ModifierSourceKind.Item, instanceID);

		/// <summary>A buff's attribute bonuses, keyed by template id.</summary>
		public static ModifierSource Buff(int templateID) => new ModifierSource(ModifierSourceKind.Buff, templateID);

		/// <summary>
		/// Dungeon difficulty scaling on an NPC, keyed by the attribute template the scaling entry
		/// names. Pass zero for the sheet-wide resource multiplier, which names no template.
		/// </summary>
		public static ModifierSource DungeonScaling(int templateID = 0) => new ModifierSource(ModifierSourceKind.DungeonScaling, templateID);

		/// <summary>
		/// An NPC's authored attribute bonus, keyed by the template it names and its position in the
		/// authored list.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The two are packed into the one 64-bit id: the template in the high word, the index in
		/// the low. Keying on the template alone merged two entries that named it — the exact case
		/// the template-level key was introduced to fix, and which it did not actually fix, because
		/// two entries naming one template still produce one key. The index is the part that differs.
		/// </para>
		/// <para>
		/// The index is a position in an authored list, so it is stable for as long as the asset is:
		/// re-ordering <c>AttributeBonuses</c> re-keys its entries, which matters only if a
		/// designer edits the list while an NPC carrying it is alive. The bonuses are applied once
		/// at spawn and released wholesale by <c>ClearAllModifierSources</c>, so nothing outlives
		/// that.
		/// </para>
		/// </remarks>
		/// <param name="templateID">The attribute template this entry names.</param>
		/// <param name="entryIndex">The entry's position in the NPC's authored bonus list.</param>
		public static ModifierSource NpcBonus(int templateID, int entryIndex) =>
			new ModifierSource(ModifierSourceKind.NpcBonus, ((long)templateID << 32) | (uint)entryIndex);

		/// <summary>A region effect, keyed by the region's instance id.</summary>
		public static ModifierSource Region(long instanceID) => new ModifierSource(ModifierSourceKind.Region, instanceID);

		public bool Equals(ModifierSource other) => Kind == other.Kind && Id == other.Id;

		public override bool Equals(object obj) => obj is ModifierSource other && Equals(other);

		public override int GetHashCode()
		{
			unchecked
			{
				return ((int)Kind * 397) ^ Id.GetHashCode();
			}
		}

		public static bool operator ==(ModifierSource a, ModifierSource b) => a.Equals(b);

		public static bool operator !=(ModifierSource a, ModifierSource b) => !a.Equals(b);

		public override string ToString() => Id == 0 ? Kind.ToString() : $"{Kind}:{Id}";
	}
}
