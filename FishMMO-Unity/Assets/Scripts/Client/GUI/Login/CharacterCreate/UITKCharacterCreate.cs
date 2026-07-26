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
		/// Initialises dropdowns and subscribes to events when the client is injected.
		/// </summary>
		public override void OnClientSet()
		{
			// Initialise race dropdown.
			if (raceDropdown != null &&
				InitialRaceNames != null &&
				InitialModelNames != null)
			{
				raceNameMap.Clear();
				InitialRaceNames.Clear();
				InitialModelNames.Clear();

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
					raceNameMap.Add(pair.Value.Name, pair.Key);
					InitialRaceNames.Add(pair.Value.Name);
					int modelCount = pair.Value.GetModelCount(CharacterGender.Unspecified);
					if (modelCount > 0)
					{
						for (int modelIndex = 0; modelIndex < modelCount; modelIndex++)
						{
							string modelName = pair.Value.GetModelName(modelIndex);
							if (!string.IsNullOrWhiteSpace(modelName))
							{
								InitialModelNames.Add(modelName);
							}
						}
					}
					else if (pair.Value.PlaceholderModel != null)
					{
						ModelIndex = 0;
					}
					else
					{
						Log.Warning("UITKCharacterCreate", $"No standard model or placeholder exists for {pair.Value.name}");
					}

					// Initialise spawn position map.
					if (!raceSpawnPositionMap.TryGetValue(pair.Value.Name, out HashSet<string> spawners))
					{
						raceSpawnPositionMap.Add(pair.Value.Name, spawners = new HashSet<string>());
					}

					foreach (WorldSceneDetails details in WorldSceneDetailsCache.Scenes.Values)
					{
						foreach (CharacterInitialSpawnPositionDetails initialSpawnPosition in details.InitialSpawnPositions.Values)
						{
							foreach (RaceTemplate raceTemplate in initialSpawnPosition.AllowedRaces)
							{
								if (pair.Value.Name == raceTemplate.Name &&
									!spawners.Contains(initialSpawnPosition.SpawnerName))
								{
									spawners.Add(initialSpawnPosition.SpawnerName);
								}
							}
						}
					}
				}
				raceDropdown.choices = new List<string>(InitialRaceNames);
				modelDropdown.choices = new List<string>(InitialModelNames);

				// Set initial race selection.
				RaceIndex = 0;
				ModelIndex = 0;
				if (raceDropdown.choices.Count > 0)
				{
					raceDropdown.index = 0;
				}
				if (modelDropdown.choices.Count > 0)
				{
					modelDropdown.index = 0;
				}
			}

			UpdateStartLocationDropdown();

			Client.NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
			Client.NetworkManager.ClientManager.RegisterBroadcast<CharacterCreateResultBroadcast>(OnClientCharacterCreateResultBroadcastReceived);
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
				Log.Info("UITKCharacterCreate",
					$"Character create result=Success name='{CharacterName}' raceIndex={RaceIndex} modelIndex={ModelIndex} spawnIndex={SelectedSpawnPosition}.");
				Hide();
				if (UIManager.TryGetTK("UICharacterSelect", out UITKCharacterSelect characterSelect))
				{
					characterSelect.Show();
				}
			}
			else
			{
				Log.Error("UITKCharacterCreate",
					$"Character create result={msg.Result} name='{CharacterName}' raceIndex={RaceIndex} modelIndex={ModelIndex} spawnIndex={SelectedSpawnPosition}. " +
					"See LoginServer CharacterCreateSystem logs for the server-side reason.");
				if (resultLabel != null)
				{
					resultLabel.text = msg.Result.ToString();
				}
			}
		}

		/// <summary>
		/// Updates the race index and resets the model index when the race selection changes.
		/// </summary>
		public void OnRaceDropdownValueChanged()
		{
			RaceIndex = raceDropdown != null ? raceDropdown.index : 0;
			// Reset Model Index.
			ModelIndex = 0;

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
			// Update start location dropdown.
			if (locationDropdown != null &&
				raceDropdown != null &&
				raceDropdown.choices.Count > RaceIndex &&
				RaceIndex >= 0 &&
				InitialSpawnLocationNames != null)
			{
				InitialSpawnLocationNames.Clear();

				string raceName = raceDropdown.choices[RaceIndex];

				// Find all spawn locations that allow the currently selected race.
				if (raceSpawnPositionMap.TryGetValue(raceName, out HashSet<string> spawners))
				{
					foreach (string spawner in spawners)
					{
						InitialSpawnLocationNames.Add(spawner);
					}
				}
				locationDropdown.choices = new List<string>(InitialSpawnLocationNames);
				SelectedSpawnPosition = InitialSpawnLocationNames.Count > 0 ? 0 : -1;
				locationDropdown.index = SelectedSpawnPosition;
			}
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
			if (Client.IsConnectionReady() &&
				Authentication.IsAllowedCharacterName(CharacterName) &&
				WorldSceneDetailsCache != null &&
				RaceIndex > -1 &&
				ModelIndex > -1 &&
				SelectedSpawnPosition > -1)
			{
				foreach (WorldSceneDetails details in WorldSceneDetailsCache.Scenes.Values)
				{
					string raceName = raceDropdown.choices[RaceIndex];

					if (details.InitialSpawnPositions.TryGetValue(InitialSpawnLocationNames[SelectedSpawnPosition], out CharacterInitialSpawnPositionDetails spawnPosition) &&
						raceNameMap.TryGetValue(raceName, out int raceTemplateID))
					{
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
				}
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
		/// Sets the locked state of the create button.
		/// </summary>
		/// <param name="locked">True to lock (disable) the button, false to unlock.</param>
		private void SetCreateButtonLocked(bool locked)
		{
			if (createButton != null)
			{
				createButton.SetEnabled(!locked);
			}
		}
	}
}
