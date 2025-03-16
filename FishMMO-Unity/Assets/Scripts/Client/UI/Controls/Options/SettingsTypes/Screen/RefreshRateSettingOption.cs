using FishMMO.Shared;
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
				Debug.LogError("RefreshRateSettingsOption: TMP_Dropdown is missing.");
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

			// Create a list to hold unique refresh rates for the current resolution
			System.Collections.Generic.List<int> refreshRates = new System.Collections.Generic.List<int>();

			foreach (Resolution res in resolutions)
			{
				if (res.width == currentResolution.width && res.height == currentResolution.height && !refreshRates.Contains(res.refreshRate))
				{
					refreshRates.Add(res.refreshRate);
				}
			}

			// Add refresh rates to the dropdown
			System.Collections.Generic.List<string> refreshRateOptions = new System.Collections.Generic.List<string>();
			foreach (int rate in refreshRates)
			{
				refreshRateOptions.Add(rate + " Hz");
			}

			RefreshRateDropdown.AddOptions(refreshRateOptions);

			// Load the saved refresh rate from Configuration GlobalSettings (default to 60Hz if not set)
			Configuration.GlobalSettings.TryGetInt(RefreshRateKey, out int savedRefreshRate, 60);
			int selectedIndex = refreshRates.IndexOf(savedRefreshRate);

			// If the saved refresh rate is not found, default to the first one
			if (selectedIndex == -1)
			{
				selectedIndex = 0;
			}

			RefreshRateDropdown.value = selectedIndex;
			ApplyRefreshRate(savedRefreshRate);
		}

		public override void Load()
		{
			// Load the saved refresh rate from Configuration GlobalSettings
			Configuration.GlobalSettings.TryGetInt(RefreshRateKey, out int savedRefreshRate, 60);

			// Apply the saved refresh rate
			ApplyRefreshRate(savedRefreshRate);
		}

		public override void Save()
		{
			// Get the selected refresh rate from the dropdown
			string selectedRefreshRate = RefreshRateDropdown.options[RefreshRateDropdown.value].text.Replace(" Hz", "");

			// Save the selected refresh rate to Configuration GlobalSettings
			Configuration.GlobalSettings.Set(RefreshRateKey, int.Parse(selectedRefreshRate));

			// Apply the selected refresh rate
			ApplyRefreshRate(int.Parse(selectedRefreshRate));
		}

		private void ApplyRefreshRate(int refreshRate)
		{
			// Apply the selected refresh rate to the screen
			Resolution currentResolution = Screen.currentResolution;
			Screen.SetResolution(currentResolution.width, currentResolution.height, Screen.fullScreen, refreshRate);
		}
	}
}