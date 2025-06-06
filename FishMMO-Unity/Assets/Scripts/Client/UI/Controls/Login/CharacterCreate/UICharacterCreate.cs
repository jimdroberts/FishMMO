using FishNet.Transporting;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UICharacterCreate : UIControl
	{
		public Button CreateButton;
		public TMP_Text CreateResultText;
		public TMP_Dropdown StartRaceDropdown;
		public TMP_Dropdown StartLocationDropdown;
		public RectTransform CharacterParent;

		public string CharacterName = "";
		public int RaceIndex = -1;
		public List<string> InitialRaceNames = new List<string>();
		public List<string> InitialSpawnLocationNames = new List<string>();
		public WorldSceneDetailsCache WorldSceneDetailsCache = null;
		public int SelectedSpawnPosition = -1;

		private Dictionary<string, int> raceNameMap = new Dictionary<string, int>();

		private Dictionary<string, HashSet<string>> raceSpawnPositionMap = new Dictionary<string, HashSet<string>>();

		public override void OnClientSet()
		{
			// initialize race dropdown
			if (StartRaceDropdown != null &&
				InitialRaceNames != null)
			{
				raceNameMap.Clear();
				InitialRaceNames.Clear();

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

				// set initial race selection
				RaceIndex = 0;
			}

			UpdateStartLocationDropdown();

			Client.NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
			Client.NetworkManager.ClientManager.RegisterBroadcast<CharacterCreateResultBroadcast>(OnClientCharacterCreateResultBroadcastReceived);
		}


		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState -= ClientManager_OnClientConnectionState;
			Client.NetworkManager.ClientManager.UnregisterBroadcast<CharacterCreateResultBroadcast>(OnClientCharacterCreateResultBroadcastReceived);
		}

		private void ClientManager_OnClientConnectionState(ClientConnectionStateArgs obj)
		{
			if (obj.ConnectionState == LocalConnectionState.Stopped)
			{
				Hide();
			}
		}

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

		public void OnCharacterNameChangeEndEdit(TMP_InputField inputField)
		{
			CharacterName = inputField.text;
		}

		public void OnRaceDropdownValueChanged(TMP_Dropdown dropdown)
		{
			RaceIndex = dropdown.value;

			UpdateStartLocationDropdown();
		}

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

		public void OnSpawnLocationDropdownValueChanged(TMP_Dropdown dropdown)
		{
			SelectedSpawnPosition = dropdown.value;
		}

		public void OnClick_CreateCharacter()
		{
			if (Client.IsConnectionReady() &&
				Constants.Authentication.IsAllowedCharacterName(CharacterName) &&
				WorldSceneDetailsCache != null &&
				RaceIndex > -1 &&
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
							SceneName = spawnPosition.SceneName,
							SpawnerName = spawnPosition.SpawnerName,
						}, Channel.Reliable);
						SetCreateButtonLocked(true);
						return;
					}
				}
			}
		}

		public override void OnQuitToLogin()
		{
			base.OnQuitToLogin();

			SetCreateButtonLocked(false);
		}

		public void OnClick_QuitToLogin()
		{
			// we should go back to login..
			Client.QuitToLogin();
		}

		public void OnClick_Quit()
		{
			Client.Quit();
		}

		private void SetCreateButtonLocked(bool locked)
		{
			CreateButton.interactable = !locked;
		}
	}
}