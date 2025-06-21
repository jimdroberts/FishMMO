using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class BrightnessSettingOption : SettingOption
	{
		private const string BrightnessKey = "Brightness";

		public Slider BrightnessSlider;

		public override void Initialize()
		{
			if (BrightnessSlider == null)
			{
				Log.Error("BrightnessSettingsOption: Slider is missing.");
			}
			else
			{
				BrightnessSlider.onValueChanged.RemoveAllListeners();
				BrightnessSlider.onValueChanged.AddListener((value) => { Save(); });
			}
		}

		public override void Load()
		{
			Configuration.GlobalSettings.TryGetFloat(BrightnessKey, out float brightness, 1.0f);
			
			BrightnessSlider.value = brightness;
			RenderSettings.ambientLight = new Color(brightness, brightness, brightness, brightness);
		}

		public override void Save()
		{
			Configuration.GlobalSettings.Set(BrightnessKey, BrightnessSlider.value);
			RenderSettings.ambientLight = new Color(BrightnessSlider.value, BrightnessSlider.value, BrightnessSlider.value, BrightnessSlider.value);
		}
	}
}