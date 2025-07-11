using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Client
{
	public class VSyncSettingOption : SettingOption
	{
		private const string VSyncKey = "VSync";

		public Toggle VsyncToggle;

		public override void Initialize()
		{
			if (VsyncToggle == null)
			{
				Log.Error("VSyncSettingOption", "Toggle is missing.");
			}
			else
			{
				VsyncToggle.onValueChanged.RemoveAllListeners();
				VsyncToggle.onValueChanged.AddListener((value) => { Save(); });
			}
		}

		public override void Load()
		{
			Configuration.GlobalSettings.TryGetBool(VSyncKey, out bool vsync, false);
			
			VsyncToggle.isOn = vsync;
			QualitySettings.vSyncCount = VsyncToggle.isOn ? 1 : 0;
		}

		public override void Save()
		{
			Configuration.GlobalSettings.Set(VSyncKey, VsyncToggle.isOn);
			QualitySettings.vSyncCount = VsyncToggle.isOn ? 1 : 0;
		}
	}
}