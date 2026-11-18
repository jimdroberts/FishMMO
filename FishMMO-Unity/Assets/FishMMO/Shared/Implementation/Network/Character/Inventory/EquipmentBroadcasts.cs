using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Tells the owner where an item that left an equipment socket actually landed. Server to
	/// owner only.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Equipment requests no longer travel as broadcasts at all: an equip or unequip is replicate
	/// INPUT (<see cref="CharacterReplicateData.EquipmentRequest"/>), applied by the owner and the
	/// server on the same tick and confirmed by the reconcile. The reliable equip acknowledgement
	/// this file used to define is gone with it — it was applied on receipt, while the reconciles
	/// for the ticks before the request were still queued behind it, so every equip was undone by a
	/// stale snapshot and re-done by the next, and every unequip was re-equipped and then dropped
	/// into the wrong container.
	/// </para>
	/// <para>
	/// The unequip destination is the one thing the reconcile cannot settle. The socket empties on
	/// both peers, but the container chooses the landing slot from ITS copy of the container, and
	/// the owner's copy can differ from the server's by a grant that landed on one side first. So
	/// the server reports the slot it chose, and the owner moves the item there by identity if it
	/// put it anywhere else.
	/// </para>
	/// </remarks>
	public struct EquipmentUnequipItemBroadcast : IBroadcast
	{
		/// <summary>Identity of the item that was unequipped. The owner finds it by this, not by where it thinks it is.</summary>
		public long ItemID;
		/// <summary>Equipment slot the item left.</summary>
		public byte Slot;
		/// <summary>Container the item landed in.</summary>
		public InventoryType ToInventory;
		/// <summary>Slot within <see cref="ToInventory"/> the item landed in.</summary>
		public int ToSlot;
	}
}
