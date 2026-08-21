using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Identifies which item operation a <see cref="ItemOperationFailedBroadcast"/> refers to.
	/// </summary>
	/// <remarks>
	/// The values mirror the server's internal ingress operation codes, but this is a separate,
	/// wire-visible enum on purpose: the server's guard codes are an implementation detail and must
	/// be free to change without silently redefining a network contract.
	/// </remarks>
	public enum ItemOperationType : byte
	{
		/// <summary>Unspecified or unrecognised operation.</summary>
		Unknown = 0,
		/// <summary>Remove (destroy/drop) an item from the character's inventory.</summary>
		InventoryRemove = 1,
		/// <summary>Move an item into, out of, or within the inventory.</summary>
		InventorySwap = 2,
		/// <summary>Equip an item from a container.</summary>
		EquipmentEquip = 3,
		/// <summary>Unequip an item into a container.</summary>
		EquipmentUnequip = 4,
		/// <summary>Remove (destroy) an item from the character's bank.</summary>
		BankRemove = 5,
		/// <summary>Move an item into, out of, or within the bank.</summary>
		BankSwap = 6,
	}

	/// <summary>
	/// Why the server refused an item operation. Deliberately coarse.
	/// </summary>
	/// <remarks>
	/// These are UI hints, not diagnostics. A reason must never let a client distinguish states it
	/// is not otherwise entitled to know about, so there is no "that slot is empty on the server"
	/// or "no such character" value here — everything the server declines for a validation reason
	/// collapses into <see cref="Rejected"/>.
	/// </remarks>
	public enum ItemOperationFailureReason : byte
	{
		/// <summary>No reason given.</summary>
		Unknown = 0,
		/// <summary>
		/// The server declined the operation. Covers every validation failure: bad slot, locked
		/// slot, dead character, out-of-range banker, destination full, item not where the client
		/// thought it was.
		/// </summary>
		Rejected = 1,
		/// <summary>
		/// The request arrived faster than the per-connection ingress debounce allows, or an
		/// identical request is still in flight. The client should release its pending lock and
		/// may retry.
		/// </summary>
		Throttled = 2,
		/// <summary>
		/// The server's async worker queue was saturated when the operation was persisted.
		/// </summary>
		/// <remarks>
		/// TREAT THIS AS "OUTCOME UNKNOWN", NOT AS "DID NOT HAPPEN". <c>EnqueuePersistence</c> never
		/// discards work — a false return means it ran on the thread-pool fallback instead of the
		/// worker — so the server-side mutation has in fact been committed and written. What is
		/// missing is only the acknowledgement. A client that reverts its optimistic change on this
		/// reason will disagree with the server until the next login; it should request a full
		/// container refresh instead. Paired with <c>ServerBusyBroadcast</c>.
		/// </remarks>
		ServerBusy = 3,
	}

	/// <summary>
	/// Sent to a client when an item operation it requested did not happen.
	/// </summary>
	/// <remarks>
	/// <para>
	/// WHY THIS EXISTS: every item handler on the server was written to <c>return</c> silently when
	/// it declined a request — roughly two dozen such returns across inventory, bank and equipment.
	/// The client had already moved its own view of the slot, or was holding a drag, and nothing
	/// ever arrived to tell it otherwise, so the UI showed a stale slot until the panel was rebuilt
	/// or the player relogged. Combined with the persistence bugs that meant a player could watch an
	/// item sit in a slot that the server did not believe existed.
	/// </para>
	/// <para>
	/// A failure message carries no item identity, only slot indices the client already sent. It is
	/// a "resync these slots" instruction, not a data source: the client must re-read the affected
	/// slots from its authoritative container state rather than infer anything from this message.
	/// </para>
	/// </remarks>
	public struct ItemOperationFailedBroadcast : IBroadcast
	{
		/// <summary>The operation the client requested.</summary>
		public ItemOperationType Operation;

		/// <summary>Why it was refused.</summary>
		public ItemOperationFailureReason Reason;

		/// <summary>
		/// The container the operation started from, as the client named it in its request.
		/// </summary>
		public InventoryType Container;

		/// <summary>
		/// Primary slot index involved, or -1 when the operation names no single slot.
		/// </summary>
		public int Slot;

		/// <summary>
		/// Destination slot index for swap and equip operations, or -1 when not applicable.
		/// </summary>
		public int SecondarySlot;
	}
}
