using FishNet.Transporting;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Auth.Core;
using FishMMO.Logging;
using System;
using System.Collections;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit implementation of the login control providing username/email/password input,
	/// registration access, sign-in and the full authentication-result handling flow.
	/// </summary>
	/// <remarks>
	/// <para><b>Secure password input limitation:</b> <see cref="TextField"/> stores text as a
	/// managed <see cref="string"/> and exposes no <c>byte[]</c>, <c>char[]</c>, or
	/// <c>SecureString</c> API. .NET strings are immutable and cannot be deterministically zeroed.
	/// The password-field masking provides visual masking only. The SRP protocol ensures the
	/// password never travels on the wire, which is the primary protection.</para>
	/// </remarks>
	public class UITKLogin : UITKControl
	{
		/// <summary>
		/// The name of the username TextField in the UI.
		/// </summary>
		private const string USERNAME_NAME = "login-username";
		/// <summary>
		/// The name of the email TextField in the UI.
		/// </summary>
		private const string EMAIL_NAME = "login-email";
		/// <summary>
		/// The name of the password TextField in the UI.
		/// </summary>
		private const string PASSWORD_NAME = "login-password";
		/// <summary>
		/// The name of the age DropdownField in the UI.
		/// </summary>
		private const string AGE_SELECT_NAME = "login-age";
		/// <summary>
		/// The name of the register button in the UI.
		/// </summary>
		private const string REGISTER_BUTTON_NAME = "login-register-btn";
		/// <summary>
		/// The name of the sign-in button in the UI.
		/// </summary>
		private const string SIGN_IN_BUTTON_NAME = "login-signin-btn";
		/// <summary>
		/// The name of the options button in the UI.
		/// </summary>
		private const string OPTIONS_BUTTON_NAME = "login-options-btn";
		/// <summary>
		/// The name of the quit button in the UI.
		/// </summary>
		private const string QUIT_BUTTON_NAME = "login-quit-btn";
		/// <summary>
		/// The name of the handshake message Label in the UI.
		/// </summary>
		private const string HANDSHAKE_NAME = "login-handshake";

		private TextField username;
		private TextField email;
		private TextField password;
		private DropdownField ageSelect;
		private Button registerButton;
		private Button signInButton;
		private Label handshakeMessage;

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
		/// True when this panel owns the active authentication flow (login, account
		/// verification, or TOTP). Gates auth-result handling so results belonging to
		/// <see cref="UITKRegister"/> — which shares the same
		/// <see cref="ClientLoginAuthenticator.OnClientAuthenticationResult"/> event — are not
		/// acted on here.
		/// </summary>
		/// <remarks>
		/// Panel visibility cannot serve this purpose, even though it looks equivalent. Both
		/// multi-step flows hide this panel while a modal dialog collects the next input:
		/// <see cref="OnAccountUnverified"/> and <see cref="OnTwoFactorRequired"/> call
		/// <see cref="Hide"/> before opening theirs. Gating on <c>Visible</c> therefore dropped
		/// every result that arrived after that point — the <c>AccountVerified</c> that follows
		/// a correct verification code, and the <c>LoginSuccess</c> or <c>TwoFactorInvalid</c>
		/// that follows a TOTP code — so an account with 2FA enabled could enter the right code
		/// and simply sit on the dialog forever. <see cref="UILogin"/> and
		/// <see cref="UITKRegister"/> both use an explicit flag for exactly this reason.
		/// </remarks>
		private bool isAuthFlowActive;

		/// <summary>
		/// Resolves and caches visual elements and wires up button callbacks.
		/// </summary>
		public override void OnStarting()
		{
			if (Root == null)
			{
				return;
			}

			username = Root.Q<TextField>(USERNAME_NAME);
			email = Root.Q<TextField>(EMAIL_NAME);
			password = Root.Q<TextField>(PASSWORD_NAME);
			ageSelect = Root.Q<DropdownField>(AGE_SELECT_NAME);
			registerButton = Root.Q<Button>(REGISTER_BUTTON_NAME);
			signInButton = Root.Q<Button>(SIGN_IN_BUTTON_NAME);
			handshakeMessage = Root.Q<Label>(HANDSHAKE_NAME);

			if (password != null)
			{
				password.isPasswordField = true;
			}

			if (registerButton != null)
			{
				registerButton.clicked += OnClick_OnRegister;
			}
			if (signInButton != null)
			{
				signInButton.clicked += OnClick_Login;
			}

			Button optionsButton = Root.Q<Button>(OPTIONS_BUTTON_NAME);
			if (optionsButton != null)
			{
				optionsButton.clicked += OnClick_OnOptions;
			}

			Button quitButton = Root.Q<Button>(QUIT_BUTTON_NAME);
			if (quitButton != null)
			{
				quitButton.clicked += OnClick_Quit;
			}
		}

		/// <summary>
		/// Subscribes to connection and authentication events when the client is injected.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
			Client.LoginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;
			Client.OnReconnectFailed += ClientManager_OnReconnectFailed;
		}

		/// <summary>
		/// Unsubscribes from connection and authentication events when the client is cleared.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState -= ClientManager_OnClientConnectionState;
			Client.LoginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;
			Client.OnReconnectFailed -= ClientManager_OnReconnectFailed;
		}

		/// <summary>
		/// Shows the login panel and unlocks sign-in controls when quitting to login.
		/// </summary>
		public override void OnQuitToLogin()
		{
			base.OnQuitToLogin();

			Show();

			SetSignInLocked(false);
		}

		/// <summary>
		/// Hides the login panel and resets the handshake message.
		/// </summary>
		public override void Hide()
		{
			base.Hide();

			// Reset handshake message and hide the panel.
			if (handshakeMessage != null)
			{
				handshakeMessage.text = "";
			}
		}

		/// <summary>
		/// Handles client connection state changes. Resets handshake message and unlocks sign-in controls when disconnected.
		/// </summary>
		/// <param name="obj">Connection state arguments.</param>
		private void ClientManager_OnClientConnectionState(ClientConnectionStateArgs obj)
		{
			if (obj.ConnectionState == LocalConnectionState.Stopped)
			{
				if (handshakeMessage != null)
				{
					handshakeMessage.text = "";
				}
				SetSignInLocked(false);
				pendingVerifyUsername = null;
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
		private void Authenticator_OnClientAuthenticationResult(ClientAuthenticationResult result)
		{
			// Only process auth results when this panel owns the active flow. See
			// isAuthFlowActive for why panel visibility is not a usable substitute.
			if (!isAuthFlowActive) return;

			switch (result)
			{
				case ClientAuthenticationResult.AccountCreated:
					OnLoginAuthenticationDialog("Your account has been created!");
					break;
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
				case ClientAuthenticationResult.TokenInvalid:
				case ClientAuthenticationResult.TokenExpired:
				case ClientAuthenticationResult.TokenRevoked:
				case ClientAuthenticationResult.VersionMismatch:
					OnVersionMismatch();
					break;
				case ClientAuthenticationResult.TokenDecryptFailed:
					OnLoginAuthenticationDialog("Authentication failed. Please log in again.");
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

			if (UIManager.TryGetTK("UIDialogInputBox", out UITKDialogInputBox uiDialogInputBox))
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

			if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox uiDialogBox))
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
		/// <param name="message">The prompt to display.</param>
		private void OpenTotpDialog(string message)
		{
			if (UIManager.TryGetTK("UIDialogInputBox", out UITKDialogInputBox uiDialogInputBox))
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
		/// Called when the server rejects the client due to a game version mismatch.
		/// Shows a dialog and disconnects.
		/// </summary>
		private void OnVersionMismatch()
		{
			string myVersion = MainBootstrapSystem.GameVersion ?? "unknown";
			// TryGetTK, not TryGet: every other dialog in this panel uses the UI Toolkit box,
			// and a UI Toolkit scene has no UGUI UIDialogBox registered — so this lookup always
			// failed and the player was disconnected with no explanation at all.
			if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox uiDialogBox))
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
			if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox uiDialogBox))
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
			if (handshakeMessage != null)
			{
				handshakeMessage.text = "Connected";
			}

			OnLoginSuccessStart?.Invoke();

			Client.StartCoroutine(OnProcessLoginSuccess());
		}

		/// <summary>
		/// Coroutine for post-login processing, requests character list after delay.
		/// </summary>
		/// <returns>IEnumerator for coroutine.</returns>
		private IEnumerator OnProcessLoginSuccess()
		{
			// Wait 1 second before requesting the character list.
			yield return new WaitForSeconds(1.0f);

			Hide();

			// Request the character list after login is successfully finished.
			CharacterRequestListBroadcast requestCharacterList = new CharacterRequestListBroadcast();
			Client.Broadcast(requestCharacterList, Channel.Reliable);

			OnLoginSuccessEnd?.Invoke();

			SetSignInLocked(false);
		}

		/// <summary>
		/// Hides the login panel and shows the registration panel.
		/// </summary>
		public void OnClick_OnRegister()
		{
			if (UIManager.TryGetTK("UIRegister", out UITKRegister uiRegister))
			{
				Hide();
				uiRegister.Show();
			}
		}

		/// <summary>
		/// Shows the options panel. Options remains a reused UGUI overlay until its
		/// SettingOption-driven settings subsystem is converted in a dedicated pass.
		/// </summary>
		public void OnClick_OnOptions()
		{
			if (UIManager.TryGetTK("UIOptions", out UITKOptions uiOptions))
			{
				uiOptions.Show();
			}
		}

		/// <summary>
		/// Validates input and initiates the login process.
		/// </summary>
		public void OnClick_Login()
		{
			string usernameText = username != null ? username.value : null;
			string emailText = email != null ? email.value : null;

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
					if (handshakeMessage != null)
					{
						handshakeMessage.text = "Invalid username or email format. Use 3-32 characters (letters, numbers, underscores).";
					}
				}
				else
				{
					if (handshakeMessage != null)
					{
						handshakeMessage.text = "Please enter a username or email address.";
					}
				}
				Log.Warning("UITKLogin", "Login validation failed: invalid identifier.");
				return;
			}

			string passwordText = password != null ? password.value : null;
			if (!Authentication.IsAllowedPassword(passwordText))
			{
				if (string.IsNullOrWhiteSpace(passwordText))
				{
					if (handshakeMessage != null)
					{
						handshakeMessage.text = "Please enter a password.";
					}
				}
				else
				{
					if (handshakeMessage != null)
					{
						handshakeMessage.text = "Invalid password. Must be 8-32 characters with allowed symbols.";
					}
				}
				Log.Warning("UITKLogin", "Login validation failed: invalid password.");
				return;
			}

			SetSignInLocked(true);

			StartCoroutine(Client.GetLoginServerList((e) =>
			{
				if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox uiDialogBox))
				{
					uiDialogBox.Open(e);
				}
				Log.Warning("UITKLogin", e);
				SetSignInLocked(false);
			},
			(servers, token) =>
			{
					if (!string.IsNullOrEmpty(token)) Client.LoginAuthenticator.ConnectionToken = token;
				Connect("Connecting...", identifier, passwordText);
			}));
		}

		/// <summary>
		/// Attempts to connect to the login server with the provided credentials.
		/// </summary>
		/// <param name="handshakeMsg">Message to display during handshake.</param>
		/// <param name="identifier">Login identifier (username or email).</param>
		/// <param name="passwordText">Password to use.</param>
		/// <param name="address">Optional server address.</param>
		/// <param name="port">Optional server port.</param>
		private void Connect(string handshakeMsg, string identifier, string passwordText, string address = null, ushort port = 0)
		{
			// Validate preconditions individually so the player gets a specific error
			// message instead of the button silently unlocking with no feedback.
			if (!Client.IsConnectionReady(LocalConnectionState.Stopped))
			{
				if (handshakeMessage != null)
				{
					handshakeMessage.text = "Connection already in progress. Please wait.";
				}
				Log.Warning("UITKLogin", "Connect failed: connection is not in Stopped state.");
				SetSignInLocked(false);
				return;
			}

			if (!Authentication.IsAllowedUsername(identifier) && !Authentication.IsAllowedEmailUsername(identifier))
			{
				if (handshakeMessage != null)
				{
					handshakeMessage.text = "Invalid username or email format.";
				}
				Log.Warning("UITKLogin", "Connect failed: identifier validation failed.");
				SetSignInLocked(false);
				return;
			}

			if (!Authentication.IsAllowedPassword(passwordText))
			{
				if (handshakeMessage != null)
				{
					handshakeMessage.text = "Invalid password format.";
				}
				Log.Warning("UITKLogin", "Connect failed: password validation failed.");
				SetSignInLocked(false);
				return;
			}

			if (!Client.TryGetRandomLoginServerPort(out ushort serverPort))
			{
				if (handshakeMessage != null)
				{
					handshakeMessage.text = "No login servers available. Check your internet connection.";
				}
				Log.Warning("UITKLogin", "Connect failed: no login server addresses available.");
				SetSignInLocked(false);
				return;
			}

			if (handshakeMessage != null)
			{
				handshakeMessage.text = handshakeMsg;
			}
			Client.LoginAuthenticator.SetLoginCredentials(identifier, passwordText);
			Client.ConnectToServer(serverPort);
		}

		/// <summary>
		/// Quits the client application.
		/// </summary>
		public void OnClick_Quit()
		{
			Client.Quit();
		}

		/// <summary>
		/// Sets the locked state for signing in (enables/disables controls).
		/// </summary>
		/// <param name="locked">True to lock (disable) controls, false to unlock.</param>
		public void SetSignInLocked(bool locked)
		{
			// Track auth-flow ownership: locking = start, unlocking = end. Every path that
			// begins a login or a verification step locks, and every terminal path unlocks.
			isAuthFlowActive = locked;

			if (registerButton != null)
			{
				registerButton.SetEnabled(!locked);
			}
			if (signInButton != null)
			{
				signInButton.SetEnabled(!locked);
			}
			if (username != null)
			{
				username.SetEnabled(!locked);
			}
			if (email != null)
			{
				email.SetEnabled(!locked);
			}
			if (password != null)
			{
				password.SetEnabled(!locked);
			}
			if (ageSelect != null)
			{
				ageSelect.SetEnabled(!locked);
			}
		}
	}
}
