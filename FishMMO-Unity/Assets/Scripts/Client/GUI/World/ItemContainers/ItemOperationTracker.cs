using System;
using System.Collections.Generic;
using FishNet.Transporting;
using FishMMO.Logging;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// The client's record of which item slots are waiting on the server, shared by the inventory,
	/// bank and equipment panels.
	/// </summary>
	/// <remarks>
	/// <para>
	/// WHY THIS IS SHARED AND NOT PER-PANEL. An item operation almost never involves one panel.
	/// Equipping starts in the inventory and finishes in the equipment window; a bank deposit
	/// starts in one grid and lands in another. The panel that sends the request is frequently not
	/// the panel that owns the slot which must be locked while it is in flight — dropping an
	/// inventory item onto an equipment socket is handled by the equipment panel, but the slot
	/// that must stop accepting clicks is the inventory one. A per-panel lock table cannot express
	/// that, and the gap it leaves is exactly the double-submit this is here to prevent.
	/// </para>
	/// <para>
	/// It also owns the client's <see cref="ItemOperationFailedBroadcast"/> handler, for the same
	/// reason: one message can concern two containers, and it needs to be read once and applied to
	/// both rather than three times with each panel ignoring two thirds of it. Registration is
	/// reference-counted across the panels that attach, so it survives any one of them being
	/// destroyed and unregisters exactly once when the last one goes.
	/// </para>
	/// <para>
	/// Nothing here is authoritative. A pending mark says "we have asked and not yet been
	/// answered"; it never says anything about what the slot contains. Rendering still comes from
	/// the replicated containers, always.
	/// </para>
	/// </remarks>
	public static class ItemOperationTracker
	{
		/// <summary>Pending slots of the character's inventory.</summary>
		private static readonly ItemSlotPendingSet inventorySlots = new ItemSlotPendingSet();

		/// <summary>Pending slots of the character's bank.</summary>
		private static readonly ItemSlotPendingSet bankSlots = new ItemSlotPendingSet();

		/// <summary>Pending slots of the character's equipment.</summary>
		private static readonly ItemSlotPendingSet equipmentSlots = new ItemSlotPendingSet();

		/// <summary>Number of panels currently attached to the broadcast handler.</summary>
		private static int attachCount;

		/// <summary>True while the failure handler is registered with FishNet.</summary>
		private static bool attached;

		/// <summary>
		/// Raised when a slot starts or stops waiting on the server, so the owning panel can
		/// repaint its lock overlay.
		/// </summary>
		/// <remarks>
		/// Arguments: which container, which slot, and whether it is now pending. Panels must
		/// ignore containers that are not theirs — every panel hears every change.
		/// </remarks>
		public static event Action<ReferenceButtonType, int, bool> SlotPendingChanged;

		/// <summary>
		/// Raised when the client has been told its view of a container may be wrong and cannot
		/// work out the truth from the message alone.
		/// </summary>
		/// <remarks>
		/// Currently only <see cref="ItemOperationFailureReason.ServerBusy"/>, which means "outcome
		/// unknown" rather than "did not happen" — the server-side mutation was committed and only
		/// the acknowledgement went missing. Reverting on that reason would leave the client
		/// disagreeing with the server until the next login, so the panels re-render every slot
		/// from their replicated container instead. That is the best available answer: there is no
		/// server-to-client "resend this container" message today.
		/// </remarks>
		public static event Action<ReferenceButtonType> ResyncRequested;

		/// <summary>
		/// Returns the pending set for a container, or null for a type that has no slots.
		/// </summary>
		/// <param name="type">Which container.</param>
		/// <returns>The set, or null when <paramref name="type"/> is not a container.</returns>
		public static ItemSlotPendingSet For(ReferenceButtonType type)
		{
			switch (type)
			{
				case ReferenceButtonType.Inventory: return inventorySlots;
				case ReferenceButtonType.Bank:      return bankSlots;
				case ReferenceButtonType.Equipment: return equipmentSlots;
				default:                            return null;
			}
		}

		/// <summary>
		/// Maps the wire-level <see cref="InventoryType"/> onto the UI's container type.
		/// </summary>
		/// <param name="type">Container as named in the request that failed.</param>
		/// <returns>The matching UI container type, or <c>None</c>.</returns>
		public static ReferenceButtonType FromInventoryType(InventoryType type)
		{
			switch (type)
			{
				case InventoryType.Inventory: return ReferenceButtonType.Inventory;
				case InventoryType.Bank:      return ReferenceButtonType.Bank;
				case InventoryType.Equipment: return ReferenceButtonType.Equipment;
				default:                      return ReferenceButtonType.None;
			}
		}

		/// <summary>
		/// Reports whether a slot is waiting on the server.
		/// </summary>
		/// <param name="type">Which container.</param>
		/// <param name="slot">Slot index within it.</param>
		/// <returns>True while a request on that slot is outstanding.</returns>
		public static bool IsPending(ReferenceButtonType type, int slot)
		{
			ItemSlotPendingSet set = For(type);
			return set != null && set.IsPending(slot);
		}

		/// <summary>
		/// Claims a slot for a request that is about to be sent.
		/// </summary>
		/// <remarks>
		/// Returns false when the slot already has a request in flight, and the caller must then
		/// send nothing. A caller claiming two slots must check both before sending either, and
		/// release the first if the second refuses — see the panels' <c>TryBeginPair</c>.
		/// </remarks>
		/// <param name="type">Which container.</param>
		/// <param name="slot">Slot index within it.</param>
		/// <returns>True when the slot was free and is now claimed.</returns>
		public static bool TryBegin(ReferenceButtonType type, int slot)
		{
			ItemSlotPendingSet set = For(type);
			if (set == null || !set.TryBegin(slot))
			{
				return false;
			}

			SlotPendingChanged?.Invoke(type, slot, true);
			return true;
		}

		/// <summary>
		/// Ends the wait on a slot, if it had one.
		/// </summary>
		/// <param name="type">Which container.</param>
		/// <param name="slot">Slot index within it.</param>
		public static void Release(ReferenceButtonType type, int slot)
		{
			ItemSlotPendingSet set = For(type);
			if (set == null || !set.Release(slot))
			{
				return;
			}

			SlotPendingChanged?.Invoke(type, slot, false);
		}

		/// <summary>
		/// Ends every wait on one container.
		/// </summary>
		/// <remarks>
		/// For teardown: panel close, character change, quit to login. A lock that outlives the
		/// request it was taken for is worse than no lock, because the slot looks normal and
		/// silently refuses every click.
		/// </remarks>
		/// <param name="type">Which container.</param>
		public static void ReleaseAll(ReferenceButtonType type)
		{
			ItemSlotPendingSet set = For(type);
			if (set == null || !set.HasAnyPending)
			{
				return;
			}

			releasedScratch.Clear();
			set.ReleaseAll(releasedScratch);
			for (int i = 0; i < releasedScratch.Count; ++i)
			{
				SlotPendingChanged?.Invoke(type, releasedScratch[i], false);
			}
		}

		/// <summary>Reused by <see cref="ReleaseAll"/> so teardown allocates nothing.</summary>
		private static readonly List<int> releasedScratch = new List<int>();

		/// <summary>
		/// Ends every wait on every container.
		/// </summary>
		public static void ReleaseEverything()
		{
			ReleaseAll(ReferenceButtonType.Inventory);
			ReleaseAll(ReferenceButtonType.Bank);
			ReleaseAll(ReferenceButtonType.Equipment);
		}

		/// <summary>
		/// Hands back any slot whose reply never arrived.
		/// </summary>
		/// <remarks>
		/// Safe to call from more than one panel in the same frame: the guards are self-clearing
		/// and report a timeout exactly once, so the second call finds nothing to do.
		/// </remarks>
		public static void Tick()
		{
			TickOne(ReferenceButtonType.Inventory, inventorySlots);
			TickOne(ReferenceButtonType.Bank, bankSlots);
			TickOne(ReferenceButtonType.Equipment, equipmentSlots);
		}

		/// <summary>
		/// Times out one container's pending slots.
		/// </summary>
		private static void TickOne(ReferenceButtonType type, ItemSlotPendingSet set)
		{
			if (!set.HasAnyPending)
			{
				return;
			}

			List<int> expired = set.CollectExpired();
			for (int i = 0; i < expired.Count; ++i)
			{
				Log.Debug("ItemOperationTracker", $"No reply for [{type}:{expired[i]}]; releasing the slot.");
				SlotPendingChanged?.Invoke(type, expired[i], false);
			}

			/* A timed-out operation is an operation whose outcome nobody knows, which is the same
			 * position ServerBusy leaves the client in. Re-render rather than assume. */
			if (expired.Count > 0)
			{
				ResyncRequested?.Invoke(type);
			}
		}

		/// <summary>
		/// Registers the failure handler on behalf of one panel.
		/// </summary>
		/// <remarks>
		/// Reference-counted. Until something registers a handler for
		/// <see cref="ItemOperationFailedBroadcast"/>, FishNet logs an unregistered-broadcast
		/// warning on the client for every refused item operation — and, worse, the refusal that
		/// was added precisely so the UI could unlock itself goes unheard.
		/// </remarks>
		public static void Attach()
		{
			/* Counted before the network manager is consulted, and unconditionally. Returning
			 * early without counting would leave the caller's matching Detach decrementing
			 * somebody else's claim, and the handler would then be torn down while two panels
			 * still needed it. If there is no network manager yet, the count still stands and the
			 * next panel to attach performs the registration. */
			++attachCount;

			if (attached || Client.NetworkManager == null)
			{
				return;
			}

			attached = true;
			Client.NetworkManager.ClientManager.RegisterBroadcast<ItemOperationFailedBroadcast>(OnItemOperationFailed);
		}

		/// <summary>
		/// Releases one panel's claim on the failure handler.
		/// </summary>
		public static void Detach()
		{
			if (attachCount < 1)
			{
				return;
			}

			--attachCount;
			if (attachCount > 0)
			{
				return;
			}

			DetachAll();
		}

		/// <summary>
		/// Unregisters the handler and drops every outstanding wait.
		/// </summary>
		private static void DetachAll()
		{
			if (attached && Client.NetworkManager != null)
			{
				Client.NetworkManager.ClientManager.UnregisterBroadcast<ItemOperationFailedBroadcast>(OnItemOperationFailed);
			}

			attached = false;
			attachCount = 0;

			// Nothing can answer these any more.
			ReleaseEverything();
		}

		/// <summary>
		/// Applies a server refusal: unlocks the slots it names, and asks for a re-render when the
		/// message cannot say what actually happened.
		/// </summary>
		/// <remarks>
		/// The message carries no item data by design — only the operation, a coarse reason, and
		/// the slot indices the client itself sent. It is an instruction to look again at those
		/// slots, never a source of truth about them, so nothing here writes to a container.
		/// </remarks>
		private static void OnItemOperationFailed(ItemOperationFailedBroadcast msg, Channel channel)
		{
			ReferenceButtonType source = FromInventoryType(msg.Container);

			switch (msg.Operation)
			{
				case ItemOperationType.InventoryRemove:
					Release(ReferenceButtonType.Inventory, msg.Slot);
					break;

				case ItemOperationType.BankRemove:
					Release(ReferenceButtonType.Bank, msg.Slot);
					break;

				case ItemOperationType.InventorySwap:
					// From is in whichever container the client named; To is always an inventory slot.
					Release(source, msg.Slot);
					Release(ReferenceButtonType.Inventory, msg.SecondarySlot);
					break;

				case ItemOperationType.BankSwap:
					// Mirror image of the above: To is always a bank slot.
					Release(source, msg.Slot);
					Release(ReferenceButtonType.Bank, msg.SecondarySlot);
					break;

				case ItemOperationType.EquipmentEquip:
					// Slot is the source container index, SecondarySlot the equipment socket.
					Release(source, msg.Slot);
					Release(ReferenceButtonType.Equipment, msg.SecondarySlot);
					break;

				case ItemOperationType.EquipmentUnequip:
					// Container here is the DESTINATION, and the message names no slot in it.
					Release(ReferenceButtonType.Equipment, msg.Slot);
					break;

				default:
					/* An operation this build does not know about. Release everything rather than
					 * leave a slot locked by a message we failed to understand — a stuck slot is a
					 * worse outcome than an over-eager unlock. */
					ReleaseEverything();
					break;
			}

			if (msg.Reason == ItemOperationFailureReason.ServerBusy)
			{
				/* NOT a failure. EnqueuePersistence never discards work, so the mutation HAS been
				 * committed server-side and only the acknowledgement is missing. Anything that
				 * looks like a revert here would put the client permanently out of step with the
				 * server; re-render from the replicated containers instead. */
				Log.Debug("ItemOperationTracker", $"Server busy on [{msg.Operation}]; outcome unknown, resyncing the view.");
				ResyncRequested?.Invoke(source);
				if (msg.Operation == ItemOperationType.EquipmentEquip ||
					msg.Operation == ItemOperationType.EquipmentUnequip)
				{
					ResyncRequested?.Invoke(ReferenceButtonType.Equipment);
				}
			}
		}
	}
}
