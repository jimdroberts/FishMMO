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
	/// particular to a backpack: a move into it is an inventory swap and right-clicking an item
	/// wears it.
	/// <para>
	/// Moving an item is a PRESS to pick it up and a PRESS to put it down. A release does nothing
	/// here or in any other panel — the note above <c>Notify</c> in <see cref="UITKSlotPanelBase"/>
	/// says why, because this class used to be the one that completed a drop on the release.
	/// </para>
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
		protected override ReferenceButtonType? QuickTransferTarget => ReferenceButtonType.Bank;

		/// <inheritdoc/>
		protected override void SendQuickTransferRequest(int fromSlot, int toSlot)
		{
			/* Into the bank, so it is the bank's request. The server refuses it when no banker is
			 * in range, which is what keeps a shift-click from banking from anywhere. */
			Client.Broadcast(new BankSwapItemSlotsBroadcast()
			{
				From = fromSlot,
				To = toSlot,
				FromInventory = InventoryType.Inventory,
			}, Channel.Reliable);
		}

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

		/// <inheritdoc/>
		protected override void SendSplitRequest(int fromSlot, int toSlot, InventoryType fromInventory, uint amount)
		{
			Client.Broadcast(new InventorySplitItemBroadcast()
			{
				From = fromSlot,
				To = toSlot,
				Amount = amount,
				FromInventory = fromInventory,
			}, Channel.Reliable);
		}

		/// <summary>
		/// Right-click wears the item if it can be worn, and otherwise offers to split it.
		/// </summary>
		/// <remarks>
		/// <c>InventoryController.Activate</c> is the "use this item" path and it does nothing at
		/// all today — its body is a log line and a commented-out OnUseItem, and the server has no
		/// matching handler — so right-clicking an item was silently doing nothing. Equipping is
		/// the behaviour the slot actually needs, and the broadcast for it already exists and is
		/// already handled server-side; the equipment panel has been sending it for click-to-drop
		/// all along. The destination slot comes from the item's own template, which is what makes
		/// a single right-click meaningful: a breastplate has exactly one slot it can go to.
		/// <para>
		/// Anything that is not wearable gets the grid's own meaning for the click, the split
		/// prompt (issue #198), so a stack of arrows in the bag splits the same way it does in the
		/// bank.
		/// </para>
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

			if (!container.TryGetItem(slotIndex, out Item item))
			{
				return;
			}

			/* While a trade window is open, right-click means "put this on the table" for
			 * every item, wearable or not — the convention every MMO trade window follows,
			 * and the one thing a player with a trade open is trying to do with their bag.
			 * The trade panel pre-checks the slot and the server decides. */
			if (UIManager.TryGetTK("UITrade", out UITKTrade uiTrade) && uiTrade.SessionOpen && uiTrade.Visible)
			{
				uiTrade.TryOfferInventorySlot(slotIndex);
				return;
			}

			if (!(item.Template is EquippableItemTemplate equippable))
			{
				// Not wearable: the click means what it means in every item grid.
				base.HandleSlotRightClick(slotIndex);
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
