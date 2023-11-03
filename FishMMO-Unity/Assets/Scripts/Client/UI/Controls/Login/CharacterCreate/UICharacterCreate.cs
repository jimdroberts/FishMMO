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
		public TMP_Dropdown startLocationDropdown;
		public RectTransform characterParent;

		public string characterName = "";
		public string raceName = "";
		public List<string> initialSpawnLocationNames = new List<string>();
		public WorldSceneDetailsCache worldSceneDetailsCache = null;
		public int selectedSpawnPosition = -1;

		public override void OnStarting()
		{
			initialSpawnLocationNames.Clear();
			if (startLocationDropdown != null &&
				initialSpawnLocationNames != null &&
				worldSceneDetailsCache != null &&
				worldSceneDetailsCache.Scenes != null)
			{
				foreach (WorldSceneDetails details in worldSceneDetailsCache.Scenes.Values)
				{
					foreach (CharacterInitialSpawnPosition initialSpawnLocation in details.InitialSpawnPositions.Values)
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
				Visible = false;
			}
		}

		private void OnClientCharacterCreateResultBroadcastReceived(CharacterCreateResultBroadcast msg)
		{
			SetCreateButtonLocked(false);
			if (msg.result == CharacterCreateResult.Success)
			{
				Visible = false;
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

		public void OnSpawnLocationDropdownValueChanged(TMP_Dropdown dropdown)
		{
			selectedSpawnPosition = dropdown.value;
		}

		public void OnClick_CreateCharacter()
		{
			if (Client.IsConnectionReady() &&
				!string.IsNullOrWhiteSpace(characterName) &&
				worldSceneDetailsCache != null &&
				selectedSpawnPosition >= 0)
			{
				foreach (WorldSceneDetails details in worldSceneDetailsCache.Scenes.Values)
				{
					if (details.InitialSpawnPositions.TryGetValue(initialSpawnLocationNames[selectedSpawnPosition], out CharacterInitialSpawnPosition spawnPosition))
					{
						// create character
						Client.NetworkManager.ClientManager.Broadcast(new CharacterCreateBroadcast()
						{
							characterName = characterName,
							raceName = raceName,
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