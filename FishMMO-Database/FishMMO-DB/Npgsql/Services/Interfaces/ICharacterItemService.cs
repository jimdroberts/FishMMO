using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// One row a snapshot assigned an identity to, so the caller can write it back onto the
	/// runtime item.
	/// </summary>
	/// <remarks>
	/// Matched on <see cref="Container"/> and <see cref="Slot"/> rather than on the id, because the
	/// id is precisely the thing the caller did not know when it built the row.
	/// </remarks>
	public readonly struct CharacterItemIdAssignment
	{
		/// <summary>The container the row was written to.</summary>
		public readonly ItemContainerType Container;

		/// <summary>The slot the row was written to.</summary>
		public readonly int Slot;

		/// <summary>The identity the database assigned.</summary>
		public readonly long ID;

		/// <summary>
		/// The template of the row that was written, so the caller can refuse to hand the identity
		/// to a different item that has since moved into the same slot.
		/// </summary>
		public readonly int TemplateID;

		public CharacterItemIdAssignment(ItemContainerType container, int slot, long id, int templateID)
		{
			Container = container;
			Slot = slot;
			ID = id;
			TemplateID = templateID;
		}
	}

	/// <summary>
	/// Service for a character's items across every container.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Replaces <c>ICharacterInventoryService</c>, <c>ICharacterEquipmentService</c> and
	/// <c>ICharacterBankService</c>, which were three copies of one interface over three tables.
	/// </para>
	/// <para>
	/// <b>IDENTITY CONTRACT — read this before touching the SQL.</b> A row represents an ITEM, keyed
	/// by <c>id</c>. <c>container</c> and <c>slot</c> are ordinary mutable columns, so moving an item
	/// updates the row it already had rather than creating a new one. The previous schema keyed rows
	/// <c>(character_id, slot)</c> in three separate tables, which had three consequences that
	/// together made <c>Item.ID</c> useless as an identity and forced a second process-local id
	/// alongside it: an item that moved slots became a different row, two items through one slot
	/// shared a row, and the three tables' independent identity sequences handed the same number to
	/// three different items. None of those survive here.
	/// </para>
	/// <para>
	/// A <c>CharacterItemData.ID</c> of zero means "never written". Both write paths accept one:
	/// they draw the next identity from the table's own sequence and return it, and the caller must
	/// write that value back onto the runtime item. An item whose id is never written back gets a
	/// fresh row on every save, which is churn rather than corruption — but it also means the item's
	/// attribute-ledger key changes underneath it, so the write-back is not optional.
	/// </para>
	/// </remarks>
	public interface ICharacterItemService :
		IPersistAction<CharacterItemData, long>,
		IPersistManyAction<CharacterItemData>,
		IDeleteByKeyVersionedAction<long>,
		IFetchCollectionByKeyAction<long, CharacterItemData>
	{
		/// <summary>
		/// Deletes one item by its identity, if <paramref name="incomingVersion"/> is newer.
		/// </summary>
		/// <remarks>
		/// Addressed by the item rather than by the slot it happens to be sitting in. Under the old
		/// per-slot schema a delete had to name a <c>(character, slot)</c> pair and quote a version
		/// that belonged to whatever item occupied it, which is how a vacated slot ended up holding
		/// a tombstone stamped <c>long.MaxValue</c> and became permanently unwritable. There is no
		/// such ambiguity when the row and the item are the same thing.
		/// </remarks>
		/// <param name="characterId">The character that owns the item. Checked, so one character cannot delete another's row.</param>
		/// <param name="itemId">The item's identity.</param>
		/// <param name="incomingVersion">The authoritative, monotonic version for this delete.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		Task<DatabaseResult> DeleteItemAsync(long characterId, long itemId, long incomingVersion, CancellationToken cancellationToken = default);

		/// <summary>
		/// Replaces every item this character owns with an authoritative snapshot of the server's
		/// in-memory state.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The backstop for the incremental per-item writes, which can be silently rejected (a stale
		/// version, a dropped async work item, a handler that returned early). Without it, a
		/// rejection meant permanent loss at the next login; with it, the worst case is a glitch
		/// that survives until the next save tick.
		/// </para>
		/// <para>
		/// <b>Every row for the character is deleted and re-inserted.</b> Existing identities are
		/// supplied explicitly and preserved, so an item keeps its number; rows whose id is zero
		/// draw a new one and are reported back through the return value. Deleting first is what
		/// makes the statement immune to the <c>(character_id, container, slot)</c> unique index:
		/// two items swapping slots have no intermediate state in which both hold the same one.
		/// </para>
		/// <para>
		/// Deliberately NOT version-gated. Version gating is the mechanism that makes an incremental
		/// write disappear, so gating the backstop as well would defeat its purpose. It is safe
		/// because the snapshot is authoritative by construction: it states, for one character at
		/// one instant, exactly which items exist and where they are. It cannot mint an item, and
		/// the delete cannot lose one, because every row it removes is a row the server has just
		/// re-stated or believes does not exist.
		/// </para>
		/// <para>
		/// ORDERING REQUIREMENT: the caller must enqueue this through the same per-character key as
		/// the incremental writes, so the two are serialised FIFO — otherwise an in-flight snapshot
		/// can land after a newer incremental write and roll it back. It must also only be issued by
		/// a server that currently owns the character's session; there is no session guard here.
		/// </para>
		/// </remarks>
		/// <param name="characterId">The character whose items are being replaced.</param>
		/// <param name="containers">
		/// Which containers this snapshot speaks for. Rows in a container NOT listed here are left
		/// untouched, so a caller that could only read two of the three containers does not prune
		/// the third. Listing a container and supplying none of its items is the legitimate way to
		/// say "this container is empty"; omitting it says "I do not know".
		/// </param>
		/// <param name="items">Every item the character holds in <paramref name="containers"/>.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>The identity assigned to each row that was supplied without one.</returns>
		Task<DatabaseResult<IReadOnlyList<CharacterItemIdAssignment>>> SaveSnapshotAsync(long characterId, IReadOnlyCollection<ItemContainerType> containers, IEnumerable<CharacterItemData> items, CancellationToken cancellationToken = default);
	}
}
