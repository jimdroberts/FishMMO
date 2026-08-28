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
		/// Full-screen forms are not windows: there is nowhere to drag them to.
		/// </summary>
		/// <remarks>See <see cref="UITKControl.CanDrag"/>, which defaults every
		/// <see cref="UITKPanelLayer.Window"/> panel to draggable.</remarks>
		protected override bool CanDrag => false;

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
		/// The name of the refresh button in the UI.
		/// </summary>
		private const string REFRESH_BUTTON_NAME = "character-refresh-btn";
		/// <summary>
		/// The name of the status Label in the UI.
		/// </summary>
		private const string STATUS_NAME = "character-status";

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
		/// <summary>Re-requests the character list; disabled while a request is outstanding.</summary>
		private Button refreshButton;
		/// <summary>Explains a list that did not arrive. Collapsed when there is nothing to say.</summary>
		private Label statusLabel;

		/// <summary>
		/// The list of all character row view models currently displayed.
		/// </summary>
		private readonly List<CharacterRow> characterList = new List<CharacterRow>();
		/// <summary>
		/// The currently selected character row, or null if none is selected.
		/// </summary>
		private CharacterRow selectedCharacter;

		/// <summary>
		/// The account's characters as the server last described them.
		/// </summary>
		/// <remarks>
		/// The rows used to be the only record of what the server had sent, and rows do not
		/// survive this panel. It is authored <c>StartOpen: 0</c>, so on the first login it has no
		/// visual tree at all when the list arrives and <see cref="CreateCharacterRow"/> dropped
		/// every character on the floor — the player reached an empty character screen with a
		/// Create button and no explanation. On later arrivals the handler hid the panel first,
		/// which disables the UIDocument and discards the tree, built the rows into that discarded
		/// tree, and then showed the panel again — re-cloning the UXML and its empty list.
		/// <para>
		/// Holding the characters here makes the rows a rendering of state rather than the state
		/// itself, so <see cref="RebuildCharacterRows"/> can run from
		/// <see cref="OnAfterShow"/>/<see cref="OnAfterStarting"/> against whatever tree is
		/// actually on screen. See <see cref="UITKControl.OnAfterShow"/>.
		/// </para>
		/// </remarks>
		private readonly List<CharacterDetails> characters = new List<CharacterDetails>();

		/// <summary>
		/// Name of the character the player had highlighted, so the highlight survives a rebuild.
		/// </summary>
		private string selectedCharacterName;

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
			refreshButton = Root.Q<Button>(REFRESH_BUTTON_NAME);
			statusLabel = Root.Q<Label>(STATUS_NAME);

			if (refreshButton != null)
			{
				refreshButton.clicked += OnClick_Refresh;
			}

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

			// Enter plays the highlighted character, Escape goes back to the login screen.
			// Enter observes the same lock as the Connect button it mirrors; see LoginKeys.Attach.
			LoginKeys.Attach(this, Root, OnClick_SelectCharacter, OnClick_QuitToLogin, () => !replyGuard.IsPending);
			LoginKeys.SetTabOrder(Root, connectButton, createButton, deleteButton, refreshButton, quitToLoginButton, quitButton);
		}

		/// <summary>
		/// Subscribes to connection, authentication, and character broadcast events when the client is injected.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
			Client.NetworkManager.ClientManager.RegisterBroadcast<CharacterListBroadcast>(OnClientCharacterListBroadcastReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<CharacterListResultBroadcast>(OnClientCharacterListResultBroadcastReceived);
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
			Client.NetworkManager.ClientManager.UnregisterBroadcast<CharacterListResultBroadcast>(OnClientCharacterListResultBroadcastReceived);
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
		/// Forgets the account's characters and removes every row.
		/// </summary>
		public void DestroyCharacterList()
		{
			ClearCharacterRows();
			characters.Clear();
			selectedCharacterName = null;
		}

		/// <summary>
		/// Removes the row elements without forgetting the characters they describe.
		/// </summary>
		/// <remarks>
		/// Separate from <see cref="DestroyCharacterList"/> because a tree rebuild throws the rows
		/// away and must not throw the account's characters away with them.
		/// </remarks>
		private void ClearCharacterRows()
		{
			for (int i = 0; i < characterList.Count; ++i)
			{
				characterList[i].Root.RemoveFromHierarchy();
			}
			characterList.Clear();
			selectedCharacter = null;
		}

		/// <summary>
		/// Renders <see cref="characters"/> into whatever visual tree is currently on screen.
		/// </summary>
		/// <remarks>
		/// Idempotent, and a no-op while there is no tree to build into — the panel is shown
		/// later and this runs again from <see cref="OnAfterStarting"/>.
		/// </remarks>
		private void RebuildCharacterRows()
		{
			if (characterListContainer == null)
			{
				return;
			}

			ClearCharacterRows();

			for (int i = 0; i < characters.Count; ++i)
			{
				CreateCharacterRow(characters[i]);
			}

			RestoreSelection();
		}

		/// <summary>
		/// Re-highlights the row the player had chosen, if it is still in the list.
		/// </summary>
		private void RestoreSelection()
		{
			if (string.IsNullOrEmpty(selectedCharacterName))
			{
				return;
			}

			for (int i = 0; i < characterList.Count; ++i)
			{
				if (characterList[i].Details != null &&
					characterList[i].Details.CharacterName == selectedCharacterName)
				{
					OnCharacterSelected(characterList[i]);
					return;
				}
			}

			// The character it named is gone — deleted, or absent from a fresher list.
			selectedCharacterName = null;
		}

		/// <summary>
		/// Handles an incoming character list broadcast and populates the character rows.
		/// </summary>
		/// <param name="msg">The broadcast message containing character details.</param>
		/// <param name="channel">The network channel used.</param>
		private void OnClientCharacterListBroadcastReceived(CharacterListBroadcast msg, Channel channel)
		{
			// The wait is over; see RequestCharacterList.
			characterListGuard.Clear();
			SetStatus(null);
			SetRefreshLocked(false);

			/* State first, rows second, and no Hide() in between. This handler used to hide the
			 * panel, build the rows, and let the post-processing coroutine show it again — which
			 * built every row into a visual tree that Hide() had already discarded, so the player
			 * was shown a freshly cloned, empty list. See the remarks on `characters`. */
			DestroyCharacterList();

			if (msg.Characters != null)
			{
				for (int i = 0; i < msg.Characters.Length; ++i)
				{
					characters.Add(msg.Characters[i]);
				}
			}

			RebuildCharacterRows();

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

			/* Only if this screen is still the one the player is on. A list can arrive at any
			 * time — the refresh button, a second push from the server, a reply that overtook a
			 * quit — and showing unconditionally put the character list on top of the character
			 * the player was in the middle of creating. The rows are already built, so the list
			 * is correct whenever this panel does come back. */
			if (!AnotherPanelOwnsTheScreen())
			{
				Show();
			}
		}

		/// <summary>
		/// Whether a panel downstream of this one currently owns the screen.
		/// </summary>
		/// <remarks>
		/// Both of these are reached <i>from</i> this screen and replace it: character creation is
		/// a form the player is filling in, and the world list is the next step after a selection
		/// that has already succeeded. Neither should be covered by a list arriving behind it.
		/// </remarks>
		private bool AnotherPanelOwnsTheScreen()
		{
			return (UIManager.TryGetTK("UICharacterCreate", out UITKControl characterCreate) && characterCreate.Visible) ||
				(UIManager.TryGetTK("UIServerSelect", out UITKControl serverSelect) && serverSelect.Visible);
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

			/* Into the model, not straight into the tree. This broadcast arrives while the
			 * character-create panel is the one on screen, so the row would be built into a tree
			 * this panel discards on its way back — and then rebuilt from a model that had never
			 * heard of the new character. */
			characters.Add(details);
			RebuildCharacterRows();
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

			if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox dialogBox))
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
			SetDeleteButtonLocked(false);

			/* An empty name is how the server reports a delete that failed — see
			 * CharacterSelectSystem.SendDeleteFailure. It matches no row, so the loop below simply
			 * found nothing and the panel said nothing at all: the character stayed in the list,
			 * the button came back, and the player was left to guess whether the click had
			 * registered. */
			if (string.IsNullOrEmpty(msg.CharacterName))
			{
				LoginNotice.Show("The character could not be deleted. Please try again.");
				return;
			}

			// Remove the character from our characters list.
			for (int i = characters.Count - 1; i >= 0; --i)
			{
				if (characters[i] != null &&
					characters[i].CharacterName == msg.CharacterName)
				{
					characters.RemoveAt(i);
				}
			}

			if (selectedCharacterName == msg.CharacterName)
			{
				selectedCharacterName = null;
			}

			RebuildCharacterRows();
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

			// By name, because the row object does not survive a tree rebuild. See RestoreSelection.
			selectedCharacterName = selectedCharacter?.Details?.CharacterName;
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
				/* Captured now, read later. The confirmation callback used to reach back into
				 * `selectedCharacter`, which is a live reference to a row — and rows are rebuilt
				 * whenever a list arrives or the visual tree is replaced. A refresh, a fresh push
				 * from the server or a hide/show between opening the dialog and pressing Confirm
				 * therefore either nulled it (a NullReferenceException inside the callback, with
				 * the delete button left locked) or pointed it at whichever character now occupies
				 * that slot — deleting the wrong one, irrecoverably, on a Yes the player gave for
				 * a different question. */
				string characterName = selectedCharacter.Details.CharacterName;

				if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox tooltip))
				{
					SetDeleteButtonLocked(true);

					// Naming the character is also what makes the confirmation answerable.
					tooltip.Open($"Are you sure you would like to delete '{characterName}'?", () =>
					{
						// Delete character.
						Client.Broadcast(new CharacterDeleteBroadcast()
						{
							CharacterName = characterName,
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

			/* The list belongs to the account that just logged out. Leaving the rows behind meant
			 * the next account to sign in saw the previous one's characters until its own list
			 * arrived — and saw them again, indefinitely, if it never did. */
			DestroyCharacterList();
			characterListGuard.Clear();
			SetRefreshLocked(false);
			SetStatus(null);
		}

		/// <summary>
		/// Puts this panel back on screen for a player backing out of a screen downstream of it.
		/// </summary>
		/// <remarks>
		/// The selection that took the player away from here is over the moment they come back,
		/// so the wait it armed has to be cleared with them. <see cref="OnClick_SelectCharacter"/>
		/// locks Connect and arms <see cref="replyGuard"/>, and the reply that normally clears it
		/// is a broadcast like any other: if it is lost, the panel the player has just returned to
		/// comes back with a dead Connect button and then announces, half a minute later, that the
		/// server did not respond to a request the player has already abandoned.
		/// <para>
		/// Only the panel state is reset. The character list itself is kept — it still describes
		/// the same account — so the rows are on screen the moment this returns rather than after
		/// another round trip. See <see cref="OnQuitToLogin"/> for the case where the list must go.
		/// </para>
		/// </remarks>
		public void ReturnFromDownstreamPanel()
		{
			SetConnectButtonLocked(false);
			SetDeleteButtonLocked(false);

			Show();
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

		/// <summary>
		/// Guards the character-list request, which is the one request in this flow that nothing
		/// was watching.
		/// </summary>
		/// <remarks>
		/// Kept separate from <see cref="replyGuard"/> because the two waits overlap in neither
		/// direction and have different exits: the connect guard puts this panel back, and this
		/// one has to be able to ask again. The request itself is sent a second after login
		/// success from <see cref="UITKLogin"/>, at the exact moment that panel hides itself, so
		/// there is no screen at all behind an unanswered one.
		/// </remarks>
		private readonly PendingReplyGuard characterListGuard = new PendingReplyGuard();

		/// <summary>
		/// Asks the server for this account's character list, arming a deadline for the reply.
		/// </summary>
		/// <remarks>
		/// The request used to be broadcast bare from <c>UITKLogin.OnProcessLoginSuccess</c> with
		/// nothing armed on either side. The server has three refusal paths that answer nothing at
		/// all — a 2000ms per-connection cooldown, another request in flight, and the async
		/// handler's catch-all — and the client is at its most exposed at that moment: the login
		/// panel has just hidden itself and this panel only shows when a list arrives. That is the
		/// screen with no panel on it and no way out but Alt+F4.
		/// <para>
		/// The server now answers every request (see <c>CharacterListResultBroadcast</c>). This
		/// deadline covers what a reply cannot: a request that never reached the server, and a
		/// server that stops existing between receiving it and answering it.
		/// </para>
		/// </remarks>
		public void RequestCharacterList()
		{
			if (Client == null)
			{
				return;
			}

			characterListGuard.Begin();
			SetRefreshLocked(true);
			SetStatus("Loading characters...");

			Client.Broadcast(new CharacterRequestListBroadcast(), Channel.Reliable);
		}

		/// <summary>
		/// Handles a character-list request the server declined to answer with a list.
		/// </summary>
		/// <param name="msg">The refusal.</param>
		/// <param name="channel">The network channel used.</param>
		private void OnClientCharacterListResultBroadcastReceived(CharacterListResultBroadcast msg, Channel channel)
		{
			characterListGuard.Clear();
			SetRefreshLocked(false);

			/* Show, unless something downstream of this screen is already up. This message is
			 * otherwise the only thing standing between the player and an empty screen: whatever
			 * hid the panel — login success, or this panel's own handler for a previous list —
			 * has already run. The status line below is held as state, so it is still there when
			 * the player comes back. */
			if (!AnotherPanelOwnsTheScreen())
			{
				Show();
			}

			SetStatus(msg.Result == CharacterListResult.Busy
				? "The server is busy. Press Refresh to try again."
				: "Your characters could not be loaded. This is a server-side problem, not a problem with your account. Press Refresh to try again.");
		}

		/// <inheritdoc/>
		protected override void OnTick()
		{
			base.OnTick();

			// Login-flow notices are refused while another dialog is up; see LoginNotice.
			LoginNotice.Pump();

			if (replyGuard.HasExpired())
			{
				SetConnectButtonLocked(false);
				SetDeleteButtonLocked(false);
				Show();
				LoginNotice.Show("The server did not respond. Please try again.");
			}

			if (characterListGuard.HasExpired())
			{
				SetRefreshLocked(false);
				Show();
				SetStatus("The server did not answer the character list request. Press Refresh to try again.");
			}
		}

		/// <summary>
		/// Re-requests the character list.
		/// </summary>
		/// <remarks>
		/// There was no way to ask twice. The request was sent exactly once per session, from the
		/// login panel, and any refusal or loss of it was final for that session.
		/// </remarks>
		public void OnClick_Refresh()
		{
			DestroyCharacterList();
			RequestCharacterList();
		}

		/// <summary>
		/// Writes the status line, collapsing it when there is nothing to say.
		/// </summary>
		/// <param name="text">Message to display, or null/empty to hide the line.</param>
		private void SetStatus(string text)
		{
			/* Held as state as well as written to the tree. Enabling the UIDocument re-clones the
			 * UXML, so a message written before a Show() is discarded — and every caller here is
			 * "explain why, then show the panel". See UITKControl.OnAfterShow. */
			this.pendingStatus = text;

			if (statusLabel == null)
			{
				return;
			}

			bool hasText = !string.IsNullOrEmpty(text);
			statusLabel.text = hasText ? text : string.Empty;
			statusLabel.style.display = hasText ? DisplayStyle.Flex : DisplayStyle.None;
		}

		/// <summary>
		/// Sets the locked state of the refresh button.
		/// </summary>
		/// <param name="locked">True to lock (disable) the button, false to unlock.</param>
		private void SetRefreshLocked(bool locked)
		{
			if (refreshButton != null)
			{
				refreshButton.SetEnabled(!locked);
			}
		}

		/// <summary>
		/// Re-applies the status line and the refresh lock after the visual tree was rebuilt.
		/// </summary>
		/// <remarks>
		/// The elements are new after every hide/show, so a message written before the rebuild is
		/// gone. This panel is shown precisely <i>because</i> something went wrong, so losing the
		/// sentence explaining it would leave an empty list with no explanation.
		/// </remarks>
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();

			ReapplyPanelState();
		}

		/// <inheritdoc/>
		protected override void OnAfterShow()
		{
			base.OnAfterShow();

			ReapplyPanelState();
		}

		/// <summary>
		/// Writes everything this panel holds as state into the tree that is on screen now.
		/// </summary>
		/// <remarks>
		/// The rows, the status line and the two button locks are all written into elements that
		/// a later hide/show replaces. The locks matter as much as the rows: Connect came back
		/// enabled after a rebuild while the reply guard was still pending, so a second click sent
		/// a second selection for a request that was already in flight.
		/// </remarks>
		private void ReapplyPanelState()
		{
			RebuildCharacterRows();

			SetStatus(this.pendingStatus);
			SetRefreshLocked(characterListGuard.IsPending);

			if (connectButton != null)
			{
				connectButton.SetEnabled(!replyGuard.IsPending);
			}
		}

		/// <summary>The status message this panel wants displayed, held across tree rebuilds.</summary>
		private string pendingStatus;

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
