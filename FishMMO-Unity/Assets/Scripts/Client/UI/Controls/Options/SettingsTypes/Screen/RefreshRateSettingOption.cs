using FishMMO.Shared;
using FishMMO.Logging;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace FishMMO.Client
{
	public class RefreshRateSettingOption : SettingOption
	{
		private const string RefreshRateKey = "Refresh Rate";

		public TMP_Dropdown RefreshRateDropdown;

		// Initialize with the settings UI GameObject containing the UI component
		public override void Initialize()
		{
			if (RefreshRateDropdown == null)
			{
				Log.Error("RefreshRateSettingsOption", "TMP_Dropdown is missing.");
			}
			else
			{
				RefreshRateDropdown.onValueChanged.RemoveAllListeners();
				RefreshRateDropdown.onValueChanged.AddListener((value) => { Save(); });

				// Populate the dropdown with available refresh rates for the current resolution
				PopulateRefreshRates();
			}
		}

		private void PopulateRefreshRates()
		{
			RefreshRateDropdown.ClearOptions();
			Resolution currentResolution = Screen.currentResolution;
			Resolution[] resolutions = Screen.resolutions;

			// Use a Dictionary to store unique refresh rates (using float for precision)
			// and their corresponding RefreshRate struct for application
			Dictionary<float, RefreshRate> uniqueRefreshRates = new Dictionary<float, RefreshRate>();

			foreach (Resolution res in resolutions)
			{
				// Compare width and height
				if (res.width == currentResolution.width && res.height == currentResolution.height)
				{
					float rateValue = (float)res.refreshRateRatio.numerator / res.refreshRateRatio.denominator;
					if (!uniqueRefreshRates.ContainsKey(rateValue))
					{
						uniqueRefreshRates.Add(rateValue, res.refreshRateRatio);
					}
				}
			}

			// Convert to a sorted list of float refresh rates for display and indexing
			List<float> sortedRefreshRates = new List<float>(uniqueRefreshRates.Keys);
			sortedRefreshRates.Sort();

			// Add refresh rates to the dropdown
			List<string> refreshRateOptions = new List<string>();
			foreach (float rate in sortedRefreshRates)
			{
				refreshRateOptions.Add($"{rate:F0} Hz"); // Format to show as integer (e.g., 60 Hz)
			}

			RefreshRateDropdown.AddOptions(refreshRateOptions);

			// Load the saved refresh rate from Configuration GlobalSettings (default to 60Hz if not set)
			Configuration.GlobalSettings.TryGetInt(RefreshRateKey, out int savedRefreshRateInt, 60);

			// Now, use savedRefreshRateInt (the integer value) to find the index in sortedRefreshRates
			int selectedIndex = -1;
			for (int i = 0; i < sortedRefreshRates.Count; i++)
			{
				// Check if the saved refresh rate matches one of the available rates
				if (Mathf.Approximately(sortedRefreshRates[i], savedRefreshRateInt))
				{
					selectedIndex = i;
					break;
				}
			}

			// If the saved refresh rate is not found, default to the first one
			if (selectedIndex == -1)
			{
				selectedIndex = 0;
			}

			RefreshRateDropdown.value = selectedIndex;

			// Get the actual RefreshRate struct to apply based on the selectedIndex
			if (sortedRefreshRates.Count > 0)
			{
				ApplyRefreshRate(uniqueRefreshRates[sortedRefreshRates[selectedIndex]]);
			}
		}

		public override void Load()
		{
			// Load the saved refresh rate from Configuration GlobalSettings
			Configuration.GlobalSettings.TryGetInt(RefreshRateKey, out int savedRefreshRateInt, 60);

			// To apply the loaded refresh rate, we need to find the closest available RefreshRate struct
			Resolution currentResolution = Screen.currentResolution;

			// Find the actual RefreshRate struct from available resolutions that matches the loaded int refresh rate
			Resolution[] resolutions = Screen.resolutions;
			RefreshRate bestMatch = new RefreshRate { numerator = (uint)savedRefreshRateInt, denominator = 1 }; // Default to loaded if no exact match found

			foreach (Resolution res in resolutions)
			{
				if (res.width == currentResolution.width && res.height == currentResolution.height)
				{
					if (Mathf.Approximately((float)res.refreshRateRatio.numerator / res.refreshRateRatio.denominator, savedRefreshRateInt))
					{
						bestMatch = res.refreshRateRatio;
						break;
					}
				}
			}

			ApplyRefreshRate(bestMatch);
		}

		public override void Save()
		{
			// Get the selected refresh rate string from the dropdown (e.g., "60 Hz")
			string selectedRefreshRateText = RefreshRateDropdown.options[RefreshRateDropdown.value].text;

			// Parse the integer part (e.g., 60)
			int selectedRefreshRateInt = int.Parse(selectedRefreshRateText.Replace(" Hz", ""));

			// Save the selected refresh rate as an integer to Configuration GlobalSettings
			Configuration.GlobalSettings.Set(RefreshRateKey, selectedRefreshRateInt);

			// To apply, we need to find the corresponding RefreshRate struct
			Resolution currentResolution = Screen.currentResolution;
			RefreshRate actualRefreshRateToApply = new RefreshRate { numerator = (uint)selectedRefreshRateInt, denominator = 1 }; // Default

			// Iterate through available resolutions to find the exact RefreshRate struct
			Resolution[] resolutions = Screen.resolutions;
			foreach (Resolution res in resolutions)
			{
				if (res.width == currentResolution.width && res.height == currentResolution.height)
				{
					if (Mathf.Approximately((float)res.refreshRateRatio.numerator / res.refreshRateRatio.denominator, selectedRefreshRateInt))
					{
						actualRefreshRateToApply = res.refreshRateRatio;
						break;
					}
				}
			}

			ApplyRefreshRate(actualRefreshRateToApply);
		}

		private void ApplyRefreshRate(RefreshRate refreshRate)
		{
#if !UNITY_WEBGL
			// Apply the selected refresh rate to the screen
			Resolution currentResolution = Screen.currentResolution;
			Screen.SetResolution(currentResolution.width, currentResolution.height, Screen.fullScreenMode, refreshRate);
#endif
		}
	}
}