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
			if (Client.IsConnectionReady(LocalConnectionState.Stopped) &&
				Constants.Authentication.IsAllowedUsername(username.text) &&
				Constants.Authentication.IsAllowedPassword(password.text))
			{
				SetSignInLocked(true);

				StartCoroutine(Client.GetLoginServerList((error) =>
				{
					Debug.LogError(error);
					SetSignInLocked(false);
				},
				(servers) =>
				{
					// set username and password in the authenticator
					Client.LoginAuthenticator.SetLoginCredentials(username.text, password.text, true);

					handshakeMSG.text = "Creating account.";

					Connect();
				}));
			}
		}

		public void OnClick_Login()
		{
			if (Client.IsConnectionReady(LocalConnectionState.Stopped) &&
				Constants.Authentication.IsAllowedUsername(username.text) &&
				Constants.Authentication.IsAllowedPassword(password.text))
			{
				// set username and password in the authenticator
				Client.LoginAuthenticator.SetLoginCredentials(username.text, password.text);

				handshakeMSG.text = "Connecting...";

				Connect();
			}
		}

		private void Connect()
		{
			SetSignInLocked(true);

			StartCoroutine(Client.GetLoginServerList((error) =>
			{
				Debug.LogError(error);
				SetSignInLocked(false);
			},
			(servers) =>
			{
				if (Client.TryGetRandomLoginServerAddress(out ServerAddress serverAddress))
				{
					Client.ConnectToServer(serverAddress.address, serverAddress.port);
				}
			}));
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