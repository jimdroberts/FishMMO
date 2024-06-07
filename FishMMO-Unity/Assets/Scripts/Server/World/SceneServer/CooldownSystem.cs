using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Shared;
using System.Collections.Generic;

namespace FishMMO.Server
{
	// Cooldown System handles all of the cooldowns for the scene server.
	public class CooldownSystem : ServerBehaviour
	{
		private SceneServerAuthenticator loginAuthenticator;
		private LocalConnectionState serverState;

		/*public float saveRate = 60.0f;
		private float nextSave = 0.0f;

		public Dictionary<string, InventoryController> inventories = new Dictionary<string, InventoryController>();*/

		private Dictionary<long, CooldownInstance> cooldowns = new Dictionary<long, CooldownInstance>();

		public override void InitializeOnce()
		{
			//nextSave = saveRate;

			loginAuthenticator = FindObjectOfType<SceneServerAuthenticator>();
			if (loginAuthenticator == null)
				return;

			if (ServerManager != null)
			{
				ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;
				ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;

				loginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;
			}
			else
			{
				enabled = false;
			}
		}

		public override void Destroying()
		{
			if (ServerManager != null)
			{
				loginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;
			}
		}

		/*void LateUpdate()
		{
			if (serverState == LocalConnectionState.Started)
			{
				if (nextSave < 0)
				{
					nextSave = saveRate;
					
					Debug.Log("CharacterInventoryManager: Save" + "[" + DateTime.UtcNow + "]");

					// all characters inventories are periodically saved
					// TODO: create an InventoryService with a save inventories function
					//Database.Instance.SaveInventories(new List<Character>(characters.Values));
				}
				nextSave -= Time.deltaTime;
			}
		}*/

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			serverState = args.ConnectionState;
		}

		private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
		{
		}

		private void Authenticator_OnClientAuthenticationResult(NetworkConnection conn, bool authenticated)
		{
		}

		public void SendCooldownAddBroadcast(NetworkConnection conn, float value)
		{

		}
	}
}