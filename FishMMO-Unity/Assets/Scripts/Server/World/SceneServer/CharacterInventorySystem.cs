using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Shared;

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

				ServerManager.RegisterBroadcast<EquipmentEquipItemBroadcast>(OnServerEquipmentItemBroadcastReceived, true);
				ServerManager.RegisterBroadcast<EquipmentUnequipItemBroadcast>(OnServerUnequipItemBroadcastReceived, true);
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				loginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;

				ServerManager.UnregisterBroadcast<InventoryRemoveItemBroadcast>(OnServerInventoryRemoveItemBroadcastReceived);
				ServerManager.UnregisterBroadcast<InventorySwapItemSlotsBroadcast>(OnServerInventorySwapItemSlotsBroadcastReceived);

				ServerManager.UnregisterBroadcast<EquipmentEquipItemBroadcast>(OnServerEquipmentItemBroadcastReceived);
				ServerManager.UnregisterBroadcast<EquipmentUnequipItemBroadcast>(OnServerUnequipItemBroadcastReceived);
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

			Character character = conn.FirstObject.GetComponent<Character>();
			if (character != null &&
				!character.IsTeleporting)
			{
				character.InventoryController.RemoveItem(msg.slot);
				conn.Broadcast(msg, true, Channel.Reliable);
			}
		}

		private void OnServerInventorySwapItemSlotsBroadcastReceived(NetworkConnection conn, InventorySwapItemSlotsBroadcast msg)
		{
			if (conn == null ||
				conn.FirstObject == null)
			{
				return;
			}

			Character character = conn.FirstObject.GetComponent<Character>();
			if (character != null &&
				!character.IsTeleporting &&
				character.InventoryController != null &&
				character.InventoryController.SwapItemSlots(msg.from, msg.to))
			{
				conn.Broadcast(msg, true, Channel.Reliable);
			}
		}

		private void OnServerEquipmentItemBroadcastReceived(NetworkConnection conn, EquipmentEquipItemBroadcast msg)
		{
			if (conn == null ||
				conn.FirstObject == null)
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
				conn.Broadcast(msg, true, Channel.Reliable);
			}
		}

		private void OnServerUnequipItemBroadcastReceived(NetworkConnection conn, EquipmentUnequipItemBroadcast msg)
		{
			if (conn == null ||
				conn.FirstObject == null)
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

			if (character.EquipmentController.Unequip(character.InventoryController, msg.slot))
			{
				conn.Broadcast(msg, true, Channel.Reliable);
			}
		}
	}
}