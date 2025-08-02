using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Client
{
	public class VSyncSettingOption : SettingOption
	{
		/// <summary>
		/// The configuration key used to store the VSync setting.
		/// </summary>
		private const string VSyncKey = "VSync";

		/// <summary>
		/// The UI Toggle component for enabling/disabling VSync.
		/// </summary>
		public Toggle VsyncToggle;

		/// <summary>
		/// Initializes the VSync setting, sets up listeners, and validates required fields.
		/// </summary>
		public override void Initialize()
		{
			if (VsyncToggle == null)
			{
				Log.Error("VSyncSettingOption", "Toggle is missing.");
			}
			else
			{
				// Remove any existing listeners and add a new one to save the setting when toggled.
				VsyncToggle.onValueChanged.RemoveAllListeners();
				VsyncToggle.onValueChanged.AddListener((value) => { Save(); });
			}
		}

		/// <summary>
		/// Loads the VSync value from configuration and updates the UI and quality settings.
		/// </summary>
		public override void Load()
		{
			// Try to get the VSync value from configuration, defaulting to false.
			Configuration.GlobalSettings.TryGetBool(VSyncKey, out bool vsync, false);
			VsyncToggle.isOn = vsync;
			// Update Unity's quality settings based on the toggle value.
			QualitySettings.vSyncCount = VsyncToggle.isOn ? 1 : 0;
		}

		/// <summary>
		/// Saves the current VSync value to configuration and updates quality settings.
		/// </summary>
		public override void Save()
		{
			Configuration.GlobalSettings.Set(VSyncKey, VsyncToggle.isOn);
			QualitySettings.vSyncCount = VsyncToggle.isOn ? 1 : 0;
		}
	}
}