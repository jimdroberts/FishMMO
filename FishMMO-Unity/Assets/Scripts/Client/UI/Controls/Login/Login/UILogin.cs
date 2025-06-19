using FishNet.Transporting;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;
using System;
using System.Collections;

namespace FishMMO.Client
{
	public class UILogin : UIControl
	{
		public TMP_InputField Username;
		public TMP_InputField Password;
		public Button RegisterButton;
		public Button SignInButton;
		public TMP_Text HandshakeMSG;

		/// <summary>
		/// Called when a Login Success Client Authentication result is received from the server.
		/// </summary>
		public Action OnLoginSuccessStart;
		/// <summary>
		/// Called after OnLoginSuccessStart finishes.
		/// </summary>
		public Action OnLoginSuccessEnd;

		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
			Client.LoginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;
			Client.OnReconnectFailed += ClientManager_OnReconnectFailed;
		}

		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState -= ClientManager_OnClientConnectionState;
			Client.LoginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;
			Client.OnReconnectFailed -= ClientManager_OnReconnectFailed;
		}

		public override void OnQuitToLogin()
		{
			base.OnQuitToLogin();

			Show();

			SetSignInLocked(false);
		}

		public override void Hide()
		{
			base.Hide();
			
			// Reset handshake message and hide the panel
			HandshakeMSG.text = "";
		}

		private void ClientManager_OnClientConnectionState(ClientConnectionStateArgs obj)
		{
			if (obj.ConnectionState == LocalConnectionState.Stopped)
			{
				HandshakeMSG.text = "";
				SetSignInLocked(false);
			}
		}

		private void ClientManager_OnReconnectFailed()
		{
			Show();
			SetSignInLocked(false);
		}

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

		private void OnLoginAuthenticationDialog(string errorMsg)
		{
			if (UIManager.TryGet("UIDialogBox", out UIDialogBox uiDialogBox))
			{
				uiDialogBox.Open(errorMsg);
			}
			Client.ForceDisconnect();
			SetSignInLocked(false);
		}

		private void OnLoginSuccess()
		{
			HandshakeMSG.text = "Connected";
			
			OnLoginSuccessStart?.Invoke();

			Client.StartCoroutine(OnProcessLoginSuccess());
		}

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

		public void OnClick_OnRegister()
		{
			SetSignInLocked(true);

			StartCoroutine(Client.GetLoginServerList((e) =>
			{
				if (UIManager.TryGet("UIDialogBox", out UIDialogBox uiDialogBox))
				{
					uiDialogBox.Open(e);
				}
				Debug.LogError(e);
				SetSignInLocked(false);
			},
			(servers) =>
			{
				Connect("Creating account.", Username.text, Password.text, true);
			}));
		}

		public void OnClick_OnOptions()
		{
			if (UIManager.TryGet("UIOptions", out UIOptions uiOptions))
			{
				uiOptions.Show();
			}
		}

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
				Debug.LogWarning(e);
				SetSignInLocked(false);
			},
			(servers) =>
			{
				Connect("Connecting...", Username.text, Password.text);
			}));
		}

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

		public void OnClick_Quit()
		{
			Client.Quit();
		}

		/// <summary>
		/// Sets locked state for signing in.
		/// </summary>
		public void SetSignInLocked(bool locked)
		{
			RegisterButton.interactable = !locked;
			SignInButton.interactable = !locked;
			Username.enabled = !locked;
			Password.enabled = !locked;
		}
	}
}