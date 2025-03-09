using FishMMO.Shared;
using TMPro;
using UnityEngine;

namespace FishMMO.Client
{
	public class ResolutionSettingOption : SettingOption
	{
		public const string ResolutionWidthKey = "Resolution Width";
		public const string ResolutionHeightKey = "Resolution Height";

		private TMP_Dropdown resolutionDropdown;

		// Initialize the settings class with the UI GameObject containing the UI component
		public override void Initialize(RectTransform transform)
		{
			if (transform == null)
			{
				Debug.LogError("ResolutionSettingsOption: transform is null.");
			}
			else
			{
				resolutionDropdown = transform.GetComponent<TMP_Dropdown>();
				if (resolutionDropdown == null)
				{
					Debug.LogError("ResolutionSettingsOption: TMP_Dropdown is missing.");
				}
				else
				{
					resolutionDropdown.onValueChanged.RemoveAllListeners();
					resolutionDropdown.onValueChanged.AddListener((value) => { Save(); });

					// Populate the dropdown with all available resolutions
					PopulateResolutions();
				}
			}
		}

		private void PopulateResolutions()
		{
			resolutionDropdown.ClearOptions();
			Resolution[] resolutions = Screen.resolutions;

			// Create a list to hold the resolution names for the dropdown
			System.Collections.Generic.List<string> options = new System.Collections.Generic.List<string>();

			foreach (Resolution res in resolutions)
			{
				string resolutionString = res.width + " x " + res.height;
				options.Add(resolutionString);
			}

			// Add the list of resolutions as options in the dropdown
			resolutionDropdown.AddOptions(options);

			// Load the saved resolution width and height
			Configuration.GlobalSettings.TryGetInt(ResolutionWidthKey, out int savedWidth);
			Configuration.GlobalSettings.TryGetInt(ResolutionHeightKey, out int savedHeight);

			// Find the matching resolution in the list and set the dropdown index
			int selectedIndex = GetResolutionIndex(savedWidth, savedHeight);
			resolutionDropdown.value = selectedIndex;
			ApplyResolution(selectedIndex);
		}

		public override void Load()
		{
			// Load the saved resolution width and height
			Configuration.GlobalSettings.TryGetInt(ResolutionWidthKey, out int savedWidth);
			Configuration.GlobalSettings.TryGetInt(ResolutionHeightKey, out int savedHeight);

			// Find the matching resolution in the list and set the dropdown index
			int selectedIndex = GetResolutionIndex(savedWidth, savedHeight);
			resolutionDropdown.value = selectedIndex;
			ApplyResolution(selectedIndex);
		}

		public override void Save()
		{
			// Get the selected resolution
			int selectedResolutionIndex = resolutionDropdown.value;
			Resolution selectedResolution = Screen.resolutions[selectedResolutionIndex];

			// Save the width and height of the selected resolution
			Configuration.GlobalSettings.Set(ResolutionWidthKey, selectedResolution.width);
			Configuration.GlobalSettings.Set(ResolutionHeightKey, selectedResolution.height);

			ApplyResolution(selectedResolutionIndex);
		}

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