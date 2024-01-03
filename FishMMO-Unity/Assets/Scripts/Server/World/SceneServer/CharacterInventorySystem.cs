using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Server.DatabaseServices;
using System.Collections.Generic;

namespace FishMMO.Server
{
	// Character Inventory Manager handles the players inventory
	public class CharacterInventorySystem : ServerBehaviour
	{
		private SceneServerAuthenticator loginAuthenticator;
		private LocalConnectionState serverState;

		/*public float saveRate = 60.0f;
		private float nextSave = 0.0f;

		public Dictionary<string, InventoryController> inventories = new Dictionary<string, InventoryController>();*/

		public override void InitializeOnce()
		{
			//nextSave = saveRate;

			if (ServerManager != null)
			{
				ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;
				ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;
			}
			else
			{
				enabled = false;
			}
		}

		/*void LateUpdate()
		{
			if (serverState == LocalConnectionState.Started)
			{
				if (nextSave < 0)
				{
					nextSave = saveRate;
					
					Debug.Log("CharacterInventoryManager: Save");

					// all characters inventories are periodically saved
					// TODO: create an InventoryService with a save inventories function
					//Database.Instance.SaveInventories(new List<Character>(characters.Values));
				}
				nextSave -= Time.deltaTime;
			}
		}*/

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			loginAuthenticator = FindObjectOfType<SceneServerAuthenticator>();
			if (loginAuthenticator == null)
				return;

			serverState = args.ConnectionState;

			if (args.ConnectionState == LocalConnectionState.Started)
			{
				loginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;

				ServerManager.RegisterBroadcast<InventoryRemoveItemBroadcast>(OnServerInventoryRemoveItemBroadcastReceived, true);
				ServerManager.RegisterBroadcast<InventorySwapItemSlotsBroadcast>(OnServerInventorySwapItemSlotsBroadcastReceived, true);

				ServerManager.RegisterBroadcast<EquipmentEquipItemBroadcast>(OnServerEquipmentEquipItemBroadcastReceived, true);
				ServerManager.RegisterBroadcast<EquipmentUnequipItemBroadcast>(OnServerEquipmentUnequipItemBroadcastReceived, true);

				ServerManager.RegisterBroadcast<BankRemoveItemBroadcast>(OnServerBankRemoveItemBroadcastReceived, true);
				ServerManager.RegisterBroadcast<BankSwapItemSlotsBroadcast>(OnServerBankSwapItemSlotsBroadcastReceived, true);
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				loginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;

				ServerManager.UnregisterBroadcast<InventoryRemoveItemBroadcast>(OnServerInventoryRemoveItemBroadcastReceived);
				ServerManager.UnregisterBroadcast<InventorySwapItemSlotsBroadcast>(OnServerInventorySwapItemSlotsBroadcastReceived);

				ServerManager.UnregisterBroadcast<EquipmentEquipItemBroadcast>(OnServerEquipmentEquipItemBroadcastReceived);
				ServerManager.UnregisterBroadcast<EquipmentUnequipItemBroadcast>(OnServerEquipmentUnequipItemBroadcastReceived);

				ServerManager.UnregisterBroadcast<BankRemoveItemBroadcast>(OnServerBankRemoveItemBroadcastReceived);
				ServerManager.UnregisterBroadcast<BankSwapItemSlotsBroadcast>(OnServerBankSwapItemSlotsBroadcastReceived);
			}
		}

		private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
		{
		}

		private void Authenticator_OnClientAuthenticationResult(NetworkConnection conn, bool authenticated)
		{
		}

		private void OnServerInventoryRemoveItemBroadcastReceived(NetworkConnection conn, InventoryRemoveItemBroadcast msg)
		{
			if (conn == null ||
				conn.FirstObject == null)
			{
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				return;
			}

			Character character = conn.FirstObject.GetComponent<Character>();
			if (character != null &&
				!character.IsTeleporting)
			{
				Item item = character.InventoryController.RemoveItem(msg.slot);

				// remove the item from the database
				CharacterInventoryService.Delete(dbContext, character.ID, msg.slot);
				dbContext.SaveChanges();

				conn.Broadcast(msg, true, Channel.Reliable);
			}
		}

