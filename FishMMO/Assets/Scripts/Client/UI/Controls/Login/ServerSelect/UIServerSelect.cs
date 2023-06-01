using FishNet.Managing;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	public class UIServerSelect : UIControl
	{
		public Button connectToServerButton;
		public Button refreshButton;
		public RectTransform serverParent;
		public ServerDetailsButton serverButtonPrefab;

		private List<ServerDetailsButton> serverList = new List<ServerDetailsButton>();
		private ServerDetailsButton selectedServer;

		public float refreshRate = 5.0f;
		private float nextRefresh = 0.0f;

		public override void OnStarting()
		{
			nextRefresh = refreshRate;

			Client.NetworkManager.ClientManager.RegisterBroadcast<ServerListBroadcast>(OnClientServerListBroadcastReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<WorldSceneConnectBroadcast>(OnClientWorldSceneConnectBroadcastReceived);

			Client.LoginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;
		}


		public override void OnDestroying()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<ServerListBroadcast>(OnClientServerListBroadcastReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<WorldSceneConnectBroadcast>(OnClientWorldSceneConnectBroadcastReceived);

			Client.LoginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;

			DestroyServerList();
		}

		void Update()
		{
			if (nextRefresh > 0.0f)
			{
				nextRefresh -= Time.deltaTime;
			}
		}

		private void Authenticator_OnClientAuthenticationResult(ClientAuthenticationResult result)
		{
			switch (result)
			{
				case ClientAuthenticationResult.InvalidUsernameOrPassword:
					break;
				case ClientAuthenticationResult.AlreadyOnline:
					break;
				case ClientAuthenticationResult.Banned:
					break;
				case ClientAuthenticationResult.LoginSuccess:
					break;
				case ClientAuthenticationResult.WorldLoginSuccess:
					break;
				case ClientAuthenticationResult.SceneLoginSuccess:
					SetConnectToServerLocked(false);
					visible = false;
					break;
				default:
					break;
			}
		}

		public void DestroyServerList()
		{
			if (serverList != null)
			{
				for (int i = 0; i < serverList.Count; ++i)
				{
					serverList[i].OnServerSelected -= OnServerSelected;
					Destroy(serverList[i].gameObject);
				}
				serverList.Clear();
			}
		}

		private void OnClientServerListBroadcastReceived(ServerListBroadcast msg)
		{
			if (msg.servers != null)
			{
				DestroyServerList();

				serverList = new List<ServerDetailsButton>();
				for (int i = 0; i < msg.servers.Count; ++i)
				{
					ServerDetailsButton newServer = Instantiate(serverButtonPrefab, serverParent);
					newServer.Initialize(msg.servers[i]);
					newServer.OnServerSelected += OnServerSelected;
					serverList.Add(newServer);
				}
			}

			visible = true;
		}

		private void OnClientWorldSceneConnectBroadcastReceived(WorldSceneConnectBroadcast msg)
		{
			if (Client.IsConnectionReady() &&
				selectedServer != null)
			{
				// connect to the scene server
				Client.ConnectToServer(msg.address, msg.port);

				SetConnectToServerLocked(true);
			}
		}

		private void OnServerSelected(ServerDetailsButton button)
		{
			ServerDetailsButton prevButton = selectedServer;
			if (prevButton != null)
			{
				prevButton.SetLabelColors(Color.black);
			}

			selectedServer = button;
			if (selectedServer != null)
			{
				selectedServer.SetLabelColors(Color.green);
			}
		}

		public void OnClick_ConnectToServer()
		{
			if (Client.IsConnectionReady() &&
				selectedServer != null)
			{
				// connect to the world server
				Client.ConnectToServer(selectedServer.details.address, selectedServer.details.port);

				SetConnectToServerLocked(true);
			}
		}

		public void OnClick_Refresh()
		{
			// TODO -- there should be a timer on the server too
			if (nextRefresh < 0)
			{
				nextRefresh = refreshRate;

				// request an updated server list
				RequestServerListBroadcast requestServerList = new RequestServerListBroadcast();
				Client.NetworkManager.ClientManager.Broadcast(requestServerList);
			}
		}

		public void OnClick_QuitToLogin()
		{
			StopAllCoroutines();

			// we should go back to login..
#if UNITY_EDITOR
			EditorApplication.ExitPlaymode();
#else
			Application.Quit();
#endif 
		}

		public void OnClick_Quit()
		{
			StopAllCoroutines();
#if UNITY_EDITOR
			EditorApplication.ExitPlaymode();
#else
			Application.Quit();
#endif
		}

		/// <summary>
		/// Sets locked state for signing in.
		/// </summary>
		private void SetConnectToServerLocked(bool locked)
		{
			connectToServerButton.interactable = !locked;
		}
	}
}