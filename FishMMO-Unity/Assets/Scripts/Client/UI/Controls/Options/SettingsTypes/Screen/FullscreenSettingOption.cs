using FishMMO.Shared;
using TMPro;
using UnityEngine;

namespace FishMMO.Client
{
	public class FullscreenSettingOption : SettingOption
	{
		private const string FullscreenKey = "Fullscreen";

		public TMP_Dropdown FullscreenDropdown;

		// Initialize with the settings UI GameObject containing the UI component
		public override void Initialize()
		{
			if (FullscreenDropdown == null)
			{
				Debug.LogError("FullscreenSettingsOption: TMP_Dropdown is missing.");
			}
			else
			{
				FullscreenDropdown.onValueChanged.RemoveAllListeners();
				FullscreenDropdown.onValueChanged.AddListener((value) => { Save(); });

				PopulateFullscreenSettings();
			}
		}

		private void PopulateFullscreenSettings()
		{
			FullscreenDropdown.ClearOptions();
			System.Collections.Generic.List<string> options = new System.Collections.Generic.List<string>()
			{
				FullScreenMode.ExclusiveFullScreen.ToString(),
				FullScreenMode.FullScreenWindow.ToString(),
				FullScreenMode.MaximizedWindow.ToString(),
				FullScreenMode.Windowed.ToString(),
			};
			// Add the list of resolutions as options in the dropdown
			FullscreenDropdown.AddOptions(options);
		}

		public override void Load()
		{
#if !UNITY_WEBGL
			Configuration.GlobalSettings.TryGetBool(FullscreenKey, out bool isFullscreen, false);

			FullscreenDropdown.value = isFullscreen ? 1 : 0;
			Screen.fullScreen = isFullscreen;
#endif
		}

		public override void Save()
		{
			bool isFullscreen = FullscreenDropdown.value == 1;
			Configuration.GlobalSettings.Set(FullscreenKey, isFullscreen);
			Screen.fullScreen = isFullscreen;
		}
	}
}