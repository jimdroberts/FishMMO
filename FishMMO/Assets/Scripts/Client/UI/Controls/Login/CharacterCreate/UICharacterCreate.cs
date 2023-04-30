using FishNet.Managing;
using FishNet.Transporting;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Client
{
	public class UICharacterCreate : UIControl
	{
		public Button createButton;
		public TMP_Text createResultText;
		public TMP_Dropdown startLocationDropdown;
		public RectTransform characterParent;

		private NetworkManager networkManager;

		public string characterName = "";
		public string raceName = "";
		public List<string> initialSpawnLocationNames = new List<string>();
		public WorldSceneDetailsCache worldSceneDetailsCache = null;
		public int selectedSpawnPosition = -1;

		public override void OnStarting()
		{
			networkManager = FindObjectOfType<NetworkManager>();
			if (networkManager == null)
			{
				Debug.LogError("UICharacterSelect: NetworkManager not found, HUD will not function.");
				return;
			}

			initialSpawnLocationNames.Clear();
			if (startLocationDropdown != null &&
				initialSpawnLocationNames != null &&
				worldSceneDetailsCache != null &&
				worldSceneDetailsCache.scenes != null)
			{
				foreach (WorldSceneDetails details in worldSceneDetailsCache.scenes.Values)
				{
					foreach (CharacterInitialSpawnPosition initialSpawnLocation in details.initialSpawnPositions.Values)
					{
						initialSpawnLocationNames.Add(initialSpawnLocation.spawnerName);
					}
				}
				startLocationDropdown.ClearOptions();
				startLocationDropdown.AddOptions(initialSpawnLocationNames);
				selectedSpawnPosition = startLocationDropdown.value;
			}

			networkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
			networkManager.ClientManager.RegisterBroadcast<CharacterCreateResultBroadcast>(OnClientCharacterCreateResultBroadcastReceived);
		}


		public override void OnDestroying()
		{
			if (networkManager != null)
			{
				networkManager.ClientManager.OnClientConnectionState -= ClientManager_OnClientConnectionState;
				networkManager.ClientManager.UnregisterBroadcast<CharacterCreateResultBroadcast>(OnClientCharacterCreateResultBroadcastReceived);
			}
		}

		private void ClientManager_OnClientConnectionState(ClientConnectionStateArgs obj)
		{
			if (obj.ConnectionState == LocalConnectionState.Stopped)
			{
				visible = false;
			}
		}

		private void OnClientCharacterCreateResultBroadcastReceived(CharacterCreateResultBroadcast msg)
		{
			SetCreateButtonLocked(false);
			if (msg.result == CharacterCreateResult.Success)
			{
				visible = false;
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
			if (Client.Instance.IsConnectionReady() &&
				!string.IsNullOrWhiteSpace(characterName) &&
				worldSceneDetailsCache != null &&
				selectedSpawnPosition >= 0)
			{
				foreach (WorldSceneDetails details in worldSceneDetailsCache.scenes.Values)
				{
					if (details.initialSpawnPositions.TryGetValue(initialSpawnLocationNames[selectedSpawnPosition], out CharacterInitialSpawnPosition spawnPosition))
					{
						// create character
						networkManager.ClientManager.Broadcast(new CharacterCreateBroadcast()
						{
							characterName = characterName,
							raceName = raceName,
							initialSpawnPosition = spawnPosition,
						});
						SetCreateButtonLocked(true);
						return;
					}
				}
				
			}
		}

		public void OnClick_QuitToLogin()
		{
			// we should go back to login..
#if UNITY_EDITOR
			EditorApplication.ExitPlaymode();
#else
			Application.Quit();
#endif
		}

		public void OnClick_Quit()
		{
#if UNITY_EDITOR
			EditorApplication.ExitPlaymode();
#else
			Application.Quit();
#endif
		}

		private void SetCreateButtonLocked(bool locked)
		{
			createButton.interactable = !locked;
		}
	}
}