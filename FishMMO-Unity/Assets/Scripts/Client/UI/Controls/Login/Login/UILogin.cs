using FishNet.Transporting;
using TMPro;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UILogin : UIControl
	{
		public TMP_InputField username;
		public TMP_InputField password;
		public Button registerButton;
		public Button signInButton;
		public TMP_Text handshakeMSG;

		public override void OnStarting()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
			Client.LoginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;
			Client.OnReconnectFailed += ClientManager_OnReconnectFailed;
		}


		public override void OnDestroying()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState -= ClientManager_OnClientConnectionState;

			Client.LoginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;

			Client.OnReconnectFailed -= ClientManager_OnReconnectFailed;
		}

		public override void OnQuitToLogin()
		{
			Show();// override setting, this is our main menu
			SetSignInLocked(false);
		}

		private void ClientManager_OnClientConnectionState(ClientConnectionStateArgs obj)
		{
			//handshakeMSG.text = obj.ConnectionState.ToString();
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
					handshakeMSG.text = "Account created successfully.";
					Client.ForceDisconnect();
					SetSignInLocked(false);
					break;
				case ClientAuthenticationResult.InvalidUsernameOrPassword:
					// update the handshake message
					handshakeMSG.text = "Invalid Username or Password.";
					Client.ForceDisconnect();
					SetSignInLocked(false);
					break;
				case ClientAuthenticationResult.AlreadyOnline:
					handshakeMSG.text = "Account is already online.";
					Client.ForceDisconnect();
					SetSignInLocked(false);
					break;
				case ClientAuthenticationResult.Banned:
					// update the handshake message
					handshakeMSG.text = "Account is banned. Please contact the system administrator.";
					Client.ForceDisconnect();
					SetSignInLocked(false);
					break;
				case ClientAuthenticationResult.LoginSuccess:
					// reset handshake message and hide the panel
					handshakeMSG.text = "";
					Hide();

					// request the character list
					CharacterRequestListBroadcast requestCharacterList = new CharacterRequestListBroadcast();
					Client.NetworkManager.ClientManager.Broadcast(requestCharacterList, Channel.Reliable);
					break;
				case ClientAuthenticationResult.WorldLoginSuccess:
					break;
				case ClientAuthenticationResult.ServerFull:
					break;
				default:
					break;
			}
			SetSignInLocked(false);
		}

		public void OnClick_OnRegister()
		{
			if (Client.IsConnectionReady(LocalConnectionState.Stopped) &&
				Client.LoginAuthenticator.IsAllowedUsername(username.text) &&
				Client.LoginAuthenticator.IsAllowedPassword(password.text))
			{
				// set username and password in the authenticator
				Client.LoginAuthenticator.SetLoginCredentials(username.text, password.text, true);

				handshakeMSG.text = "Creating account.";

				Connect();
			}
		}

		public void OnClick_Login()
		{
			if (Client.IsConnectionReady(LocalConnectionState.Stopped) &&
				Client.LoginAuthenticator.IsAllowedUsername(username.text) &&
				Client.LoginAuthenticator.IsAllowedPassword(password.text))
			{
				// set username and password in the authenticator
				Client.LoginAuthenticator.SetLoginCredentials(username.text, password.text);

				handshakeMSG.text = "Connecting...";

				Connect();
			}
		}

		private void Connect()
		{
			if (Client.TryGetRandomLoginServerAddress(out ServerAddress serverAddress))
			{
				Client.ConnectToServer(serverAddress.address, serverAddress.port);

				SetSignInLocked(true);
			}
			else
			{
				handshakeMSG.text = "Failed to get a login server!";
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
			registerButton.interactable = !locked;
			signInButton.interactable = !locked;
			username.enabled = !locked;
			password.enabled = !locked;
		}
	}
}