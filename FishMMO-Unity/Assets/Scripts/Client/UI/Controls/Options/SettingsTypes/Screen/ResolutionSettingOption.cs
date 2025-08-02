using FishMMO.Shared;
using FishMMO.Logging;
using TMPro;
using UnityEngine;

namespace FishMMO.Client
{
	public class ResolutionSettingOption : SettingOption
	{
		/// <summary>
		/// The configuration key used to store the resolution width.
		/// </summary>
		public const string ResolutionWidthKey = "Resolution Width";
		/// <summary>
		/// The configuration key used to store the resolution height.
		/// </summary>
		public const string ResolutionHeightKey = "Resolution Height";

		/// <summary>
		/// The default resolution width if no value is saved.
		/// </summary>
		public int DefaultResolutionWidth = 1280;
		/// <summary>
		/// The default resolution height if no value is saved.
		/// </summary>
		public int DefaultResolutionHeight = 800;

		/// <summary>
		/// The dropdown UI component for selecting screen resolution.
		/// </summary>
		public TMP_Dropdown ResolutionDropdown;

		// Initialize the settings class with the UI GameObject containing the UI component
		/// <summary>
		/// Initializes the resolution setting, sets up listeners, and populates available resolutions.
		/// </summary>
		public override void Initialize()
		{
			if (ResolutionDropdown == null)
			{
				Log.Error("ResolutionSettingsOption", "TMP_Dropdown is missing.");
			}
			else
			{
				// Remove any existing listeners and add a new one to save the setting when changed.
				ResolutionDropdown.onValueChanged.RemoveAllListeners();
				ResolutionDropdown.onValueChanged.AddListener((value) => { Save(); });

				// Populate the dropdown with all available resolutions
				PopulateResolutions();
			}
		}

		/// <summary>
		/// Populates the dropdown with all available screen resolutions and sets the current selection.
		/// </summary>
		private void PopulateResolutions()
		{
			ResolutionDropdown.ClearOptions();
			Resolution[] resolutions = Screen.resolutions;

			// Create a list to hold the resolution names for the dropdown
			System.Collections.Generic.List<string> options = new System.Collections.Generic.List<string>();

			foreach (Resolution res in resolutions)
			{
				string resolutionString = res.width + " x " + res.height;
				options.Add(resolutionString);
			}

			// Add the list of resolutions as options in the dropdown
			ResolutionDropdown.AddOptions(options);

			// Load the saved resolution width and height
			Configuration.GlobalSettings.TryGetInt(ResolutionWidthKey, out int savedWidth);
			Configuration.GlobalSettings.TryGetInt(ResolutionHeightKey, out int savedHeight);

			// Find the matching resolution in the list and set the dropdown index
			int selectedIndex = GetResolutionIndex(savedWidth, savedHeight);
			ResolutionDropdown.value = selectedIndex;
			ApplyResolution(selectedIndex);
		}

		/// <summary>
		/// Loads the saved resolution from configuration and applies it.
		/// </summary>
		public override void Load()
		{
			// Load the saved resolution width and height
			// Load the saved resolution width and height, defaulting if not found.
			Configuration.GlobalSettings.TryGetInt(ResolutionWidthKey, out int savedWidth, DefaultResolutionWidth);
			Configuration.GlobalSettings.TryGetInt(ResolutionHeightKey, out int savedHeight, DefaultResolutionHeight);

			// Find the matching resolution in the list and set the dropdown index
			int selectedIndex = GetResolutionIndex(savedWidth, savedHeight);
			ResolutionDropdown.value = selectedIndex;
			ApplyResolution(selectedIndex);
		}

		/// <summary>
		/// Saves the selected resolution to configuration and applies it.
		/// </summary>
		public override void Save()
		{
			// Get the selected resolution
			// Get the selected resolution
			int selectedResolutionIndex = ResolutionDropdown.value;
			Resolution selectedResolution = Screen.resolutions[selectedResolutionIndex];

			// Save the width and height of the selected resolution
			Configuration.GlobalSettings.Set(ResolutionWidthKey, selectedResolution.width);
			Configuration.GlobalSettings.Set(ResolutionHeightKey, selectedResolution.height);

			ApplyResolution(selectedResolutionIndex);
		}

		/// <summary>
		/// Finds the index of the resolution matching the given width and height.
		/// </summary>
		/// <param name="width">The width to match.</param>
		/// <param name="height">The height to match.</param>
		/// <returns>The index of the matching resolution, or 0 if not found.</returns>
		private int GetResolutionIndex(int width, int height)
		{
			Resolution[] resolutions = Screen.resolutions;

			// Loop through available resolutions to find the index that matches the saved width and height
			for (int i = 0; i < resolutions.Length; i++)
			{
				if (resolutions[i].width == width && resolutions[i].height == height)
				{
					return i;
				}
			}

			// Default to the first resolution if no match is found
			return 0;
		}

		/// <summary>
		/// Applies the resolution at the given index to the screen.
		/// </summary>
		/// <param name="index">The index of the resolution to apply.</param>
		private void ApplyResolution(int index)
		{
#if !UNITY_WEBGL
			// Get the corresponding resolution from Screen.resolutions and apply it
			Resolution[] resolutions = Screen.resolutions;
			if (index >= 0 && index < resolutions.Length)
			{
				Resolution res = resolutions[index];
				Screen.SetResolution(res.width, res.height, Screen.fullScreen);
			}
#endif
		}
	}
}