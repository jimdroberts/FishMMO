using FishNet.Connection;
using FishNet.Transporting;

namespace Server
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
				nextSave -= Time.deltaTime;
				if (nextSave < 0)
				{
					nextSave = saveRate;
					
					Debug.Log("[" + DateTime.UtcNow + "] CharacterInventoryManager: Save");

					// all characters inventories are periodically saved
					// TODO: create an InventoryService with a save inventories function
					//Database.Instance.SaveInventories(new List<Character>(characters.Values));
				}
			}
		}*/

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs obj)
		{
			loginAuthenticator = FindObjectOfType<SceneServerAuthenticator>();
			if (loginAuthenticator == null)
				return;

			serverState = obj.ConnectionState;

			if (obj.ConnectionState == LocalConnectionState.Started)
			{
				loginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;

				ServerManager.RegisterBroadcast<InventoryRemoveItemBroadcast>(OnServerInventoryRemoveItemBroadcastReceived, true);
				ServerManager.RegisterBroadcast<InventoryMoveItemBroadcast>(OnServerInventoryMoveItemBroadcastReceived, true);
			}
			else if (obj.ConnectionState == LocalConnectionState.Stopped)
			{
				loginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;

				ServerManager.UnregisterBroadcast<InventoryRemoveItemBroadcast>(OnServerInventoryRemoveItemBroadcastReceived);
				ServerManager.UnregisterBroadcast<InventoryMoveItemBroadcast>(OnServerInventoryMoveItemBroadcastReceived);
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
			if (conn.FirstObject == null)
			{
				return;
			}

			InventoryController inventory = conn.FirstObject.GetComponent<InventoryController>();
			if (inventory == null)
			{
				// no inventory???
				return;
			}
		}

		private void OnServerInventoryMoveItemBroadcastReceived(NetworkConnection conn, InventoryMoveItemBroadcast msg)
		{
			if (conn.FirstObject == null)
			{
				return;
			}

			InventoryController inventory = conn.FirstObject.GetComponent<InventoryController>();
			if (inventory == null)
			{
				// no inventory???
				return;
			}

			if (inventory.SwapItemSlots(msg.fromSlot, msg.toSlot))
			{
				conn.Broadcast(msg);
			}
		}
	}
}