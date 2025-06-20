using UnityEngine;
using FishMMO.Shared;
using System.Linq;
using System.Collections.Generic;

namespace FishMMO.Client
{
	public class UIOptions : UIControl
	{
		private List<SettingOption> settingOptions;

		public override void OnStarting()
		{
			// load configuration
			if (Configuration.GlobalSettings == null)
			{
				Configuration.GlobalSettings = new Configuration(Constants.GetWorkingDirectory());
				if (!Configuration.GlobalSettings.Load(Configuration.DEFAULT_FILENAME + Configuration.EXTENSION))
				{
					// If we failed to load the file.. save a new one
					Configuration.GlobalSettings.Set("Version", Constants.Configuration.Version);
					Configuration.GlobalSettings.Set("IPFetchHost", Constants.Configuration.IPFetchHost);
#if !UNITY_EDITOR && !UNITY_WEBGL
					Configuration.GlobalSettings.Save();
#endif
				}
			}

			settingOptions = gameObject.GetComponentsInChildren<SettingOption>().ToList();
			if (settingOptions == null || settingOptions.Count < 1)
			{
				Debug.Log("No SettingOptions have been found.");
			}
			else
			{
				//Debug.Log($"Found {settingOptions.Count} settings.");
				for (int i = 0; i < settingOptions.Count; ++i)
				{
					SettingOption settings = settingOptions[i];
					//Debug.Log($"Loading {settings.gameObject.name}");
					settings.Initialize();
					settings.Load();
				}
			}
		}

		public void LoadAll()
		{
			if (settingOptions == null || settingOptions.Count < 1)
			{
				return;
			}

			for (int i = 0; i < settingOptions.Count; ++i)
			{
				settingOptions[i].Load();
			}
		}

		public void SaveAll()
		{
			if (settingOptions == null || settingOptions.Count < 1)
			{
				return;
			}

			for (int i = 0; i < settingOptions.Count; ++i)
			{
				settingOptions[i].Save();
			}

#if !UNITY_EDITOR && !UNITY_WEBGL
			Configuration.GlobalSettings.Save();
#endif
		}

		public override void OnDestroying()
		{
		}
	}
}