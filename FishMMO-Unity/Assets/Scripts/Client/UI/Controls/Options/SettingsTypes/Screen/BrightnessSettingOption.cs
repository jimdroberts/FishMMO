using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class BrightnessSettingOption : SettingOption
	{
		private const string BrightnessKey = "Brightness";

		public Slider brightnessSlider;

		public override void Initialize(RectTransform transform)
		{
			if (transform == null)
			{
				Debug.LogError("BrightnessSettingsOption: transform is null.");
			}
			else
			{
				brightnessSlider = transform.GetComponent<Slider>();
				if (brightnessSlider == null)
				{
					Debug.LogError("BrightnessSettingsOption: Slider is missing.");
				}
				else
				{
					brightnessSlider.onValueChanged.RemoveAllListeners();
					brightnessSlider.onValueChanged.AddListener((value) => { Save(); });
				}
			}
		}

		public override void Load()
		{
			if (Configuration.GlobalSettings.TryGetFloat(BrightnessKey, out float brightness))
			{
				brightnessSlider.value = brightness;
				RenderSettings.ambientLight = new Color(brightness, brightness, brightness, brightness);
			}
		}

		public override void Save()
		{
			Configuration.GlobalSettings.Set(BrightnessKey, brightnessSlider.value);
			RenderSettings.ambientLight = new Color(brightnessSlider.value, brightnessSlider.value, brightnessSlider.value, brightnessSlider.value);
		}
	}
}