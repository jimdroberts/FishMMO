using FishMMO.Shared;
using FishMMO.Logging;
using System.Linq;
using System.Collections.Generic;

namespace FishMMO.Client
{
	public class UIOptions : UIControl
	{
		/// <summary>
		/// List of all setting options found in child objects.
		/// </summary>
		private List<SettingOption> settingOptions;

		/// <summary>
		/// Called when the UI is starting. Loads configuration and initializes all setting options.
		/// </summary>
		public override void OnStarting()
		{
			// Load configuration if not already loaded. If loading fails, set defaults and save.
			if (Configuration.GlobalSettings == null)
			{
				Configuration.SetGlobalSettings(new Configuration(Constants.GetWorkingDirectory()));
				if (!Configuration.GlobalSettings.Load(Configuration.DEFAULT_FILENAME))
				{
					// If we failed to load the file, save a new one with default settings.
					Configuration.GlobalSettings.Set("IPFetchHost", Constants.Configuration.IPFetchHost);
#if !UNITY_EDITOR && !UNITY_WEBGL
				   Configuration.GlobalSettings.Save();
#endif
				}
			}

			// Find all SettingOption components in child objects and initialize/load them.
			settingOptions = gameObject.GetComponentsInChildren<SettingOption>().ToList();
			if (settingOptions == null || settingOptions.Count < 1)
			{
				Log.Debug("UIOptions", "No SettingOptions have been found.");
			}
			else
			{
				for (int i = 0; i < settingOptions.Count; ++i)
				{
					SettingOption settings = settingOptions[i];
					settings.Initialize();
					settings.Load();
				}
			}
		}

		/// <summary>
		/// Loads all setting options from configuration.
		/// </summary>
		public void LoadAll()
		{
			// If there are no setting options, nothing to load.
			if (settingOptions == null || settingOptions.Count < 1)
			{
				return;
			}

			// Load each setting option.
			for (int i = 0; i < settingOptions.Count; ++i)
			{
				settingOptions[i].Load();
			}
		}

		/// <summary>
		/// Saves all setting options to configuration.
		/// </summary>
		public void SaveAll()
		{
			// If there are no setting options, nothing to save.
			if (settingOptions == null || settingOptions.Count < 1)
			{
				return;
			}

			// Save each setting option.
			for (int i = 0; i < settingOptions.Count; ++i)
			{
				settingOptions[i].Save();
			}

#if !UNITY_EDITOR && !UNITY_WEBGL
		   Configuration.GlobalSettings.Save();
#endif
		}

		/// <summary>
		/// Called when the UI is being destroyed. (No implementation)
		/// </summary>
		public override void OnDestroying()
		{
		}
	}
}