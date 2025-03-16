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
            System.Collections.Generic.List<string> options = new System.Collections.Generic.List<string>();

#if !UNITY_WEBGL
            // Add FullScreenWindow (available on all platforms)
            options.Add(FullScreenMode.FullScreenWindow.ToString());

            // Add ExclusiveFullScreen (only available on Windows)
	#if UNITY_STANDALONE_WIN
            options.Add(FullScreenMode.ExclusiveFullScreen.ToString());
	#endif

            // Add MaximizedWindow (available on Windows and macOS)
	#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX
            options.Add(FullScreenMode.MaximizedWindow.ToString());
	#endif

            // Add Windowed (available on desktop platforms)
	#if UNITY_STANDALONE || UNITY_EDITOR
            options.Add(FullScreenMode.Windowed.ToString());
	#endif
#endif

            // Add the list of fullscreen options to the dropdown
            FullscreenDropdown.AddOptions(options);
        }

        public override void Load()
        {
#if !UNITY_WEBGL
            Configuration.GlobalSettings.TryGetInt(FullscreenKey, out int fullScreenMode, (int)FullScreenMode.FullScreenWindow);

            FullscreenDropdown.value = fullScreenMode;
            Screen.fullScreenMode = (FullScreenMode)FullscreenDropdown.value;
#endif
        }

        public override void Save()
        {
#if !UNITY_WEBGL
            Configuration.GlobalSettings.Set(FullscreenKey, FullscreenDropdown.value);
            Screen.fullScreenMode = (FullScreenMode)FullscreenDropdown.value;
#endif
        }
    }
}