using FishNet.Transporting;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;
using FishMMO.Auth.Core;
using FishMMO.Logging;
using System;
using System.Collections;

namespace FishMMO.Client
{
	/// <summary>
	/// Login UI control providing username/password input, registration, and sign-in.
	/// </summary>
	/// <remarks>
	/// <para><b>Secure password input limitation:</b> <see cref="TMP_InputField"/> stores
	/// text as a managed <see cref="string"/> (<c>m_Text</c>) and exposes no <c>byte[]</c>,
	/// <c>char[]</c>, or <c>SecureString</c> API. .NET strings are immutable and cannot be
	/// deterministically zeroed; <c>Password.text = null</c> only removes the reference \u2014
	/// the string data lingers in the managed heap until overwritten by the GC.</para>
	/// <para>The <c>InputType.Password</c> content type provides visual masking (asterisks) only.
	/// This is an inherent limitation of the Unity UI stack and the .NET runtime.
	/// The SRP protocol ensures the password never travels on the wire, which is the
	/// primary protection. See also the design note on <c>ClientLoginAuthenticator.password</c>.</para>
	/// </remarks>
	public class UILogin : UIControl
	{
		/// <summary>
		/// Input field for the username.
		/// </summary>
		public TMP_InputField Username;
		/// <summary>
		/// Input field for the email address.
		/// </summary>
		public TMP_InputField Email;
		/// <summary>
		/// Input field for the password.
		/// </summary>
		public TMP_InputField Password;
		/// <summary>
		/// Dropdown for age selection. Must match the age used during registration.
		/// </summary>
		public TMP_Dropdown AgeSelect;
		/// <summary>
		/// Button to register a new account.
		/// </summary>
		public Button RegisterButton;
		/// <summary>
		/// Button to sign in to an account.
		/// </summary>
		public Button SignInButton;
		/// <summary>
		/// Text field for displaying handshake and status messages.
		/// </summary>
		public TMP_Text HandshakeMSG;

		/// <summary>
		/// Called when a Login Success Client Authentication result is received from the server.
		/// </summary>
		public Action OnLoginSuccessStart;
		/// <summary>
		/// Called after OnLoginSuccessStart finishes.
		/// </summary>
		public Action OnLoginSuccessEnd;

		/// <summary>
		/// Temporarily stores the login identifier for verification code submission
		/// when the server responds with AccountUnverified.
		/// </summary>
		private string pendingVerifyUsername;

		/// <summary>
		/// Called when the client is set. Subscribes to connection and authentication events.
		/// </summary>
		public override void OnStarting()
		{
			base.OnStarting();

			// Wire button listeners programmatically only if the scene does not
			// already have persistent OnClick bindings (avoids double-fire when
			// Inspector wiring is present, while surviving scene/prefab reimports
			// that clear Inspector OnClick lists).
			if (RegisterButton != null && RegisterButton.onClick.GetPersistentEventCount() == 0)
				RegisterButton.onClick.AddListener(OnClick_OnRegister);
			if (SignInButton != null && SignInButton.onClick.GetPersistentEventCount() == 0)
				SignInButton.onClick.AddListener(OnClick_Login);
		}

		/// <inheritdoc/>
		public override void OnDestroying()
		{
			base.OnDestroying();

			if (RegisterButton != null) RegisterButton.onClick.RemoveListener(OnClick_OnRegister);
			if (SignInButton != null) SignInButton.onClick.RemoveListener(OnClick_Login);
		}

		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
			Client.NetworkManager.ClientManager.RegisterBroadcast<LoginQueuePositionBroadcast>(OnLoginQueuePosition);
			Client.LoginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;
			Client.OnReconnectFailed += ClientManager_OnReconnectFailed;
		}

