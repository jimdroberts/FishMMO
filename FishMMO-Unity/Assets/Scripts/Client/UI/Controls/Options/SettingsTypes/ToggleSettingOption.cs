using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class ToggleSettingOption : SettingOption
	{
		public string ToggleKey = "";

		public Toggle Toggle;

		public override void Initialize()
		{
			if (string.IsNullOrEmpty(ToggleKey))
			{
				Debug.LogError($"ToggleKey cannot be null on {gameObject.name}!");
			}
			if (Toggle == null)
			{
				Debug.LogError("ToggleSettingOption: Toggle is missing.");
			}
			else
			{
				Toggle.onValueChanged.RemoveAllListeners();
				Toggle.onValueChanged.AddListener((value) => { Save(); });
			}
		}

		public override void Load()
		{
			Configuration.GlobalSettings.TryGetBool(ToggleKey, out bool value, Toggle.isOn);
			
			Toggle.isOn = value;
		}

		public override void Save()
		{
			Configuration.GlobalSettings.Set(ToggleKey, Toggle.isOn);
		}
	}
}