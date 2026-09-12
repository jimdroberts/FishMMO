using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Shared.Core;

using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit implementation of the bank panel.
	/// </summary>
	/// <remarks>
	/// The grid, its slots, drag-and-drop, tooltips and the capacity readout all live in
	/// <see cref="UITKItemGridPanel"/>, which the inventory panel shares. What is left here is what
	/// is actually particular to a bank: it is opened by a banker rather than by a keybind, its
	/// slots belong to the bank container, and a move into it is a bank swap.
	/// </remarks>
	public class UITKBank : UITKItemGridPanel
	{
		/// <summary>
		/// Opens the inventory alongside this panel when it is shown, if the inventory is closed.
		/// An open inventory is left alone. Issue #208.
		/// </summary>
		[Header("Interaction")]
		[Tooltip("Open the inventory panel when this panel opens (if it is closed). An open inventory is left as it is.")]
		[SerializeField]
		private bool openInventoryOnShow = true;

		/// <inheritdoc />
		protected override bool OpensInventoryOnShow => openInventoryOnShow;

		/// <inheritdoc/>
		protected override string Prefix => "bank";

		/// <inheritdoc/>
		protected override ReferenceButtonType DragType => ReferenceButtonType.Bank;

		/// <inheritdoc/>
		protected override InventoryType OwnInventoryType => InventoryType.Bank;

		/// <summary>
		/// Registers the banker broadcast handler and joins the shared operation tracker.
		/// </summary>
		public override void OnClientSet()
		{
			base.OnClientSet();
			Client.NetworkManager.ClientManager.RegisterBroadcast<BankerBroadcast>(OnClientBankerBroadcastReceived);
		}

		/// <summary>
		/// Unregisters the banker broadcast handler and leaves the shared operation tracker.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<BankerBroadcast>(OnClientBankerBroadcastReceived);
			base.OnClientUnset();
		}

		/// <summary>
		/// Shows the bank panel when a banker interaction succeeds, otherwise hides it.
		/// </summary>
		private void OnClientBankerBroadcastReceived(BankerBroadcast msg, Channel channel)
		{
			if (OwnContainer == null)
			{
				Hide();
				return;
			}

			Show();
		}

		/// <summary>
		/// Right-click wears the item if it can be worn, and otherwise offers to split it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The bank used to inherit the grid's meaning for this click and nothing else, so a
		/// right-click on a sword did nothing at all — <see cref="UITKItemGridPanel"/>'s handler
		/// only prompts for a stack, and a sword is not one — and the one way to equip out of the
		/// bank was to press, drag and release on the single socket the item's own template names.
		/// The same click in the bag wears the item, so the gesture players already have was
		/// silently a no-op here. Issue #268.
		/// </para>
		/// <para>
		/// The destination comes from the item's template rather than from where the pointer is,
		/// which is what makes one click enough: a breastplate has exactly one socket it can go to.
		/// </para>
		/// <para>
		/// No trade branch, unlike the inventory: a trade offer is drawn from the inventory, so a
		/// bank slot has nothing to be offered to.
		/// </para>
		/// </remarks>
		/// <param name="slotIndex">The slot that was clicked.</param>
		protected override void HandleSlotRightClick(int slotIndex)
		{
			if (IsSlotBlocked(slotIndex))
			{
				return;
			}

			IItemContainer container = OwnContainer;
			if (container == null ||
				!container.TryGetItem(slotIndex, out Item item))
			{
				return;
			}

			if (!(item.Template is EquippableItemTemplate equippable))
			{
				// Not wearable: the click means what it means in every item grid.
				base.HandleSlotRightClick(slotIndex);
				return;
			}

			/* The same pre-flight a drop onto a socket runs. The server re-validates and remains
			 * the only authority, but a request it is certain to reject costs a round trip and
			 * leaves both slots marked pending until the refusal arrives. */
			if (!container.CanManipulate() ||
				!CharacterStateValidation.CanAct(Character) ||
				container.IsSlotLocked(slotIndex))
			{
				return;
			}

			if (!Character.TryGet(out IEquipmentController equipmentController))
			{
				return;
			}

			// Claim both ends before sending, or neither: a slot marked as waiting for a request
			// that was never sent stays locked until the tracker's timeout.
			if (!ItemOperationTracker.TryBegin(ReferenceButtonType.Bank, slotIndex))
			{
				return;
			}
			if (!ItemOperationTracker.TryBegin(ReferenceButtonType.Equipment, (int)equippable.Slot))
			{
				ItemOperationTracker.Release(ReferenceButtonType.Bank, slotIndex);
				return;
			}

			/* Queued on the controller and applied inside the owner's next replicate tick, on both
			 * peers at once — see IEquipmentController. A local refusal frees the marks now; a
			 * server refusal arrives as a reconcile that puts the item back in the bank. */
			if (!equipmentController.RequestEquip(item, slotIndex, InventoryType.Bank, equippable.Slot))
			{
				ItemOperationTracker.Release(ReferenceButtonType.Bank, slotIndex);
				ItemOperationTracker.Release(ReferenceButtonType.Equipment, (int)equippable.Slot);
			}
		}

		/// <inheritdoc/>
		protected override ReferenceButtonType? QuickTransferTarget => ReferenceButtonType.Inventory;

		/// <inheritdoc/>
		protected override void SendQuickTransferRequest(int fromSlot, int toSlot)
		{
			// Into the inventory, so it is the inventory's request even though the bank sends it.
			Client.Broadcast(new InventorySwapItemSlotsBroadcast()
			{
				From = fromSlot,
				To = toSlot,
				FromInventory = InventoryType.Bank,
			}, Channel.Reliable);
		}

		/// <inheritdoc/>
		protected override void SendSwapRequest(int fromSlot, int toSlot, InventoryType fromInventory)
		{
			Client.Broadcast(new BankSwapItemSlotsBroadcast()
			{
				From = fromSlot,
				To = toSlot,
				FromInventory = fromInventory,
			}, Channel.Reliable);
		}

		/// <inheritdoc/>
		protected override void SendSplitRequest(int fromSlot, int toSlot, InventoryType fromInventory, uint amount)
		{
			Client.Broadcast(new BankSplitItemBroadcast()
			{
				From = fromSlot,
				To = toSlot,
				Amount = amount,
				FromInventory = fromInventory,
			}, Channel.Reliable);
		}
	}
}
