using FishNet.Transporting;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;
using FishMMO.Logging;
using System;
using System.Collections;

namespace FishMMO.Client
{
	public class UILogin : UIControl
	{
		/// <summary>
		/// Input field for the username.
		/// </summary>
		public TMP_InputField Username;
		/// <summary>
		/// Input field for the password.
		/// </summary>
		public TMP_InputField Password;
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
		/// Called when the register button is clicked. Initiates account creation process.
		/// </summary>
		public void OnClick_OnRegister()
		{
			SetSignInLocked(true);

			StartCoroutine(Client.GetLoginServerList((e) =>
			{
				if (UIManager.TryGet("UIDialogBox", out UIDialogBox uiDialogBox))
				{
					uiDialogBox.Open(e);
				}
				Log.Error("UILogin", e);
				SetSignInLocked(false);
			},
			(servers) =>
			{
				Connect("Creating account.", Username.text, Password.text, true);
			}));
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
			if (!Constants.Authentication.IsAllowedUsername(Username.text) ||
				!Constants.Authentication.IsAllowedPassword(Password.text))
			{
				return;
			}

			SetSignInLocked(true);

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
				Connect("Connecting...", Username.text, Password.text);
			}));
		}

		/// <summary>
		/// Attempts to connect to the login server with provided credentials.
		/// </summary>
		/// <param name="handshakeMessage">Message to display during handshake.</param>
		/// <param name="username">Username to use.</param>
		/// <param name="password">Password to use.</param>
		/// <param name="isRegistration">True if registering a new account.</param>
		/// <param name="address">Optional server address.</param>
		/// <param name="port">Optional server port.</param>
		private void Connect(string handshakeMessage, string username, string password, bool isRegistration = false, string address = null, ushort port = 0)
		{
			if (Client.IsConnectionReady(LocalConnectionState.Stopped) &&
				Constants.Authentication.IsAllowedUsername(username) &&
				Constants.Authentication.IsAllowedPassword(password) &&
				Client.TryGetRandomLoginServerAddress(out ServerAddress serverAddress) &&
				Constants.IsAddressValid(serverAddress.Address))
			{
				HandshakeMSG.text = handshakeMessage;
				Client.LoginAuthenticator.SetLoginCredentials(username, password, isRegistration);
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
			Password.enabled = !locked;
		}
	}
}