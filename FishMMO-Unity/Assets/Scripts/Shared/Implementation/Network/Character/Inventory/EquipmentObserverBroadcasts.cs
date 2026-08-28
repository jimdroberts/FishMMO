using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Tells everyone observing a character that one of its equipment slots changed.
	/// </summary>
	/// <remarks>
	/// <para>
	/// FishNet state forwarding is off, so a peer's <c>EquipmentController</c> is only ever filled
	/// by the spawn payload; the equip/unequip acknowledgements go to the requesting owner alone.
	/// Without this message an observer would keep rendering whatever a character wore when it
	/// came into view. Sent server → observers (owner excluded — it has the acknowledgement and
	/// the reconcile) on the reliable channel: equipment changes are rare and event driven, and a
	/// dropped one has no self-correcting replacement short of the observer leaving and re-entering
	/// range.
	/// </para>
	/// <para>
	/// Carries only what an observer can use. An observer never holds the item's inventory
	/// identity, so the instance id and stack size that the owner-shaped spawn payload carries are
	/// omitted; template plus seed is exactly what <c>EquipmentVisualController</c> needs to pick a
	/// mesh.
	/// </para>
	/// </remarks>
	public struct EquipmentObservedSlotBroadcast : IBroadcast
	{
		/// <summary>NetworkObject id of the character whose slot changed.</summary>
		/// <remarks>
		/// A broadcast is not addressed to a NetworkBehaviour the way an RPC is; the handler is
		/// registered once per client and resolves the target through the spawned-object map.
		/// </remarks>
		public int CharacterObjectID;

		/// <summary>Equipment slot that changed (byte cast of <see cref="ItemSlot"/>).</summary>
		public byte Slot;

		/// <summary>Template now in the slot, or <c>0</c> when the slot was emptied.</summary>
		public int TemplateID;

		/// <summary>Generation seed of the item now in the slot (0 when not generated or empty).</summary>
		public int Seed;

		/// <summary>True when this message empties the slot rather than filling it.</summary>
		public bool IsEmpty => TemplateID == 0;
	}
}
