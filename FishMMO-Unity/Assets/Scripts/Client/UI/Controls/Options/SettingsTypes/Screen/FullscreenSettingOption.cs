using FishMMO.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	public class FullscreenSettingOption : SettingOption
	{
		private const string FullscreenKey = "Fullscreen";

		private TMP_Dropdown fullscreenDropdown;

		// Initialize with the settings UI GameObject containing the UI component
		public override void Initialize(RectTransform transform)
		{
			if (transform == null)
			{
				Debug.LogError("FullscreenSettingsOption: transform is null.");
			}
			else
			{
				fullscreenDropdown = transform.GetComponent<TMP_Dropdown>();
				if (fullscreenDropdown == null)
				{
					Debug.LogError("FullscreenSettingsOption: TMP_Dropdown is missing.");
				}
				else
				{
					fullscreenDropdown.onValueChanged.RemoveAllListeners();
					fullscreenDropdown.onValueChanged.AddListener((value) => { Save(); });

					PopulateFullscreenSettings();
				}
			}
		}

		private void PopulateFullscreenSettings()
		{
			fullscreenDropdown.ClearOptions();
			System.Collections.Generic.List<string> options = new System.Collections.Generic.List<string>()
			{
				FullScreenMode.ExclusiveFullScreen.ToString(),
				FullScreenMode.FullScreenWindow.ToString(),
				FullScreenMode.MaximizedWindow.ToString(),
				FullScreenMode.Windowed.ToString(),
			};
			// Add the list of resolutions as options in the dropdown
			fullscreenDropdown.AddOptions(options);
		}

		public override void Load()
		{
#if !UNITY_WEBGL
			Configuration.GlobalSettings.TryGetBool(FullscreenKey, out bool isFullscreen);
			fullscreenDropdown.value = isFullscreen ? 1 : 0;
			Screen.fullScreen = isFullscreen;
#endif
		}

		public override void Save()
		{
			bool isFullscreen = fullscreenDropdown.value == 1;
			Configuration.GlobalSettings.Set(FullscreenKey, isFullscreen);
			Screen.fullScreen = isFullscreen;
		}
	}
}