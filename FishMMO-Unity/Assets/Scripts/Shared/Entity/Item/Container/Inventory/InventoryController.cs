using FishNet.Transporting;
using System.Runtime.CompilerServices;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Controls the character's inventory slots, handling item activation, slot manipulation, and network synchronization.
	/// Manages client-server broadcasts for inventory changes and slot management.
	/// </summary>
	public class InventoryController : ItemContainer, IInventoryController
	{
		/// <summary>
		/// Called when the inventory controller is initialized. Adds 32 slots for items.
		/// </summary>
		public override void OnAwake()
		{
			AddSlots(null, 32);
		}

		/// <summary>
		/// Resets the state of the inventory controller, clearing all items and calling base reset logic.
		/// </summary>
		/// <param name="asServer">True if called on the server.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			Clear();
		}

#if !UNITY_SERVER
		/// <summary>
		/// Called when the character starts. Registers client broadcast handlers for inventory operations if the local player owns this inventory.
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

			// Register client broadcast handlers for inventory item operations.
			ClientManager.RegisterBroadcast<InventorySetItemBroadcast>(OnClientInventorySetItemBroadcastReceived);
			ClientManager.RegisterBroadcast<InventorySetMultipleItemsBroadcast>(OnClientInventorySetMultipleItemsBroadcastReceived);
			ClientManager.RegisterBroadcast<InventoryRemoveItemBroadcast>(OnClientInventoryRemoveItemBroadcastReceived);
			ClientManager.RegisterBroadcast<InventorySwapItemSlotsBroadcast>(OnClientInventorySwapItemSlotsBroadcastReceived);
		}

		/// <summary>
		/// Called when the character stops. Unregisters client broadcast handlers for inventory operations if the local player owns this inventory.
		/// </summary>
		public override void OnStopCharacter()
		{
			base.OnStopCharacter();

			if (base.IsOwner)
			{
				ClientManager.UnregisterBroadcast<InventorySetItemBroadcast>(OnClientInventorySetItemBroadcastReceived);
				ClientManager.UnregisterBroadcast<InventorySetMultipleItemsBroadcast>(OnClientInventorySetMultipleItemsBroadcastReceived);
				ClientManager.UnregisterBroadcast<InventoryRemoveItemBroadcast>(OnClientInventoryRemoveItemBroadcastReceived);
				ClientManager.UnregisterBroadcast<InventorySwapItemSlotsBroadcast>(OnClientInventorySwapItemSlotsBroadcastReceived);
			}
		}

		/// <summary>
		/// Handles a broadcast from the server to set a single inventory item.
		/// Updates the specified slot with the received item details.
		/// </summary>
		/// <param name="msg">The broadcast message containing item data.</param>
		/// <param name="channel">The network channel used for the broadcast.</param>
		private void OnClientInventorySetItemBroadcastReceived(InventorySetItemBroadcast msg, Channel channel)
		{
			Item newItem = new Item(msg.InstanceID, msg.Seed, msg.TemplateID, msg.StackSize);
			SetItemSlot(newItem, msg.Slot);
		}

		/// <summary>
		/// Handles a broadcast from the server to set multiple inventory items.
		/// Updates each specified slot with the received item details.
		/// </summary>
		/// <param name="msg">The broadcast message containing multiple items.</param>
		/// <param name="channel">The network channel used for the broadcast.</param>
		private void OnClientInventorySetMultipleItemsBroadcastReceived(InventorySetMultipleItemsBroadcast msg, Channel channel)
		{
			foreach (InventorySetItemBroadcast subMsg in msg.Items)
			{
				Item newItem = new Item(subMsg.InstanceID, subMsg.Seed, subMsg.TemplateID, subMsg.StackSize);
				SetItemSlot(newItem, subMsg.Slot);
			}
		}

		/// <summary>
		/// Handles a broadcast from the server to remove an item from an inventory slot.
		/// Removes the item from the specified slot with server authority.
		/// </summary>
		/// <param name="msg">The broadcast message containing the slot to remove.</param>
		/// <param name="channel">The network channel used for the broadcast.</param>
		private void OnClientInventoryRemoveItemBroadcastReceived(InventoryRemoveItemBroadcast msg, Channel channel)
		{
			RemoveItem(msg.Slot);
		}

		/// <summary>
		/// Handles a broadcast from the server to swap item slots in the inventory or between inventories.
		/// Performs the swap operation based on the source inventory type.
		/// </summary>
		/// <param name="msg">The broadcast message containing swap details.</param>
		/// <param name="channel">The network channel used for the broadcast.</param>
		private void OnClientInventorySwapItemSlotsBroadcastReceived(InventorySwapItemSlotsBroadcast msg, Channel channel)
		{
			switch (msg.FromInventory)
			{
				case InventoryType.Inventory:
					SwapItemSlots(msg.From, msg.To);
					break;
				case InventoryType.Equipment:
					// Equipment swaps are not handled here.
					break;
				case InventoryType.Bank:
					if (Character.TryGet(out IBankController bankController) &&
						bankController.TryGetItem(msg.From, out Item bankItem))
					{
						if (TryGetItem(msg.To, out Item inventoryItem))
						{
							bankController.SetItemSlot(inventoryItem, msg.From);
						}
						else
						{
							bankController.SetItemSlot(null, msg.From);
						}

						SetItemSlot(bankItem, msg.To);
					}
					break;
				default: return;
			}
		}
#endif

		/// <summary>
		/// Determines if the inventory can be manipulated (e.g., items moved or swapped).
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
		/// Activates the item in the specified inventory slot, typically triggering its use effect.
		/// Only activates if the character is alive and the item exists in the slot.
		/// </summary>
		/// <param name="index">The inventory slot index to activate.</param>
		public void Activate(int index)
		{
			if (!Character.TryGet(out ICharacterDamageController damageController) ||
				!damageController.IsAlive)
			{
				// Cannot activate an item while dead.
				return;
			}
			if (TryGetItem(index, out Item item))
			{
				Log.Debug("InventoryController", $"Using item in slot[" + index + "]");
				//items[index].OnUseItem();
			}
		}

		/// <summary>
		/// Determines if two item slots can be swapped, preventing swaps within the same inventory slot.
		/// </summary>
		/// <param name="from">The source slot index.</param>
		/// <param name="to">The destination slot index.</param>
		/// <param name="fromInventory">The inventory type of the source slot.</param>
		/// <returns>True if the slots can be swapped, false otherwise.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool CanSwapItemSlots(int from, int to, InventoryType fromInventory)
		{
			return !(fromInventory == InventoryType.Inventory && from == to);
		}
	}
}