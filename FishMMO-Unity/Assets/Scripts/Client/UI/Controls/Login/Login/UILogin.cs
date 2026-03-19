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
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
			Client.LoginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;
			Client.OnReconnectFailed += ClientManager_OnReconnectFailed;
		}

		/// <summary>
		/// Called when the client is unset. Unsubscribes from connection and authentication events.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState -= ClientManager_OnClientConnectionState;
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
			if (obj.ConnectionState == LocalConnectionState.Stopped)
			{
				HandshakeMSG.text = "";
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
				case ClientAuthenticationResult.WorldLoginSuccess:
					break;
				case ClientAuthenticationResult.ServerFull:
					OnLoginAuthenticationDialog("Server is currently full please wait a while and try again.");
					break;
				default:
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
		/// <param name="errorMsg">The error message to display.</param>
		private void OnLoginAuthenticationDialog(string errorMsg)
		{
			if (UIManager.TryGet("UIDialogBox", out UIDialogBox uiDialogBox))
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
			string usernameText = Username.text;
			string emailText = Email.text;

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

			if (!Authentication.IsAllowedPassword(Password.text))
			{
				return;
			}

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
			(servers) =>
			{
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
			if (Client.IsConnectionReady(LocalConnectionState.Stopped) &&
				(Authentication.IsAllowedUsername(identifier) || Authentication.IsAllowedEmailUsername(identifier)) &&
				Authentication.IsAllowedPassword(password) &&
				Client.TryGetRandomLoginServerAddress(out ServerAddress serverAddress) &&
				Authentication.IsAddressValid(serverAddress.Address))
			{
				HandshakeMSG.text = handshakeMessage;
				Client.LoginAuthenticator.SetLoginCredentials(identifier, password);
				Client.ConnectToServer(serverAddress.Address, serverAddress.Port);
			}
			else
			{
				SetSignInLocked(false);
			}
		}

		/// <summary>
		/// Called when the quit button is clicked. Quits the client application.
		/// </summary>
		public void OnClick_Quit()
		{
			Client.Quit();
		}

		/// <summary>
		/// Sets locked state for signing in (enables/disables controls).
		/// </summary>
		/// <param name="locked">True to lock (disable) controls, false to unlock.</param>
		public void SetSignInLocked(bool locked)
		{
			RegisterButton.interactable = !locked;
			SignInButton.interactable = !locked;
			Username.enabled = !locked;
			Email.enabled = !locked;
			Password.enabled = !locked;
			AgeSelect.interactable = !locked;
		}
	}
}