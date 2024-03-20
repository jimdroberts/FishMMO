using FishNet.Object;
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
		public Button createButton;
		public TMP_Text createResultText;
		public TMP_Dropdown startRaceDropdown;
		public TMP_Dropdown startLocationDropdown;
		public RectTransform characterParent;

		public string characterName = "";
		public int raceIndex = -1;
		public List<string> initialRaceNames = new List<string>();
		public List<string> initialSpawnLocationNames = new List<string>();
		public WorldSceneDetailsCache worldSceneDetailsCache = null;
		public int selectedSpawnPosition = -1;

		public override void OnStarting()
		{
			if (startRaceDropdown != null &&
				initialRaceNames != null)
			{
				initialRaceNames.Clear();

				for (int i = 0; i < Client.NetworkManager.SpawnablePrefabs.GetObjectCount(); ++i)
				{
					NetworkObject prefab = Client.NetworkManager.SpawnablePrefabs.GetObject(true, i);
					if (prefab != null)
					{
						Character character = prefab.gameObject.GetComponent<Character>();
						if (character != null)
						{
							initialRaceNames.Add(character.gameObject.name);
						}
					}
				}

				startRaceDropdown.ClearOptions();
				startRaceDropdown.AddOptions(initialRaceNames);
				raceIndex = startRaceDropdown.value;
			}

			if (startLocationDropdown != null &&
				initialSpawnLocationNames != null &&
				worldSceneDetailsCache != null &&
				worldSceneDetailsCache.Scenes != null)
			{
				initialSpawnLocationNames.Clear();

				foreach (WorldSceneDetails details in worldSceneDetailsCache.Scenes.Values)
				{
					foreach (CharacterInitialSpawnPositionDetails initialSpawnLocation in details.InitialSpawnPositions.Values)
					{
						initialSpawnLocationNames.Add(initialSpawnLocation.SpawnerName);
					}
				}
				startLocationDropdown.ClearOptions();
				startLocationDropdown.AddOptions(initialSpawnLocationNames);
				selectedSpawnPosition = startLocationDropdown.value;
			}

			Client.NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
			Client.NetworkManager.ClientManager.RegisterBroadcast<CharacterCreateResultBroadcast>(OnClientCharacterCreateResultBroadcastReceived);
		}


		public override void OnDestroying()
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
			if (msg.result == CharacterCreateResult.Success)
			{
				Hide();
				UIManager.Show("UICharacterSelect");
			}
			else if (createResultText != null)
			{
				createResultText.text = msg.result.ToString();
			}
		}

		public void OnCharacterNameChangeEndEdit(TMP_InputField inputField)
		{
			characterName = inputField.text;
		}

		public void OnRaceDropdownValueChanged(TMP_Dropdown dropdown)
		{
			raceIndex = dropdown.value;
		}

		public void OnSpawnLocationDropdownValueChanged(TMP_Dropdown dropdown)
		{
			selectedSpawnPosition = dropdown.value;
		}

		public void OnClick_CreateCharacter()
		{
			if (Client.IsConnectionReady() &&
				Constants.Authentication.IsAllowedCharacterName(characterName) &&
				worldSceneDetailsCache != null &&
				raceIndex > -1 &&
				selectedSpawnPosition > -1)
			{
				foreach (WorldSceneDetails details in worldSceneDetailsCache.Scenes.Values)
				{
					if (details.InitialSpawnPositions.TryGetValue(initialSpawnLocationNames[selectedSpawnPosition], out CharacterInitialSpawnPositionDetails spawnPosition))
					{
						// create character
						Client.Broadcast(new CharacterCreateBroadcast()
						{
							characterName = characterName,
							raceIndex = raceIndex,
							initialSpawnPosition = spawnPosition,
						}, Channel.Reliable);
						SetCreateButtonLocked(true);
						return;
					}
				}
			}
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
			createButton.interactable = !locked;
		}
	}
}