using FishNet.Transporting;
using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	/// <summary>
	/// Controls the player's bank inventory, handling currency and item slots.
	/// Manages client-server synchronization and broadcast handling for bank operations.
	/// </summary>
	public class BankController : ItemContainer, IBankController
	{
		/// <summary>
		/// The ID of the last interactable bank object used by the player.
		/// Used for tracking which bank was accessed most recently.
		/// </summary>
		public long LastInteractableID { get; set; }

		/// <summary>
		/// The amount of currency stored in the bank.
		/// </summary>
		public long Currency { get; set; }

		/// <summary>
		/// Called when the bank controller is initialized. Resets currency and adds 100 item slots.
		/// </summary>
		public override void OnAwake()
		{
			Currency = 0;
			AddSlots(null, 100);
		}

		/// <summary>
		/// Resets the state of the bank controller, clearing all items and calling base reset logic.
		/// </summary>
		/// <param name="asServer">True if called on the server.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			Clear();
		}

#if !UNITY_SERVER
		/// <summary>
		/// Called when the character starts. Registers client broadcast handlers for bank operations if the local player owns this bank.
		/// Disables the controller for non-owners.
		/// </summary>
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();

			if (!base.IsOwner)
			{
				enabled = false;
				return;
			}

			// Register client broadcast handlers for bank item operations.
			ClientManager.RegisterBroadcast<BankSetItemBroadcast>(OnClientBankSetItemBroadcastReceived);
			ClientManager.RegisterBroadcast<BankSetMultipleItemsBroadcast>(OnClientBankSetMultipleItemsBroadcastReceived);
			ClientManager.RegisterBroadcast<BankRemoveItemBroadcast>(OnClientBankRemoveItemBroadcastReceived);
			ClientManager.RegisterBroadcast<BankSwapItemSlotsBroadcast>(OnClientBankSwapItemSlotsBroadcastReceived);
		}

		/// <summary>
		/// Called when the character stops. Unregisters client broadcast handlers for bank operations if the local player owns this bank.
		/// </summary>
		public override void OnStopCharacter()
		{
			base.OnStopClient();

			if (base.IsOwner)
			{
				ClientManager.UnregisterBroadcast<BankSetItemBroadcast>(OnClientBankSetItemBroadcastReceived);
				ClientManager.UnregisterBroadcast<BankSetMultipleItemsBroadcast>(OnClientBankSetMultipleItemsBroadcastReceived);
				ClientManager.UnregisterBroadcast<BankRemoveItemBroadcast>(OnClientBankRemoveItemBroadcastReceived);
				ClientManager.UnregisterBroadcast<BankSwapItemSlotsBroadcast>(OnClientBankSwapItemSlotsBroadcastReceived);
			}
		}

		/// <summary>
		/// Handles a broadcast from the server to set a single item in the bank.
		/// Updates the specified slot with the received item details.
		/// </summary>
		/// <param name="msg">The broadcast message containing item data.</param>
		/// <param name="channel">The network channel used for the broadcast.</param>
		private void OnClientBankSetItemBroadcastReceived(BankSetItemBroadcast msg, Channel channel)
		{
			Item newItem = new Item(msg.InstanceID, msg.Seed, msg.TemplateID, msg.StackSize);
			SetItemSlot(newItem, msg.Slot);
		}

		/// <summary>
		/// Handles a broadcast from the server to set multiple items in the bank.
		/// Updates each specified slot with the received item details.
		/// </summary>
		/// <param name="msg">The broadcast message containing multiple items.</param>
		/// <param name="channel">The network channel used for the broadcast.</param>
		private void OnClientBankSetMultipleItemsBroadcastReceived(BankSetMultipleItemsBroadcast msg, Channel channel)
		{
			foreach (BankSetItemBroadcast subMsg in msg.Items)
			{
				Item newItem = new Item(subMsg.InstanceID, subMsg.Seed, subMsg.TemplateID, subMsg.StackSize);
				SetItemSlot(newItem, subMsg.Slot);
			}
		}

		/// <summary>
		/// Handles a broadcast from the server to remove an item from a bank slot.
		/// Removes the item from the specified slot with server authority.
		/// </summary>
		/// <param name="msg">The broadcast message containing the slot to remove.</param>
		/// <param name="channel">The network channel used for the broadcast.</param>
		private void OnClientBankRemoveItemBroadcastReceived(BankRemoveItemBroadcast msg, Channel channel)
		{
			RemoveItem(msg.Slot);
		}

		/// <summary>
		/// Handles a broadcast from the server to swap item slots in the bank or between inventories.
		/// Performs the swap operation based on the source inventory type.
		/// </summary>
		/// <param name="msg">The broadcast message containing swap details.</param>
		/// <param name="channel">The network channel used for the broadcast.</param>
		private void OnClientBankSwapItemSlotsBroadcastReceived(BankSwapItemSlotsBroadcast msg, Channel channel)
		{
			switch (msg.FromInventory)
			{
				case InventoryType.Inventory:
					// Swap between inventory and bank.
					if (Character.TryGet(out IInventoryController inventoryController) &&
						inventoryController.TryGetItem(msg.From, out Item inventoryItem))
					{
						if (TryGetItem(msg.To, out Item bankItem))
						{
							inventoryController.SetItemSlot(bankItem, msg.From);
						}
						else
						{
							inventoryController.SetItemSlot(null, msg.From);
						}

						SetItemSlot(inventoryItem, msg.To);
					}
					break;
				case InventoryType.Equipment:
					// Equipment swaps are not handled here.
					break;
				case InventoryType.Bank:
					// Swap within the bank itself.
					SwapItemSlots(msg.From, msg.To);
					break;
				default: return;
			}
		}
#endif

		/// <summary>
		/// Determines if the bank can be manipulated (e.g., items moved or swapped).
		/// Always returns true unless base logic restricts manipulation.
		/// </summary>
		/// <returns>True if manipulation is allowed, false otherwise.</returns>
		public override bool CanManipulate()
		{
			if (!base.CanManipulate())
			{
				return false;
			}

			// Additional character state checks could be added here if needed.
			return true;
		}

		/// <summary>
		/// Determines if two item slots can be swapped, preventing swaps within the same bank slot.
		/// </summary>
		/// <param name="from">The source slot index.</param>
		/// <param name="to">The destination slot index.</param>
		/// <param name="fromInventory">The inventory type of the source slot.</param>
		/// <returns>True if the slots can be swapped, false otherwise.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool CanSwapItemSlots(int from, int to, InventoryType fromInventory)
		{
			return !(fromInventory == InventoryType.Bank && from == to);
		}
	}
}