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
		private const string USERNAME_NAME = "login-username";
		private const string EMAIL_NAME = "login-email";
		private const string PASSWORD_NAME = "login-password";
		private const string AGE_SELECT_NAME = "login-age";
		private const string REGISTER_BUTTON_NAME = "login-register-btn";
		private const string SIGN_IN_BUTTON_NAME = "login-signin-btn";
		private const string OPTIONS_BUTTON_NAME = "login-options-btn";
		private const string QUIT_BUTTON_NAME = "login-quit-btn";
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
		/// Shows a dialog box for login/authentication errors and disconnects the client.
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
				return;
			}

			string passwordText = password != null ? password.value : null;
			if (!Authentication.IsAllowedPassword(passwordText))
			{
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
			(servers) =>
			{
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
			if (Client.IsConnectionReady(LocalConnectionState.Stopped) &&
				(Authentication.IsAllowedUsername(identifier) || Authentication.IsAllowedEmailUsername(identifier)) &&
				Authentication.IsAllowedPassword(passwordText) &&
				Client.TryGetRandomLoginServerAddress(out ServerAddress serverAddress) &&
				Authentication.IsAddressValid(serverAddress.Address))
			{
				if (handshakeMessage != null)
				{
					handshakeMessage.text = handshakeMsg;
				}
				Client.LoginAuthenticator.SetLoginCredentials(identifier, passwordText);
				Client.ConnectToServer(serverAddress.Address, serverAddress.Port);
			}
			else
			{
				SetSignInLocked(false);
			}
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
