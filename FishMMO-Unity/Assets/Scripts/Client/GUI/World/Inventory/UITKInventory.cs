using FishNet.Transporting;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit implementation of the inventory panel.
	/// </summary>
	/// <remarks>
	/// The grid, its slots, drag-and-drop, tooltips and the capacity readout all live in
	/// <see cref="UITKItemGridPanel"/>, which the bank shares. What is left here is what is actually
	/// particular to a backpack: a move into it is an inventory swap, right-clicking an item wears
	/// it, and releasing a drag over a slot completes it as well as clicking does.
	/// </remarks>
	public class UITKInventory : UITKItemGridPanel
	{
		/// <inheritdoc/>
		protected override string Prefix => "inv";

		/// <inheritdoc/>
		protected override ReferenceButtonType DragType => ReferenceButtonType.Inventory;

		/// <inheritdoc/>
		protected override InventoryType OwnInventoryType => InventoryType.Inventory;

		/// <inheritdoc/>
		protected override void SendSwapRequest(int fromSlot, int toSlot, InventoryType fromInventory)
		{
			Client.Broadcast(new InventorySwapItemSlotsBroadcast()
			{
				From = fromSlot,
				To = toSlot,
				FromInventory = fromInventory,
			}, Channel.Reliable);
		}

		/// <summary>
		/// Also completes a drag on pointer UP, so a held drag can be dropped rather than clicked.
		/// </summary>
		/// <remarks>
		/// The bank has no equivalent: its slots are only ever a destination reached by clicking.
		/// Here a player can press, drag across the grid and release, which never produces a second
		/// pointer-down on the destination slot.
		/// </remarks>
		protected override void RegisterExtraSlotCallbacks(VisualElement slotRoot, int slotIndex)
		{
			slotRoot.RegisterCallback<PointerUpEvent>(evt => OnSlotPointerUp(evt, slotIndex));
		}

		/// <summary>
		/// Completes an in-progress drag when the pointer is released over a slot.
		/// </summary>
		private void OnSlotPointerUp(PointerUpEvent evt, int slotIndex)
		{
			if (Character == null || Client == null || evt.button != 0)
			{
				return;
			}

			bool draggingNow = UIManager.TryGetTK(DRAG_OBJECT_NAME, out UITKDragObject dragObject) && dragObject.IsDragging;
			if (!draggingNow)
			{
				return;
			}

			// Same slot the drag came from: this is a click, not a drag. Leave it armed.
			if (dragObject.Type == ReferenceButtonType.Inventory &&
				(int)dragObject.ReferenceID == slotIndex)
			{
				return;
			}

			IItemContainer container = OwnContainer;
			if (container != null)
			{
				CompleteDropOntoSlot(dragObject, container, slotIndex);
			}
		}

		/// <summary>
		/// Right-click wears the item, if it can be worn.
		/// </summary>
		/// <remarks>
		/// <c>InventoryController.Activate</c> is the "use this item" path and it does nothing at
		/// all today — its body is a log line and a commented-out OnUseItem, and the server has no
		/// matching handler — so right-clicking an item was silently doing nothing. Equipping is
		/// the behaviour the slot actually needs, and the broadcast for it already exists and is
		/// already handled server-side; the equipment panel has been sending it for click-to-drop
		/// all along. The destination slot comes from the item's own template, which is what makes
		/// a single right-click meaningful: a breastplate has exactly one slot it can go to.
		/// </remarks>
		protected override void HandleSlotRightClick(int slotIndex)
		{
			if (IsSlotBlocked(slotIndex))
			{
				return;
			}

			IItemContainer container = OwnContainer;
			if (container == null)
			{
				return;
			}

			if (!container.TryGetItem(slotIndex, out Item item) ||
				!(item.Template is EquippableItemTemplate equippable))
			{
				return;
			}

			/* The same pre-flight CompleteDropOntoSlot runs. The server re-validates and remains
			 * the only authority, but a request it is certain to reject costs a round trip and
			 * leaves both slots marked pending until the refusal arrives. */
			if (!container.CanManipulate() ||
				!CharacterStateValidation.CanAct(Character) ||
				container.IsSlotLocked(slotIndex))
			{
				return;
			}

			if (!ItemOperationTracker.TryBegin(ReferenceButtonType.Inventory, slotIndex))
			{
				return;
			}
			if (!ItemOperationTracker.TryBegin(ReferenceButtonType.Equipment, (int)equippable.Slot))
			{
				ItemOperationTracker.Release(ReferenceButtonType.Inventory, slotIndex);
				return;
			}

			/* Queued on the controller and applied inside the owner's next replicate tick, on both
			 * peers at once — see IEquipmentController. A local refusal frees the marks now; a
			 * server refusal arrives as a reconcile that moves the item back. */
			if (!Character.TryGet(out IEquipmentController equipmentController) ||
				!equipmentController.RequestEquip(item, slotIndex, InventoryType.Inventory, equippable.Slot))
			{
				ItemOperationTracker.Release(ReferenceButtonType.Inventory, slotIndex);
				ItemOperationTracker.Release(ReferenceButtonType.Equipment, (int)equippable.Slot);
			}
		}
	}
}