		/// <summary>
		/// Keeps the reply watchdog alive while this client is waiting in the login queue.
		/// </summary>
		/// <remarks>
		/// The queue is the one place a login legitimately takes longer than
		/// <see cref="PendingReplyGuard.DefaultTimeoutSeconds"/>, and its position updates are
		/// handled by <see cref="Client"/> rather than by this panel — so the watchdog saw a
		/// server that had said nothing for thirty seconds, announced that it had not responded,
		/// and handed the sign-in controls back while the queue dialog was still counting down
		/// beside it. Clicking sign-in from there only produced "connection already in progress".
		/// <para>
		/// A position update is proof the server is still working this login, so it buys the
		/// deadline again exactly like an intermediate auth result does. Positions above zero
		/// travel unreliably, but they are re-sent every couple of seconds, so a dropped one
		/// costs nothing.
		/// </para>
		/// </remarks>
		private void OnLoginQueuePosition(LoginQueuePositionBroadcast msg, Channel channel)
		{
			// -1 means the wait was abandoned; Client tears the session down and explains it,
			// and refreshing a deadline for a login that is over would only delay this panel
			// noticing.
			if (msg.QueuePosition >= 0)
			{
				replyGuard.Refresh();
			}
		}


		/// <summary>
		/// Called when the client is unset. Unsubscribes from connection and authentication events.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState -= ClientManager_OnClientConnectionState;
			Client.NetworkManager.ClientManager.UnregisterBroadcast<LoginQueuePositionBroadcast>(OnLoginQueuePosition);
			Client.LoginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;
			Client.OnReconnectFailed -= ClientManager_OnReconnectFailed;
		}

		/// <summary>
		/// Called when quitting to login. Shows the login panel and unlocks sign-in controls.
		/// </summary>
		public override void OnQuitToLogin()
		{
			base.OnQuitToLogin();

			Show();

			SetSignInLocked(false);
		}

		/// <summary>
		/// Hides the login panel and resets handshake message.
		/// </summary>
		public override void Hide()
		{
			base.Hide();

			// Reset handshake message and hide the panel
			HandshakeMSG.text = "";
		}

		/// <summary>
		/// Handles client connection state changes. Resets handshake message and unlocks sign-in controls when disconnected.
		/// </summary>
		/// <param name="obj">Connection state arguments.</param>
		private void ClientManager_OnClientConnectionState(ClientConnectionStateArgs obj)
		{
			Log.Debug("UILogin", $"[{GetInstanceID()}] ClientManager_OnClientConnectionState: state={obj.ConnectionState} isAuthFlowActive={isAuthFlowActive}");
			if (obj.ConnectionState == LocalConnectionState.Stopped)
			{
				HandshakeMSG.text = "";
				pendingVerifyUsername = null;
				// Deferred by a frame: the server may send a final auth result (e.g.
				// InvalidUsernameOrPassword) and then close the connection in the same
				// tick. That result is delivered via a queued main-thread callback that
				// can arrive after this Stopped event. Unlocking synchronously here would
				// clear isAuthFlowActive before Authenticator_OnClientAuthenticationResult
				// processes that already-in-flight result, silently swallowing it.
				// The "dropped without explanation" verdict is deferred with it, for the
				// same reason: judged here it would be true for every such result, and we
				// would tell the player the server never answered a frame before showing
				// them its answer.
				// Coroutines can't start on an inactive GameObject - this handler fires
				// for both UILogin and UIRegister regardless of which panel is shown, so
				// fall back to an immediate unlock when hidden (isAuthFlowActive is
				// already false there, since a hidden panel never initiated the flow).
				if (gameObject.activeInHierarchy)
				{
					StartCoroutine(DeferredUnlockAfterDisconnect());
				}
				else
				{
					SetSignInLocked(false);
				}
			}
		}

		/// <summary>
		/// Tells the player that sign-in ended without the server saying why.
		/// </summary>
		/// <remarks>See <see cref="authResultSeen"/> for when this applies.</remarks>
		private void ShowUnexplainedDisconnect()
		{
			const string message = "Could not sign in.\nThe connection to the login server was closed before it answered.\n" +
				"Please check your connection and try again, and make sure your client is up to date.";

			Show();
			HandshakeMSG.text = "Sign-in failed. The server closed the connection.";

			if (UIManager.TryGet("UIDialogBox", out UIDialogBox uiDialogBox))
			{
				uiDialogBox.Open(message);
			}
			else
			{
				Log.Warning("UILogin", message);
			}
		}

		/// <summary>
		/// Unlocks sign-in controls shortly after disconnect, giving an already-in-flight
		/// auth result callback a chance to run first. A single frame is not enough — the
		/// result is produced by server-side SRP verification and marshaled back to the
		/// main thread, which can take longer than one Update tick. If the proper result
		/// handler (e.g. <see cref="OnLoginAuthenticationDialog"/>) already ran and cleared
		/// isAuthFlowActive by the time this check runs, this is a harmless no-op.
		/// See <see cref="ClientManager_OnClientConnectionState"/>.
		/// </summary>
		private IEnumerator DeferredUnlockAfterDisconnect()
		{
			Log.Debug("UILogin", $"[{GetInstanceID()}] DeferredUnlockAfterDisconnect: started, isAuthFlowActive={isAuthFlowActive}");
			yield return new WaitForSeconds(1.5f);
			Log.Debug("UILogin", $"[{GetInstanceID()}] DeferredUnlockAfterDisconnect: resumed after wait, isAuthFlowActive={isAuthFlowActive}");

			/* Judged here rather than at the Stopped event, and read before
			 * SetSignInLocked clears isAuthFlowActive. A result that was already in
			 * flight when the connection dropped has had the wait above to arrive and
			 * set authResultSeen; asking at Stopped time would call every one of those
			 * unexplained and show the "server never answered" dialog immediately
			 * before the server's actual answer. */
			bool droppedWithoutExplanation = isAuthFlowActive && !authResultSeen;

			SetSignInLocked(false);

			if (droppedWithoutExplanation)
			{
				ShowUnexplainedDisconnect();
			}
		}

		/// <summary>
		/// Handles reconnect failure. Shows login panel and unlocks sign-in controls.
		/// </summary>
		private void ClientManager_OnReconnectFailed()
		{
			Show();
			SetSignInLocked(false);
		}

		/// <summary>
		/// Handles authentication results and displays appropriate dialogs or proceeds with login success.
		/// </summary>
		/// <param name="result">The result of client authentication.</param>
		/// <summary>
		/// True when this panel has an active authentication flow (login, verification,
		/// or TOTP). Used to gate auth-result handling and prevent cross-talk with
		/// UIRegister, which shares the same <see cref="ClientLoginAuthenticator.OnClientAuthenticationResult"/> event.
		/// </summary>
		private bool isAuthFlowActive;

		/// <summary>
		/// True once the server has answered this panel's in-flight request with an
		/// authentication result, so the player has already been told what happened.
		/// </summary>
		/// <remarks>
		/// The login handshake is refused before authentication for reasons the server
		/// deliberately does not put on the wire — an expired or unverifiable connection token,
		/// a protocol version outside the supported range, an oversized field, a tripped
		/// handshake rate limit. Every one of those is a bare transport close, and the Stopped
		/// handler's job is to hand the controls back, so the player clicked Sign In and watched
		/// the form reset with no message at all. Losing the login server mid-flow looked
		/// identical.
		/// <para>
		/// This distinguishes "the server said no" from "the server said nothing": a result of
		/// any kind means the specific message has already been shown and the generic one would
		/// only contradict it. Reset when a new attempt starts.
		/// </para>
		/// </remarks>
		private bool authResultSeen;

		private void Authenticator_OnClientAuthenticationResult(ClientAuthenticationResult result)
		{
			Log.Debug("UILogin", $"[{GetInstanceID()}] Authenticator_OnClientAuthenticationResult: result={result} isAuthFlowActive={isAuthFlowActive}");
			/* Any result is the server telling us it is still working this request —
			 * the SRP exchange and the two-factor prompt both report progress before
			 * they finish, and a client can sit in the login queue for minutes. Each
			 * one buys the reply deadline again rather than counting against it. */
			replyGuard.Refresh();

			// Only process auth results when this panel owns the active flow.
			// Without this guard, hidden panels would still react to auth results
			// intended for the other panel (e.g., UILogin receiving AccountCreated
			// during UIRegister's registration and force-disconnecting).
			if (!isAuthFlowActive) return;

			// The server answered, so whatever happens next has an explanation of its own.
			authResultSeen = true;

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
				case ClientAuthenticationResult.AccountUnverified:
					OnAccountUnverified();
					break;
				case ClientAuthenticationResult.AccountVerified:
					OnAccountVerified();
					break;
				case ClientAuthenticationResult.TwoFactorRequired:
					OnTwoFactorRequired();
					break;
				case ClientAuthenticationResult.TwoFactorInvalid:
					OnTwoFactorInvalid();
					break;
				case ClientAuthenticationResult.LoginSuccess:
					OnLoginSuccess();
					break;
				case ClientAuthenticationResult.ServerFull:
					OnLoginAuthenticationDialog("Server is currently full please wait a while and try again.");
					break;
				case ClientAuthenticationResult.ServerBusy:
					OnLoginAuthenticationDialog("Server is busy. Please try again.");
					break;
				case ClientAuthenticationResult.VersionMismatch:
					OnVersionMismatch();
					break;
				/* A rejected session token is not a version problem. Reporting it as one told
				 * the player to update a client that is perfectly current, and hid the real
				 * cause — an expired or revoked session — behind advice that cannot fix it. */
				case ClientAuthenticationResult.TokenInvalid:
				case ClientAuthenticationResult.TokenExpired:
				case ClientAuthenticationResult.TokenRevoked:
				case ClientAuthenticationResult.TokenDecryptFailed:
					OnLoginAuthenticationDialog("Your session has expired. Please log in again.");
					break;
				// Not applicable during login flow.
				case ClientAuthenticationResult.SrpVerify:
				case ClientAuthenticationResult.SrpProof:
				case ClientAuthenticationResult.WorldLoginSuccess:
				case ClientAuthenticationResult.SceneLoginSuccess:
				case ClientAuthenticationResult.NoCharacterSelected:
					break;
			}
		}

		/// <summary>
		/// Handles AccountUnverified: stays connected and opens the verification code input dialog.
		/// Sets <see cref="pendingVerifyUsername"/> only now — not eagerly in OnClick_Login —
		/// so the identifier doesn't linger in memory if the connection drops before this point.
		/// </summary>
		private void OnAccountUnverified()
		{
			// Capture identifier from authenticator (still set at this point; cleared only after SRP proof).
			string identifier = Client.LoginAuthenticator.PendingLoginIdentifier;
			if (string.IsNullOrEmpty(identifier))
			{
				// Identifier already cleared — can't verify. Return to login.
				Client.ForceDisconnect();
				SetSignInLocked(false);
				return;
			}
			pendingVerifyUsername = identifier;
			SetSignInLocked(true);
			Hide();

			if (UIManager.TryGet("UIDialogInputBox", out UIDialogInputBox uiDialogInputBox))
			{
				uiDialogInputBox.Open(
					"Your account has not been verified. Please enter the verification code sent to your email.",
					(code) =>
					{
						if (!string.IsNullOrWhiteSpace(pendingVerifyUsername) && !string.IsNullOrWhiteSpace(code))
						{
							Client.LoginAuthenticator.SendVerifyCode(pendingVerifyUsername, code.Trim());
						}
					},
					() =>
					{
						pendingVerifyUsername = null;
						Client.ForceDisconnect();
						SetSignInLocked(false);
						Show();
					});
			}
		}

		/// <summary>
		/// Handles successful account verification: disconnects and returns to the login screen.
		/// </summary>
		private void OnAccountVerified()
		{
			pendingVerifyUsername = null;
			Client.ForceDisconnect();
			SetSignInLocked(false);

			if (UIManager.TryGet("UIDialogBox", out UIDialogBox uiDialogBox))
			{
				uiDialogBox.Open("Your account has been verified! You may now log in.");
			}
			Show();
		}

		/// <summary>
		/// Handles TwoFactorRequired: opens a TOTP code input dialog.
		/// </summary>
		private void OnTwoFactorRequired()
		{
			SetSignInLocked(true);
			Hide();
			OpenTotpDialog("Enter the 6-digit code from your authenticator app.");
		}

		/// <summary>
		/// Handles TwoFactorInvalid: re-opens the TOTP dialog with an error message.
		/// </summary>
		private void OnTwoFactorInvalid()
		{
			OpenTotpDialog("Invalid code. Please try again.");
		}

		/// <summary>
		/// Opens the TOTP code input dialog with the specified prompt.
		/// </summary>
		private void OpenTotpDialog(string message)
		{
			if (UIManager.TryGet("UIDialogInputBox", out UIDialogInputBox uiDialogInputBox))
			{
				uiDialogInputBox.Open(
					message,
					(code) =>
					{
						if (!string.IsNullOrWhiteSpace(code))
						{
							Client.LoginAuthenticator.SendTotpCode(code.Trim());
						}
					},
					() =>
					{
						pendingVerifyUsername = null;
						Client.ForceDisconnect();
						SetSignInLocked(false);
						Show();
					});
			}
		}

		/// <summary>
		/// Shows a dialog box for login/authentication errors and disconnects client.
		/// </summary>
		/// <summary>
		/// Called when the server rejects the client due to a game version mismatch.
		/// Shows a dialog and disconnects.
		/// </summary>
		private void OnVersionMismatch()
		{
			string myVersion = MainBootstrapSystem.GameVersion ?? "unknown";
			if (UIManager.TryGet("UIDialogBox", out UIDialogBox uiDialogBox))
			{
				uiDialogBox.Open($"Game version mismatch.\n\nYour client is version {myVersion}.\nThe server expects a different version.\n\nPlease update your client to match the server.");
			}
			Client.ForceDisconnect();
			SetSignInLocked(false);
		}

		/// <summary>
		/// Shows a dialog for authentication errors and disconnects the client.
			/// </summary>
			/// <param name="errorMsg">The error message to display.</param>
			private void OnLoginAuthenticationDialog(string errorMsg)
		{
			bool found = UIManager.TryGet("UIDialogBox", out UIDialogBox uiDialogBox);
			Log.Debug("UILogin", $"OnLoginAuthenticationDialog: found={found} msg={errorMsg}");
			if (found)
			{
				uiDialogBox.Open(errorMsg);
			}
			Client.ForceDisconnect();
			SetSignInLocked(false);
		}

		/// <summary>
		/// Handles successful login, updates handshake message, and starts post-login coroutine.
		/// </summary>
		private void OnLoginSuccess()
		{
			HandshakeMSG.text = "Connected";

			OnLoginSuccessStart?.Invoke();

			Client.StartCoroutine(OnProcessLoginSuccess());
		}

		/// <summary>
		/// Coroutine for post-login processing, requests character list after delay.
		/// </summary>
		/// <returns>IEnumerator for coroutine.</returns>
		IEnumerator OnProcessLoginSuccess()
		{
			// Wait 1 second before requesting the character list
			yield return new WaitForSeconds(1.0f);

			Hide();

			// Request the character list after login is successfully finished
			CharacterRequestListBroadcast requestCharacterList = new CharacterRequestListBroadcast();
			Client.Broadcast(requestCharacterList, Channel.Reliable);

			OnLoginSuccessEnd?.Invoke();

			SetSignInLocked(false);
		}

		/// <summary>
		/// Called when the register button is clicked. Hides the login panel and shows the registration panel.
		/// </summary>
		public void OnClick_OnRegister()
		{
			if (UIManager.TryGet("UIRegister", out UIRegister uiRegister))
			{
				Hide();
				uiRegister.Show();
			}
		}

		/// <summary>
		/// Called when the options button is clicked. Shows the options panel.
		/// </summary>
		public void OnClick_OnOptions()
		{
			if (UIManager.TryGet("UIOptions", out UIOptions uiOptions))
			{
				uiOptions.Show();
			}
		}

		/// <summary>
		/// Called when the login button is clicked. Validates input and initiates login process.
		/// </summary>
		public void OnClick_Login()
		{
			string usernameText = Username != null ? Username.text : string.Empty;
			string emailText = Email != null ? Email.text : string.Empty;

			// Determine which identifier to use: prefer email if filled, otherwise username.
			string identifier;
			if (!string.IsNullOrWhiteSpace(emailText) && Authentication.IsAllowedEmailUsername(emailText))
			{
				identifier = emailText;
			}
			else if (!string.IsNullOrWhiteSpace(usernameText) && Authentication.IsAllowedUsername(usernameText))
			{
				identifier = usernameText;
			}
			else
			{
				// Provide user-facing feedback so the player knows why the button did nothing.
				if (!string.IsNullOrWhiteSpace(emailText) || !string.IsNullOrWhiteSpace(usernameText))
				{
					HandshakeMSG.text = "Invalid username or email format. Use 3-32 characters (letters, numbers, underscores).";
				}
				else
				{
					HandshakeMSG.text = "Please enter a username or email address.";
				}
				Log.Warning("UILogin", "Login validation failed: invalid identifier.");
				return;
			}

			if (Password == null || !Authentication.IsAllowedPassword(Password.text))
			{
				if (Password == null || string.IsNullOrWhiteSpace(Password.text))
				{
					HandshakeMSG.text = "Please enter a password.";
				}
				else
				{
					HandshakeMSG.text = "Invalid password. Must be 8-32 characters with allowed symbols.";
				}
				Log.Warning("UILogin", "Login validation failed: invalid password.");
				return;
			}

			authResultSeen = false;
			SetSignInLocked(true);

			string password = Password.text;

			StartCoroutine(Client.GetLoginServerList((e) =>
			{
				if (UIManager.TryGet("UIDialogBox", out UIDialogBox uiDialogBox))
				{
					uiDialogBox.Open(e);
				}
				Log.Warning("UILogin", e);
				SetSignInLocked(false);
			},
			(servers, token) =>
			{
					Log.Debug("UILogin", $"GetLoginServerList onDone: tokenIsNullOrEmpty={string.IsNullOrEmpty(token)}");
					if (!string.IsNullOrEmpty(token)) Client.LoginAuthenticator.ConnectionToken = token;
				Connect("Connecting...", identifier, password);
			}));
		}

		/// <summary>
		/// Attempts to connect to the login server with provided credentials.
		/// </summary>
		/// <param name="handshakeMessage">Message to display during handshake.</param>
		/// <param name="identifier">Login identifier (username or email).</param>
		/// <param name="password">Password to use.</param>
		/// <param name="address">Optional server address.</param>
		/// <param name="port">Optional server port.</param>
		private void Connect(string handshakeMessage, string identifier, string password, string address = null, ushort port = 0)
		{
			// Validate preconditions individually so the player gets a specific error
			// message instead of the button silently unlocking with no feedback.
			if (!Client.IsConnectionReady(LocalConnectionState.Stopped))
			{
				HandshakeMSG.text = "Connection already in progress. Please wait.";
				Log.Warning("UILogin", "Connect failed: connection is not in Stopped state.");
				SetSignInLocked(false);
				return;
			}

			if (!Authentication.IsAllowedUsername(identifier) && !Authentication.IsAllowedEmailUsername(identifier))
			{
				HandshakeMSG.text = "Invalid username or email format.";
				Log.Warning("UILogin", "Connect failed: identifier validation failed.");
				SetSignInLocked(false);
				return;
			}

			if (!Authentication.IsAllowedPassword(password))
			{
				HandshakeMSG.text = "Invalid password format.";
				Log.Warning("UILogin", "Connect failed: password validation failed.");
				SetSignInLocked(false);
				return;
			}

			if (!Client.TryGetRandomLoginServerPort(out ushort serverPort))
			{
				HandshakeMSG.text = "No login servers available. Check your internet connection.";
				Log.Warning("UILogin", "Connect failed: no login server addresses available.");
				SetSignInLocked(false);
				return;
			}

			HandshakeMSG.text = handshakeMessage;
			Client.LoginAuthenticator.SetLoginCredentials(identifier, password);
			Client.ConnectToServer(serverPort);
		}

		/// <summary>
		/// Called when the quit button is clicked. Quits the client application.
		/// </summary>
		public void OnClick_Quit()
		{
			Client.Quit();
		}


		/// <summary>
		/// Guards the control this panel disables while a server reply is outstanding.
		/// </summary>
		/// <remarks>See <see cref="PendingReplyGuard"/>.</remarks>
		private readonly PendingReplyGuard replyGuard = new PendingReplyGuard();

		/// <inheritdoc/>
		protected override void OnTick()
		{
			base.OnTick();

			if (replyGuard.HasExpired())
			{
				ReleaseControls(true);
				if (HandshakeMSG != null) HandshakeMSG.text = "The server did not respond. Please try again.";
			}
		}

		/// <summary>
		/// Sets locked state for signing in (enables/disables controls).
		/// Also manages the <see cref="isAuthFlowActive"/> flag: locking marks
		/// the start of an auth flow; unlocking marks its termination. This flag
		/// gates <see cref="Authenticator_OnClientAuthenticationResult"/> to prevent
		/// cross-talk with UIRegister.
		/// </summary>
		/// <param name="locked">True to lock (disable) controls, false to unlock.</param>
		public void SetSignInLocked(bool locked)
		{
			// Locking means a request is outstanding; unlocking means it is not.
			// See PendingReplyGuard for why the wait needs a deadline.
			if (locked) { replyGuard.Begin(); } else { replyGuard.Clear(); }

			// Track auth-flow ownership: locking = start, unlocking = end.
			// This prevents hidden panels from reacting to auth results intended
			// for the other panel (e.g., UILogin receiving AccountCreated during
			// UIRegister's registration and force-disconnecting).
			if (locked) isAuthFlowActive = true;
			else isAuthFlowActive = false;

			ReleaseControls(!locked);
		}

		/// <summary>
		/// Enables or disables this panel's controls without touching the auth-flow flag.
		/// </summary>
		/// <remarks>
		/// Split out for the reply timeout. <c>SetSignInLocked(false)</c> also clears
		/// <c>isAuthFlowActive</c>, which is what gates this panel's auth-result handler —
		/// so handing the controls back that way would make the panel ignore a reply that
		/// arrives after the deadline, turning a merely slow login into a stuck one. The
		/// timeout is deliberately non-destructive: it re-enables the controls and says so,
		/// and a late reply is still handled normally.
		/// </remarks>
		/// <param name="interactable">True to enable the controls.</param>
		private void ReleaseControls(bool interactable)
		{
			if (RegisterButton != null) RegisterButton.interactable = interactable;
			if (SignInButton != null) SignInButton.interactable = interactable;
			if (Username != null) Username.enabled = interactable;
			if (Email != null) Email.enabled = interactable;
			if (Password != null) Password.enabled = interactable;
			if (AgeSelect != null) AgeSelect.interactable = interactable;
		}
	}
}