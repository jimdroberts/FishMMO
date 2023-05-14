using FishNet.Managing;
using FishNet.Transporting;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	public class UILogin : UIControl
	{
		private NetworkManager networkManager;
		private ClientLoginAuthenticator loginAuthenticator;

		public TMP_InputField username;
		public TMP_InputField password;
		public Button signInButton;
		public TMP_Text handshakeMSG;

		public override void OnStarting()
		{
			networkManager = FindObjectOfType<NetworkManager>();
			loginAuthenticator = FindObjectOfType<ClientLoginAuthenticator>();
			
			if (networkManager == null)
			{
				Debug.LogError("UILogin: NetworkManager not found, HUD will not function.");
				return;
			}
			else if (loginAuthenticator == null)
			{
				Debug.LogError("UILogin: LoginAuthenticator not found, HUD will not function.");
				return;
			}
			else
			{
				networkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
				loginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;
				Client.Instance.OnReconnectFailed += ClientManager_OnReconnectFailed;
			}
		}


		public override void OnDestroying()
		{
			if (networkManager != null)
			{
				networkManager.ClientManager.OnClientConnectionState -= ClientManager_OnClientConnectionState;
			}

			if (loginAuthenticator != null)
			{
				loginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;
			}

            Client.Instance.OnReconnectFailed -= ClientManager_OnReconnectFailed;
        }

		private void ClientManager_OnClientConnectionState(ClientConnectionStateArgs obj)
		{
			//handshakeMSG.text = obj.ConnectionState.ToString();
		}

		private void ClientManager_OnReconnectFailed()
		{
			visible = true;
			SetSignInLocked(false);
		}

		private void Authenticator_OnClientAuthenticationResult(ClientAuthenticationResult result)
		{
			switch (result)
			{
				case ClientAuthenticationResult.InvalidUsernameOrPassword:
					// update the handshake message
					handshakeMSG.text = "Invalid Username or Password.";
					Client.Instance.ForceDisconnect();
					SetSignInLocked(false);
					break;
				case ClientAuthenticationResult.AlreadyOnline:
					handshakeMSG.text = "Account is already online.";
                    Client.Instance.ForceDisconnect();
                    SetSignInLocked(false);
                    break;
				case ClientAuthenticationResult.Banned:
					// update the handshake message
					handshakeMSG.text = "Account is banned. Please contact the system administrator.";
                    Client.Instance.ForceDisconnect();
                    SetSignInLocked(false);
                    break;
				case ClientAuthenticationResult.LoginSuccess:
					// reset handshake message and hide the panel
					handshakeMSG.text = "";
					visible = false;

					// request the character list
					CharacterRequestListBroadcast requestCharacterList = new CharacterRequestListBroadcast();
					networkManager.ClientManager.Broadcast(requestCharacterList);
					break;
				case ClientAuthenticationResult.WorldLoginSuccess:
					break;
				default:
					break;
			}
			SetSignInLocked(false);
		}

		public void OnClick_Login()
		{
			if (Client.Instance.IsConnectionReady(LocalConnectionState.Stopped) &&
				loginAuthenticator.IsAllowedUsername(username.text) &&
				loginAuthenticator.IsAllowedPassword(password.text))
			{
				// set username and password in the authenticator
				loginAuthenticator.SetLoginCredentials(username.text, password.text);

                handshakeMSG.text = "";

                if (Client.Instance.TryGetRandomLoginServerAddress(out ServerAddress serverAddress))
				{
					Client.Instance.ConnectToServer(serverAddress.address, serverAddress.port);

					SetSignInLocked(true);
				}
				else
				{
                    handshakeMSG.text = "Failed to get a login server!";
                }
			}
		}

		public void OnClick_Quit()
		{
#if UNITY_EDITOR
			EditorApplication.ExitPlaymode();
#else
			Application.Quit();
#endif
		}

		/// <summary>
		/// Sets locked state for signing in.
		/// </summary>
		public void SetSignInLocked(bool locked)
		{
			signInButton.interactable = !locked;
			username.enabled = !locked;
			password.enabled = !locked;
		}
	}
}