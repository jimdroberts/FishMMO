using FishNet.Transporting;
using FishMMO.Shared;

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
		protected override void SendSwapRequest(int fromSlot, int toSlot, InventoryType fromInventory)
		{
			Client.Broadcast(new BankSwapItemSlotsBroadcast()
			{
				From = fromSlot,
				To = toSlot,
				FromInventory = fromInventory,
			}, Channel.Reliable);
		}
	}
}
