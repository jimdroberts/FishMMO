using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIOptions : UIControl
	{
		// Reference to the UI GameObjects containing the UI components
		public RectTransform ResolutionDropdown;
		public RectTransform FullscreenDropdown;
		public RectTransform RefreshRateDropdown;
		public RectTransform VSyncToggle;
		public RectTransform BrightnessSlider;

		public RectTransform ShowDamageToggle;
		public RectTransform ShowHealsToggle;
		public RectTransform ShowAchievementsToggle;
		public RectTransform IgnorePartyInvitesToggle;
		public RectTransform IgnoreGuildInvitesToggle;

#region Screen
		private ResolutionSettingOption resolutionSetting = new ResolutionSettingOption();
		private FullscreenSettingOption fullscreenSetting = new FullscreenSettingOption();
		private RefreshRateSettingOption refreshRateSetting = new RefreshRateSettingOption();
		private VSyncSettingOption vsyncSetting = new VSyncSettingOption();
		private BrightnessSettingOption brightnessSetting = new BrightnessSettingOption();
#endregion

#region Gameplay
		private ToggleSettingOption showDamageSettings = new ToggleSettingOption("ShowDamage");
		private ToggleSettingOption showHealsSettings = new ToggleSettingOption("ShowHeals");
		private ToggleSettingOption showAchievementsSettings = new ToggleSettingOption("ShowAchievementCompletion");
		private ToggleSettingOption ignorePartyInvitesSettings = new ToggleSettingOption("IgnorePartyInvites");
		private ToggleSettingOption ignoreGuildInvitesSettings = new ToggleSettingOption("IgnoreGuildInvites");
#endregion

		public override void OnStarting()
		{
			// load configuration
			if (Configuration.GlobalSettings == null)
			{
				Configuration.GlobalSettings = new Configuration(Constants.GetWorkingDirectory());
				if (!Configuration.GlobalSettings.Load(Configuration.DEFAULT_FILENAME + Configuration.EXTENSION))
				{
					// if we failed to load the file.. save a new one
					Configuration.GlobalSettings.Set("Version", Constants.Configuration.Version);

					Configuration.GlobalSettings.Set("Resolution Width", 1280);
					Configuration.GlobalSettings.Set("Resolution Height", 800);
					Configuration.GlobalSettings.Set("Fullscreen", false);
					Configuration.GlobalSettings.Set("VSync", false);
					Configuration.GlobalSettings.Set("Refresh Rate", (uint)60);
					Configuration.GlobalSettings.Set("Brightness", 1.0f);

					Configuration.GlobalSettings.Set("ShowDamage", true);
					Configuration.GlobalSettings.Set("ShowHeals", true);
					Configuration.GlobalSettings.Set("ShowAchievementCompletion", true);
					Configuration.GlobalSettings.Set("IgnorePartyInvites", false);
					Configuration.GlobalSettings.Set("IgnoreGuildInvites", false);

					Configuration.GlobalSettings.Set("IPFetchHost", Constants.Configuration.IPFetchHost);
#if !UNITY_EDITOR
					Configuration.GlobalSettings.Save();
#endif
				}
			}

			// Initialize each settings module
			resolutionSetting.Initialize(ResolutionDropdown);
			fullscreenSetting.Initialize(FullscreenDropdown);
			refreshRateSetting.Initialize(RefreshRateDropdown);
			vsyncSetting.Initialize(VSyncToggle);
			brightnessSetting.Initialize(BrightnessSlider);

			showDamageSettings.Initialize(ShowDamageToggle);
			showHealsSettings.Initialize(ShowHealsToggle);
			showAchievementsSettings.Initialize(ShowAchievementsToggle);
			ignorePartyInvitesSettings.Initialize(IgnorePartyInvitesToggle);
			ignoreGuildInvitesSettings.Initialize(IgnoreGuildInvitesToggle);

			// Load settings for each module
			resolutionSetting.Load();
			fullscreenSetting.Load();
			refreshRateSetting.Load();
			vsyncSetting.Load();
			brightnessSetting.Load();

			showDamageSettings.Load();
			showHealsSettings.Load();
			showAchievementsSettings.Load();
			ignorePartyInvitesSettings.Load();
			ignoreGuildInvitesSettings.Load();
		}

		public override void OnDestroying()
		{
		}
	}
}