using FishNet.Transporting;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;
using FishMMO.Auth.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI control for selecting a world server from the available server list.
	/// Manages server list display, selection highlighting, refresh timing,
	/// and connection to the chosen world server.
	/// </summary>
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
		/// Cached label of <see cref="RefreshButton"/>, resolved on first use.
		/// </summary>
		private TMPro.TMP_Text refreshButtonLabel;
		/// <summary>
		/// The button's original text, restored when the cooldown ends.
		/// </summary>
		private string refreshButtonReadyText = "Refresh";
		/// <summary>
		/// Last countdown value written to the label, so it is only rewritten on change.
		/// -1 forces the first update through.
		/// </summary>
		private int lastShownCooldownSeconds = -1;

		/// <summary>
		/// Called when the client is set. Registers for server list and authentication events.
		/// </summary>
		public override void OnClientSet()
		{
			/* Start ready rather than on cooldown. The cooldown exists to stop the list
			 * being spammed, which the first request cannot do — and blocking it meant a
			 * player who arrived before the server's own push (or whose push failed) had a
			 * dead Refresh button for five seconds with no indication why. */
			nextRefresh = 0.0f;
			UpdateRefreshButton();

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
				UpdateRefreshButton();
			}
		}

		/// <summary>
		/// Keeps the refresh button's enabled state and label in step with the cooldown.
		/// </summary>
		/// <remarks>
		/// The refresh request is gated on <see cref="nextRefresh"/>, so for the first few
		/// seconds after this panel appears the button silently did nothing when clicked. A
		/// player waiting on a server list that has not arrived reads that as the client
		/// having hung. Disabling the button and counting down makes the wait legible.
		/// <para>Redundant writes are skipped: the label is rebuilt only when the displayed
		/// whole second actually changes, not every frame.</para>
		/// </remarks>
		private void UpdateRefreshButton()
		{
			if (RefreshButton == null)
			{
				return;
			}

			bool ready = nextRefresh <= 0.0f;
			if (RefreshButton.interactable != ready)
			{
				RefreshButton.interactable = ready;
			}

			if (refreshButtonLabel == null)
			{
				refreshButtonLabel = RefreshButton.GetComponentInChildren<TMPro.TMP_Text>();
				if (refreshButtonLabel == null)
				{
					return;
				}
				refreshButtonReadyText = refreshButtonLabel.text;
			}

			int secondsLeft = ready ? 0 : Mathf.CeilToInt(nextRefresh);
			if (secondsLeft == lastShownCooldownSeconds)
			{
				return;
			}
			lastShownCooldownSeconds = secondsLeft;

			refreshButtonLabel.text = ready
				? refreshButtonReadyText
				: $"{refreshButtonReadyText} ({secondsLeft})";
		}

		/// <summary>
		/// Handles authentication results and displays appropriate dialogs.
		/// </summary>
		/// <param name="result">The result of client authentication.</param>
		private void Authenticator_OnClientAuthenticationResult(ClientAuthenticationResult result)
		{
			// Only react while this panel is shown. ClientLoginAuthenticator raises this
			// event to every subscriber, so without the guard a result belonging to another
			// panel's flow (e.g. a wrong password on the login screen) is handled here too,
			// and OnLoginAuthenticationDialog/OnClick_QuitToLogin below tears down the
			// connection and resets state before the owning panel's handler ever runs.
			// UILogin guards the same event with its own isAuthFlowActive flag.
			if (!Visible) return;

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
					// Null check first: touching OnServerSelected on an already-destroyed
					// button throws MissingReferenceException, which aborts the rest of the
					// teardown and leaks every entry after it.
					if (serverList[i] == null)
					{
						continue;
					}
					serverList[i].OnServerSelected -= OnServerSelected;
					Destroy(serverList[i].gameObject);
				}
				serverList.Clear();
			}

			/* Clear the selection with the list it points into. A refresh destroys every
			 * button, so leaving this set left it referencing a destroyed component — which
			 * Unity reports as null, so the Connect button silently did nothing until the
			 * player noticed they had to pick a server again. UITKServerSelect already does
			 * this; this is the copy that did not. */
			selectedServer = null;
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
				for (int i = 0; i < msg.Servers.Length; ++i)
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
				selectedServer != null &&
				selectedServer.Details != null)
			{
				SetConnectToServerLocked(true);

				// Connect to the world server. The token is requested from the Login Server
				// first — the World Server sits behind the same proxy and needs the real IP,
				// and the Login Server is the only party that still knows it. Requesting it
				// now (rather than at login) also means a long stay on this screen or in the
				// login queue cannot outlive the token's short expiry.
				Client.RequestHopTokenThenConnect(selectedServer.Details.Port, true);
			}
		}

		/// <summary>
		/// Called when the refresh button is clicked. Requests an updated server list if allowed by timer.
		/// </summary>
		public void OnClick_Refresh()
		{
			/* <= 0, not < 0: the cooldown is initialised to exactly 0 so the first
			 * request is allowed immediately, and a strict < would never match it. */
			if (nextRefresh <= 0.0f)
			{
				nextRefresh = RefreshRate;
				UpdateRefreshButton();

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