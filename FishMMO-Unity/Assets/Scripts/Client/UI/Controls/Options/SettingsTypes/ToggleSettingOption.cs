using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class ToggleSettingOption : SettingOption
	{
		private string toggleKey = "";

		public Toggle toggle;

		public ToggleSettingOption(string toggleKey)
		{
			this.toggleKey = toggleKey;
		}

		public override void Initialize(RectTransform transform)
		{
			if (transform == null)
			{
				Debug.LogError("ToggleSettingOption: transform is null.");
			}
			else
			{
				toggle = transform.GetComponent<Toggle>();
				if (toggle == null)
				{
					Debug.LogError("ToggleSettingOption: Toggle is missing.");
				}
				else
				{
					toggle.onValueChanged.RemoveAllListeners();
					toggle.onValueChanged.AddListener((value) => { Save(); });
				}
			}
		}

		public override void Load()
		{
			if (Configuration.GlobalSettings.TryGetBool(toggleKey, out bool value))
			{
				toggle.isOn = value;
			}
		}

		public override void Save()
		{
			Configuration.GlobalSettings.Set(toggleKey, toggle.isOn);
		}
	}
}