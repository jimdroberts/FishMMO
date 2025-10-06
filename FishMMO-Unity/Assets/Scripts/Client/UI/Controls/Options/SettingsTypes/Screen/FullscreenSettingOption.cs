using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Logging;
using TMPro;
using UnityEngine;

namespace FishMMO.Client
{
	public class FullscreenSettingOption : SettingOption
	{
		/// <summary>
		/// The configuration key used to store the fullscreen mode setting.
		/// </summary>
		private const string FullscreenKey = "Fullscreen";

		/// <summary>
		/// The dropdown UI component for selecting fullscreen mode.
		/// </summary>
		public TMP_Dropdown FullscreenDropdown;

		// Initialize with the settings UI GameObject containing the UI component
		/// <summary>
		/// Initializes the fullscreen setting, sets up listeners, and populates available fullscreen modes.
		/// </summary>
		public override void Initialize()
		{
			if (FullscreenDropdown == null)
			{
				Log.Error("FullscreenSettingsOption", "TMP_Dropdown is missing.");
			}
			else
			{
				// Remove any existing listeners and add a new one to save the setting when changed.
				FullscreenDropdown.onValueChanged.RemoveAllListeners();
				FullscreenDropdown.onValueChanged.AddListener((value) => { Save(); });

				// Populate the dropdown with available fullscreen modes
				PopulateFullscreenSettings();
			}
		}

		/// <summary>
		/// Populates the dropdown with all available fullscreen modes and sets the current selection.
		/// </summary>
		private void PopulateFullscreenSettings()
		{
			FullscreenDropdown.ClearOptions();
			List<string> options = new List<string>();

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

		/// <summary>
		/// Loads the saved fullscreen mode from configuration and applies it.
		/// </summary>
		public override void Load()
		{
#if !UNITY_WEBGL
			// Load the saved fullscreen mode from configuration, defaulting to FullScreenWindow if not set.
			Configuration.GlobalSettings.TryGetInt(FullscreenKey, out int fullScreenMode, (int)FullScreenMode.FullScreenWindow);

			FullscreenDropdown.value = fullScreenMode;
			Screen.fullScreenMode = (FullScreenMode)FullscreenDropdown.value;
#endif
		}

		/// <summary>
		/// Saves the selected fullscreen mode to configuration and applies it.
		/// </summary>
		public override void Save()
		{
#if !UNITY_WEBGL
			Configuration.GlobalSettings.Set(FullscreenKey, FullscreenDropdown.value);
			Screen.fullScreenMode = (FullScreenMode)FullscreenDropdown.value;
#endif
		}
	}
}