		private void OnServerInventorySwapItemSlotsBroadcastReceived(NetworkConnection conn, InventorySwapItemSlotsBroadcast msg)
		{
			if (conn == null ||
				msg.to == msg.from ||
				conn.FirstObject == null)
			{
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				return;
			}

			Character character = conn.FirstObject.GetComponent<Character>();
			if (character == null ||
				character.IsTeleporting ||
				character.InventoryController == null ||
				character.InventoryController.IsSlotEmpty(msg.from))
			{
				return;
			}

			switch (msg.fromInventory)
			{
				case InventoryType.Inventory:
					// swap the items in the inventory
					if (character.InventoryController.SwapItemSlots(msg.from, msg.to, out Item fromItem, out Item toItem))
					{
						if (toItem == null)
						{
							CharacterInventoryService.Delete(dbContext, character.ID, msg.from);
						}
						else
						{
							CharacterInventoryService.SetSlot(dbContext, character.ID, toItem);
						}
						CharacterInventoryService.SetSlot(dbContext, character.ID, fromItem);
						dbContext.SaveChanges();

						conn.Broadcast(msg, true, Channel.Reliable);
					}
					break;
				case InventoryType.Equipment:
					break;
				case InventoryType.Bank:
					if (character.BankController != null &&
						character.BankController.TryGetItem(msg.from, out Item bankItem))
					{
						if (character.InventoryController.TryGetItem(msg.to, out Item inventoryItem))
						{
							CharacterInventoryService.SetSlot(dbContext, character.ID, inventoryItem);
							character.BankController.SetItemSlot(inventoryItem, msg.from);
						}
						else
						{
							CharacterInventoryService.Delete(dbContext, character.ID, bankItem.Slot);
							character.BankController.SetItemSlot(null, msg.from);
						}

						CharacterBankService.SetSlot(dbContext, character.ID, bankItem);
						character.InventoryController.SetItemSlot(bankItem, msg.to);

						dbContext.SaveChanges();

						conn.Broadcast(msg, true, Channel.Reliable);
					}
					break;
				default: break;
			}
		}

		private void OnServerEquipmentEquipItemBroadcastReceived(NetworkConnection conn, EquipmentEquipItemBroadcast msg)
		{
			if (conn == null ||
				conn.FirstObject == null)
			{
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				return;
			}

			Character character = conn.FirstObject.GetComponent<Character>();
			if (character == null ||
				character.IsTeleporting ||
				character.EquipmentController == null)
			{
				return;
			}

			switch (msg.fromInventory)
			{
				case InventoryType.Inventory:
					if (character.InventoryController != null &&
						character.InventoryController.TryGetItem(msg.inventoryIndex, out Item inventoryItem) &&
						character.EquipmentController.Equip(inventoryItem, msg.inventoryIndex, character.InventoryController, (ItemSlot)msg.slot))
					{
						if (character.InventoryController.TryGetItem(msg.inventoryIndex, out Item prevItem))
						{
							CharacterInventoryService.SetSlot(dbContext, character.ID, prevItem);
						}
						else
						{
							// remove the item from the database
							CharacterInventoryService.Delete(dbContext, character.ID, inventoryItem.Slot);
						}
						CharacterEquipmentService.SetSlot(dbContext, character.ID, inventoryItem);
						dbContext.SaveChanges();

						conn.Broadcast(msg, true, Channel.Reliable);
					}
					break;
				case InventoryType.Equipment:
					return;
				case InventoryType.Bank:
					if (character.BankController != null &&
						character.BankController.TryGetItem(msg.inventoryIndex, out Item bankItem) &&
						character.EquipmentController.Equip(bankItem, msg.inventoryIndex, character.BankController, (ItemSlot)msg.slot))
					{
						if (character.BankController.TryGetItem(msg.inventoryIndex, out Item prevItem))
						{
							CharacterBankService.SetSlot(dbContext, character.ID, prevItem);
						}
						else
						{
							// remove the item from the database
							CharacterBankService.Delete(dbContext, character.ID, bankItem.Slot);
						}
						CharacterEquipmentService.SetSlot(dbContext, character.ID, bankItem);
						dbContext.SaveChanges();

						conn.Broadcast(msg, true, Channel.Reliable);
					}
					break;
				default: return;
			}
		}

