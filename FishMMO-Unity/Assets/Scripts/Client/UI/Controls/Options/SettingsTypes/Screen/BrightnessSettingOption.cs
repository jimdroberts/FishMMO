using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Client
{
	public class BrightnessSettingOption : SettingOption
	{
		/// <summary>
		/// The configuration key used to store the brightness setting.
		/// </summary>
		private const string BrightnessKey = "Brightness";

		/// <summary>
		/// The slider UI component for adjusting brightness.
		/// </summary>
		public Slider BrightnessSlider;

		/// <summary>
		/// Initializes the brightness setting, sets up listeners, and validates required fields.
		/// </summary>
		public override void Initialize()
		{
			if (BrightnessSlider == null)
			{
				Log.Error("BrightnessSettingsOption", "Slider is missing.");
			}
			else
			{
				// Remove any existing listeners and add a new one to save the setting when changed.
				BrightnessSlider.onValueChanged.RemoveAllListeners();
				BrightnessSlider.onValueChanged.AddListener((value) => { Save(); });
			}
		}

		/// <summary>
		/// Loads the brightness value from configuration and updates the UI and ambient light.
		/// </summary>
		public override void Load()
		{
			// Load the brightness value from configuration, defaulting to 1.0f if not set.
			Configuration.GlobalSettings.TryGetFloat(BrightnessKey, out float brightness, 1.0f);

			BrightnessSlider.value = brightness;
			// Update the ambient light color based on the slider value.
			RenderSettings.ambientLight = new Color(brightness, brightness, brightness, brightness);
		}

		/// <summary>
		/// Saves the current brightness value to configuration and updates ambient light.
		/// </summary>
		public override void Save()
		{
			Configuration.GlobalSettings.Set(BrightnessKey, BrightnessSlider.value);
			RenderSettings.ambientLight = new Color(BrightnessSlider.value, BrightnessSlider.value, BrightnessSlider.value, BrightnessSlider.value);
		}
	}
}