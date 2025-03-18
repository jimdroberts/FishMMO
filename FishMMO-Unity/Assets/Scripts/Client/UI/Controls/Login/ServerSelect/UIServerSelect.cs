using FishNet.Transporting;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;

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

		public float RefreshRate = 5.0f;
		private float nextRefresh = 0.0f;

		public override void OnClientSet()
		{
			nextRefresh = RefreshRate;

			Client.NetworkManager.ClientManager.RegisterBroadcast<ServerListBroadcast>(OnClientServerListBroadcastReceived);

			Client.LoginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;
		}

		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<ServerListBroadcast>(OnClientServerListBroadcastReceived);

			Client.LoginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;
		}

		public override void OnDestroying()
		{
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
					OnLoginAuthenticationDialog("Invalid Username or Password.");
					break;
				case ClientAuthenticationResult.AlreadyOnline:
					OnLoginAuthenticationDialog("Account is already online.");
					break;
				case ClientAuthenticationResult.Banned:
					OnLoginAuthenticationDialog("Account is banned. Please contact the system administrator.");
					break;
				case ClientAuthenticationResult.ServerFull:
					OnLoginAuthenticationDialog("Server is currently full please wait a while and try again.");
					break;
				case ClientAuthenticationResult.LoginSuccess:
					break;
				case ClientAuthenticationResult.WorldLoginSuccess:
					break;
				case ClientAuthenticationResult.SceneLoginSuccess:
					{
						SetConnectToServerLocked(false);
						Hide();
					}
					break;
				default:
					break;
			}
		}

		private void OnLoginAuthenticationDialog(string errorMsg)
		{
			if (UIManager.TryGet("UIDialogBox", out UIDialogBox uiDialogBox))
			{
				uiDialogBox.Open(errorMsg);
			}
			SetConnectToServerLocked(false);

			OnClick_QuitToLogin();
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

		private void OnClientServerListBroadcastReceived(ServerListBroadcast msg, Channel channel)
		{
			if (msg.Servers != null)
			{
				DestroyServerList();

				serverList = new List<ServerDetailsButton>();
				for (int i = 0; i < msg.Servers.Count; ++i)
				{
					ServerDetailsButton newServer = Instantiate(serverButtonPrefab, serverParent);
					newServer.Initialize(msg.Servers[i]);
					newServer.OnServerSelected += OnServerSelected;
					serverList.Add(newServer);
				}
			}

			Show();
		}

		private void OnServerSelected(ServerDetailsButton button)
		{
			ServerDetailsButton prevButton = selectedServer;
			if (prevButton != null)
			{
				prevButton.ResetLabelColor();
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
				SetConnectToServerLocked(true);

				// Connect to the world server
				Client.ConnectToServer(selectedServer.Details.Address, selectedServer.Details.Port, true);
			}
		}

		public void OnClick_Refresh()
		{
			if (nextRefresh < 0)
			{
				nextRefresh = RefreshRate;

				// Request an updated server list
				RequestServerListBroadcast requestServerList = new RequestServerListBroadcast();
				Client.Broadcast(requestServerList, Channel.Reliable);
			}
		}

		public override void OnQuitToLogin()
		{
			base.OnQuitToLogin();
			SetConnectToServerLocked(false);
		}

		public void OnClick_QuitToLogin()
		{
			Client.QuitToLogin();
		}

		public void OnClick_Quit()
		{
			StopAllCoroutines();
			Client.Quit();
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