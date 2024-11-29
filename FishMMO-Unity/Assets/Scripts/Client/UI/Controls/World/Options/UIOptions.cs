using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	public class UIOptions : UIControl
	{
		public Dropdown ResolutionDropdown;
		public Dropdown RefreshRateDropdown;
		public Toggle FullscreenToggle;
		public Toggle VsyncToggle;

		private Resolution[] resolutions;

		public override void OnStarting()
		{
			// Initialize UI elements and set their values
			InitializeResolutions();
			InitializeRefreshRates();

			FullscreenToggle.isOn = Screen.fullScreen;
			VsyncToggle.isOn = QualitySettings.vSyncCount > 0;

			// Add listeners for UI changes
			ResolutionDropdown.onValueChanged.AddListener(ChangeResolution);
			RefreshRateDropdown.onValueChanged.AddListener(ChangeRefreshRate);
			FullscreenToggle.onValueChanged.AddListener(ToggleFullscreen);
			VsyncToggle.onValueChanged.AddListener(ToggleVSync);
		}

		public override void OnDestroying()
		{
			ResolutionDropdown.onValueChanged.RemoveListener(ChangeResolution);
			RefreshRateDropdown.onValueChanged.RemoveListener(ChangeRefreshRate);
			FullscreenToggle.onValueChanged.RemoveListener(ToggleFullscreen);
			VsyncToggle.onValueChanged.RemoveListener(ToggleVSync);
		}

		// Initialize resolution dropdown list
		private void InitializeResolutions()
		{
			resolutions = Screen.resolutions;

			// Clear any previous options in the dropdown
			ResolutionDropdown.ClearOptions();

			// Get available resolutions
			var resolutionOptions = new System.Collections.Generic.List<string>();
			foreach (var res in resolutions)
			{
				// Exclude duplicate resolutions (same width, height, and refresh rate)
				string resolutionString = res.width + "x" + res.height;
				if (!resolutionOptions.Contains(resolutionString))
				{
					resolutionOptions.Add(resolutionString);
				}
			}

			// Add resolutions to dropdown
			ResolutionDropdown.AddOptions(resolutionOptions);
			ResolutionDropdown.value = GetCurrentResolutionIndex();
		}

		// Initialize refresh rate dropdown list
		private void InitializeRefreshRates()
		{
			var refreshRates = new System.Collections.Generic.List<string>();
			int currentWidth = Screen.width;
			int currentHeight = Screen.height;

			// Get refresh rates for the current screen resolution
			foreach (var res in resolutions)
			{
				if (res.width == currentWidth && res.height == currentHeight)
				{
					// Use numerator/denominator to format refresh rate
					var refreshRate = res.refreshRateRatio;
					string rateText = refreshRate.numerator.ToString();
					if (refreshRate.denominator != 1)
					{
						rateText += "/" + refreshRate.denominator.ToString();
					}

					if (!refreshRates.Contains(rateText)) // Avoid duplicates
					{
						refreshRates.Add(rateText);
					}
				}
			}

			// Clear previous options
			RefreshRateDropdown.ClearOptions();

			// Add refresh rates to dropdown
			RefreshRateDropdown.AddOptions(refreshRates);
			RefreshRateDropdown.value = GetCurrentRefreshRateIndex();
		}

		// Get the current resolution index from the resolutions list
		private int GetCurrentResolutionIndex()
		{
			for (int i = 0; i < resolutions.Length; i++)
			{
				if (resolutions[i].width == Screen.width && resolutions[i].height == Screen.height)
				{
					return i;
				}
			}
			return 0;
		}

		// Get the current refresh rate index
		private int GetCurrentRefreshRateIndex()
		{
			int currentWidth = Screen.width;
			int currentHeight = Screen.height;
			var currentRefreshRate = Screen.currentResolution.refreshRateRatio;

			string formattedCurrentRate = currentRefreshRate.numerator.ToString();
			if (currentRefreshRate.denominator != 1)
			{
				formattedCurrentRate += "/" + currentRefreshRate.denominator.ToString();
			}

			for (int i = 0; i < resolutions.Length; i++)
			{
				if (resolutions[i].width == currentWidth && resolutions[i].height == currentHeight)
				{
					var resRate = resolutions[i].refreshRateRatio;
					string formattedRate = resRate.numerator.ToString();
					if (resRate.denominator != 1)
					{
						formattedRate += "/" + resRate.denominator.ToString();
					}

					if (formattedRate == formattedCurrentRate)
					{
						return i;
					}
				}
			}
			return 0;
		}

		// Change screen resolution based on dropdown selection
		private void ChangeResolution(int index)
		{
			Resolution selectedResolution = resolutions[index];
			// Use the new RefreshRate with numerator/denominator
			Screen.SetResolution(selectedResolution.width, selectedResolution.height, Screen.fullScreenMode, selectedResolution.refreshRateRatio);
		}

		// Change refresh rate based on dropdown selection
		private void ChangeRefreshRate(int index)
		{
			string rateText = RefreshRateDropdown.options[index].text;
			string[] rateParts = rateText.Split('/');

			int numerator = int.Parse(rateParts[0]);
			int denominator = rateParts.Length > 1 ? int.Parse(rateParts[1]) : 1;

			// Set the refresh rate using the new RefreshRate type
			/*Screen.currentResolution = new Resolution
			{
				width = Screen.width,
				height = Screen.height,
				refreshRate = new RefreshRate(numerator, denominator)
			};*/
		}

		// Toggle fullscreen mode
		private void ToggleFullscreen(bool isFullscreen)
		{
			Screen.fullScreen = isFullscreen;
		}

		// Toggle vertical sync (V-Sync)
		private void ToggleVSync(bool isVSyncOn)
		{
			QualitySettings.vSyncCount = isVSyncOn ? 1 : 0;
		}
	}
}