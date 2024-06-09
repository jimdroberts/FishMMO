using FishNet.Transporting;
using TMPro;
using UnityEngine;
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
			StopAllCoroutines();
			SetSignInLocked(false);
		}

		private void ClientManager_OnClientConnectionState(ClientConnectionStateArgs obj)
		{
			if (obj.ConnectionState == LocalConnectionState.Stopped)
			{
				handshakeMSG.text = "";
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
					{
						if (UIManager.TryGet("UIDialogBox", out UIDialogBox uiDialogBox))
						{
							uiDialogBox.Open("Your account has been created!");
						}
						Client.ForceDisconnect();
						SetSignInLocked(false);
					}

					break;
				case ClientAuthenticationResult.InvalidUsernameOrPassword:
					{
						if (UIManager.TryGet("UIDialogBox", out UIDialogBox uiDialogBox))
						{
							uiDialogBox.Open("Invalid Username or Password.");
						}
						Client.ForceDisconnect();
						SetSignInLocked(false);
					}
					break;
				case ClientAuthenticationResult.AlreadyOnline:
					{
						if (UIManager.TryGet("UIDialogBox", out UIDialogBox uiDialogBox))
						{
							uiDialogBox.Open("Account is already online.");
						}
						Client.ForceDisconnect();
						SetSignInLocked(false);
					}
					break;
				case ClientAuthenticationResult.Banned:
					{
						if (UIManager.TryGet("UIDialogBox", out UIDialogBox uiDialogBox))
						{
							uiDialogBox.Open("Account is banned. Please contact the system administrator.");
						}
						Client.ForceDisconnect();
						SetSignInLocked(false);
					}
					break;
				case ClientAuthenticationResult.LoginSuccess:
					// reset handshake message and hide the panel
					handshakeMSG.text = "";
					Hide();

					// request the character list
					CharacterRequestListBroadcast requestCharacterList = new CharacterRequestListBroadcast();
					Client.Broadcast(requestCharacterList, Channel.Reliable);
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
			SetSignInLocked(true);

			StartCoroutine(Client.GetLoginServerList((e) =>
			{
				Debug.LogError(e);
				SetSignInLocked(false);
			},
			(servers) =>
			{
				Connect("Creating account.", username.text, password.text, true);
			}));
		}

		public void OnClick_Login()
		{
			SetSignInLocked(true);

			StartCoroutine(Client.GetLoginServerList((e) =>
			{
				Debug.LogError(e);
				SetSignInLocked(false);
			},
			(servers) =>
			{
				servers.GetRandom();
				Connect("Connecting...", username.text, password.text);
			}));
		}

		private void Connect(string handshakeMessage, string username, string password, bool isRegistration = false, string address = null, ushort port = 0)
		{
			if (Client.IsConnectionReady(LocalConnectionState.Stopped) &&
				Constants.Authentication.IsAllowedUsername(username) &&
				Constants.Authentication.IsAllowedPassword(password) &&
				Client.TryGetRandomLoginServerAddress(out ServerAddress serverAddress))
			{
				handshakeMSG.text = handshakeMessage;
				Client.LoginAuthenticator.SetLoginCredentials(username, password, isRegistration);
				Client.ConnectToServer(serverAddress.address, serverAddress.port);
			}
		}

		public void OnClick_Quit()
		{
			StopAllCoroutines();
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