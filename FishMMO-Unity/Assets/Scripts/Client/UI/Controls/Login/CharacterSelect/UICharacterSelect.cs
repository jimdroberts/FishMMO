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
		/// <summary>
		/// Button to connect with the selected character.
		/// </summary>
		public Button ConnectButton;
		/// <summary>
		/// Button to delete the selected character.
		/// </summary>
		public Button DeleteButton;
		/// <summary>
		/// Parent transform for the selected character UI.
		/// </summary>
		public RectTransform SelectedCharacterParent;
		/// <summary>
		/// Parent transform for character selection buttons.
		/// </summary>
		public RectTransform CharacterButtonParent;
		/// <summary>
		/// Prefab for individual character selection button.
		/// </summary>
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

		/// <summary>
		/// List of currently displayed character buttons.
		/// </summary>
		private List<CharacterDetailsButton> characterList = new List<CharacterDetailsButton>();
		/// <summary>
		/// The currently selected character button.
		/// </summary>
		private CharacterDetailsButton selectedCharacter;

		/// <summary>
		/// Stores the previous color for label reset.
		/// </summary>
		private Color previousColor;

		/// <summary>
		/// Called when the client is set. Subscribes to connection, authentication, and character broadcast events.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
			Client.NetworkManager.ClientManager.RegisterBroadcast<CharacterListBroadcast>(OnClientCharacterListBroadcastReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<CharacterCreateBroadcast>(OnClientCharacterCreateBroadcastReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<CharacterDeleteBroadcast>(OnClientCharacterDeleteBroadcastReceived);

			Client.LoginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;
		}

		/// <summary>
		/// Called when the client is unset. Unsubscribes from connection, authentication, and character broadcast events.
		/// </summary>
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

		/// <summary>
		/// Called when the UI is being destroyed. Cleans up character list.
		/// </summary>
		public override void OnDestroying()
		{
			DestroyCharacterList();
		}

		/// <summary>
		/// Handles client connection state changes. Hides panel when disconnected.
		/// </summary>
		/// <param name="obj">Connection state arguments.</param>
		private void ClientManager_OnClientConnectionState(ClientConnectionStateArgs obj)
		{
			if (obj.ConnectionState == LocalConnectionState.Stopped)
			{
				Hide();
			}
		}

		/// <summary>
		/// Handles authentication results and updates UI accordingly.
		/// </summary>
		/// <param name="result">The result of client authentication.</param>
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

		/// <summary>
		/// Destroys all character buttons and clears the character list.
		/// </summary>
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

		/// <summary>
		/// Handles incoming character list broadcast, populates character buttons.
		/// </summary>
		/// <param name="msg">The broadcast message containing character details.</param>
		/// <param name="channel">The network channel used.</param>
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

		/// <summary>
		/// Called when character list is ready. Invokes start event and begins post-processing coroutine.
		/// </summary>
		private void OnCharacterListReady()
		{
			OnCharacterListStart?.Invoke();

			Client.StartCoroutine(OnProcessCharacterList());
		}

		/// <summary>
		/// Coroutine for post-character-list processing, resets camera and shows panel.
		/// </summary>
		/// <returns>IEnumerator for coroutine.</returns>
		IEnumerator OnProcessCharacterList()
		{
			if (CinematicCamera != null)
			{
				CinematicCamera.Reset();
				yield return CinematicCamera.MoveToNextWaypoint(() =>
				{
					//Log.Debug("Camera movement completed!");
				}, true);
			}

			OnCharacterListEnd?.Invoke();
			Show();
		}

		/// <summary>
		/// Handles character creation broadcast, adds new character button.
		/// </summary>
		/// <param name="msg">The broadcast message for character creation.</param>
		/// <param name="channel">The network channel used.</param>
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

		/// <summary>
		/// Handles character deletion broadcast, removes character button.
		/// </summary>
		/// <param name="msg">The broadcast message for character deletion.</param>
		/// <param name="channel">The network channel used.</param>
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

		/// <summary>
		/// Handles character selection, updates button colors.
		/// </summary>
		/// <param name="button">The selected character button.</param>
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

		/// <summary>
		/// Called when the connect button is clicked. Initiates character selection and connection.
		/// </summary>
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

		/// <summary>
		/// Called when the delete button is clicked. Prompts for confirmation and deletes character if confirmed.
		/// </summary>
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

		/// <summary>
		/// Called when the create character button is clicked. Shows character creation panel.
		/// </summary>
		public void OnClick_CreateCharacter()
		{
			if (UIManager.TryGet("UICharacterCreate", out UICharacterCreate createCharacter))
			{
				Hide();
				createCharacter.Show();
			}
		}

		/// <summary>
		/// Called when quitting to login. Stops character list coroutine and unlocks buttons.
		/// </summary>
		public override void OnQuitToLogin()
		{
			base.OnQuitToLogin();

			Client.StopCoroutine(OnProcessCharacterList());

			SetDeleteButtonLocked(false);
			SetConnectButtonLocked(false);
		}

		/// <summary>
		/// Called when the quit to login button is clicked. Stops character list coroutine and returns to login screen.
		/// </summary>
		public void OnClick_QuitToLogin()
		{
			Client.StopCoroutine(OnProcessCharacterList());

			Client.QuitToLogin();
		}

		/// <summary>
		/// Called when the quit button is clicked. Quits the client application.
		/// </summary>
		public void OnClick_Quit()
		{
			Client.Quit();
		}

		/// <summary>
		/// Sets locked state for connect button (enables/disables connect button).
		/// </summary>
		/// <param name="locked">True to lock (disable) the button, false to unlock.</param>
		private void SetConnectButtonLocked(bool locked)
		{
			ConnectButton.interactable = !locked;
		}

		/// <summary>
		/// Sets locked state for delete button (enables/disables delete button).
		/// </summary>
		/// <param name="locked">True to lock (disable) the button, false to unlock.</param>
		private void SetDeleteButtonLocked(bool locked)
		{
			DeleteButton.interactable = !locked;
		}
	}
}