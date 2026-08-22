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
		/// Cached refresh button, disabled while the refresh cooldown is running.
		/// </summary>
		private Button refreshButton;
		/// <summary>
		/// The refresh button's original text, restored when the cooldown ends.
		/// </summary>
		private string refreshButtonReadyText = "Refresh";
		/// <summary>
		/// Last countdown value written to the button, so it is only rewritten on change.
		/// -1 forces the first update through.
		/// </summary>
		private int lastShownCooldownSeconds = -1;

		/// <summary>
		/// The list of all server row view models currently displayed.
		/// </summary>
		private readonly List<ServerRow> serverList = new List<ServerRow>();
		/// <summary>
		/// The currently selected server row, or null if none is selected.
		/// </summary>
		private ServerRow selectedServer;

		/// <summary>
		/// The worlds as the server last described them.
		/// </summary>
		/// <remarks>
		/// The rows used to be the only record of the list, and rows do not survive this panel.
		/// It is authored <c>StartOpen: 0</c>, so the very first world list a client ever
		/// receives arrives while there is no visual tree: row construction was gated on
		/// <c>serverListContainer != null</c>, that gate was false, and the <see cref="Show"/> on
		/// the line below it then presented an empty world list to a player who had just
		/// successfully selected a character. The same happens on every later arrival that lands
		/// while the panel is hidden, because showing it re-clones the UXML.
		/// <para>
		/// Holding the details here makes the rows a rendering of state, which
		/// <see cref="RebuildServerRows"/> re-runs against whatever tree is on screen. See
		/// <see cref="UITKControl.OnAfterShow"/>.
		/// </para>
		/// </remarks>
		private readonly List<WorldServerDetails> servers = new List<WorldServerDetails>();

		/// <summary>
		/// Name of the world the player had highlighted, so the highlight survives a rebuild.
		/// </summary>
		private string selectedServerName;

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

			refreshButton = Root.Q<Button>(REFRESH_BUTTON_NAME);
			if (refreshButton != null)
			{
				refreshButtonReadyText = refreshButton.text;
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

			// Enter connects to the highlighted world, Escape goes back to the login screen.
			// Enter observes the same lock as the Connect button it mirrors; see LoginKeys.Attach.
			LoginKeys.Attach(Root, OnClick_ConnectToServer, OnClick_QuitToLogin, () => !replyGuard.IsPending);
			LoginKeys.SetTabOrder(Root, connectButton, refreshButton, quitToLoginButton, quitButton);
		}

		/// <summary>
		/// Registers for server list and authentication events when the client is injected.
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
		protected override void OnTick()
		{
			base.OnTick();

			// Login-flow notices are refused while another dialog is up; see LoginNotice.
			LoginNotice.Pump();

			if (nextRefresh > 0.0f)
			{
				nextRefresh -= Time.deltaTime;
				UpdateRefreshButton();
			}

			if (replyGuard.HasExpired())
			{
				/* Connect disables its own button and then waits for a hop token, a world-server
				 * connection and a SceneLoginSuccess — three separate things, any of which can
				 * simply not happen. This was the one login lock in the whole flow with no
				 * deadline behind it, so a token request the login server never answered left the
				 * player looking at a world list whose only useful button was permanently
				 * greyed out. */
				SetConnectToServerLocked(false);
				Show();
				LoginNotice.Show("The server did not respond to the connection request. Please try again.");
			}
		}

		/// <summary>
		/// Guards the connect button while the world-server handoff is outstanding.
		/// </summary>
		/// <remarks>See <see cref="PendingReplyGuard"/>.</remarks>
		private readonly PendingReplyGuard replyGuard = new PendingReplyGuard();

		/// <summary>
		/// Keeps the refresh button's enabled state and text in step with the cooldown.
		/// </summary>
		/// <remarks>
		/// The refresh request is gated on <see cref="nextRefresh"/>, so for the first few
		/// seconds after this panel appears the button silently did nothing when clicked. A
		/// player waiting on a server list that has not arrived reads that as the client
		/// having hung. Disabling the button and counting down makes the wait legible.
		/// <para>Redundant writes are skipped: the text is rebuilt only when the displayed
		/// whole second actually changes, not every frame.</para>
		/// </remarks>
		private void UpdateRefreshButton()
		{
			if (refreshButton == null)
			{
				return;
			}

			bool ready = nextRefresh <= 0.0f;
			if (refreshButton.enabledSelf != ready)
			{
				refreshButton.SetEnabled(ready);
			}

			int secondsLeft = ready ? 0 : Mathf.CeilToInt(nextRefresh);
			if (secondsLeft == lastShownCooldownSeconds)
			{
				return;
			}
			lastShownCooldownSeconds = secondsLeft;

			refreshButton.text = ready
				? refreshButtonReadyText
				: $"{refreshButtonReadyText} ({secondsLeft})";
		}

		/// <summary>
		/// Handles authentication results and displays appropriate dialogs.
		/// </summary>
		/// <param name="result">The result of client authentication.</param>
		private void Authenticator_OnClientAuthenticationResult(ClientAuthenticationResult result)
		{
			/* Any result is the server still working this handoff — the world connection
			 * re-runs the SRP exchange, which reports progress before it finishes — so each one
			 * buys the reply deadline again rather than counting against it. Before the panel
			 * visibility check, because the panel hides itself on SceneLoginSuccess. */
			replyGuard.Refresh();

			// Only react while this panel is shown. ClientLoginAuthenticator raises this
			// event to every subscriber, so without the guard a result belonging to another
			// panel's flow (e.g. a wrong password on the login screen) is handled here too,
			// and OnLoginAuthenticationDialog below steps on the owning panel's own error
			// handling. Mirrors the guard in UIServerSelect.
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
				case ClientAuthenticationResult.ServerLocked:
					OnLoginAuthenticationDialog("This world is closed for maintenance. Please try again later.");
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
			LoginNotice.Show(errorMsg);
			SetConnectToServerLocked(false);

			OnClick_QuitToLogin();
		}

		/// <summary>
		/// Forgets the world list and removes every row.
		/// </summary>
		public void DestroyServerList()
		{
			ClearServerRows();
			servers.Clear();
			selectedServerName = null;
		}

		/// <summary>
		/// Removes the row elements without forgetting the worlds they describe.
		/// </summary>
		/// <remarks>
		/// Separate from <see cref="DestroyServerList"/> because a tree rebuild throws the rows
		/// away and must not throw the world list away with them.
		/// </remarks>
		private void ClearServerRows()
		{
			for (int i = 0; i < serverList.Count; ++i)
			{
				serverList[i].Root.RemoveFromHierarchy();
			}
			serverList.Clear();
			selectedServer = null;
		}

		/// <summary>
		/// Renders <see cref="servers"/> into whatever visual tree is currently on screen.
		/// </summary>
		/// <remarks>
		/// Idempotent, and a no-op while there is no tree to build into — the panel is shown
		/// later and this runs again from <see cref="OnAfterStarting"/>.
		/// </remarks>
		private void RebuildServerRows()
		{
			if (serverListContainer == null)
			{
				return;
			}

			ClearServerRows();

			for (int i = 0; i < servers.Count; ++i)
			{
				CreateServerRow(servers[i]);
			}

			RestoreSelection();
		}

		/// <summary>
		/// Re-highlights the world the player had chosen, if it is still in the list.
		/// </summary>
		private void RestoreSelection()
		{
			if (string.IsNullOrEmpty(selectedServerName))
			{
				return;
			}

			for (int i = 0; i < serverList.Count; ++i)
			{
				if (serverList[i].Details != null &&
					serverList[i].Details.Name == selectedServerName)
				{
					OnServerSelected(serverList[i]);
					return;
				}
			}

			// That world is no longer being advertised.
			selectedServerName = null;
		}

		/// <summary>
		/// Handles an incoming server list broadcast and populates the server rows.
		/// </summary>
		/// <param name="msg">The broadcast message containing server details.</param>
		/// <param name="channel">The network channel used.</param>
		private void OnClientServerListBroadcastReceived(ServerListBroadcast msg, Channel channel)
		{
			/* State first, rows second, and no gate on the container existing. See the remarks on
			 * `servers` for what that gate cost: the first world list of every session was
			 * discarded before it could be drawn. */
			if (msg.Servers != null)
			{
				servers.Clear();

				for (int i = 0; i < msg.Servers.Length; ++i)
				{
					servers.Add(msg.Servers[i]);
				}
			}

			RebuildServerRows();

			/* Only if the player is still somewhere this list belongs. A world list is pushed
			 * after a character selection and again on every refresh, so one can arrive just
			 * after a quit to login — and showing unconditionally put the world list on top of
			 * the sign-in form of an account that had already logged out. */
			if (!LoginScreenOwnsTheScreen())
			{
				Show();
			}
		}

		/// <summary>
		/// Whether the player has gone back to the sign-in screen.
		/// </summary>
		/// <remarks>
		/// The login and registration panels are upstream of this one: reaching either means the
		/// session this list describes is over, or was never entered.
		/// </remarks>
		private bool LoginScreenOwnsTheScreen()
		{
			return (UIManager.TryGetTK("UILogin", out UITKControl login) && login.Visible) ||
				(UIManager.TryGetTK("UIRegister", out UITKControl register) && register.Visible);
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

			// By name, because the row object does not survive a tree rebuild. See RestoreSelection.
			selectedServerName = selectedServer?.Details?.Name;
		}

		/// <summary>
		/// Initiates connection to the selected world server.
		/// </summary>
		public void OnClick_ConnectToServer()
		{
			if (Client.IsConnectionReady() &&
				selectedServer != null &&
				selectedServer.Details != null)
			{
				SetConnectToServerLocked(true);

				// Connect to the world server, asking the Login Server for a connection
				// token first — the World Server is behind the same proxy and needs the real
				// IP, which only the Login Server still knows. Mirrors UIServerSelect.
				Client.RequestHopTokenThenConnect(selectedServer.Details.Port, true);
			}
		}

		/// <summary>
		/// Requests an updated server list if allowed by the refresh timer.
		/// </summary>
		public void OnClick_Refresh()
		{
			/* <= 0, not < 0: the cooldown is initialised to exactly 0 so the first
			 * request is allowed immediately, and a strict < would never match it. */
			if (nextRefresh <= 0.0f)
			{
				nextRefresh = RefreshRate;
				UpdateRefreshButton();

				// Request an updated server list.
				RequestServerListBroadcast requestServerList = new RequestServerListBroadcast();
				Client.Broadcast(requestServerList, Channel.Reliable);
			}
		}

		/// <summary>
		/// Unlocks the connect button and forgets the world list when quitting to login.
		/// </summary>
		/// <remarks>
		/// The list belongs to the account that just logged out — it is sent in answer to that
		/// account's character selection. Leaving it behind meant the next account to sign in saw
		/// the previous one's worlds, with the previous one's row still highlighted, until its own
		/// list arrived; and saw them indefinitely if it never did. UITKCharacterSelect has
		/// cleared its list here for the same reason.
		/// </remarks>
		public override void OnQuitToLogin()
		{
			base.OnQuitToLogin();
			SetConnectToServerLocked(false);
			DestroyServerList();
		}

		/// <summary>
		/// Re-renders the world list and the control states after the visual tree was rebuilt.
		/// </summary>
		/// <remarks>
		/// Every element this panel writes to is replaced on each hide/show. The connect lock
		/// matters as much as the rows do: it came back enabled while the hop-token request it
		/// was guarding was still outstanding, so a second click started a second handoff.
		/// </remarks>
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			ReapplyPanelState();
		}

		/// <inheritdoc cref="OnAfterStarting"/>
		protected override void OnAfterShow()
		{
			base.OnAfterShow();
			ReapplyPanelState();
		}

		/// <summary>
		/// Writes everything this panel holds as state into the tree that is on screen now.
		/// </summary>
		private void ReapplyPanelState()
		{
			RebuildServerRows();

			if (connectButton != null)
			{
				connectButton.SetEnabled(!replyGuard.IsPending);
			}

			// The countdown is written only when the displayed second changes, so a new button
			// element needs the cached value invalidated or it would keep the UXML's bare text.
			lastShownCooldownSeconds = -1;
			UpdateRefreshButton();
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
			// Locking means a request is outstanding; unlocking means it is not.
			// See PendingReplyGuard for why the wait needs a deadline.
			if (locked) { replyGuard.Begin(); } else { replyGuard.Clear(); }

			if (connectButton != null)
			{
				connectButton.SetEnabled(!locked);
			}
		}
	}
}
