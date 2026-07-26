using FishNet.Transporting;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Auth.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit implementation of the world server selection control. Displays the available
	/// world servers, allows selecting and connecting to one, and refreshing the list.
	/// </summary>
	public class UITKServerSelect : UITKControl
	{
		/// <summary>
		/// The name of the connect button in the UI.
		/// </summary>
		private const string CONNECT_BUTTON_NAME = "server-connect-btn";
		/// <summary>
		/// The name of the refresh button in the UI.
		/// </summary>
		private const string REFRESH_BUTTON_NAME = "server-refresh-btn";
		/// <summary>
		/// The name of the quit-to-login button in the UI.
		/// </summary>
		private const string QUIT_LOGIN_BUTTON_NAME = "server-quit-login-btn";
		/// <summary>
		/// The name of the quit button in the UI.
		/// </summary>
		private const string QUIT_BUTTON_NAME = "server-quit-btn";
		/// <summary>
		/// The name of the server list container in the UI.
		/// </summary>
		private const string SERVER_LIST_NAME = "server-list";

		/// <summary>
		/// The USS class applied to each server row.
		/// </summary>
		private const string SERVER_ROW_CLASS = "server-row";
		/// <summary>
		/// The USS class applied to the selected server row.
		/// </summary>
		private const string SERVER_ROW_SELECTED_CLASS = "server-row--selected";
		/// <summary>
		/// The USS class applied to the server name label within a row.
		/// </summary>
		private const string SERVER_ROW_NAME_CLASS = "server-row__name";
		/// <summary>
		/// The USS class applied to the server status label within a row.
		/// </summary>
		private const string SERVER_ROW_STATUS_CLASS = "server-row__status";

		/// <summary>
		/// View model for a single server row.
		/// </summary>
		private class ServerRow
		{
			/// <summary>
			/// The root VisualElement for this server row.
			/// </summary>
			public VisualElement Root;
			/// <summary>
			/// The Label displaying the server name.
			/// </summary>
			public Label Name;
			/// <summary>
			/// The Label displaying the server status.
			/// </summary>
			public Label Status;
			/// <summary>
			/// The world server details associated with this row.
			/// </summary>
			public WorldServerDetails Details;
		}

		private VisualElement serverListContainer;
		private Button connectButton;

		/// <summary>
		/// The list of all server row view models currently displayed.
		/// </summary>
		private readonly List<ServerRow> serverList = new List<ServerRow>();
		/// <summary>
		/// The currently selected server row, or null if none is selected.
		/// </summary>
		private ServerRow selectedServer;

		/// <summary>
		/// How often the server list can be refreshed (seconds).
		/// </summary>
		public float RefreshRate = 5.0f;

		/// <summary>
		/// Time until the next allowed refresh.
		/// </summary>
		private float nextRefresh = 0.0f;

		/// <summary>
		/// Resolves and caches visual elements and wires up button callbacks.
		/// </summary>
		public override void OnStarting()
		{
			if (Root == null)
			{
				return;
			}

			serverListContainer = Root.Q<VisualElement>(SERVER_LIST_NAME);
			connectButton = Root.Q<Button>(CONNECT_BUTTON_NAME);

			if (connectButton != null)
			{
				connectButton.clicked += OnClick_ConnectToServer;
			}

			Button refreshButton = Root.Q<Button>(REFRESH_BUTTON_NAME);
			if (refreshButton != null)
			{
				refreshButton.clicked += OnClick_Refresh;
			}

			Button quitToLoginButton = Root.Q<Button>(QUIT_LOGIN_BUTTON_NAME);
			if (quitToLoginButton != null)
			{
				quitToLoginButton.clicked += OnClick_QuitToLogin;
			}

			Button quitButton = Root.Q<Button>(QUIT_BUTTON_NAME);
			if (quitButton != null)
			{
				quitButton.clicked += OnClick_Quit;
			}
		}

		/// <summary>
		/// Registers for server list and authentication events when the client is injected.
		/// </summary>
		public override void OnClientSet()
		{
			nextRefresh = RefreshRate;

			Client.NetworkManager.ClientManager.RegisterBroadcast<ServerListBroadcast>(OnClientServerListBroadcastReceived);

			Client.LoginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;
		}

		/// <summary>
		/// Unregisters server list and authentication events when the client is cleared.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<ServerListBroadcast>(OnClientServerListBroadcastReceived);

			Client.LoginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;
		}

		/// <summary>
		/// Cleans up the server list when the control is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			DestroyServerList();
		}

		/// <summary>
		/// Unity Update callback. Decrements the refresh cooldown timer each frame.
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
				case ClientAuthenticationResult.ServerBusy:
					OnLoginAuthenticationDialog("Server is busy. Please try again.");
					break;
				case ClientAuthenticationResult.NoCharacterSelected:
					OnLoginAuthenticationDialog("No character selected. Please select a character first.");
					break;
				case ClientAuthenticationResult.TokenInvalid:
				case ClientAuthenticationResult.TokenExpired:
				case ClientAuthenticationResult.TokenRevoked:
				case ClientAuthenticationResult.TokenDecryptFailed:
					OnLoginAuthenticationDialog("Authentication failed. Please log in again.");
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
				// Not applicable during server select flow.
				case ClientAuthenticationResult.AccountCreated:
				case ClientAuthenticationResult.SrpVerify:
				case ClientAuthenticationResult.SrpProof:
				case ClientAuthenticationResult.AccountUnverified:
				case ClientAuthenticationResult.AccountVerified:
				case ClientAuthenticationResult.TwoFactorRequired:
				case ClientAuthenticationResult.TwoFactorInvalid:
					break;
			}
		}

		/// <summary>
		/// Shows a dialog box for login/authentication errors and returns to the login screen.
		/// </summary>
		/// <param name="errorMsg">The error message to display.</param>
		private void OnLoginAuthenticationDialog(string errorMsg)
		{
			if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox uiDialogBox))
			{
				uiDialogBox.Open(errorMsg);
			}
			SetConnectToServerLocked(false);

			OnClick_QuitToLogin();
		}

		/// <summary>
		/// Removes all server rows and clears the server list.
		/// </summary>
		public void DestroyServerList()
		{
			for (int i = 0; i < serverList.Count; ++i)
			{
				serverList[i].Root.RemoveFromHierarchy();
			}
			serverList.Clear();
			selectedServer = null;
		}

		/// <summary>
		/// Handles an incoming server list broadcast and populates the server rows.
		/// </summary>
		/// <param name="msg">The broadcast message containing server details.</param>
		/// <param name="channel">The network channel used.</param>
		private void OnClientServerListBroadcastReceived(ServerListBroadcast msg, Channel channel)
		{
			if (msg.Servers != null && serverListContainer != null)
			{
				DestroyServerList();

				for (int i = 0; i < msg.Servers.Length; ++i)
				{
					CreateServerRow(msg.Servers[i]);
				}
			}

			Show();
		}

		/// <summary>
		/// Builds a server row element for the supplied server details.
		/// </summary>
		/// <param name="details">The world server details.</param>
		private void CreateServerRow(WorldServerDetails details)
		{
			ServerRow row = new ServerRow()
			{
				Details = details,
			};

			VisualElement rowRoot = new VisualElement();
			rowRoot.AddToClassList(SERVER_ROW_CLASS);

			Label name = new Label((details.Locked ? "[Locked] " : " ") + details.Name);
			name.AddToClassList(SERVER_ROW_NAME_CLASS);

			Label status = new Label(details.CharacterCount.ToString());
			status.AddToClassList(SERVER_ROW_STATUS_CLASS);

			rowRoot.Add(name);
			rowRoot.Add(status);
			rowRoot.RegisterCallback<PointerDownEvent>((evt) => OnServerSelected(row));

			row.Root = rowRoot;
			row.Name = name;
			row.Status = status;

			serverListContainer.Add(rowRoot);
			serverList.Add(row);
		}

		/// <summary>
		/// Handles server selection and updates the highlighted row.
		/// </summary>
		/// <param name="row">The selected server row.</param>
		private void OnServerSelected(ServerRow row)
		{
			if (selectedServer != null)
			{
				selectedServer.Root.RemoveFromClassList(SERVER_ROW_SELECTED_CLASS);
			}

			selectedServer = row;
			if (selectedServer != null)
			{
				selectedServer.Root.AddToClassList(SERVER_ROW_SELECTED_CLASS);
			}
		}

		/// <summary>
		/// Initiates connection to the selected world server.
		/// Fetches a fresh IPFetch connection token first — World rejects the first
		/// ClientHandshake without one (cookieLen=0 / no real IP bound).
		/// </summary>
		public void OnClick_ConnectToServer()
		{
			if (Client.IsConnectionReady() &&
				selectedServer != null)
			{
				SetConnectToServerLocked(true);

				ushort port = selectedServer.Details.Port;
				// Drop any leftover login token cache so handoff always gets a fresh HMAC
				// (login already consumed the prior token; TTL is ~60s).
				Client.InvalidateLoginServerCache();
				StartCoroutine(Client.ConnectToServerWithConnectionToken(
					port,
					isWorldServer: true,
					onFail: (err) =>
					{
						if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox uiDialogBox))
							uiDialogBox.Open(err ?? "Failed to fetch world connection token.");
						SetConnectToServerLocked(false);
					}));
			}
		}

		/// <summary>
		/// Requests an updated server list if allowed by the refresh timer.
		/// </summary>
		public void OnClick_Refresh()
		{
			if (nextRefresh < 0)
			{
				nextRefresh = RefreshRate;

				// Request an updated server list.
				RequestServerListBroadcast requestServerList = new RequestServerListBroadcast();
				Client.Broadcast(requestServerList, Channel.Reliable);
			}
		}

		/// <summary>
		/// Unlocks the connect button when quitting to login.
		/// </summary>
		public override void OnQuitToLogin()
		{
			base.OnQuitToLogin();
			SetConnectToServerLocked(false);
		}

		/// <summary>
		/// Returns to the login screen.
		/// </summary>
		public void OnClick_QuitToLogin()
		{
			Client.QuitToLogin();
		}

		/// <summary>
		/// Stops coroutines and quits the client.
		/// </summary>
		public void OnClick_Quit()
		{
			StopAllCoroutines();
			Client.Quit();
		}

		/// <summary>
		/// Sets the locked state of the connect button.
		/// </summary>
		/// <param name="locked">True to lock (disable) the button, false to unlock.</param>
		private void SetConnectToServerLocked(bool locked)
		{
			if (connectButton != null)
			{
				connectButton.SetEnabled(!locked);
			}
		}
	}
}
