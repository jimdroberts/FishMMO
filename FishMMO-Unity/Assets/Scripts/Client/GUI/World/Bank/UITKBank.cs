using FishNet.Transporting;
using FishMMO.Shared;

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
