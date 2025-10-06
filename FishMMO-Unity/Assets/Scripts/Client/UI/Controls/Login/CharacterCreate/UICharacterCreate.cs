using FishNet.Transporting;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Client
{
	public class UICharacterCreate : UIControl
	{
		/// <summary>
		/// Button to create a new character.
		/// </summary>
		public Button CreateButton;
		/// <summary>
		/// Text field for displaying character creation result messages.
		/// </summary>
		public TMP_Text CreateResultText;
		/// <summary>
		/// Dropdown for selecting starting race.
		/// </summary>
		public TMP_Dropdown StartRaceDropdown;
		/// <summary>
		/// Dropdown for selecting starting model.
		/// </summary>
		public TMP_Dropdown StartModelDropdown;
		/// <summary>
		/// Dropdown for selecting starting location.
		/// </summary>
		public TMP_Dropdown StartLocationDropdown;
		/// <summary>
		/// Parent transform for character preview UI.
		/// </summary>
		public RectTransform CharacterParent;

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
		/// List of available race names for dropdown.
		/// </summary>
		public List<string> InitialRaceNames = new List<string>();
		/// <summary>
		/// List of available model names for dropdown.
		/// </summary>
		public List<string> InitialModelNames = new List<string>();
		/// <summary>
		/// List of available spawn location names for dropdown.
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

		/// <summary>
		/// Called when the client is set. Initializes dropdowns and subscribes to events.
		/// </summary>
		public override void OnClientSet()
		{
			// initialize race dropdown
			if (StartRaceDropdown != null &&
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
					if (pair.Value != null && pair.Value.Models != null)
					{
						InitialModelNames.AddRange(pair.Value.Models.Select(m => m.Asset.name));
					}
					else if (pair.Value.PlaceholderModel != null)
					{
						ModelIndex = 0;
					}
					else
					{
						Log.Warning("UICharacterCreate", $"No standard model or placeholder exists for {pair.Value.name}");
					}

					// initialize spawn position map
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
				StartRaceDropdown.ClearOptions();
				StartRaceDropdown.AddOptions(InitialRaceNames);
				StartModelDropdown.ClearOptions();
				StartModelDropdown.AddOptions(InitialModelNames);

				// set initial race selection
				RaceIndex = 0;
				ModelIndex = 0;
			}

			UpdateStartLocationDropdown();

			Client.NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
			Client.NetworkManager.ClientManager.RegisterBroadcast<CharacterCreateResultBroadcast>(OnClientCharacterCreateResultBroadcastReceived);
		}

		/// <summary>
		/// Called when the client is unset. Unsubscribes from events.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState -= ClientManager_OnClientConnectionState;
			Client.NetworkManager.ClientManager.UnregisterBroadcast<CharacterCreateResultBroadcast>(OnClientCharacterCreateResultBroadcastReceived);
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
		/// Handles character creation result broadcast, updates UI and shows select panel on success.
		/// </summary>
		/// <param name="msg">The broadcast message for character creation result.</param>
		/// <param name="channel">The network channel used.</param>
		private void OnClientCharacterCreateResultBroadcastReceived(CharacterCreateResultBroadcast msg, Channel channel)
		{
			SetCreateButtonLocked(false);
			if (msg.Result == CharacterCreateResult.Success)
			{
				Hide();
				UIManager.Show("UICharacterSelect");
			}
			else if (CreateResultText != null)
			{
				CreateResultText.text = msg.Result.ToString();
			}
		}

		/// <summary>
		/// Called when character name input field edit ends. Updates character name.
		/// </summary>
		/// <param name="inputField">The input field for character name.</param>
		public void OnCharacterNameChangeEndEdit(TMP_InputField inputField)
		{
			CharacterName = inputField.text;
		}

		/// <summary>
		/// Called when race dropdown value changes. Updates race index and resets model index.
		/// </summary>
		/// <param name="dropdown">The race dropdown.</param>
		public void OnRaceDropdownValueChanged(TMP_Dropdown dropdown)
		{
			RaceIndex = dropdown.value;
			// Reset Model Index
			ModelIndex = 0;

			UpdateStartLocationDropdown();
		}

		/// <summary>
		/// Called when model dropdown value changes. Updates model index and model preview.
		/// </summary>
		/// <param name="dropdown">The model dropdown.</param>
		public void OnModelDropdownValueChanged(TMP_Dropdown dropdown)
		{
			ModelIndex = dropdown.value;

			UpdateModel();
		}

		/// <summary>
		/// Updates the character model preview. (Not implemented)
		/// </summary>
		private void UpdateModel()
		{

		}

		/// <summary>
		/// Updates the start location dropdown based on selected race.
		/// </summary>
		private void UpdateStartLocationDropdown()
		{
			// update start location dropdown
			if (StartLocationDropdown != null &&
				InitialSpawnLocationNames != null)
			{
				InitialSpawnLocationNames.Clear();

				string raceName = StartRaceDropdown.options[RaceIndex].text;

				// find all spawn locations that allow the currently selected race
				if (raceSpawnPositionMap.TryGetValue(raceName, out HashSet<string> spawners))
				{
					foreach (string spawner in spawners)
					{
						InitialSpawnLocationNames.Add(spawner);
					}
				}
				StartLocationDropdown.ClearOptions();
				StartLocationDropdown.AddOptions(InitialSpawnLocationNames);
				SelectedSpawnPosition = 0;
			}
		}

		/// <summary>
		/// Called when spawn location dropdown value changes. Updates selected spawn position.
		/// </summary>
		/// <param name="dropdown">The spawn location dropdown.</param>
		public void OnSpawnLocationDropdownValueChanged(TMP_Dropdown dropdown)
		{
			SelectedSpawnPosition = dropdown.value;
		}

		/// <summary>
		/// Called when the create button is clicked. Validates input and broadcasts character creation request.
		/// </summary>
		public void OnClick_CreateCharacter()
		{
			if (Client.IsConnectionReady() &&
				Constants.Authentication.IsAllowedCharacterName(CharacterName) &&
				WorldSceneDetailsCache != null &&
				RaceIndex > -1 &&
				ModelIndex > -1 &&
				SelectedSpawnPosition > -1)
			{
				foreach (WorldSceneDetails details in WorldSceneDetailsCache.Scenes.Values)
				{
					string raceName = StartRaceDropdown.options[RaceIndex].text;

					if (details.InitialSpawnPositions.TryGetValue(InitialSpawnLocationNames[SelectedSpawnPosition], out CharacterInitialSpawnPositionDetails spawnPosition) &&
						raceNameMap.TryGetValue(raceName, out int raceTemplateID))
					{
						// create character
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
		/// Called when quitting to login. Unlocks create button.
		/// </summary>
		public override void OnQuitToLogin()
		{
			base.OnQuitToLogin();

			SetCreateButtonLocked(false);
		}

		/// <summary>
		/// Called when the quit to login button is clicked. Returns to login screen.
		/// </summary>
		public void OnClick_QuitToLogin()
		{
			// we should go back to login..
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
		/// Sets locked state for create button (enables/disables create button).
		/// </summary>
		/// <param name="locked">True to lock (disable) the button, false to unlock.</param>
		private void SetCreateButtonLocked(bool locked)
		{
			CreateButton.interactable = !locked;
		}
	}
}