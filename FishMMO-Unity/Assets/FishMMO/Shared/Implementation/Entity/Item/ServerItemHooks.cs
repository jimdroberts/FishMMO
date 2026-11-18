using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// One vacated inventory slot, recorded for the persistence layer before the container forgot it.
	/// </summary>
	/// <remarks>
	/// <c>ItemContainer.RemoveItem</c> sets the removed item's <c>Slot</c> to -1 on the way out,
	/// so anything that needs to tell the owning client which slot emptied has to capture it first.
	/// </remarks>
	public readonly struct RemovedItemRecord
	{
		/// <summary>The item's database identity, or 0 for an item never written.</summary>
		public readonly long ItemID;

		/// <summary>The version the delete is authorised against.</summary>
		public readonly long Version;

		/// <summary>The slot the item was removed from.</summary>
		public readonly int Slot;

		public RemovedItemRecord(long itemID, long version, int slot)
		{
			ItemID = itemID;
			Version = version;
			Slot = slot;
		}
	}

	/// <summary>
	/// Server-installed callbacks that let shared code hand an item change to the persistence
	/// layer without referencing the server assembly.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why these exist.</b> The ECA actions that give, remove, equip and unequip items are
	/// shared code that runs on the server, and they used to mutate the containers and stop.
	/// Nothing persisted the change and nothing told the owning client, so a quest that handed
	/// out an item produced an item that vanished at the next snapshot or reappeared after a
	/// crash, and the player saw it only after a relog. Equip and unequip are covered by the
	/// controller's own <see cref="IEquipmentController.OnServerEquipmentChanged"/>; these two
	/// cover the inventory.
	/// </para>
	/// <para>
	/// Installed by <c>CharacterInventorySystem</c> when it initialises and cleared when it goes
	/// away. Null on every other peer, and shared callers must treat null as "not the server".
	/// </para>
	/// </remarks>
	public static class ServerItemHooks
	{
		/// <summary>
		/// Grants an item to a character's inventory: places it, tells the owner, persists it.
		/// Returns true when the whole item was placed.
		/// </summary>
		public static Func<ICharacter, Item, bool> GrantInventoryItem;

		/// <summary>
		/// Reports inventory rows that changed or ceased to exist outside the inventory system's
		/// own handlers, so they are written and the owner is told.
		/// </summary>
		/// <remarks>
		/// Arguments: the character, the items whose rows changed (a reduced stack), and the items
		/// that were removed outright.
		/// </remarks>
		public static Action<ICharacter, IReadOnlyList<Item>, IReadOnlyList<RemovedItemRecord>> InventoryChanged;
	}
}
