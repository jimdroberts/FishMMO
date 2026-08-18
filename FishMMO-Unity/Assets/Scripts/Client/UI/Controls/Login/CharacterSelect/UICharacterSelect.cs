using FishNet.Transporting;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;
using FishMMO.Auth.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI control for selecting, creating, or deleting characters after login.
	/// Displays a list of available characters, handles character selection highlighting,
	/// and manages connection to the world server with the chosen character.
	/// </summary>
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
			Client.NetworkManager.ClientManager.RegisterBroadcast<CharacterSelectResultBroadcast>(OnClientCharacterSelectResultBroadcastReceived);

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
			Client.NetworkManager.ClientManager.UnregisterBroadcast<CharacterSelectResultBroadcast>(OnClientCharacterSelectResultBroadcastReceived);

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
			// Only react while this panel is shown. ClientLoginAuthenticator raises this
			// event to every subscriber, so without the guard a result belonging to another
			// panel's flow (e.g. a wrong password on the login screen) is handled here too,
			// and the Client.QuitToLogin() below force-disconnects and resets state before
			// the owning panel's handler ever runs — the login error dialog never appears.
			// UILogin guards the same event with its own isAuthFlowActive flag.
			if (!Visible) return;

			switch (result)
			{
				case ClientAuthenticationResult.InvalidUsernameOrPassword:
				case ClientAuthenticationResult.AlreadyOnline:
				case ClientAuthenticationResult.Banned:
				case ClientAuthenticationResult.ServerFull:
				case ClientAuthenticationResult.ServerBusy:
				case ClientAuthenticationResult.NoCharacterSelected:
				case ClientAuthenticationResult.TokenInvalid:
				case ClientAuthenticationResult.TokenExpired:
				case ClientAuthenticationResult.TokenRevoked:
				case ClientAuthenticationResult.TokenDecryptFailed:
					Client.QuitToLogin();
					break;
				case ClientAuthenticationResult.LoginSuccess:
					break;
				case ClientAuthenticationResult.WorldLoginSuccess:
					Hide();
					break;
				case ClientAuthenticationResult.SceneLoginSuccess:
					Hide();
					break;
				// Not applicable during character select flow.
				case ClientAuthenticationResult.AccountCreated:
				case ClientAuthenticationResult.SrpVerify:
				case ClientAuthenticationResult.SrpProof:
				case ClientAuthenticationResult.AccountUnverified:
				case ClientAuthenticationResult.AccountVerified:
				case ClientAuthenticationResult.TwoFactorRequired:
				case ClientAuthenticationResult.TwoFactorInvalid:
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
					// Null check first: touching OnCharacterSelected on an already-destroyed
					// button throws MissingReferenceException, which aborts the rest of the
					// teardown and leaks every entry after it.
					if (characterList[i] == null)
					{
						continue;
					}
					characterList[i].OnCharacterSelected -= OnCharacterSelected;
					Destroy(characterList[i].gameObject);
				}
				characterList.Clear();
			}
			selectedCharacter = null;
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
				if (msg.Characters.Length < 1)
				{
					OnCharacterListReady();
					return;
				}

				for (int i = 0; i < msg.Characters.Length; ++i)
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

			StopProcessCharacterList();
			processCharacterListRoutine = Client.StartCoroutine(OnProcessCharacterList());
		}

		/// <summary>
		/// Handle for the in-flight <see cref="OnProcessCharacterList"/> coroutine.
		/// </summary>
		/// <remarks>
		/// <c>StopCoroutine(OnProcessCharacterList())</c> builds a brand new enumerator and asks
		/// Unity to stop that one, which never matches the running instance — so the coroutine
		/// carried on and called <see cref="UIControl.Show"/> on the character-select panel
		/// after the player had already quit to login, putting it back on top of the login
		/// screen. Keeping the handle is what makes stopping it actually work.
		/// </remarks>
		private Coroutine processCharacterListRoutine;

		/// <summary>Stops the character-list post-processing coroutine if one is running.</summary>
		private void StopProcessCharacterList()
		{
			if (processCharacterListRoutine == null)
			{
				return;
			}
			if (Client != null)
			{
				Client.StopCoroutine(processCharacterListRoutine);
			}
			processCharacterListRoutine = null;
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

			processCharacterListRoutine = null;

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
			/* Remove the entry from the list as well as destroying its GameObject.
			 *
			 * Leaving it behind kept a destroyed CharacterDetailsButton in characterList, and
			 * every later walk of that list touched it: DestroyCharacterList unsubscribes
			 * OnCharacterSelected before its own null check, which throws
			 * MissingReferenceException on a destroyed component — so deleting a character and
			 * then quitting to login (or receiving a new character list) tore the panel down
			 * half-finished. selectedCharacter could point at the same destroyed button, which
			 * made the next Connect click throw instead of doing anything. */
			if (characterList != null)
			{
				for (int i = characterList.Count - 1; i >= 0; --i)
				{
					CharacterDetailsButton button = characterList[i];
					if (button == null)
					{
						characterList.RemoveAt(i);
						continue;
					}

					if (button.Details == null ||
						button.Details.CharacterName != msg.CharacterName)
					{
						continue;
					}

					button.OnCharacterSelected -= OnCharacterSelected;
					if (ReferenceEquals(selectedCharacter, button))
					{
						selectedCharacter = null;
					}
					characterList.RemoveAt(i);
					Destroy(button.gameObject);
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

			StopProcessCharacterList();

			SetDeleteButtonLocked(false);
			SetConnectButtonLocked(false);
		}

		/// <summary>
		/// Called when the quit to login button is clicked. Stops character list coroutine and returns to login screen.
		/// </summary>
		public void OnClick_QuitToLogin()
		{
			StopProcessCharacterList();

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
		/// <summary>
		/// Handles a refused character selection so the player is told why rather than being
		/// left on a screen that appears to have ignored the click.
		/// </summary>
		/// <param name="msg">The refusal message.</param>
		/// <param name="channel">The network channel used.</param>
		private void OnClientCharacterSelectResultBroadcastReceived(CharacterSelectResultBroadcast msg, Channel channel)
		{
			if (msg.Result == CharacterSelectResult.Success)
			{
				return;
			}

			// The connect button was locked when the request went out; a refusal never reaches
			// the world-server-list path that would normally unlock it.
			SetConnectButtonLocked(false);

			/* Put the panel back. OnClick_SelectCharacter hides it optimistically, and only the
			 * success path (ServerListBroadcast -> UIServerSelect.Show) puts anything else on
			 * screen. A refusal therefore left the player looking at an empty scene with no way
			 * back once the dialog below was dismissed. */
			Show();

			string message = msg.Result == CharacterSelectResult.OtherCharacterInWorld
				? $"'{msg.CharacterName}' is still in the world. Select that character to rejoin it, or wait for it to leave combat."
				: "Character selection failed. Please try again.";

			if (UIManager.TryGet("UIDialogBox", out UIDialogBox dialogBox))
			{
				dialogBox.Open(message);
			}
			else
			{
				FishMMO.Logging.Log.Warning("UICharacterSelect", message);
			}
		}

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