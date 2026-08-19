using FishNet.Transporting;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Auth.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit implementation of the character selection control. Lists the account's
	/// characters, allows selecting/connecting/deleting and entering character creation.
	/// </summary>
	public class UITKCharacterSelect : UITKControl
	{
		/// <summary>
		/// The name of the connect button in the UI.
		/// </summary>
		private const string CONNECT_BUTTON_NAME = "character-connect-btn";
		/// <summary>
		/// The name of the delete button in the UI.
		/// </summary>
		private const string DELETE_BUTTON_NAME = "character-delete-btn";
		/// <summary>
		/// The name of the create button in the UI.
		/// </summary>
		private const string CREATE_BUTTON_NAME = "character-create-btn";
		/// <summary>
		/// The name of the quit-to-login button in the UI.
		/// </summary>
		private const string QUIT_LOGIN_BUTTON_NAME = "character-quit-login-btn";
		/// <summary>
		/// The name of the quit button in the UI.
		/// </summary>
		private const string QUIT_BUTTON_NAME = "character-quit-btn";
		/// <summary>
		/// The name of the character list container in the UI.
		/// </summary>
		private const string CHARACTER_LIST_NAME = "character-list";

		/// <summary>
		/// The USS class applied to each character row.
		/// </summary>
		private const string CHARACTER_ROW_CLASS = "character-row";
		/// <summary>
		/// The USS class applied to the selected character row.
		/// </summary>
		private const string CHARACTER_ROW_SELECTED_CLASS = "character-row--selected";
		/// <summary>
		/// The USS class applied to the character name label within a row.
		/// </summary>
		private const string CHARACTER_ROW_NAME_CLASS = "character-row__name";
		/// <summary>
		/// The USS class applied to the character scene label within a row.
		/// </summary>
		private const string CHARACTER_ROW_SCENE_CLASS = "character-row__scene";

		/// <summary>
		/// View model for a single character row.
		/// </summary>
		private class CharacterRow
		{
			/// <summary>
			/// The root VisualElement for this character row.
			/// </summary>
			public VisualElement Root;
			/// <summary>
			/// The Label displaying the character name.
			/// </summary>
			public Label Name;
			/// <summary>
			/// The Label displaying the scene name.
			/// </summary>
			public Label Scene;
			/// <summary>
			/// The character details associated with this row.
			/// </summary>
			public CharacterDetails Details;
		}

		/// <summary>
		/// Called when a Character List is received and ready to use.
		/// </summary>
		public Action OnCharacterListStart;

		/// <summary>
		/// Called after OnCharacterListStart finishes.
		/// </summary>
		public Action OnCharacterListEnd;

		/// <summary>
		/// Reference to the Cinematic Camera attached to this UI control.
		/// </summary>
		public CinematicCamera CinematicCamera;

		private VisualElement characterListContainer;
		private Button connectButton;
		private Button deleteButton;

		/// <summary>
		/// The list of all character row view models currently displayed.
		/// </summary>
		private readonly List<CharacterRow> characterList = new List<CharacterRow>();
		/// <summary>
		/// The currently selected character row, or null if none is selected.
		/// </summary>
		private CharacterRow selectedCharacter;

		/// <summary>
		/// Resolves and caches visual elements and wires up button callbacks.
		/// </summary>
		public override void OnStarting()
		{
			if (Root == null)
			{
				return;
			}

			characterListContainer = Root.Q<VisualElement>(CHARACTER_LIST_NAME);
			connectButton = Root.Q<Button>(CONNECT_BUTTON_NAME);
			deleteButton = Root.Q<Button>(DELETE_BUTTON_NAME);

			if (connectButton != null)
			{
				connectButton.clicked += OnClick_SelectCharacter;
			}
			if (deleteButton != null)
			{
				deleteButton.clicked += OnClick_DeleteCharacter;
			}

			Button createButton = Root.Q<Button>(CREATE_BUTTON_NAME);
			if (createButton != null)
			{
				createButton.clicked += OnClick_CreateCharacter;
			}

			Button quitToLoginButton = Root.Q<Button>(QUIT_LOGIN_BUTTON_NAME);
			if (quitToLoginButton != null)
			{
				quitToLoginButton.clicked += OnClick_QuitToLogin;
			}

			Button quitButton = Root.Q<Button>(QUIT_BUTTON_NAME);
			if (quitButton != null)
			{
				quitButton.clicked += OnClick_Quit;
			}
		}

		/// <summary>
		/// Subscribes to connection, authentication, and character broadcast events when the client is injected.
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
		/// Unsubscribes from connection, authentication, and character broadcast events when the client is cleared.
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
		/// Cleans up the character list when the control is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			DestroyCharacterList();
		}

		/// <summary>
		/// Handles client connection state changes. Hides the panel when disconnected.
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
		/// Handles authentication results and updates the UI accordingly.
		/// </summary>
		/// <param name="result">The result of client authentication.</param>
		private void Authenticator_OnClientAuthenticationResult(ClientAuthenticationResult result)
		{
			// Only react while this panel is shown. ClientLoginAuthenticator raises this
			// event to every subscriber, so without the guard a result meant for another
			// panel (e.g. a wrong password on the login screen) reaches the Client.QuitToLogin()
			// below, which force-disconnects and resets state before the owning panel's
			// handler runs — its error dialog never appears. Mirrors UICharacterSelect.
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
		/// Removes all character rows and clears the character list.
		/// </summary>
		public void DestroyCharacterList()
		{
			for (int i = 0; i < characterList.Count; ++i)
			{
				characterList[i].Root.RemoveFromHierarchy();
			}
			characterList.Clear();
			selectedCharacter = null;
		}

		/// <summary>
		/// Handles an incoming character list broadcast and populates the character rows.
		/// </summary>
		/// <param name="msg">The broadcast message containing character details.</param>
		/// <param name="channel">The network channel used.</param>
		private void OnClientCharacterListBroadcastReceived(CharacterListBroadcast msg, Channel channel)
		{
			Hide();

			if (msg.Characters != null)
			{
				DestroyCharacterList();

				// No characters were sent.
				if (msg.Characters.Length < 1)
				{
					OnCharacterListReady();
					return;
				}

				for (int i = 0; i < msg.Characters.Length; ++i)
				{
					CreateCharacterRow(msg.Characters[i]);
				}
			}

			OnCharacterListReady();
		}

		/// <summary>
		/// Invokes the start event and begins the post-processing coroutine when the list is ready.
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
		/// carried on and called <see cref="UITKControl.Show"/> on this panel after the player
		/// had already quit to login, putting it back on top of the login screen. Keeping the
		/// handle is what makes stopping it actually work.
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
		/// Coroutine for post-character-list processing, resets the camera and shows the panel.
		/// </summary>
		/// <returns>IEnumerator for coroutine.</returns>
		private IEnumerator OnProcessCharacterList()
		{
			if (CinematicCamera != null)
			{
				CinematicCamera.Reset();
				yield return CinematicCamera.MoveToNextWaypoint(() =>
				{
				}, true);
			}

			processCharacterListRoutine = null;

			OnCharacterListEnd?.Invoke();
			Show();
		}

		/// <summary>
		/// Handles a character creation broadcast and adds a new character row.
		/// </summary>
		/// <param name="msg">The broadcast message for character creation.</param>
		/// <param name="channel">The network channel used.</param>
		private void OnClientCharacterCreateBroadcastReceived(CharacterCreateBroadcast msg, Channel channel)
		{
			// New characters can be constructed with basic data; they have no equipped items.
			CharacterDetails details = new CharacterDetails()
			{
				CharacterName = msg.CharacterName,
				SceneName = msg.SceneName,
				RaceTemplateID = msg.RaceTemplateID,
			};
			CreateCharacterRow(details);
		}

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
				// The selection stands and the server list takes over from here, so the wait
				// this panel was holding is finished. Leaving it armed would fire the reply
				// timeout later and put this panel back on top of the server-select screen.
				replyGuard.Clear();
				return;
			}

			// The connect button was locked when the request went out; a refusal never reaches
			// the world-server-list path that would normally unlock it.
			SetConnectButtonLocked(false);

			/* Put the panel back. OnClick_SelectCharacter hides it optimistically, and only the
			 * success path (ServerListBroadcast -> UITKServerSelect.Show) puts anything else on
			 * screen. A refusal therefore left the player looking at an empty scene with no way
			 * back once the dialog below was dismissed. */
			Show();

			string message = msg.Result == CharacterSelectResult.OtherCharacterInWorld
				? $"'{msg.CharacterName}' is still in the world. Select that character to rejoin it, or wait for it to leave combat."
				: "Character selection failed. Please try again.";

			if (UIManager.TryGetTK("UITKDialogBox", out UITKDialogBox dialogBox))
			{
				dialogBox.Open(message);
			}
			else
			{
				FishMMO.Logging.Log.Warning("UITKCharacterSelect", message);
			}
		}

		/// <summary>
		/// Handles a character deletion broadcast and removes the matching character row.
		/// </summary>
		/// <param name="msg">The broadcast message for character deletion.</param>
		/// <param name="channel">The network channel used.</param>
		private void OnClientCharacterDeleteBroadcastReceived(CharacterDeleteBroadcast msg, Channel channel)
		{
			// Remove the character from our characters list.
			for (int i = characterList.Count - 1; i >= 0; --i)
			{
				if (characterList[i].Details.CharacterName == msg.CharacterName)
				{
					if (selectedCharacter == characterList[i])
					{
						selectedCharacter = null;
					}
					characterList[i].Root.RemoveFromHierarchy();
					characterList.RemoveAt(i);
				}
			}

			SetDeleteButtonLocked(false);
		}

		/// <summary>
		/// Builds a character row element for the supplied character details.
		/// </summary>
		/// <param name="details">The character details.</param>
		private void CreateCharacterRow(CharacterDetails details)
		{
			if (characterListContainer == null)
			{
				return;
			}

			CharacterRow row = new CharacterRow()
			{
				Details = details,
			};

			VisualElement rowRoot = new VisualElement();
			rowRoot.AddToClassList(CHARACTER_ROW_CLASS);

			Label name = new Label(details.CharacterName);
			name.AddToClassList(CHARACTER_ROW_NAME_CLASS);

			// A character whose body is still in the world reads as an ordinary row otherwise,
			// so the player would select it with no idea they are resuming a session that kept
			// running — and possibly ended — without them.
			Label scene = new Label(details.IsCombatLogged
				? $"{details.SceneName} — still in world (combat logout), select to rejoin"
				: details.SceneName);
			scene.AddToClassList(CHARACTER_ROW_SCENE_CLASS);

			rowRoot.Add(name);
			rowRoot.Add(scene);
			rowRoot.RegisterCallback<PointerDownEvent>((evt) => OnCharacterSelected(row));

			row.Root = rowRoot;
			row.Name = name;
			row.Scene = scene;

			characterListContainer.Add(rowRoot);
			characterList.Add(row);
		}

		/// <summary>
		/// Handles character selection and updates the highlighted row.
		/// </summary>
		/// <param name="row">The selected character row.</param>
		private void OnCharacterSelected(CharacterRow row)
		{
			if (selectedCharacter != null)
			{
				selectedCharacter.Root.RemoveFromClassList(CHARACTER_ROW_SELECTED_CLASS);
			}

			selectedCharacter = row;
			if (selectedCharacter != null)
			{
				selectedCharacter.Root.AddToClassList(CHARACTER_ROW_SELECTED_CLASS);
			}
		}

		/// <summary>
		/// Initiates character selection and connection.
		/// </summary>
		public void OnClick_SelectCharacter()
		{
			if (Client.IsConnectionReady() &&
				selectedCharacter != null &&
				selectedCharacter.Details != null)
			{
				Hide();

				// Tell the login server about our character selection.
				Client.Broadcast(new CharacterSelectBroadcast()
				{
					CharacterName = selectedCharacter.Details.CharacterName,
				}, Channel.Reliable);
				SetConnectButtonLocked(true);
			}
		}

		/// <summary>
		/// Prompts for confirmation and deletes the selected character if confirmed.
		/// </summary>
		public void OnClick_DeleteCharacter()
		{
			if (Client.IsConnectionReady() &&
				selectedCharacter != null &&
				selectedCharacter.Details != null)
			{
				if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox tooltip))
				{
					SetDeleteButtonLocked(true);

					tooltip.Open("Are you sure you would like to delete this character?", () =>
					{
						// Delete character.
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
		/// Shows the character creation panel.
		/// </summary>
		public void OnClick_CreateCharacter()
		{
			if (UIManager.TryGetTK("UICharacterCreate", out UITKCharacterCreate createCharacter))
			{
				Hide();
				createCharacter.Show();
			}
		}

		/// <summary>
		/// Stops the character list coroutine and unlocks buttons when quitting to login.
		/// </summary>
		public override void OnQuitToLogin()
		{
			base.OnQuitToLogin();

			StopProcessCharacterList();

			SetDeleteButtonLocked(false);
			SetConnectButtonLocked(false);
		}

		/// <summary>
		/// Stops the character list coroutine and returns to the login screen.
		/// </summary>
		public void OnClick_QuitToLogin()
		{
			StopProcessCharacterList();

			Client.QuitToLogin();
		}

		/// <summary>
		/// Quits the client application.
		/// </summary>
		public void OnClick_Quit()
		{
			Client.Quit();
		}


		/// <summary>
		/// Guards the control this panel disables while a server reply is outstanding.
		/// </summary>
		/// <remarks>See <see cref="PendingReplyGuard"/>.</remarks>
		private readonly PendingReplyGuard replyGuard = new PendingReplyGuard();

		/// <inheritdoc/>
		protected override void OnTick()
		{
			base.OnTick();

			if (replyGuard.HasExpired())
			{
				SetConnectButtonLocked(false);
				SetDeleteButtonLocked(false);
				Show();
				if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox dialogBox)) dialogBox.Open("The server did not respond. Please try again.");
			}
		}

		/// <summary>
		/// Sets the locked state of the connect button.
		/// </summary>
		/// <param name="locked">True to lock (disable) the button, false to unlock.</param>
		private void SetConnectButtonLocked(bool locked)
		{
			// Locking means a request is outstanding; unlocking means it is not.
			// See PendingReplyGuard for why the wait needs a deadline.
			if (locked) { replyGuard.Begin(); } else { replyGuard.Clear(); }

			if (connectButton != null)
			{
				connectButton.SetEnabled(!locked);
			}
		}

		/// <summary>
		/// Sets the locked state of the delete button.
		/// </summary>
		/// <param name="locked">True to lock (disable) the button, false to unlock.</param>
		private void SetDeleteButtonLocked(bool locked)
		{
			if (deleteButton != null)
			{
				deleteButton.SetEnabled(!locked);
			}
		}
	}
}
