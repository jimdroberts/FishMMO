using UnityEngine.UI;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Client
{
	public class ToggleSettingOption : SettingOption
	{
		/// <summary>
		/// The configuration key used to store the toggle value.
		/// </summary>
		public string ToggleKey = "";

		/// <summary>
		/// The UI Toggle component associated with this setting.
		/// </summary>
		public Toggle Toggle;

		/// <summary>
		/// Initializes the toggle setting, sets up listeners, and validates required fields.
		/// </summary>
		public override void Initialize()
		{
			if (string.IsNullOrEmpty(ToggleKey))
			{
				Log.Error("ToggleSettingOption", $"ToggleKey cannot be null on {gameObject.name}!");
			}
			if (Toggle == null)
			{
				Log.Error("ToggleSettingOption", "Toggle is missing.");
			}
			else
			{
				// Remove any existing listeners and add a new one to save the setting when toggled.
				Toggle.onValueChanged.RemoveAllListeners();
				Toggle.onValueChanged.AddListener((value) => { Save(); });
			}
		}

		/// <summary>
		/// Loads the toggle value from configuration and updates the UI.
		/// </summary>
		public override void Load()
		{
			// Try to get the toggle value from configuration, defaulting to the current UI value.
			Configuration.GlobalSettings.TryGetBool(ToggleKey, out bool value, Toggle.isOn);
			Toggle.isOn = value;
		}

		/// <summary>
		/// Saves the current toggle value to configuration.
		/// </summary>
		public override void Save()
		{
			Configuration.GlobalSettings.Set(ToggleKey, Toggle.isOn);
		}
	}
}