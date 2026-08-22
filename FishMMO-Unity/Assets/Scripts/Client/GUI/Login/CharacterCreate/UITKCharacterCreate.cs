using FishNet.Transporting;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Auth.Core;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit implementation of the character creation control. Provides race/model/location
	/// selection and a name field, then broadcasts the creation request to the server.
	/// </summary>
	public class UITKCharacterCreate : UITKControl
	{
		/// <summary>
		/// Full-screen forms are not windows: there is nowhere to drag them to.
		/// </summary>
		/// <remarks>See <see cref="UITKControl.CanDrag"/>, which defaults every
		/// <see cref="UITKPanelLayer.Window"/> panel to draggable.</remarks>
		protected override bool CanDrag => false;

		/// <summary>
		/// The name of the create-submit button in the UI.
		/// </summary>
		private const string CREATE_BUTTON_NAME = "create-submit-btn";
		/// <summary>
		/// The name of the quit-to-login button in the UI.
		/// </summary>
		private const string QUIT_LOGIN_BUTTON_NAME = "create-quit-login-btn";
		/// <summary>
		/// The name of the quit button in the UI.
		/// </summary>
		private const string QUIT_BUTTON_NAME = "create-quit-btn";
		/// <summary>
		/// The name of the character name TextField in the UI.
		/// </summary>
		private const string NAME_FIELD_NAME = "create-name";
		/// <summary>
		/// The name of the race DropdownField in the UI.
		/// </summary>
		private const string RACE_DROPDOWN_NAME = "create-race";
		/// <summary>
		/// The name of the model DropdownField in the UI.
		/// </summary>
		private const string MODEL_DROPDOWN_NAME = "create-model";
		/// <summary>
		/// The name of the location DropdownField in the UI.
		/// </summary>
		private const string LOCATION_DROPDOWN_NAME = "create-location";
		/// <summary>
		/// The name of the result Label in the UI.
		/// </summary>
		private const string RESULT_NAME = "create-result";

		/// <summary>
		/// The name of the character being created.
		/// </summary>
		public string CharacterName = "";

		/// <summary>
		/// The selected race index.
		/// </summary>
		public int RaceIndex = -1;

		/// <summary>
		/// The selected model index.
		/// </summary>
		public int ModelIndex = -1;

		/// <summary>
		/// List of available race names for the dropdown.
		/// </summary>
		public List<string> InitialRaceNames = new List<string>();

		/// <summary>
		/// List of available model names for the dropdown.
		/// </summary>
		public List<string> InitialModelNames = new List<string>();

		/// <summary>
		/// List of available spawn location names for the dropdown.
		/// </summary>
		public List<string> InitialSpawnLocationNames = new List<string>();

		/// <summary>
		/// Cache containing details for world scenes and spawn positions.
		/// </summary>
		public WorldSceneDetailsCache WorldSceneDetailsCache = null;

		/// <summary>
		/// The selected spawn position index.
		/// </summary>
		public int SelectedSpawnPosition = -1;

		/// <summary>
		/// Maps race names to their template IDs.
		/// </summary>
		private Dictionary<string, int> raceNameMap = new Dictionary<string, int>();

		/// <summary>
		/// Maps race names to allowed spawn positions.
		/// </summary>
		private Dictionary<string, HashSet<string>> raceSpawnPositionMap = new Dictionary<string, HashSet<string>>();

		private TextField nameField;
		private DropdownField raceDropdown;
		private DropdownField modelDropdown;
		private DropdownField locationDropdown;
		private Button createButton;
		private Label resultLabel;

		/// <summary>
		/// Resolves and caches visual elements and wires up callbacks.
		/// </summary>
		public override void OnStarting()
		{
			if (Root == null)
			{
				return;
			}

			nameField = Root.Q<TextField>(NAME_FIELD_NAME);
			raceDropdown = Root.Q<DropdownField>(RACE_DROPDOWN_NAME);
			modelDropdown = Root.Q<DropdownField>(MODEL_DROPDOWN_NAME);
			locationDropdown = Root.Q<DropdownField>(LOCATION_DROPDOWN_NAME);
			createButton = Root.Q<Button>(CREATE_BUTTON_NAME);
			resultLabel = Root.Q<Label>(RESULT_NAME);

			if (nameField != null)
			{
				nameField.RegisterValueChangedCallback((evt) => CharacterName = evt.newValue);
			}
			if (raceDropdown != null)
			{
				raceDropdown.RegisterValueChangedCallback((evt) => OnRaceDropdownValueChanged());
			}
			if (modelDropdown != null)
			{
				modelDropdown.RegisterValueChangedCallback((evt) => OnModelDropdownValueChanged());
			}
			if (locationDropdown != null)
			{
				locationDropdown.RegisterValueChangedCallback((evt) => OnSpawnLocationDropdownValueChanged());
			}
			if (createButton != null)
			{
				createButton.clicked += OnClick_CreateCharacter;
			}

			/* Back, not quit-to-login. This button is labelled "Back" and sits beside a separate
			 * "Quit" — and Escape on this same screen has always gone to the character list, with
			 * a comment below saying that is what Back means here. The two disagreed: the button
			 * called Client.QuitToLogin(), which tears the whole session down, so a player who
			 * changed their mind about creating a character was logged out and landed back on the
			 * sign-in screen. They now share one handler, so they cannot drift apart again.
			 *
			 * This is the one place the UI Toolkit panels deliberately do NOT reproduce the UGUI
			 * behaviour: the old CharacterCreate "Back" button was wired to OnClick_QuitToLogin in
			 * the scene. Point this at OnClick_QuitToLogin to restore that exactly. */
			Button quitToLoginButton = Root.Q<Button>(QUIT_LOGIN_BUTTON_NAME);
			if (quitToLoginButton != null)
			{
				quitToLoginButton.clicked += OnClick_Back;
			}

			Button quitButton = Root.Q<Button>(QUIT_BUTTON_NAME);
			if (quitButton != null)
			{
				quitButton.clicked += OnClick_Quit;
			}

			// Enter creates, Escape goes back to the character list rather than to login — Back
			// on this screen means "I changed my mind about creating", not "log me out". Escape
			// and the Back button share OnClick_Back for exactly that reason.
			// Enter observes the same lock as the Create button it mirrors; see LoginKeys.Attach.
			LoginKeys.Attach(this, Root, OnClick_CreateCharacter, OnClick_Back, () => !replyGuard.IsPending);
			LoginKeys.SetTabOrder(Root, nameField, raceDropdown, modelDropdown, locationDropdown, createButton, quitToLoginButton, quitButton);
		}

		/// <summary>
		/// Returns to the character list without tearing the session down. Bound to both the Back
		/// button and Escape.
		/// </summary>
		/// <remarks>
		/// Unlocking is not cosmetic here. Leaving while a creation was in flight left the reply
		/// guard armed, and the guard's expiry calls <see cref="UITKControl.Show"/> on this panel —
		/// so half a minute after the player had moved on, the character-create form reappeared
		/// over the character list, the world list, or the game itself.
		/// </remarks>
		public void OnClick_Back()
		{
			SetCreateButtonLocked(false);

			if (UIManager.TryGetTK("UICharacterSelect", out UITKCharacterSelect characterSelect))
			{
				Hide();
				characterSelect.Show();
				return;
			}

			// No character list panel; the login screen is the only other place to be.
			OnClick_QuitToLogin();
		}

		/// <summary>
		/// Subscribes to events and populates the dropdowns when the client is injected.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
			Client.NetworkManager.ClientManager.RegisterBroadcast<CharacterCreateResultBroadcast>(OnClientCharacterCreateResultBroadcastReceived);

			PopulateDropdowns();
		}

		/// <summary>
		/// Re-populates the dropdowns after the visual tree was rebuilt.
		/// </summary>
		/// <remarks>
		/// This is half of the fix for a character-create screen that was inert on the first
		/// login. The scene sets <c>StartOpen: 0</c>, so the document is disabled and
		/// <c>TryStart</c> defers <see cref="OnStarting"/> to the visual-tree retry coroutine.
		/// <c>UIManager.SetClient</c> then runs <see cref="OnClientSet"/> — which is where the
		/// population lived — while <c>raceDropdown</c>, <c>modelDropdown</c> and
		/// <c>locationDropdown</c> were all still null, and every access was guarded by a null
		/// check, so the whole thing silently did nothing. The player got three empty dropdowns and
		/// a Create button that could never satisfy its own preconditions.
		/// <para>
		/// Both hooks are needed and neither is sufficient. <see cref="OnAfterShow"/> alone misses
		/// the very first open, because <c>hasStarted</c> is still false there and
		/// <c>ReinitializeIfTreeReplaced</c> bails out; this one alone misses nothing structural
		/// but is not called on an ordinary re-show. Population is idempotent and selection is
		/// preserved by name, so running it from both costs a rebuild of three small lists.
		/// </para>
		/// <para>
		/// Reordering the one call in <c>UIManager.SetClient</c> would also have made the symptom
		/// go away, and would have left the panel just as dependent on the order two unrelated
		/// systems happen to initialise in — which is what produced the bug's nastiest property:
		/// quitting to login does <c>SetClient(null)</c> then <c>SetClient(client)</c>, by which
		/// time the tree exists, so it healed itself on the second login and presented as
		/// intermittent.
		/// </para>
		/// </remarks>
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			PopulateDropdowns();

			// The rebuilt tree carries the UXML's empty placeholder; put the message back.
			if (resultLabel != null)
			{
				resultLabel.text = this.pendingResult ?? string.Empty;
			}

			ReapplyCreateLock();
		}

		/// <inheritdoc cref="OnAfterStarting"/>
		protected override void OnAfterShow()
		{
			base.OnAfterShow();
			PopulateDropdowns();

			SetResult(null);
			ReapplyCreateLock();
			LoginKeys.FocusFirst(Root, nameField);
		}

		/// <summary>
		/// Puts the Create button back into the state the outstanding request implies.
		/// </summary>
		/// <remarks>
		/// <see cref="SetCreateButtonLocked"/> writes into a button that the next hide/show
		/// replaces, so the panel could come back offering Create while the creation it had
		/// already sent was still unanswered — and a second click creates a second character.
		/// Driven off the guard so the button cannot disagree with the wait it represents.
		/// </remarks>
		private void ReapplyCreateLock()
		{
			if (createButton != null)
			{
				createButton.SetEnabled(!replyGuard.IsPending);
			}
		}

		/// <summary>
		/// Builds the race/model/location caches and writes them into the dropdowns.
		/// </summary>
		/// <remarks>
		/// Idempotent and order-independent: safe to call before the client exists, before the
		/// visual tree exists, and any number of times afterwards.
		/// </remarks>
		private void PopulateDropdowns()
		{
			BuildTemplateCaches();
			ApplyRaceAndModelChoices();
			UpdateStartLocationDropdown();
		}

		/// <summary>
		/// True once <see cref="BuildTemplateCaches"/> has produced a usable set of races.
		/// </summary>
		private bool templateCachesBuilt;

		/// <summary>
		/// Model names per race, in template order.
		/// </summary>
		/// <remarks>
		/// Per race, not one flat list. The models of every race used to be appended to a single
		/// <c>InitialModelNames</c> and handed to the model dropdown whole, so selecting the second
		/// race offered the first race's models and the index sent to the server addressed a model
		/// that race does not have.
		/// </remarks>
		private readonly Dictionary<string, List<string>> raceModelNames = new Dictionary<string, List<string>>();

		/// <summary>
		/// Reads the race templates into the name/model/spawn caches.
		/// </summary>
		/// <remarks>
		/// Needs the client, because a race is only offerable if its prefab is actually in
		/// <c>NetworkManager.SpawnablePrefabs</c> — a race whose prefab the server will not spawn
		/// is a character the player can create and then never log in to.
		/// </remarks>
		private void BuildTemplateCaches()
		{
			if (this.templateCachesBuilt ||
				Client == null ||
				Client.NetworkManager == null ||
				WorldSceneDetailsCache == null ||
				WorldSceneDetailsCache.Scenes == null)
			{
				return;
			}

			raceNameMap.Clear();
			raceModelNames.Clear();
			raceSpawnPositionMap.Clear();
			InitialRaceNames?.Clear();
			InitialModelNames?.Clear();

			Dictionary<int, RaceTemplate> raceTemplates = RaceTemplate.GetCache<RaceTemplate>();
			foreach (KeyValuePair<int, RaceTemplate> pair in raceTemplates)
			{
				if (pair.Value.Prefab == null)
				{
					continue;
				}
				IPlayerCharacter character = pair.Value.Prefab.GetComponent<IPlayerCharacter>();
				if (character == null)
				{
					continue;
				}
				if (Client.NetworkManager.SpawnablePrefabs.GetObject(false, character.NetworkObject.PrefabId) == null)
				{
					continue;
				}

				string raceName = pair.Value.Name;
				if (raceNameMap.ContainsKey(raceName))
				{
					// Two templates claiming one name; the first wins rather than throwing.
					Log.Warning("UITKCharacterCreate", $"Duplicate race name '{raceName}'; ignoring the later template.");
					continue;
				}

				raceNameMap.Add(raceName, pair.Key);
				InitialRaceNames?.Add(raceName);

				List<string> models = new List<string>();
				int modelCount = pair.Value.GetModelCount(CharacterGender.Unspecified);
				for (int modelIndex = 0; modelIndex < modelCount; modelIndex++)
				{
					string modelName = pair.Value.GetModelName(modelIndex);
					if (!string.IsNullOrWhiteSpace(modelName))
					{
						models.Add(modelName);
					}
				}

				if (models.Count < 1)
				{
					if (pair.Value.PlaceholderModel != null)
					{
						/* A race with only a placeholder still has exactly one model — index 0 —
						 * and the dropdown needs a row for it, or the create button's
						 * ModelIndex > -1 precondition can never be met for that race. */
						models.Add("Default");
					}
					else
					{
						Log.Warning("UITKCharacterCreate", $"No standard model or placeholder exists for {pair.Value.name}");
					}
				}

				raceModelNames[raceName] = models;

				// Spawn positions this race is allowed to start at.
				if (!raceSpawnPositionMap.TryGetValue(raceName, out HashSet<string> spawners))
				{
					raceSpawnPositionMap.Add(raceName, spawners = new HashSet<string>());
				}

				foreach (WorldSceneDetails details in WorldSceneDetailsCache.Scenes.Values)
				{
					foreach (CharacterInitialSpawnPositionDetails initialSpawnPosition in details.InitialSpawnPositions.Values)
					{
						foreach (RaceTemplate raceTemplate in initialSpawnPosition.AllowedRaces)
						{
							if (raceName == raceTemplate.Name)
							{
								spawners.Add(initialSpawnPosition.SpawnerName);
							}
						}
					}
				}
			}

			// Only latch once there is something to offer, so a call that arrived before the
			// template cache was loaded does not permanently poison the panel.
			this.templateCachesBuilt = raceNameMap.Count > 0;
		}

		/// <summary>
		/// Writes the race and model choices into their dropdowns, preserving the current
		/// selection by name rather than by index.
		/// </summary>
		private void ApplyRaceAndModelChoices()
		{
			if (raceDropdown == null || raceNameMap.Count < 1)
			{
				return;
			}

			// Remember what was selected before the rebuild; indices are meaningless across one.
			string selectedRace = raceDropdown.index >= 0 && raceDropdown.index < raceDropdown.choices.Count
				? raceDropdown.choices[raceDropdown.index]
				: null;

			List<string> raceNames = new List<string>(raceNameMap.Keys);
			raceDropdown.choices = raceNames;

			int raceIndex = selectedRace != null ? raceNames.IndexOf(selectedRace) : -1;
			if (raceIndex < 0)
			{
				raceIndex = raceNames.Count > 0 ? 0 : -1;
			}
			raceDropdown.index = raceIndex;
			RaceIndex = raceIndex;

			ApplyModelChoices();
		}

		/// <summary>
		/// Writes the currently selected race's models into the model dropdown.
		/// </summary>
		private void ApplyModelChoices()
		{
			if (modelDropdown == null)
			{
				return;
			}

			string raceName = SelectedRaceName();
			List<string> models = raceName != null && raceModelNames.TryGetValue(raceName, out List<string> found)
				? found
				: new List<string>();

			string selectedModel = modelDropdown.index >= 0 && modelDropdown.index < modelDropdown.choices.Count
				? modelDropdown.choices[modelDropdown.index]
				: null;

			modelDropdown.choices = new List<string>(models);
			InitialModelNames?.Clear();
			if (InitialModelNames != null)
			{
				InitialModelNames.AddRange(models);
			}

			int modelIndex = selectedModel != null ? models.IndexOf(selectedModel) : -1;
			if (modelIndex < 0)
			{
				modelIndex = models.Count > 0 ? 0 : -1;
			}
			modelDropdown.index = modelIndex;
			ModelIndex = modelIndex;
		}

		/// <summary>
		/// The race name currently selected in the dropdown, or null when there is none.
		/// </summary>
		private string SelectedRaceName()
		{
			if (raceDropdown == null ||
				raceDropdown.index < 0 ||
				raceDropdown.index >= raceDropdown.choices.Count)
			{
				return null;
			}
			return raceDropdown.choices[raceDropdown.index];
		}

		/// <summary>
		/// Unsubscribes from events when the client is cleared.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState -= ClientManager_OnClientConnectionState;
			Client.NetworkManager.ClientManager.UnregisterBroadcast<CharacterCreateResultBroadcast>(OnClientCharacterCreateResultBroadcastReceived);
		}

		/// <summary>
		/// Handles client connection state changes. Hides the panel when disconnected.
		/// </summary>
		/// <param name="obj">Connection state arguments.</param>
		private void ClientManager_OnClientConnectionState(ClientConnectionStateArgs obj)
		{
			if (obj.ConnectionState == LocalConnectionState.Stopped)
			{
				/* Disarm before hiding. The connection is gone, so the create request this guard
				 * was waiting on can never be answered — and an armed guard that expires calls
				 * Show() on this panel, putting a dead character-create form back on top of the
				 * login screen the teardown had just restored. */
				SetCreateButtonLocked(false);

				Hide();
			}
		}

		/// <summary>
		/// Handles the character creation result broadcast, updates the UI and shows the select panel on success.
		/// </summary>
		/// <param name="msg">The broadcast message for character creation result.</param>
		/// <param name="channel">The network channel used.</param>
		private void OnClientCharacterCreateResultBroadcastReceived(CharacterCreateResultBroadcast msg, Channel channel)
		{
			SetCreateButtonLocked(false);
			if (msg.Result == CharacterCreateResult.Success)
			{
				Hide();
				if (UIManager.TryGetTK("UICharacterSelect", out UITKCharacterSelect characterSelect))
				{
					characterSelect.Show();
				}
			}
			else
			{
				SetResult(DescribeCreateResult(msg.Result));
			}
		}

		/// <summary>
		/// Updates the race index and resets the model index when the race selection changes.
		/// </summary>
		public void OnRaceDropdownValueChanged()
		{
			RaceIndex = raceDropdown != null ? raceDropdown.index : -1;

			/* The model list belongs to the race. It used to be a single flat list built by
			 * appending every race's models in turn, so choosing the second race left the first
			 * race's models on screen and the index this panel sent to the server addressed a
			 * model the chosen race does not have. */
			ApplyModelChoices();

			UpdateStartLocationDropdown();
		}

		/// <summary>
		/// Updates the model index when the model selection changes.
		/// </summary>
		public void OnModelDropdownValueChanged()
		{
			ModelIndex = modelDropdown != null ? modelDropdown.index : 0;

			UpdateModel();
		}

		/// <summary>
		/// Updates the character model preview. (Not implemented)
		/// </summary>
		private void UpdateModel()
		{
		}

		/// <summary>
		/// Updates the start location dropdown based on the selected race.
		/// </summary>
		private void UpdateStartLocationDropdown()
		{
			if (locationDropdown == null || InitialSpawnLocationNames == null)
			{
				return;
			}

			string raceName = SelectedRaceName();
			if (raceName == null)
			{
				return;
			}

			// Remember the selection by name; the index means nothing across a rebuild.
			string selectedSpawner = locationDropdown.index >= 0 && locationDropdown.index < locationDropdown.choices.Count
				? locationDropdown.choices[locationDropdown.index]
				: null;

			InitialSpawnLocationNames.Clear();

			// Find all spawn locations that allow the currently selected race.
			if (raceSpawnPositionMap.TryGetValue(raceName, out HashSet<string> spawners))
			{
				foreach (string spawner in spawners)
				{
					InitialSpawnLocationNames.Add(spawner);
				}
			}

			locationDropdown.choices = new List<string>(InitialSpawnLocationNames);

			int spawnIndex = selectedSpawner != null ? InitialSpawnLocationNames.IndexOf(selectedSpawner) : -1;
			if (spawnIndex < 0)
			{
				spawnIndex = InitialSpawnLocationNames.Count > 0 ? 0 : -1;
			}
			SelectedSpawnPosition = spawnIndex;
			locationDropdown.index = spawnIndex;
		}

		/// <summary>
		/// Updates the selected spawn position when the location selection changes.
		/// </summary>
		public void OnSpawnLocationDropdownValueChanged()
		{
			SelectedSpawnPosition = locationDropdown != null ? locationDropdown.index : -1;
		}

		/// <summary>
		/// Validates input and broadcasts the character creation request.
		/// </summary>
		public void OnClick_CreateCharacter()
		{
			/* Every precondition reports. This method used to be one big `if` with no `else` and
			 * a `foreach` that could fall off the end, so eight distinct reasons for refusing —
			 * a disconnected client, an unusable name, a race with no models, a race with no
			 * spawn point, an unloaded scene cache — all presented identically as a button that
			 * did nothing at all when clicked. */
			if (Client == null || !Client.IsConnectionReady())
			{
				SetResult("Not connected to the login server.");
				return;
			}

			if (!Authentication.IsAllowedCharacterName(CharacterName))
			{
				/* The rule quoted here was wrong in both directions — the maximum is 24, not 32,
				 * and single interior spaces are allowed, so a player typing "Aragorn of Arnor"
				 * was told letters only while a 30-character name was refused by a screen that had
				 * just said 32 was fine. Authentication owns the constraint and builds the
				 * sentence from the same constants the validator uses, so the two cannot drift
				 * apart again. */
				SetResult(string.IsNullOrWhiteSpace(CharacterName)
					? "Please enter a character name."
					: Authentication.InvalidCharacterNameError);
				return;
			}

			if (WorldSceneDetailsCache == null || WorldSceneDetailsCache.Scenes == null)
			{
				SetResult("The world data is still loading. Please try again in a moment.");
				return;
			}

			string raceName = SelectedRaceName();
			if (raceName == null || RaceIndex < 0)
			{
				SetResult("Please choose a race.");
				return;
			}

			if (ModelIndex < 0)
			{
				SetResult("Please choose a model.");
				return;
			}

			if (SelectedSpawnPosition < 0 ||
				InitialSpawnLocationNames == null ||
				SelectedSpawnPosition >= InitialSpawnLocationNames.Count)
			{
				SetResult("Please choose a starting location.");
				return;
			}

			if (!raceNameMap.TryGetValue(raceName, out int raceTemplateID))
			{
				SetResult("That race is not available on this server.");
				return;
			}

			string spawnerName = InitialSpawnLocationNames[SelectedSpawnPosition];

			foreach (WorldSceneDetails details in WorldSceneDetailsCache.Scenes.Values)
			{
				if (!details.InitialSpawnPositions.TryGetValue(spawnerName, out CharacterInitialSpawnPositionDetails spawnPosition))
				{
					continue;
				}

				SetResult("Creating character...");

				// Create character.
				Client.Broadcast(new CharacterCreateBroadcast()
				{
					CharacterName = CharacterName,
					RaceTemplateID = raceTemplateID,
					ModelIndex = ModelIndex,
					SceneName = spawnPosition.SceneName,
					SpawnerName = spawnPosition.SpawnerName,
				}, Channel.Reliable);
				SetCreateButtonLocked(true);
				return;
			}

			// The spawner is in this panel's own list but in none of the loaded scenes, which
			// means the scene cache and the spawn map disagree. Nothing the player can do.
			SetResult($"Starting location '{spawnerName}' is not available. Please choose another.");
			Log.Warning("UITKCharacterCreate", $"Spawner '{spawnerName}' was offered but exists in no loaded WorldSceneDetails.");
		}

		/// <summary>
		/// Writes the result line, holding the text across tree rebuilds.
		/// </summary>
		/// <remarks>
		/// Held as state as well as written, because the panel is re-cloned on every show and this
		/// label is the only feedback the screen has. See <see cref="UITKControl.OnAfterShow"/>.
		/// </remarks>
		/// <param name="text">The message, or null to clear the line.</param>
		private void SetResult(string text)
		{
			this.pendingResult = text;

			if (resultLabel != null)
			{
				resultLabel.text = text ?? string.Empty;
			}
		}

		/// <summary>The result message this panel wants displayed, held across tree rebuilds.</summary>
		private string pendingResult;

		/// <summary>
		/// Turns a server refusal into a sentence.
		/// </summary>
		/// <remarks>
		/// The label used to show <c>msg.Result.ToString()</c>, i.e. the raw enum member name —
		/// "CharacterNameTaken", "InvalidSpawn" — which tells a player what happened only if they
		/// happen to have read the source.
		/// </remarks>
		/// <param name="result">The server's refusal.</param>
		/// <returns>A message for the result line.</returns>
		private static string DescribeCreateResult(CharacterCreateResult result)
		{
			switch (result)
			{
				case CharacterCreateResult.TooMany:
					return "You already have the maximum number of characters on this account.";
				case CharacterCreateResult.InvalidCharacterName:
					// The shared rule, for the same reason as in OnClick_CreateCharacter.
					return Authentication.InvalidCharacterNameError;
				case CharacterCreateResult.CharacterNameTaken:
					return "That name is already taken. Please choose another.";
				case CharacterCreateResult.InvalidSpawn:
					return "That starting location is not available. Please choose another.";
				case CharacterCreateResult.Error:
					return "The server could not create the character. Please try again.";
				case CharacterCreateResult.Success:
				default:
					return string.Empty;
			}
		}

		/// <summary>
		/// Unlocks the create button when quitting to login.
		/// </summary>
		public override void OnQuitToLogin()
		{
			base.OnQuitToLogin();

			SetCreateButtonLocked(false);
		}

		/// <summary>
		/// Returns to the login screen.
		/// </summary>
		public void OnClick_QuitToLogin()
		{
			// We should go back to login.
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

			// Login-flow notices are refused while another dialog is up; see LoginNotice.
			LoginNotice.Pump();

			if (replyGuard.HasExpired())
			{
				SetCreateButtonLocked(false);

				/* Show as well as say. This panel hides itself on Stopped, and the create result
				 * is the only thing that ever brings it back — so a timeout that only wrote a
				 * label wrote it into a panel that may not be on screen. */
				Show();
				SetResult("The server did not respond. Please try again.");
			}
		}

		/// <summary>
		/// Sets the locked state of the create button.
		/// </summary>
		/// <param name="locked">True to lock (disable) the button, false to unlock.</param>
		private void SetCreateButtonLocked(bool locked)
		{
			// Locking means a request is outstanding; unlocking means it is not.
			// See PendingReplyGuard for why the wait needs a deadline.
			if (locked) { replyGuard.Begin(); } else { replyGuard.Clear(); }

			if (createButton != null)
			{
				createButton.SetEnabled(!locked);
			}
		}
	}
}
