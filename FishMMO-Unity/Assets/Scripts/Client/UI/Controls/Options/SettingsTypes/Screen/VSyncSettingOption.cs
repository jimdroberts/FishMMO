using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class VSyncSettingOption : SettingOption
	{
		private const string VSyncKey = "VSync";

		public Toggle vsyncToggle;

		public override void Initialize(RectTransform transform)
		{
			if (transform == null)
			{
				Debug.LogError("VSyncSettingOption: transform is null.");
			}
			else
			{
				vsyncToggle = transform.GetComponent<Toggle>();
				if (vsyncToggle == null)
				{
					Debug.LogError("VSyncSettingOption: Toggle is missing.");
				}
				else
				{
					vsyncToggle.onValueChanged.RemoveAllListeners();
					vsyncToggle.onValueChanged.AddListener((value) => { Save(); });
				}
			}
		}

		public override void Load()
		{
			if (Configuration.GlobalSettings.TryGetBool(VSyncKey, out bool vsync))
			{
				vsyncToggle.isOn = vsync;
				QualitySettings.vSyncCount = vsyncToggle.isOn ? 1 : 0;
			}
		}

		public override void Save()
		{
			Configuration.GlobalSettings.Set(VSyncKey, vsyncToggle.isOn);
			QualitySettings.vSyncCount = vsyncToggle.isOn ? 1 : 0;
		}
	}
}