using FishNet.Transporting;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIServerSelect : UIControl
	{
		/// <summary>
		/// Button to connect to the selected server.
		/// </summary>
		public Button ConnectToServerButton;
		/// <summary>
		/// Button to refresh the server list.
		/// </summary>
		public Button RefreshButton;
		/// <summary>
		/// Parent transform for server buttons.
		/// </summary>
		public RectTransform ServerParent;
		/// <summary>
		/// Prefab for individual server details button.
		/// </summary>
		public ServerDetailsButton ServerButtonPrefab;

		/// <summary>
		/// List of currently displayed server buttons.
		/// </summary>
		private List<ServerDetailsButton> serverList = new List<ServerDetailsButton>();
		/// <summary>
		/// The currently selected server button.
		/// </summary>
		private ServerDetailsButton selectedServer;

		/// <summary>
		/// How often the server list can be refreshed (seconds).
		/// </summary>
		public float RefreshRate = 5.0f;
		/// <summary>
		/// Time until next allowed refresh.
		/// </summary>
		private float nextRefresh = 0.0f;

		/// <summary>
		/// Called when the client is set. Registers for server list and authentication events.
		/// </summary>
		public override void OnClientSet()
		{
			nextRefresh = RefreshRate;

			Client.NetworkManager.ClientManager.RegisterBroadcast<ServerListBroadcast>(OnClientServerListBroadcastReceived);

			Client.LoginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;
		}

		/// <summary>
		/// Called when the client is unset. Unregisters server list and authentication events.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<ServerListBroadcast>(OnClientServerListBroadcastReceived);

			Client.LoginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;
		}

		/// <summary>
		/// Called when the UI is being destroyed. Cleans up server list.
		/// </summary>
		public override void OnDestroying()
		{
			DestroyServerList();
		}

		/// <summary>
		/// Unity Update loop. Handles refresh timer countdown.
		/// </summary>
		void Update()
		{
			if (nextRefresh > 0.0f)
			{
				nextRefresh -= Time.deltaTime;
			}
		}

		/// <summary>
		/// Handles authentication results and displays appropriate dialogs.
		/// </summary>
		/// <param name="result">The result of client authentication.</param>
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

		/// <summary>
		/// Shows a dialog box for login/authentication errors and returns to login screen.
		/// </summary>
		/// <param name="errorMsg">The error message to display.</param>
		private void OnLoginAuthenticationDialog(string errorMsg)
		{
			if (UIManager.TryGet("UIDialogBox", out UIDialogBox uiDialogBox))
			{
				uiDialogBox.Open(errorMsg);
			}
			SetConnectToServerLocked(false);

			OnClick_QuitToLogin();
		}

		/// <summary>
		/// Destroys all server buttons and clears the server list.
		/// </summary>
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

		/// <summary>
		/// Handles incoming server list broadcast, populates server buttons.
		/// </summary>
		/// <param name="msg">The broadcast message containing server details.</param>
		/// <param name="channel">The network channel used.</param>
		private void OnClientServerListBroadcastReceived(ServerListBroadcast msg, Channel channel)
		{
			if (msg.Servers != null)
			{
				DestroyServerList();

				serverList = new List<ServerDetailsButton>();
				for (int i = 0; i < msg.Servers.Count; ++i)
				{
					ServerDetailsButton newServer = Instantiate(ServerButtonPrefab, ServerParent);
					newServer.Initialize(msg.Servers[i]);
					newServer.OnServerSelected += OnServerSelected;
					serverList.Add(newServer);
				}
			}

			Show();
		}

		/// <summary>
		/// Handles server selection, updates button colors.
		/// </summary>
		/// <param name="button">The selected server button.</param>
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

		/// <summary>
		/// Called when the connect button is clicked. Initiates connection to selected server.
		/// </summary>
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

		/// <summary>
		/// Called when the refresh button is clicked. Requests an updated server list if allowed by timer.
		/// </summary>
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

		/// <summary>
		/// Called when quitting to login. Unlocks connect button.
		/// </summary>
		public override void OnQuitToLogin()
		{
			base.OnQuitToLogin();
			SetConnectToServerLocked(false);
		}

		/// <summary>
		/// Called when the quit to login button is clicked. Returns to login screen.
		/// </summary>
		public void OnClick_QuitToLogin()
		{
			Client.QuitToLogin();
		}

		/// <summary>
		/// Called when the quit button is clicked. Stops coroutines and quits client.
		/// </summary>
		public void OnClick_Quit()
		{
			StopAllCoroutines();
			Client.Quit();
		}

		/// <summary>
		/// Sets locked state for signing in (enables/disables connect button).
		/// </summary>
		/// <param name="locked">True to lock (disable) the button, false to unlock.</param>
		private void SetConnectToServerLocked(bool locked)
		{
			ConnectToServerButton.interactable = !locked;
		}
	}
}