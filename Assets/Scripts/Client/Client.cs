using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using Server;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Client
{
	/// <summary>
	/// Client controls connecting to servers, 
	/// </summary>
	public class Client : MonoBehaviour
	{
		private static Client instance;
		public static Client Instance
		{
			get
			{
				if (instance == null)
				{
					instance = FindObjectOfType<Client>();
					if (instance == null)
					{
						GameObject go = new GameObject("ClientBootstrap");
						instance = go.AddComponent<Client>();
					}
				}
				return instance;
			}
		}

		private NetworkManager networkManager;
		private ClientLoginAuthenticator loginAuthenticator;
		private LocalConnectionState clientState = LocalConnectionState.Stopped;

		public List<ServerAddress> loginServerAddresses;

		void Awake()
		{
			networkManager = FindObjectOfType<NetworkManager>();
			loginAuthenticator = FindObjectOfType<ClientLoginAuthenticator>();
			if (networkManager == null)
			{
				Debug.LogError("Client: NetworkManager not found.");
				return;
			}
			else if (loginAuthenticator == null)
			{
				Debug.LogError("Client: LoginAuthenticator not found.");
				return;
			}
			else
			{
				networkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
				loginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;

				networkManager.ClientManager.RegisterBroadcast<SceneWorldReconnectBroadcast>(OnClientSceneWorldReconnectBroadcastReceived);
			}
		}

		/// <summary>
		/// SceneServer told the client to reconnect to the World server
		/// </summary>
		private void OnClientSceneWorldReconnectBroadcastReceived(SceneWorldReconnectBroadcast msg)
		{
			ConnectToServer(msg.address, msg.port);
		}

		/// <summary>
		/// Checks if the current connection is valid and started. Optional authentication check (Default True).
		/// </summary>
		public bool IsConnectionReady()
		{
			return IsConnectionReady(LocalConnectionState.Started, true);
		}
		/// <summary>
		/// Checks if the current connection is valid and started. Optional authentication check (Default True).
		/// </summary>
		public bool IsConnectionReady(bool requireAuthentication)
		{
			return IsConnectionReady(LocalConnectionState.Started, requireAuthentication);
		}
		/// <summary>
		/// Checks if the current connection is valid and started. Optional authentication check (Default True).
		/// </summary>
		public bool IsConnectionReady(LocalConnectionState clientState = LocalConnectionState.Started)
		{
			return IsConnectionReady(clientState, false);
		}
		/// <summary>
		/// Checks if the current connection is valid and started. Optional authentication check (Default True).
		/// </summary>
		public bool IsConnectionReady(LocalConnectionState clientState, bool requireAuthentication)
		{
			if (loginAuthenticator == null ||
				networkManager == null ||
				this.clientState != clientState)
			{
				return false;
			}

			if (requireAuthentication &&
				(!networkManager.ClientManager.Connection.IsValid ||
				!networkManager.ClientManager.Connection.Authenticated))
			{
				return false;
			}

			return true;
		}

		private void ClientManager_OnClientConnectionState(ClientConnectionStateArgs obj)
		{
			clientState = obj.ConnectionState;
		}

		private void Authenticator_OnClientAuthenticationResult(ClientAuthenticationResult result)
		{
			switch (result)
			{
				case ClientAuthenticationResult.InvalidUsernameOrPassword:
					break;
				case ClientAuthenticationResult.Banned:
					break;
				case ClientAuthenticationResult.LoginSuccess:
					break;
				case ClientAuthenticationResult.WorldLoginSuccess:
					break;
				case ClientAuthenticationResult.SceneLoginSuccess:
					break;
				default:
					break;
			}
		}

		public void ConnectToServer(string address, ushort port)
		{
			// stop current connection if any
			networkManager.ClientManager.StopConnection();

			// connect to the server
			StartCoroutine(OnAwaitingConnectionReady(address, port));
		}

		IEnumerator OnAwaitingConnectionReady(string address, ushort port)
		{
			// wait for the connection to the current server to stop
			while (clientState != LocalConnectionState.Stopped)
			{
				yield return new WaitForSeconds(0.2f);
			}

			// connect to the next server
			networkManager.ClientManager.StartConnection(address, port);

			yield return null;
		}

		public bool TryGetRandomLoginServerAddress(out ServerAddress serverAddress)
		{
			if (loginServerAddresses != null && loginServerAddresses.Count > 0)
			{
				// pick a random login server
				serverAddress = loginServerAddresses.GetRandom();
				return true;
			}
			serverAddress = default;
			return false;
		}
	}
}