using FishNet.Transporting;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UICharacterSelect : UIControl
	{
		public Button ConnectButton;
		public Button DeleteButton;
		public RectTransform SelectedCharacterParent;
		public RectTransform CharacterButtonParent;
		public CharacterDetailsButton CharacterButtonPrefab;

		/// <summary>
		/// Called when a Character List is received and ready to use.
		/// </summary>
		public Action OnCharacterListStart;
		/// <summary>
		/// Called after OnCharacterListReceivedStart finishes.
		/// </summary>
		public Action OnCharacterListEnd;
		/// <summary>
		/// Reference to the Cinematic Camera attached to this UI control.
		/// </summary>
		public CinematicCamera CinematicCamera;

		private List<CharacterDetailsButton> characterList = new List<CharacterDetailsButton>();
		private CharacterDetailsButton selectedCharacter;

		private Color previousColor;

		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
			Client.NetworkManager.ClientManager.RegisterBroadcast<CharacterListBroadcast>(OnClientCharacterListBroadcastReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<CharacterCreateBroadcast>(OnClientCharacterCreateBroadcastReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<CharacterDeleteBroadcast>(OnClientCharacterDeleteBroadcastReceived);

			Client.LoginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;
		}

		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState -= ClientManager_OnClientConnectionState;
			Client.NetworkManager.ClientManager.UnregisterBroadcast<CharacterListBroadcast>(OnClientCharacterListBroadcastReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<CharacterCreateBroadcast>(OnClientCharacterCreateBroadcastReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<CharacterDeleteBroadcast>(OnClientCharacterDeleteBroadcastReceived);

			if (Client.LoginAuthenticator != null)
			{
				Client.LoginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;
			}
		}

		public override void OnDestroying()
		{
			DestroyCharacterList();
		}

		private void ClientManager_OnClientConnectionState(ClientConnectionStateArgs obj)
		{
			if (obj.ConnectionState == LocalConnectionState.Stopped)
			{
				Hide();
			}
		}

		private void Authenticator_OnClientAuthenticationResult(ClientAuthenticationResult result)
		{
			switch (result)
			{
				case ClientAuthenticationResult.InvalidUsernameOrPassword:
					break;
				case ClientAuthenticationResult.AlreadyOnline:
					break;
				case ClientAuthenticationResult.Banned:
					break;
				case ClientAuthenticationResult.LoginSuccess:
					//Show(); // show the panel even if we don't get the character list.. this will let us return to login or quit
					break;
				case ClientAuthenticationResult.WorldLoginSuccess:
					Hide();
					break;
				case ClientAuthenticationResult.SceneLoginSuccess:
					Hide();
					break;
				case ClientAuthenticationResult.ServerFull:
					break;
				default:
					break;
			}
			SetConnectButtonLocked(false);
		}

		public void DestroyCharacterList()
		{
			if (characterList != null)
			{
				for (int i = 0; i < characterList.Count; ++i)
				{
					characterList[i].OnCharacterSelected -= OnCharacterSelected;
					if (characterList[i] != null)
						Destroy(characterList[i].gameObject);
				}
				characterList.Clear();
			}
		}

		private void OnClientCharacterListBroadcastReceived(CharacterListBroadcast msg, Channel channel)
		{
			Hide();

			if (msg.Characters != null)
			{
				DestroyCharacterList();

				characterList = new List<CharacterDetailsButton>();

				// No characters were sent.
				if (msg.Characters.Count < 1)
				{
					OnCharacterListReady();
					return;
				}

				for (int i = 0; i < msg.Characters.Count; ++i)
				{
					CharacterDetailsButton newCharacter = Instantiate(CharacterButtonPrefab, CharacterButtonParent);
					newCharacter.Initialize(msg.Characters[i]);
					newCharacter.OnCharacterSelected += OnCharacterSelected;
					characterList.Add(newCharacter);
				}
			}

			OnCharacterListReady();
		}

		private void OnCharacterListReady()
		{
			OnCharacterListStart?.Invoke();
			
			Client.StartCoroutine(OnProcessCharacterList());
		}

		IEnumerator OnProcessCharacterList()
		{
			if (CinematicCamera != null)
			{
				CinematicCamera.Reset();
				yield return CinematicCamera.MoveToNextWaypoint(() =>
				{
					//Debug.Log("Camera movement completed!");
				}, true);
			}

			OnCharacterListEnd?.Invoke();
			Show();
		}

		private void OnClientCharacterCreateBroadcastReceived(CharacterCreateBroadcast msg, Channel channel)
		{
			// new characters can be constructed with basic data, they have no equipped items
			CharacterDetailsButton newCharacter = Instantiate(CharacterButtonPrefab, CharacterButtonParent);
			CharacterDetails details = new CharacterDetails()
			{
				CharacterName = msg.CharacterName,
				SceneName = msg.SceneName,
				RaceTemplateID = msg.RaceTemplateID,
			};
			newCharacter.Initialize(details);
			newCharacter.OnCharacterSelected += OnCharacterSelected;
			characterList.Add(newCharacter);
		}

		private void OnClientCharacterDeleteBroadcastReceived(CharacterDeleteBroadcast msg, Channel channel)
		{
			//remove the character from our characters list
			if (characterList != null)
			{
				for (int i = 0; i < characterList.Count; ++i)
				{
					if (characterList[i].Details.CharacterName == msg.CharacterName)
					{
						characterList[i].OnCharacterSelected -= OnCharacterSelected;
						characterList[i].gameObject.SetActive(false);
						Destroy(characterList[i].gameObject);
					}
				}
			}

			SetDeleteButtonLocked(false);
		}

		private void OnCharacterSelected(CharacterDetailsButton button)
		{
			CharacterDetailsButton prevButton = selectedCharacter;
			if (prevButton != null)
			{
				prevButton.ResetLabelColor();
			}

			selectedCharacter = button;
			if (selectedCharacter != null)
			{
				selectedCharacter.SetLabelColors(Color.green);
			}
		}

		public void OnClick_SelectCharacter()
		{
			if (Client.IsConnectionReady() &&
				selectedCharacter != null &&
				selectedCharacter.Details != null)
			{
				Hide();

				// tell the login server about our character selection
				Client.Broadcast(new CharacterSelectBroadcast()
				{
					CharacterName = selectedCharacter.Details.CharacterName,
				}, Channel.Reliable);
				SetConnectButtonLocked(true);
			}
		}

		public void OnClick_DeleteCharacter()
		{
			if (Client.IsConnectionReady() &&
				selectedCharacter != null &&
				selectedCharacter.Details != null)
			{
				if (UIManager.TryGet("UIDialogBox", out UIDialogBox tooltip))
				{
					SetDeleteButtonLocked(true);

					tooltip.Open("Are you sure you would like to delete this character?", () =>
					{
						// delete character
						Client.Broadcast(new CharacterDeleteBroadcast()
						{
							CharacterName = selectedCharacter.Details.CharacterName,
						}, Channel.Reliable);
						SetDeleteButtonLocked(false);
					}, () =>
					{
						SetDeleteButtonLocked(false);
					});
				}
			}
		}

		public void OnClick_CreateCharacter()
		{
			if (UIManager.TryGet("UICharacterCreate", out UICharacterCreate createCharacter))
			{
				Hide();
				createCharacter.Show();
			}
		}

		public override void OnQuitToLogin()
		{
			base.OnQuitToLogin();

			Client.StopCoroutine(OnProcessCharacterList());

			SetDeleteButtonLocked(false);
			SetConnectButtonLocked(false);
		}

		public void OnClick_QuitToLogin()
		{
			Client.StopCoroutine(OnProcessCharacterList());
			
			Client.QuitToLogin();
		}

		public void OnClick_Quit()
		{
			Client.Quit();
		}

		private void SetConnectButtonLocked(bool locked)
		{
			ConnectButton.interactable = !locked;
		}

		private void SetDeleteButtonLocked(bool locked)
		{
			DeleteButton.interactable = !locked;
		}
	}
}