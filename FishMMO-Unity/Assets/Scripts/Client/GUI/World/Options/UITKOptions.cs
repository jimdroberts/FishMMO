using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit options panel. Builds every settings control in code and binds it directly to
	/// <see cref="Configuration.GlobalSettings"/>, rather than assembling the panel from a graph
	/// of per-option components, so a configuration key cannot be lost by editing a scene.
	/// </summary>
	/// <remarks>
	/// Three groups of settings live here:
	///
	/// • <b>Screen</b> — resolution, refresh rate, fullscreen, brightness, VSync.
	///
	/// • <b>Gameplay</b> — five toggles whose keys used to be set per GameObject in the scene
	///   rather than in code. <c>ShowDamage</c> and <c>ShowHeals</c> are read live by
	///   ClientCombatDisplay; the other three are stored for consumers that do not exist yet.
	///
	/// • <b>Colours</b> — the thirteen themeable colours, opened through the shared colour picker.
	///   The values feed <see cref="UITKThemeManager"/> and are stored under the same config keys
	///   the Canvas UI used, so a config written by an older client still applies.
	/// </remarks>
	public class UITKOptions : UITKControl
	{
		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Settings;

		/// <summary>Name of the VSync toggle element in the UXML.</summary>
		private const string VSYNC_TOGGLE_NAME = "vsync-toggle";
		/// <summary>Name of the brightness slider element in the UXML.</summary>
		private const string BRIGHTNESS_SLIDER_NAME = "brightness-slider";
		/// <summary>Name of the resolution dropdown element in the UXML.</summary>
		private const string RESOLUTION_DROPDOWN_NAME = "resolution-dropdown";
		/// <summary>Name of the refresh rate dropdown element in the UXML.</summary>
		private const string REFRESHRATE_DROPDOWN_NAME = "refreshrate-dropdown";
		/// <summary>Name of the fullscreen dropdown element in the UXML.</summary>
		private const string FULLSCREEN_DROPDOWN_NAME = "fullscreen-dropdown";
		/// <summary>Name of the close button element in the UXML.</summary>
		private const string CLOSE_BUTTON_NAME = "options-close-btn";
		/// <summary>Name of the container the gameplay toggles are built into.</summary>
		private const string GAMEPLAY_LIST_NAME = "options-gameplay-list";
		/// <summary>Name of the container the colour rows are built into.</summary>
		private const string COLOR_LIST_NAME = "options-color-list";
		/// <summary>Name of the button that clears every colour override.</summary>
		private const string RESET_COLORS_NAME = "options-reset-colors-btn";

		/// <summary>Configuration key for the VSync setting.</summary>
		private const string VSyncKey = "VSync";
		/// <summary>Configuration key for the brightness setting.</summary>
		private const string BrightnessKey = "Brightness";
		/// <summary>Configuration key for the resolution width setting.</summary>
		private const string ResolutionWidthKey = "Resolution Width";
		/// <summary>Configuration key for the resolution height setting.</summary>
		private const string ResolutionHeightKey = "Resolution Height";
		/// <summary>Configuration key for the refresh rate setting.</summary>
		private const string RefreshRateKey = "Refresh Rate";
		/// <summary>Configuration key for the fullscreen mode setting.</summary>
		private const string FullscreenKey = "Fullscreen";

		/// <summary>
		/// The gameplay toggles: configuration key paired with its player-facing label.
		/// </summary>
		/// <remarks>
		/// The keys are ShowDamage, ShowHeals, IgnoreGuildInvites, IgnorePartyInvites and
		/// ShowAchievements.
		/// They are listed in code rather than authored per element so the set cannot be lost
		/// again by editing a scene.
		/// </remarks>
		private static readonly (string Key, string Label, bool Default)[] GameplayToggles =
		{
			("ShowDamage",         "Show Damage Numbers",  true),
			("ShowHeals",          "Show Healing Numbers", true),
			("ShowAchievements",   "Show Achievement Popups", true),
			("IgnorePartyInvites", "Ignore Party Invites", false),
			("IgnoreGuildInvites", "Ignore Guild Invites", false),
		};

		/// <summary>
		/// Player-facing labels for the themeable colours, indexed alongside
		/// <see cref="UITKTheme.ColorNames"/>.
		/// </summary>
		private static readonly string[] ColorLabels =
		{
			"Panel Background",
			"Slot Surface",
			"Highlight",
			"Window Background",
			"Text",
			"Health Bar",
			"Mana Bar",
			"Stamina Bar",
			"Crosshair",
			"Tooltip Title",
			"Tooltip Label",
			"Tooltip Value",
			"Tooltip Stat",
		};

		/// <summary>The VSync toggle control.</summary>
		private Toggle vsyncToggle;
		/// <summary>The brightness slider control.</summary>
		private Slider brightnessSlider;
		/// <summary>The resolution dropdown control.</summary>
		private DropdownField resolutionDropdown;
		/// <summary>The refresh rate dropdown control.</summary>
		private DropdownField refreshRateDropdown;
		/// <summary>The fullscreen mode dropdown control.</summary>
		private DropdownField fullscreenDropdown;
		/// <summary>The close button control.</summary>
		private Button closeButton;
		/// <summary>Container holding the generated gameplay toggles.</summary>
		private VisualElement gameplayList;
		/// <summary>Container holding the generated colour rows.</summary>
		private VisualElement colorList;
		/// <summary>Button that clears every colour override.</summary>
		private Button resetColorsButton;
		/// <summary>Swatch element for each colour row, indexed alongside UITKTheme.ColorNames.</summary>
		private readonly VisualElement[] colorSwatches = new VisualElement[13];

		/// <summary>
		/// Refresh rate values (Hz) matching the entries in the refresh rate dropdown.
		/// </summary>
		private readonly List<float> refreshRateValues = new List<float>();

		/// <summary>
		/// Ensures configuration is loaded, resolves all controls, populates choices and binds callbacks.
		/// </summary>
		public override void OnStarting()
		{
			EnsureConfigurationLoaded();

			if (Root == null)
			{
				return;
			}

			vsyncToggle = Root.Q<Toggle>(VSYNC_TOGGLE_NAME);
			brightnessSlider = Root.Q<Slider>(BRIGHTNESS_SLIDER_NAME);
			resolutionDropdown = Root.Q<DropdownField>(RESOLUTION_DROPDOWN_NAME);
			refreshRateDropdown = Root.Q<DropdownField>(REFRESHRATE_DROPDOWN_NAME);
			fullscreenDropdown = Root.Q<DropdownField>(FULLSCREEN_DROPDOWN_NAME);
			closeButton = Root.Q<Button>(CLOSE_BUTTON_NAME);
			gameplayList = Root.Q<VisualElement>(GAMEPLAY_LIST_NAME);
			colorList = Root.Q<VisualElement>(COLOR_LIST_NAME);
			resetColorsButton = Root.Q<Button>(RESET_COLORS_NAME);

			InitializeVSync();
			InitializeBrightness();
			InitializeResolution();
			InitializeRefreshRate();
			InitializeFullscreen();
			InitializeGameplayToggles();
			InitializeColorSettings();

			if (closeButton != null)
			{
				closeButton.clicked += Hide;
			}
		}

		/// <summary>
		/// Loads the global configuration file if it has not already been loaded, writing defaults on failure.
		/// </summary>
		private void EnsureConfigurationLoaded()
		{
			if (Configuration.GlobalSettings == null)
			{
				Configuration.SetGlobalSettings(new Configuration(Constants.GetWorkingDirectory()));
				if (!Configuration.GlobalSettings.Load(Configuration.DEFAULT_FILENAME))
				{
					Configuration.GlobalSettings.Set("APIHost", Constants.Configuration.APIHost);
#if !UNITY_EDITOR && !UNITY_WEBGL
					Configuration.GlobalSettings.Save();
#endif
				}
			}
		}

		/// <summary>
		/// Persists the global configuration to disk (no-op in editor/WebGL).
		/// </summary>
		private void SaveConfiguration()
		{
#if !UNITY_EDITOR && !UNITY_WEBGL
			Configuration.GlobalSettings.Save();
#endif
		}

		/// <summary>
		/// Builds one row per gameplay toggle and binds each to its configuration key.
		/// </summary>
		/// <remarks>
		/// Rows are generated rather than authored in the UXML so the list and
		/// <see cref="GameplayToggles"/> cannot drift apart — an earlier version kept its keys in
		/// the scene, which is how all five were lost when the panel was rebuilt.
		/// </remarks>
		private void InitializeGameplayToggles()
		{
			if (gameplayList == null)
			{
				Log.Error("UITKOptions", "Gameplay settings container is missing.");
				return;
			}

			gameplayList.Clear();

			for (int i = 0; i < GameplayToggles.Length; ++i)
			{
				(string key, string label, bool fallback) = GameplayToggles[i];

				VisualElement row = new VisualElement();
				row.AddToClassList("options-row");

				Label caption = new Label(label);
				caption.AddToClassList("fish-label");
				caption.AddToClassList("options-row-label");
				row.Add(caption);

				Toggle toggle = new Toggle { name = $"toggle-{key}" };
				toggle.AddToClassList("fish-toggle");
				toggle.AddToClassList("options-row-field");

				Configuration.GlobalSettings.TryGetBool(key, out bool value, fallback);
				toggle.value = value;

				// Captured by value; the loop variable would otherwise be shared by every handler.
				string capturedKey = key;
				toggle.RegisterValueChangedCallback((evt) =>
				{
					Configuration.GlobalSettings.Set(capturedKey, evt.newValue);
					SaveConfiguration();
				});

				row.Add(toggle);
				gameplayList.Add(row);
			}
		}

		/// <summary>
		/// Builds one row per themeable colour, each opening the shared colour picker.
		/// </summary>
		private void InitializeColorSettings()
		{
			if (colorList == null)
			{
				Log.Error("UITKOptions", "Colour settings container is missing.");
				return;
			}

			colorList.Clear();

			for (int i = 0; i < UITKTheme.ColorNames.Length; ++i)
			{
				string name = UITKTheme.ColorNames[i];
				string label = i < ColorLabels.Length ? ColorLabels[i] : name;

				VisualElement row = new VisualElement();
				row.AddToClassList("options-row");

				Label caption = new Label(label);
				caption.AddToClassList("fish-label");
				caption.AddToClassList("options-row-label");
				row.Add(caption);

				VisualElement swatch = new VisualElement { name = $"swatch-{name}" };
				swatch.AddToClassList("options-swatch");
				colorSwatches[i] = swatch;
				row.Add(swatch);

				Button edit = new Button { text = "Change" };
				edit.AddToClassList("fish-button");
				edit.AddToClassList("options-swatch-btn");

				string capturedName = name;
				int capturedIndex = i;
				edit.clicked += () => OpenColorPicker(capturedName, capturedIndex);
				row.Add(edit);

				colorList.Add(row);
			}

			if (resetColorsButton != null)
			{
				resetColorsButton.clicked += ResetColors;
			}

			RefreshSwatches();
		}

		/// <summary>
		/// Opens the shared colour picker for one themeable colour.
		/// </summary>
		/// <param name="name">One of <see cref="UITKTheme.ColorNames"/>.</param>
		/// <param name="index">Index of the colour, for updating its swatch.</param>
		private void OpenColorPicker(string name, int index)
		{
			if (!UIManager.TryGetTK("UIColorPicker", out UITKColorPicker picker))
			{
				Log.Error("UITKOptions", "Colour picker is unavailable; cannot edit theme colours.");
				return;
			}

			Color start = ResolveCurrent(name);
			picker.Open(start, (chosen) =>
			{
				UITKTheme.Write(Configuration.GlobalSettings, name, chosen);
				SaveConfiguration();

				/* Reload rather than poking the one colour: the manager owns which USS classes a
				 * colour maps onto, and re-reading configuration keeps this panel from having to
				 * duplicate that mapping. */
				UITKThemeManager.Reload();
				RefreshSwatches();
			});
		}

		/// <summary>
		/// Clears every colour override, returning the UI to the stylesheet defaults.
		/// </summary>
		private void ResetColors()
		{
			for (int i = 0; i < UITKTheme.ColorNames.Length; ++i)
			{
				UITKTheme.Clear(Configuration.GlobalSettings, UITKTheme.ColorNames[i]);
			}
			SaveConfiguration();
			UITKThemeManager.Reload();
			RefreshSwatches();
		}

		/// <summary>
		/// Reads the colour currently in force for a theme name.
		/// </summary>
		/// <param name="name">One of <see cref="UITKTheme.ColorNames"/>.</param>
		/// <returns>The overridden colour, or white when none is set.</returns>
		private static Color ResolveCurrent(string name)
		{
			UITKTheme theme = UITKThemeManager.Current;
			if (theme == null || !theme.HasOverride(name))
			{
				return Color.white;
			}

			switch (name)
			{
				case "Primary":      return theme.Primary;
				case "Secondary":    return theme.Secondary;
				case "Highlight":    return theme.Highlight;
				case "Background":   return theme.Background;
				case "Text":         return theme.Text;
				case "Health":       return theme.Health;
				case "Mana":         return theme.Mana;
				case "Stamina":      return theme.Stamina;
				case "Crosshair":    return theme.Crosshair;
				case "TooltipTitle": return theme.TooltipTitle;
				case "TooltipLabel": return theme.TooltipLabel;
				case "TooltipValue": return theme.TooltipValue;
				case "TooltipStat":  return theme.TooltipStat;
				default:             return Color.white;
			}
		}

		/// <summary>
		/// Repaints every colour swatch from the theme currently in force.
		/// </summary>
		private void RefreshSwatches()
		{
			UITKTheme theme = UITKThemeManager.Current;
			for (int i = 0; i < colorSwatches.Length && i < UITKTheme.ColorNames.Length; ++i)
			{
				VisualElement swatch = colorSwatches[i];
				if (swatch == null)
				{
					continue;
				}

				bool overridden = theme != null && theme.HasOverride(i);
				if (overridden)
				{
					swatch.style.backgroundColor = ResolveCurrent(UITKTheme.ColorNames[i]);
				}
				else
				{
					// No override: let the stylesheet show the "unset" swatch appearance.
					swatch.style.backgroundColor = StyleKeyword.Null;
				}
				swatch.EnableInClassList("options-swatch--unset", !overridden);
			}
		}

		/// <summary>
		/// Binds the VSync toggle to its saved value and applies it.
		/// </summary>
		private void InitializeVSync()
		{
			if (vsyncToggle == null)
			{
				Log.Error("UITKOptions", "VSync toggle is missing.");
				return;
			}

			Configuration.GlobalSettings.TryGetBool(VSyncKey, out bool vsync, false);
			vsyncToggle.value = vsync;
			QualitySettings.vSyncCount = vsync ? 1 : 0;

			vsyncToggle.RegisterValueChangedCallback((evt) =>
			{
				Configuration.GlobalSettings.Set(VSyncKey, evt.newValue);
				QualitySettings.vSyncCount = evt.newValue ? 1 : 0;
				SaveConfiguration();
			});
		}

		/// <summary>
		/// Binds the brightness slider to its saved value and applies it to ambient light.
		/// </summary>
		private void InitializeBrightness()
		{
			if (brightnessSlider == null)
			{
				Log.Error("UITKOptions", "Brightness slider is missing.");
				return;
			}

			brightnessSlider.lowValue = 0f;
			brightnessSlider.highValue = 1f;

			Configuration.GlobalSettings.TryGetFloat(BrightnessKey, out float brightness, 1.0f);
			brightnessSlider.value = brightness;
			RenderSettings.ambientLight = new Color(brightness, brightness, brightness, brightness);

			brightnessSlider.RegisterValueChangedCallback((evt) =>
			{
				float value = evt.newValue;
				Configuration.GlobalSettings.Set(BrightnessKey, value);
				RenderSettings.ambientLight = new Color(value, value, value, value);
				SaveConfiguration();
			});
		}

		/// <summary>
		/// Populates the resolution dropdown from the available screen resolutions and applies the saved selection.
		/// </summary>
		private void InitializeResolution()
		{
			if (resolutionDropdown == null)
			{
				Log.Error("UITKOptions", "Resolution dropdown is missing.");
				return;
			}

			Resolution[] resolutions = Screen.resolutions;
			List<string> options = new List<string>(resolutions.Length);
			for (int i = 0; i < resolutions.Length; ++i)
			{
				options.Add(resolutions[i].width + " x " + resolutions[i].height);
			}
			resolutionDropdown.choices = options;

			Configuration.GlobalSettings.TryGetInt(ResolutionWidthKey, out int savedWidth, 1280);
			Configuration.GlobalSettings.TryGetInt(ResolutionHeightKey, out int savedHeight, 800);
			int index = GetResolutionIndex(savedWidth, savedHeight);
			resolutionDropdown.index = options.Count > 0 ? index : -1;
			ApplyResolution(index);

			resolutionDropdown.RegisterValueChangedCallback((evt) =>
			{
				int selectedIndex = resolutionDropdown.index;
				Resolution[] current = Screen.resolutions;
				if (selectedIndex < 0 || selectedIndex >= current.Length)
				{
					return;
				}
				Configuration.GlobalSettings.Set(ResolutionWidthKey, current[selectedIndex].width);
				Configuration.GlobalSettings.Set(ResolutionHeightKey, current[selectedIndex].height);
				ApplyResolution(selectedIndex);
				SaveConfiguration();
			});
		}

		/// <summary>
		/// Returns the index of the resolution matching the given width and height, or 0 if not found.
		/// </summary>
		private int GetResolutionIndex(int width, int height)
		{
			Resolution[] resolutions = Screen.resolutions;
			for (int i = 0; i < resolutions.Length; i++)
			{
				if (resolutions[i].width == width && resolutions[i].height == height)
				{
					return i;
				}
			}
			return 0;
		}

		/// <summary>
		/// Applies the resolution at the given index to the screen.
		/// </summary>
		private void ApplyResolution(int index)
		{
#if !UNITY_WEBGL
			Resolution[] resolutions = Screen.resolutions;
			if (index >= 0 && index < resolutions.Length)
			{
				Resolution res = resolutions[index];
				Screen.SetResolution(res.width, res.height, Screen.fullScreen);
			}
#endif
		}

		/// <summary>
		/// Populates the refresh rate dropdown from the rates available at the current resolution and applies the saved selection.
		/// </summary>
		private void InitializeRefreshRate()
		{
			if (refreshRateDropdown == null)
			{
				Log.Error("UITKOptions", "Refresh rate dropdown is missing.");
				return;
			}

			refreshRateValues.Clear();
			Resolution currentResolution = Screen.currentResolution;
			Resolution[] resolutions = Screen.resolutions;
			Dictionary<float, RefreshRate> uniqueRefreshRates = new Dictionary<float, RefreshRate>();

			foreach (Resolution res in resolutions)
			{
				if (res.width == currentResolution.width && res.height == currentResolution.height)
				{
					float rateValue = (float)res.refreshRateRatio.numerator / res.refreshRateRatio.denominator;
					if (!uniqueRefreshRates.ContainsKey(rateValue))
					{
						uniqueRefreshRates.Add(rateValue, res.refreshRateRatio);
					}
				}
			}

			List<float> sortedRefreshRates = new List<float>(uniqueRefreshRates.Keys);
			sortedRefreshRates.Sort();

			List<string> options = new List<string>(sortedRefreshRates.Count);
			foreach (float rate in sortedRefreshRates)
			{
				refreshRateValues.Add(rate);
				options.Add($"{rate:F0} Hz");
			}
			refreshRateDropdown.choices = options;

			Configuration.GlobalSettings.TryGetInt(RefreshRateKey, out int savedRefreshRateInt, 60);
			int selectedIndex = -1;
			for (int i = 0; i < sortedRefreshRates.Count; i++)
			{
				if (Mathf.Approximately(sortedRefreshRates[i], savedRefreshRateInt))
				{
					selectedIndex = i;
					break;
				}
			}
			if (selectedIndex == -1)
			{
				selectedIndex = 0;
			}

			refreshRateDropdown.index = options.Count > 0 ? selectedIndex : -1;
			if (sortedRefreshRates.Count > 0)
			{
				ApplyRefreshRate(uniqueRefreshRates[sortedRefreshRates[selectedIndex]]);
			}

			refreshRateDropdown.RegisterValueChangedCallback((evt) =>
			{
				int index = refreshRateDropdown.index;
				if (index < 0 || index >= refreshRateValues.Count)
				{
					return;
				}
				int selectedRefreshRateInt = Mathf.RoundToInt(refreshRateValues[index]);
				Configuration.GlobalSettings.Set(RefreshRateKey, selectedRefreshRateInt);
				ApplyRefreshRate(FindRefreshRate(selectedRefreshRateInt));
				SaveConfiguration();
			});
		}

		/// <summary>
		/// Finds the <see cref="RefreshRate"/> struct at the current resolution closest to the given Hz value.
		/// </summary>
		private RefreshRate FindRefreshRate(int hz)
		{
			Resolution currentResolution = Screen.currentResolution;
			Resolution[] resolutions = Screen.resolutions;
			RefreshRate match = new RefreshRate { numerator = (uint)hz, denominator = 1 };
			foreach (Resolution res in resolutions)
			{
				if (res.width == currentResolution.width && res.height == currentResolution.height)
				{
					if (Mathf.Approximately((float)res.refreshRateRatio.numerator / res.refreshRateRatio.denominator, hz))
					{
						match = res.refreshRateRatio;
						break;
					}
				}
			}
			return match;
		}

		/// <summary>
		/// Applies the given refresh rate to the screen for the current resolution.
		/// </summary>
		private void ApplyRefreshRate(RefreshRate refreshRate)
		{
#if !UNITY_WEBGL
			Resolution currentResolution = Screen.currentResolution;
			Screen.SetResolution(currentResolution.width, currentResolution.height, Screen.fullScreenMode, refreshRate);

			/* Also cap the render loop. Screen.SetResolution changes the display mode; with
			 * vSync off it does not limit how fast Unity renders, so without this the client
			 * still draws as fast as it can and burns a core regardless of this setting. */
			Client.ApplyTargetFrameRate(Mathf.RoundToInt((float)refreshRate.numerator / refreshRate.denominator));
#endif
			/* WebGL is excluded deliberately: the browser drives presentation through
			 * requestAnimationFrame, and forcing targetFrameRate there causes stutter. */
		}

		/// <summary>
		/// Populates the fullscreen dropdown from the platform-supported modes and applies the saved selection.
		/// </summary>
		private void InitializeFullscreen()
		{
			if (fullscreenDropdown == null)
			{
				Log.Error("UITKOptions", "Fullscreen dropdown is missing.");
				return;
			}

			List<string> options = new List<string>();
#if !UNITY_WEBGL
			options.Add(FullScreenMode.FullScreenWindow.ToString());
#if UNITY_STANDALONE_WIN
			options.Add(FullScreenMode.ExclusiveFullScreen.ToString());
#endif
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX
			options.Add(FullScreenMode.MaximizedWindow.ToString());
#endif
#if UNITY_STANDALONE || UNITY_EDITOR
			options.Add(FullScreenMode.Windowed.ToString());
#endif
#endif
			fullscreenDropdown.choices = options;

#if !UNITY_WEBGL
			Configuration.GlobalSettings.TryGetInt(FullscreenKey, out int fullScreenMode, (int)FullScreenMode.FullScreenWindow);
			int index = Mathf.Clamp(fullScreenMode, 0, Mathf.Max(0, options.Count - 1));
			fullscreenDropdown.index = options.Count > 0 ? index : -1;
			Screen.fullScreenMode = (FullScreenMode)index;

			fullscreenDropdown.RegisterValueChangedCallback((evt) =>
			{
				int selectedIndex = fullscreenDropdown.index;
				if (selectedIndex < 0)
				{
					return;
				}
				Configuration.GlobalSettings.Set(FullscreenKey, selectedIndex);
				Screen.fullScreenMode = (FullScreenMode)selectedIndex;
				SaveConfiguration();
			});
#endif
		}
	}
}
