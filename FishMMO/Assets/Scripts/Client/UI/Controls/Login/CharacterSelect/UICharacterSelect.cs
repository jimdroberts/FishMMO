﻿using FishNet.Managing;
using FishNet.Transporting;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	public class UICharacterSelect : UIControl
	{
		public Button connectButton;
		public Button deleteButton;
		public RectTransform selectedCharacterParent;
		public RectTransform characterButtonParent;
		public CharacterDetailsButton characterButtonPrefab;

		private List<CharacterDetailsButton> characterList = new List<CharacterDetailsButton>();
		private CharacterDetailsButton selectedCharacter;

		public override void OnStarting()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
			Client.NetworkManager.ClientManager.RegisterBroadcast<CharacterListBroadcast>(OnClientCharacterListBroadcastReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<CharacterCreateBroadcast>(OnClientCharacterCreateBroadcastReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<CharacterDeleteBroadcast>(OnClientCharacterDeleteBroadcastReceived);

			Client.LoginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;
		}


		public override void OnDestroying()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState -= ClientManager_OnClientConnectionState;
			Client.NetworkManager.ClientManager.UnregisterBroadcast<CharacterListBroadcast>(OnClientCharacterListBroadcastReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<CharacterCreateBroadcast>(OnClientCharacterCreateBroadcastReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<CharacterDeleteBroadcast>(OnClientCharacterDeleteBroadcastReceived);

			if (Client.LoginAuthenticator != null)
			{
				Client.LoginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;
			}

			DestroyCharacterList();
		}

		private void ClientManager_OnClientConnectionState(ClientConnectionStateArgs obj)
		{
			if (obj.ConnectionState == LocalConnectionState.Stopped)
			{
				visible = false;
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
					visible = true; // show the panel even if we don't get the character list.. this will let us return to login or quit
					break;
				case ClientAuthenticationResult.WorldLoginSuccess:
					visible = false;
					break;
				case ClientAuthenticationResult.SceneLoginSuccess:
					visible = false;
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

		private void OnClientCharacterListBroadcastReceived(CharacterListBroadcast msg)
		{
			if (msg.characters != null)
			{
				DestroyCharacterList();

				characterList = new List<CharacterDetailsButton>();
				for (int i = 0; i < msg.characters.Count; ++i)
				{
					CharacterDetailsButton newCharacter = Instantiate(characterButtonPrefab, characterButtonParent);
					newCharacter.Initialize(msg.characters[i]);
					newCharacter.OnCharacterSelected += OnCharacterSelected;
					characterList.Add(newCharacter);
				}
			}

			visible = true;
		}

		private void OnClientCharacterCreateBroadcastReceived(CharacterCreateBroadcast msg)
		{
			// new characters can be constructed with basic data, they have no equipped items
			CharacterDetailsButton newCharacter = Instantiate(characterButtonPrefab, characterButtonParent);
			CharacterDetails details = new CharacterDetails()
			{
				characterName = msg.characterName,
				//modelTemplateIndex = msg.raceName,
			};
			newCharacter.Initialize(details);
			newCharacter.OnCharacterSelected += OnCharacterSelected;
			characterList.Add(newCharacter);
		}

		private void OnClientCharacterDeleteBroadcastReceived(CharacterDeleteBroadcast msg)
		{
			//remove the character from our characters list
			if (characterList != null)
			{
				for (int i = 0; i < characterList.Count; ++i)
				{
					if (characterList[i].details.characterName == msg.characterName)
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
				prevButton.SetLabelColors(Color.black);
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
				selectedCharacter.details != null)
			{
				visible = false;

				// tell the login server about our character selection
				Client.NetworkManager.ClientManager.Broadcast(new CharacterSelectBroadcast()
				{
					characterName = selectedCharacter.details.characterName,
				});
				SetConnectButtonLocked(true);
			}
		}

		public void OnClick_DeleteCharacter()
		{
			if (Client.IsConnectionReady() &&
				selectedCharacter != null &&
				selectedCharacter.details != null)
			{
				if (UIManager.TryGet("UIConfirmationTooltip", out UIConfirmationTooltip tooltip))
				{
					SetDeleteButtonLocked(true);

					tooltip.Open("Are you sure you would like to delete this character?", () =>
					{
						// delete character
						Client.NetworkManager.ClientManager.Broadcast(new CharacterDeleteBroadcast()
						{
							characterName = selectedCharacter.details.characterName,
						});
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
				visible = false;
				createCharacter.visible = true;
			}
		}

		public void OnClick_QuitToLogin()
		{
			// we should go back to login..
#if UNITY_EDITOR
			EditorApplication.ExitPlaymode();
#else
			Application.Quit();
#endif
		}

		public void OnClick_Quit()
		{
#if UNITY_EDITOR
			EditorApplication.ExitPlaymode();
#else
			Application.Quit();
#endif
		}

		private void SetConnectButtonLocked(bool locked)
		{
			connectButton.interactable = !locked;
		}

		private void SetDeleteButtonLocked(bool locked)
		{
			deleteButton.interactable = !locked;
		}
	}
}