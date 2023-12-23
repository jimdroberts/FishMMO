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
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				loginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;

				ServerManager.UnregisterBroadcast<InventoryRemoveItemBroadcast>(OnServerInventoryRemoveItemBroadcastReceived);
				ServerManager.UnregisterBroadcast<InventorySwapItemSlotsBroadcast>(OnServerInventorySwapItemSlotsBroadcastReceived);

				ServerManager.UnregisterBroadcast<EquipmentEquipItemBroadcast>(OnServerEquipmentEquipItemBroadcastReceived);
				ServerManager.UnregisterBroadcast<EquipmentUnequipItemBroadcast>(OnServerEquipmentUnequipItemBroadcastReceived);
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
				CharacterInventoryService.Delete(dbContext, character.ID, item.ID);
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

			if (character.InventoryController.SwapItemSlots(msg.from, msg.to, out Item fromItem, out Item toItem))
			{
				// save the changes to the database
				CharacterInventoryService.UpdateOrAdd(dbContext, character.ID, fromItem);
				CharacterInventoryService.UpdateOrAdd(dbContext, character.ID, toItem);
				dbContext.SaveChanges();

				conn.Broadcast(msg, true, Channel.Reliable);
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
				character.InventoryController == null ||
				character.EquipmentController == null)
			{
				return;
			}

			if (character.InventoryController.TryGetItem(msg.inventoryIndex, out Item item) &&
				character.EquipmentController.Equip(item, msg.inventoryIndex, (ItemSlot)msg.slot))
			{
				if (character.InventoryController.TryGetItem(msg.inventoryIndex, out Item prevItem))
				{
					CharacterInventoryService.UpdateOrAdd(dbContext, character.ID, prevItem);
				}
				else
				{
					// remove the item from the database
					CharacterInventoryService.Delete(dbContext, character.ID, item.ID);
				}
				CharacterEquipmentService.SetSlot(dbContext, character.ID, item);
				dbContext.SaveChanges();

				conn.Broadcast(msg, true, Channel.Reliable);
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
				character.InventoryController == null ||
				character.EquipmentController == null)
			{
				return;
			}

			if (character.EquipmentController.TryGetItem(msg.slot, out Item item) &&
				character.EquipmentController.Unequip(character.InventoryController, msg.slot))
			{
				CharacterEquipmentService.Delete(dbContext, character.ID, item.ID);
				CharacterInventoryService.UpdateOrAdd(dbContext, character.ID, item);
				dbContext.SaveChanges();

				conn.Broadcast(msg, true, Channel.Reliable);
			}
		}
	}
}