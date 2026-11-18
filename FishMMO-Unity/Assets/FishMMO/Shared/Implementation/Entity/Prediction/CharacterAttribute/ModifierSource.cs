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
		/// <b>Zero is NOT a key here.</b> An item created at runtime has no identity until its first
		/// persist returns one, and it can be equipped in the meantime — but zero is the absence of
		/// an identity, not one of its values, so two such items would share the key <c>Item(0)</c>
		/// and, because <c>SetSource</c> STATES a contribution rather than adding to one, the second
		/// would silently replace the first. <c>ItemGenerator.TryResolveLedgerSource</c> therefore
		/// DECLINES for a zero id: such an item contributes nothing until
		/// <c>Item.AssignPersistentID</c> gives it an identity, at which point the bonus is applied
		/// for the first time. The cost is a database round trip of missing stats, against a silent
		/// and permanent loss.
		/// </para>
		/// <para>
		/// That decline is also what keeps an observer's sheet honest: an observer builds its copy
		/// of a peer's equipment with no ids at all (<c>EquipmentController.WritePayload</c> does
		/// not send them to non-owners), so it applies nothing — correctly, because the server's
		/// authoritative <c>ExternalModifier</c> already contains every equipped item's bonus.
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

		/// <summary>
		/// Which CONTRIBUTION, within one source, on one attribute. Zero for a source that can only
		/// contribute once.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>A source is not a contribution.</b> <c>SetSource</c> STATES a whole contribution
		/// rather than adding to one, so a source that writes to the same attribute twice under one
		/// key keeps only the second write — silently, and permanently, because the release removes
		/// exactly what is there. That is not hypothetical: an item's generated attributes, a buff's
		/// <c>BonusAttributes</c> and an NPC's <c>AttributeBonuses</c> are all authored LISTS, and
		/// nothing stops two entries in one of them naming the same
		/// <see cref="CharacterAttributeTemplate"/> — a designer splitting a bonus into a flat part
		/// and a scalar part, or an armour piece rolling a second Armor affix on top of its base
		/// one. Under a single key the character silently gets one of the two.
		/// </para>
		/// <para>
		/// This is what separates them. <see cref="ModifierSourceKind.NpcBonus"/> solved it first by
		/// packing the index into the high word of <see cref="Id"/>; that trick does not generalise,
		/// because <see cref="ModifierSourceKind.Item"/>'s id is a full 64-bit database row and has
		/// no spare word. A field costs four bytes on a struct that never travels and is the same
		/// answer for every kind.
		/// </para>
		/// <para>
		/// <b>The release side must not have to know the indices.</b> Whatever an apply pass chooses
		/// as an index, <c>CharacterAttribute.ClearSourceGroup</c> releases every entry sharing a
		/// (<see cref="Kind"/>, <see cref="Id"/>) — so an item, a buff or a region lets go of its
		/// whole contribution without reconstructing the index scheme that wrote it. Releasing one
		/// index at a time would strand the rest the moment the two sides disagreed.
		/// </para>
		/// </remarks>
		public readonly int Index;

		public ModifierSource(ModifierSourceKind kind, long id = 0, int index = 0)
		{
			Kind = kind;
			Id = id;
			Index = index;
		}

		/// <summary>The bucket for an unattributed <see cref="CharacterAttribute.AddModifier"/>.</summary>
		public static ModifierSource Unattributed => new ModifierSource(ModifierSourceKind.Unattributed);

		/// <summary>The server's residual total.</summary>
		public static ModifierSource Authoritative => new ModifierSource(ModifierSourceKind.Authoritative);

		/// <summary>
		/// One of an equipped item's generated bonuses, keyed by
		/// <see cref="FishMMO.Shared.Item.ID"/> — its row in <c>character_item</c> — and by which of
		/// the item's attributes this is. See <see cref="ModifierSourceKind.Item"/> for why a zero
		/// id must never reach here, and <see cref="Index"/> for why one item needs more than one
		/// key.
		/// </summary>
		/// <param name="itemID">The item's <c>character_item</c> row.</param>
		/// <param name="entryIndex">
		/// Identifies which of the item's generated attributes this is. <c>ItemGenerator</c> passes
		/// the <c>ItemAttributeTemplate</c>'s own id, which is stable and order-independent — two
		/// affixes that both raise Armor are different templates and so keep separate entries.
		/// </param>
		public static ModifierSource Item(long itemID, int entryIndex = 0) =>
			new ModifierSource(ModifierSourceKind.Item, itemID, entryIndex);

		/// <summary>
		/// One of a buff's attribute bonuses, keyed by the buff's template id and by the entry's
		/// position in the authored list. See <see cref="Index"/>.
		/// </summary>
		/// <param name="templateID">The buff template, which is also its key in the buff container.</param>
		/// <param name="entryIndex">The entry's position in the authored bonus list.</param>
		public static ModifierSource Buff(int templateID, int entryIndex = 0) =>
			new ModifierSource(ModifierSourceKind.Buff, templateID, entryIndex);

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
		/// Keying on the template alone merged two entries that named it — the exact case the
		/// template-level key was introduced to fix, and which it did not actually fix, because two
		/// entries naming one template still produce one key. The index is the part that differs.
		/// </para>
		/// <para>
		/// The two used to be packed into the one 64-bit id, the template in the high word and the
		/// index in the low. They ride <see cref="Id"/> and <see cref="Index"/> now: the packing was
		/// this kind's private answer to a problem every kind has, and it could not be lent to
		/// <see cref="ModifierSourceKind.Item"/>, whose id is a full 64-bit database row with no
		/// spare word.
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
			new ModifierSource(ModifierSourceKind.NpcBonus, templateID, entryIndex);

		/// <summary>
		/// A region effect, keyed by the region's instance id.
		/// </summary>
		/// <param name="instanceID">The region's <c>NetworkObject.ObjectId</c>.</param>
		/// <param name="entryIndex">
		/// Distinguishes several contributions from one region to one attribute. Zero is correct for
		/// a region with a single <c>ApplyRegionAttributeAction</c> per attribute, which is every
		/// authored region today; see <see cref="Index"/>.
		/// </param>
		public static ModifierSource Region(long instanceID, int entryIndex = 0) =>
			new ModifierSource(ModifierSourceKind.Region, instanceID, entryIndex);

		public bool Equals(ModifierSource other) => Kind == other.Kind && Id == other.Id && Index == other.Index;

		public override bool Equals(object obj) => obj is ModifierSource other && Equals(other);

		/// <summary>
		/// True when <paramref name="other"/> comes from the same contributor, whichever of its
		/// contributions it is. The predicate <c>CharacterAttribute.ClearSourceGroup</c> releases by.
		/// </summary>
		public bool IsSameContributor(ModifierSource other) => Kind == other.Kind && Id == other.Id;

		public override int GetHashCode()
		{
			unchecked
			{
				return (((int)Kind * 397) ^ Id.GetHashCode()) * 397 ^ Index;
			}
		}

		public static bool operator ==(ModifierSource a, ModifierSource b) => a.Equals(b);

		public static bool operator !=(ModifierSource a, ModifierSource b) => !a.Equals(b);

		public override string ToString()
		{
			if (Id == 0 && Index == 0)
			{
				return Kind.ToString();
			}
			return Index == 0 ? $"{Kind}:{Id}" : $"{Kind}:{Id}#{Index}";
		}
	}
}