		private void OnServerEquipmentUnequipItemBroadcastReceived(NetworkConnection conn, EquipmentUnequipItemBroadcast msg)
		{
			if (conn == null ||
				conn.FirstObject == null)
			{
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				return;
			}

			Character character = conn.FirstObject.GetComponent<Character>();
			if (character == null ||
				character.IsTeleporting ||
				character.EquipmentController == null)
			{
				return;
			}

			switch (msg.toInventory)
			{
				case InventoryType.Inventory:
					if (character.EquipmentController.TryGetItem(msg.slot, out Item toInventory) &&
						character.EquipmentController.Unequip(character.InventoryController, msg.slot))
					{
						CharacterEquipmentService.Delete(dbContext, character.ID, toInventory.Slot);
						CharacterInventoryService.SetSlot(dbContext, character.ID, toInventory);
						dbContext.SaveChanges();

						conn.Broadcast(msg, true, Channel.Reliable);
					}
					break;
				case InventoryType.Equipment:
					break;
				case InventoryType.Bank:
					if (character.EquipmentController.TryGetItem(msg.slot, out Item toBank) &&
						character.EquipmentController.Unequip(character.BankController, msg.slot))
					{
						CharacterEquipmentService.Delete(dbContext, character.ID, toBank.Slot);
						CharacterBankService.SetSlot(dbContext, character.ID, toBank);
						dbContext.SaveChanges();

						conn.Broadcast(msg, true, Channel.Reliable);
					}
					break;
				default: return;
			}
		}

		private void OnServerBankRemoveItemBroadcastReceived(NetworkConnection conn, BankRemoveItemBroadcast msg)
		{
			if (conn == null ||
				conn.FirstObject == null)
			{
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				return;
			}

			Character character = conn.FirstObject.GetComponent<Character>();
			if (character != null &&
				!character.IsTeleporting)
			{
				Item item = character.BankController.RemoveItem(msg.slot);

				// remove the item from the database
				CharacterBankService.Delete(dbContext, character.ID, item.Slot);
				dbContext.SaveChanges();

				conn.Broadcast(msg, true, Channel.Reliable);
			}
		}

		private void OnServerBankSwapItemSlotsBroadcastReceived(NetworkConnection conn, BankSwapItemSlotsBroadcast msg)
		{
			if (conn == null ||
				msg.to == msg.from ||
				conn.FirstObject == null)
			{
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				return;
			}

			Character character = conn.FirstObject.GetComponent<Character>();
			if (character == null ||
				character.IsTeleporting ||
				character.BankController == null)
			{
				return;
			}

			switch (msg.fromInventory)
			{
				case InventoryType.Inventory:
					if (character.InventoryController != null &&
						character.InventoryController.TryGetItem(msg.from, out Item inventoryItem))
					{
						if (character.BankController.TryGetItem(msg.to, out Item bankItem))
						{
							CharacterInventoryService.SetSlot(dbContext, character.ID, bankItem);
							character.InventoryController.SetItemSlot(bankItem, msg.from);
						}
						else
						{
							CharacterInventoryService.Delete(dbContext, character.ID, msg.from);
							character.InventoryController.SetItemSlot(null, msg.from);
						}

						CharacterBankService.SetSlot(dbContext, character.ID, inventoryItem);
						character.BankController.SetItemSlot(inventoryItem, msg.to);

						dbContext.SaveChanges();

						conn.Broadcast(msg, true, Channel.Reliable);
					}
					break;
				case InventoryType.Equipment:
					break;
				case InventoryType.Bank:
					// swap the items in the bank
					if (character.BankController.SwapItemSlots(msg.from, msg.to, out Item fromItem, out Item toItem))
					{
						if (toItem == null)
						{
							CharacterBankService.Delete(dbContext, character.ID, msg.from);
						}
						else
						{
							CharacterBankService.SetSlot(dbContext, character.ID, toItem);
						}
						CharacterBankService.SetSlot(dbContext, character.ID, fromItem);
						dbContext.SaveChanges();

						conn.Broadcast(msg, true, Channel.Reliable);
					}
					break;
				default: break;
			}
		}
	}
}