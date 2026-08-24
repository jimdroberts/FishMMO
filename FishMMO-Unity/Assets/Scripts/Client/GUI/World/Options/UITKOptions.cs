using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
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
	/// Four groups of settings live here:
	///
	/// • <b>Screen</b> — resolution, refresh rate, fullscreen, brightness, VSync.
	///
	/// • <b>Controls</b> — every rebindable action in the Player map, with conflict detection and
	///   per-binding and whole-map reset.
	///
	/// • <b>Gameplay</b> — five toggles whose keys used to be set per GameObject in the scene
	///   rather than in code. <c>ShowDamage</c> and <c>ShowHeals</c> are read live by
	///   ClientCombatDisplay; the other three are stored for consumers that do not exist yet.
	///
	/// • <b>Colours</b> — the thirteen themeable colours, opened through the shared colour picker.
	///   The values feed <see cref="UITKThemeManager"/> and are stored under the same config keys
	///   the Canvas UI used, so a config written by an older client still applies.
	///
	/// <para><b>Display settings are staged, not live.</b> Every other setting here can be undone
	/// by the control that set it. A display mode cannot: pick one the monitor will not show and
	/// the player cannot see the panel that would put it back. So the three display dropdowns
	/// write to a pending selection, Apply commits it, and Apply then arms a countdown that
	/// restores the previous mode unless the player presses Keep. Nothing is written to the
	/// configuration file until Keep, so a mode that blacked out the screen is not still there
	/// after a restart.</para>
	///
	/// <para><b>Writes to disk are debounced.</b> Every control used to call
	/// <c>Configuration.Save()</c> directly, which serialises and rewrites the whole file. On a
	/// slider that is once per frame — around sixty full-file writes a second while dragging —
	/// and the colour picker did that <em>plus</em> a complete theme reload across every
	/// registered panel. Both are now coalesced onto a short quiet period and flushed on close.</para>
	///
	/// <para><b>Values read from the file are clamped.</b> A configuration file is a text file a
	/// player can edit, and it can also be truncated by a crash. An out-of-range brightness used
	/// to be written straight into <c>RenderSettings.ambientLight</c>, and an out-of-range
	/// fullscreen value straight into <c>Screen.fullScreenMode</c>. Startup must survive a corrupt
	/// settings file.</para>
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
		/// <summary>Name of the container the keybinding rows are built into.</summary>
		private const string CONTROLS_LIST_NAME = "options-controls-list";
		/// <summary>Name of the keybinding status line.</summary>
		private const string CONTROLS_STATUS_NAME = "options-controls-status";
		/// <summary>Name of the button that clears every keybinding override.</summary>
		private const string RESET_CONTROLS_NAME = "options-reset-controls-btn";
		/// <summary>Name of the display-settings apply button.</summary>
		private const string SCREEN_APPLY_NAME = "options-screen-apply-btn";
		/// <summary>Name of the display-settings revert button.</summary>
		private const string SCREEN_REVERT_NAME = "options-screen-revert-btn";
		/// <summary>Name of the display-settings keep button.</summary>
		private const string SCREEN_KEEP_NAME = "options-screen-keep-btn";
		/// <summary>Name of the display-settings status line.</summary>
		private const string SCREEN_STATUS_NAME = "options-screen-status";
		/// <summary>Name of the window-snap-grid slider element in the UXML.</summary>
		private const string SNAP_SLIDER_NAME = "ui-snap-slider";
		/// <summary>Name of the label showing the snap grid's current value.</summary>
		private const string SNAP_VALUE_NAME = "ui-snap-value";
		/// <summary>Name of the button that returns every panel to its authored position.</summary>
		private const string RESET_LAYOUT_NAME = "options-reset-layout-btn";

		/// <summary>Configuration key for the VSync setting.</summary>
		private const string VSyncKey = "VSync";

		/// <summary>Name of the frame rate limit dropdown element in the UXML.</summary>
		private const string FRAMERATE_DROPDOWN_NAME = "framerate-dropdown";

		/// <summary>Configuration key for the frame rate limit setting.</summary>
		private const string FrameRateKey = "Frame Rate Limit";
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

		/// <summary>Seconds of quiet before a pending configuration write is flushed to disk.</summary>
		private const float SaveDebounceSeconds = 0.75f;

		/// <summary>Seconds of quiet before a pending theme reload is applied.</summary>
		/// <remarks>
		/// Much shorter than the disk debounce. A theme reload is visible, so it has to keep up
		/// with the colour picker to be usable; it just must not run on every one of the sixty
		/// change callbacks a second a dragged slider produces.
		/// </remarks>
		private const float ThemeDebounceSeconds = 0.12f;

		/// <summary>Seconds before an applied display mode is put back automatically.</summary>
		/// <remarks>
		/// Long enough to read the prompt and reach the mouse, short enough that a player staring
		/// at a black screen is not doing so for long.
		/// </remarks>
		private const float DisplayRevertSeconds = 12.0f;

		/// <summary>Seconds an interactive rebind listens before giving up.</summary>
		private const float RebindTimeoutSeconds = 6.0f;

		/// <summary>Name of the action map whose bindings are editable.</summary>
		private const string PlayerActionMapName = "Player";

		/// <summary>
		/// Controls that an interactive rebind must never select.
		/// </summary>
		/// <remarks>
		/// Escape is excluded because it is the client's universal "close this" key: bound to
		/// something else, a player who opens a panel they cannot otherwise dismiss has no way
		/// out, and the rebind prompt itself has no way to be cancelled. Pointer position and
		/// delta are excluded because they are continuously moving analogue controls — a rebind
		/// listening for "any control that moved" picks one of them instantly and every attempt
		/// binds the mouse.
		/// </remarks>
		private static readonly string[] ExcludedRebindControls =
		{
			"<Keyboard>/escape",
			"<Pointer>/position",
			"<Pointer>/delta",
			"<Mouse>/position",
			"<Mouse>/delta",
			"<Mouse>/scroll",
		};

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

		/// <summary>The frame rate limit dropdown control.</summary>
		private DropdownField frameRateDropdown;

		/// <summary>
		/// Frame rate caps offered to the player, in frames per second.
		/// </summary>
		/// <remarks>
		/// The full ladder of common rates. Which of them are actually offered is decided in
		/// <see cref="BuildFrameRateChoices"/> — bounded below by the network tick rate and above
		/// by what the display can present.
		/// </remarks>
		private static readonly int[] frameRateChoices =
		{
			30, 60, 75, 90, 120, 144, 165, 180, 240, 300, 360, 480, 500
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
		/// <summary>Container holding the generated keybinding rows.</summary>
		private VisualElement controlsList;
		/// <summary>Status line for the Controls section.</summary>
		private Label controlsStatus;
		/// <summary>Apply button for the display settings.</summary>
		private Button screenApplyButton;
		/// <summary>Revert button for the display settings.</summary>
		private Button screenRevertButton;
		/// <summary>Keep button shown while the auto-revert countdown is running.</summary>
		private Button screenKeepButton;
		/// <summary>Status line for the display settings, including the countdown.</summary>
		private Label screenStatus;

		/// <summary>Swatch element for each colour row, indexed alongside UITKTheme.ColorNames.</summary>
		private readonly VisualElement[] colorSwatches = new VisualElement[13];

		/// <summary>Distinct screen resolutions, smallest first.</summary>
		/// <remarks>
		/// Deduplicated by width and height. <c>Screen.resolutions</c> returns one entry per
		/// width/height/refresh-rate combination, so a monitor offering three refresh rates listed
		/// every resolution three times and the dropdown was full of identical entries.
		/// </remarks>
		private readonly List<Vector2Int> resolutionOptions = new List<Vector2Int>();

		/// <summary>Refresh rates available at the currently selected resolution.</summary>
		private readonly List<RefreshRate> refreshRateOptions = new List<RefreshRate>();

		/// <summary>Fullscreen modes offered on this platform, parallel to the dropdown entries.</summary>
		/// <remarks>
		/// The dropdown's index is not the <see cref="FullScreenMode"/> value: the list is built
		/// conditionally per platform, so on a build without exclusive fullscreen the second entry
		/// is <c>MaximizedWindow</c> and not <c>ExclusiveFullScreen</c>. Casting the index to the
		/// enum — which is what this used to do — silently applied whichever mode happened to sit
		/// at that numeric value.
		/// </remarks>
		private readonly List<FullScreenMode> fullscreenOptions = new List<FullScreenMode>();

		/// <summary>Display mode currently in force, and the one an auto-revert returns to.</summary>
		private Vector2Int committedResolution;
		/// <summary>Refresh rate currently in force.</summary>
		private RefreshRate committedRefreshRate;
		/// <summary>Fullscreen mode currently in force.</summary>
		private FullScreenMode committedFullscreen;

		/// <summary>True while an applied display mode is awaiting confirmation.</summary>
		private bool displayRevertArmed;
		/// <summary>Unscaled time at which an unconfirmed display mode is put back.</summary>
		private float displayRevertDeadline;

		/// <summary>True when the configuration file has unsaved changes.</summary>
		private bool savePending;
		/// <summary>Unscaled time at which pending configuration changes are flushed.</summary>
		private float saveDeadline;

		/// <summary>True when a theme reload is due.</summary>
		private bool themeReloadPending;
		/// <summary>Unscaled time at which a pending theme reload runs.</summary>
		private float themeReloadDeadline;

		/// <summary>The rebind currently listening for input, or null.</summary>
		private InputActionRebindingExtensions.RebindingOperation activeRebind;
		/// <summary>The button that started the active rebind, so it can be restored.</summary>
		private Button activeRebindButton;

		/// <summary>Slider setting the grid dragged panels snap to.</summary>
		private Slider snapSlider;

		/// <summary>Label showing the snap grid in points, or "Off".</summary>
		private Label snapValueLabel;

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
			controlsList = Root.Q<VisualElement>(CONTROLS_LIST_NAME);
			controlsStatus = Root.Q<Label>(CONTROLS_STATUS_NAME);
			screenApplyButton = Root.Q<Button>(SCREEN_APPLY_NAME);
			screenRevertButton = Root.Q<Button>(SCREEN_REVERT_NAME);
			screenKeepButton = Root.Q<Button>(SCREEN_KEEP_NAME);
			screenStatus = Root.Q<Label>(SCREEN_STATUS_NAME);
			snapSlider = Root.Q<Slider>(SNAP_SLIDER_NAME);
			snapValueLabel = Root.Q<Label>(SNAP_VALUE_NAME);

			frameRateDropdown = Root.Q<DropdownField>(FRAMERATE_DROPDOWN_NAME);

			InitializeVSync();
			InitializeBrightness();
			InitializeDisplaySettings();
			InitializeFrameRateLimit();
			InitializeInterfaceSettings();
			InitializeGameplayToggles();
			InitializeColorSettings();
			InitializeControlsSection();

			if (closeButton != null)
			{
				closeButton.clicked += Hide;
			}
			if (screenApplyButton != null)
			{
				screenApplyButton.clicked += OnScreenApply;
			}
			if (screenRevertButton != null)
			{
				screenRevertButton.clicked += OnScreenRevert;
			}
			if (screenKeepButton != null)
			{
				screenKeepButton.clicked += OnScreenKeep;
			}

			/* Wired here rather than inside InitializeControlsSection: that method rebuilds the
			 * rows and is called again by ResetAllBindings, so subscribing from inside it would
			 * add one more handler to the same button on every reset. The button element itself is
			 * replaced whenever the tree is, which is what makes a single += per OnStarting
			 * correct. */
			Button resetControlsButton = Root.Q<Button>(RESET_CONTROLS_NAME);
			if (resetControlsButton != null)
			{
				resetControlsButton.clicked += ResetAllBindings;
			}

			Button resetLayoutButton = Root.Q<Button>(RESET_LAYOUT_NAME);
			if (resetLayoutButton != null)
			{
				resetLayoutButton.clicked += ResetPanelPositions;
			}
		}

		/// <summary>
		/// Re-applies state after the visual tree has been rebuilt.
		/// </summary>
		/// <remarks>
		/// The panel starts hidden, so its tree is cloned afresh on every open and every element
		/// cached above belongs to the previous one. <c>OnStarting</c> re-runs from
		/// <c>ReinitializeIfTreeReplaced</c> and rebuilds all four sections; this only has to put
		/// the display prompt back into whatever state the countdown is in, because that state
		/// lives across an open and close.
		/// </remarks>
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			RefreshDisplayPrompt();
		}

		/// <summary>
		/// Puts the display prompt back into the right state on every open.
		/// </summary>
		protected override void OnAfterShow()
		{
			RefreshDisplayPrompt();
		}

		/// <summary>
		/// Flushes anything pending and cancels an in-progress rebind when the panel closes.
		/// </summary>
		/// <remarks>
		/// A rebind left listening after the panel is gone would capture the next key the player
		/// pressed — in the world, with no prompt on screen — and assign it to whichever action
		/// the row belonged to.
		/// </remarks>
		public override void Hide(bool overrideIsAlwaysOpen)
		{
			if (!overrideIsAlwaysOpen)
			{
				CancelActiveRebind();
				FlushThemeReload();
				FlushConfiguration();
			}
			base.Hide(overrideIsAlwaysOpen);
		}

		/// <summary>
		/// Flushes anything still pending when the panel is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			CancelActiveRebind();
			FlushThemeReload();
			FlushConfiguration();
		}

		/// <summary>
		/// Drives the debounced writes and the display auto-revert countdown.
		/// </summary>
		protected override void OnTick()
		{
			float now = Time.unscaledTime;

			if (themeReloadPending && now >= themeReloadDeadline)
			{
				FlushThemeReload();
			}

			if (savePending && now >= saveDeadline)
			{
				FlushConfiguration();
			}

			if (displayRevertArmed)
			{
				float remaining = displayRevertDeadline - now;
				if (remaining <= 0.0f)
				{
					/* Nothing was confirmed, so the mode is assumed to be one the player cannot
					 * see. This is the whole reason the countdown exists. */
					RevertDisplaySettings();
					SetScreenStatus("Display settings were restored.");
				}
				else if (screenStatus != null)
				{
					screenStatus.text = $"Keep these settings? Reverting in {Mathf.CeilToInt(remaining)}s";
				}
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

				/* The configuration this panel just created is the first one to exist, and panels
				 * can be dragged before the options screen has ever been opened — so the snap grid
				 * may already have been read, and cached, from nothing. Drop that cache so the
				 * next read sees the player's saved value. */
				UITKPanelPositions.InvalidateSnapGrid();
			}
		}

		/// <summary>
		/// Marks the configuration as needing a write, to be flushed once the player stops changing things.
		/// </summary>
		/// <remarks>
		/// <c>Configuration.Save</c> serialises and rewrites the entire file. Called straight from
		/// a slider's change callback — which every setting here used to do — that is one full
		/// rewrite per frame for as long as the slider is held.
		/// </remarks>
		private void RequestSave()
		{
			savePending = true;
			saveDeadline = Time.unscaledTime + SaveDebounceSeconds;
		}

		/// <summary>
		/// Persists the global configuration to disk (no-op in editor/WebGL).
		/// </summary>
		private void FlushConfiguration()
		{
			if (!savePending)
			{
				return;
			}
			savePending = false;
#if !UNITY_EDITOR && !UNITY_WEBGL
			Configuration.GlobalSettings.Save();
#endif
		}

		/// <summary>
		/// Marks the theme as needing a reload once the player stops adjusting a colour.
		/// </summary>
		private void RequestThemeReload()
		{
			themeReloadPending = true;
			themeReloadDeadline = Time.unscaledTime + ThemeDebounceSeconds;
		}

		/// <summary>
		/// Applies a pending theme reload.
		/// </summary>
		private void FlushThemeReload()
		{
			if (!themeReloadPending)
			{
				return;
			}
			themeReloadPending = false;

			/* Reload rather than poking the one colour: the manager owns which USS classes a
			 * colour maps onto, and re-reading configuration keeps this panel from having to
			 * duplicate that mapping. */
			UITKThemeManager.Reload();
			RefreshSwatches();
		}

		// ── Interface ───────────────────────────────────────────────

		/// <summary>
		/// Binds the window snap grid and the layout reset.
		/// </summary>
		/// <remarks>
		/// Both settings describe the panels themselves rather than anything in the world, which
		/// is why they are here rather than under Gameplay. The reset is the only way back from an
		/// arrangement the player cannot fix by dragging — a panel left in a corner of a monitor
		/// they no longer have, most obviously — so it is deliberately a plain, always-available
		/// button rather than something that only appears when a saved layout exists.
		/// </remarks>
		private void InitializeInterfaceSettings()
		{
			if (snapSlider != null)
			{
				snapSlider.lowValue = 0.0f;
				snapSlider.highValue = UITKPanelPositions.MaxSnapGridSize;

				/* SetValueWithoutNotify, because assigning `value` raises the change callback —
				 * and the callback writes to the configuration and requests a disk write. Seeding
				 * a control from the file it writes back to is how a settings panel rewrites the
				 * whole file every time it is opened. */
				snapSlider.SetValueWithoutNotify(UITKPanelPositions.SnapGridSize);
				UpdateSnapValueLabel(UITKPanelPositions.SnapGridSize);

				snapSlider.RegisterValueChangedCallback((evt) =>
				{
					/* Whole points. A grid of 6.37 is not an alignment aid, and the value is
					 * shown to the player as a number of points. */
					float snapped = Mathf.Round(evt.newValue);

					UITKPanelPositions.SnapGridSize = snapped;
					UpdateSnapValueLabel(snapped);

					if (!Mathf.Approximately(snapped, evt.newValue))
					{
						// Put the rounded value back under the handle so it cannot drift.
						snapSlider.SetValueWithoutNotify(snapped);
					}
				});
			}
		}

		/// <summary>
		/// Writes the snap grid's value beside its slider.
		/// </summary>
		/// <param name="size">Grid size in panel points; zero means snapping is off.</param>
		private void UpdateSnapValueLabel(float size)
		{
			if (snapValueLabel == null)
			{
				return;
			}

			snapValueLabel.text = size <= 0.0f ? "Off" : $"{Mathf.RoundToInt(size)} px";
		}

		/// <summary>
		/// Returns every panel to the position its stylesheet gives it.
		/// </summary>
		private void ResetPanelPositions()
		{
			UIManager.ResetAllPanelPositions();
			RequestSave();
		}

		// ── Gameplay ────────────────────────────────────────────────

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
					RequestSave();
				});

				row.Add(toggle);
				gameplayList.Add(row);
			}
		}

		// ── Colours ─────────────────────────────────────────────────

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
		/// <remarks>
		/// The picker reports every change as the player drags, which is once a frame. The
		/// callback used to write the configuration file and reload the whole theme on each one:
		/// a full file rewrite plus a walk of every registered panel's visual tree, sixty times a
		/// second. The swatch — the one piece of feedback that has to be immediate — is still
		/// updated inline; the file write and the theme reload are debounced.
		/// </remarks>
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

				if (index >= 0 && index < colorSwatches.Length && colorSwatches[index] != null)
				{
					colorSwatches[index].style.backgroundColor = chosen;
					colorSwatches[index].EnableInClassList("options-swatch--unset", false);
				}

				RequestThemeReload();
				RequestSave();
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
			RequestSave();

			// A one-off action, so there is nothing to coalesce; apply it immediately.
			themeReloadPending = true;
			FlushThemeReload();
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

		// ── VSync and brightness ────────────────────────────────────

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
				RequestSave();
			});
		}

		/// <summary>
		/// Binds the brightness slider to its saved value and applies it to ambient light.
		/// </summary>
		/// <remarks>
		/// The stored value is clamped on the way in. It is a float in a text file, so it can be
		/// anything at all — and it was previously fed straight into
		/// <c>RenderSettings.ambientLight</c>, where a large value blows out the whole scene and a
		/// negative one crushes it to black, with the slider unable to represent either and so
		/// unable to put it back.
		/// </remarks>
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
			brightness = float.IsNaN(brightness) ? 1.0f : Mathf.Clamp01(brightness);
			brightnessSlider.SetValueWithoutNotify(brightness);
			ApplyBrightness(brightness);

			brightnessSlider.RegisterValueChangedCallback((evt) =>
			{
				float value = Mathf.Clamp01(evt.newValue);
				Configuration.GlobalSettings.Set(BrightnessKey, value);
				ApplyBrightness(value);
				RequestSave();
			});
		}

		/// <summary>
		/// Writes a brightness level into the scene's ambient light.
		/// </summary>
		private static void ApplyBrightness(float value)
		{
			RenderSettings.ambientLight = new Color(value, value, value, value);
		}

		// ── Display settings ────────────────────────────────────────

		/// <summary>
		/// Populates the three display dropdowns and records the mode currently in force.
		/// </summary>
		/// <remarks>
		/// Nothing here applies a mode. The old version applied the saved resolution during setup
		/// and then applied a refresh rate immediately afterwards — and that second call passed
		/// <c>Screen.currentResolution</c>, the display's mode rather than the one just requested,
		/// so opening the options panel undid the resolution it had itself just set.
		/// </remarks>
		private void InitializeDisplaySettings()
		{
			BuildResolutionOptions();
			BuildFullscreenOptions();

			committedResolution = ResolveSavedResolution();
			committedFullscreen = ResolveSavedFullscreen();

			BuildRefreshRateOptions(committedResolution);
			committedRefreshRate = ResolveSavedRefreshRate();

			if (resolutionDropdown != null)
			{
				resolutionDropdown.choices = BuildResolutionLabels();
				resolutionDropdown.index = IndexOfResolution(committedResolution);
				resolutionDropdown.RegisterValueChangedCallback(OnResolutionSelectionChanged);
			}
			if (refreshRateDropdown != null)
			{
				refreshRateDropdown.choices = BuildRefreshRateLabels();
				refreshRateDropdown.index = IndexOfRefreshRate(committedRefreshRate);
			}
			if (fullscreenDropdown != null)
			{
				List<string> labels = new List<string>(fullscreenOptions.Count);
				for (int i = 0; i < fullscreenOptions.Count; ++i)
				{
					labels.Add(fullscreenOptions[i].ToString());
				}
				fullscreenDropdown.choices = labels;
				fullscreenDropdown.index = Mathf.Max(0, fullscreenOptions.IndexOf(committedFullscreen));
			}

			RefreshDisplayPrompt();
		}

		/// <summary>
		/// Collects the distinct width/height pairs the display supports.
		/// </summary>
		private void BuildResolutionOptions()
		{
			resolutionOptions.Clear();

			Resolution[] resolutions = Screen.resolutions;
			for (int i = 0; i < resolutions.Length; ++i)
			{
				Vector2Int size = new Vector2Int(resolutions[i].width, resolutions[i].height);
				if (!resolutionOptions.Contains(size))
				{
					resolutionOptions.Add(size);
				}
			}

			if (resolutionOptions.Count == 0)
			{
				// Headless or an unusual display: offer at least the current window size.
				resolutionOptions.Add(new Vector2Int(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height)));
			}
		}

		/// <summary>
		/// Builds the platform's fullscreen mode list.
		/// </summary>
		private void BuildFullscreenOptions()
		{
			fullscreenOptions.Clear();
#if !UNITY_WEBGL
			fullscreenOptions.Add(FullScreenMode.FullScreenWindow);
#if UNITY_STANDALONE_WIN
			fullscreenOptions.Add(FullScreenMode.ExclusiveFullScreen);
#endif
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX
			fullscreenOptions.Add(FullScreenMode.MaximizedWindow);
#endif
#if UNITY_STANDALONE || UNITY_EDITOR
			fullscreenOptions.Add(FullScreenMode.Windowed);
#endif
#endif
			if (fullscreenOptions.Count == 0)
			{
				fullscreenOptions.Add(FullScreenMode.FullScreenWindow);
			}
		}

		/// <summary>
		/// Collects the refresh rates the display offers at a given resolution.
		/// </summary>
		private void BuildRefreshRateOptions(Vector2Int size)
		{
			refreshRateOptions.Clear();

			Resolution[] resolutions = Screen.resolutions;
			for (int i = 0; i < resolutions.Length; ++i)
			{
				if (resolutions[i].width != size.x || resolutions[i].height != size.y)
				{
					continue;
				}

				RefreshRate rate = resolutions[i].refreshRateRatio;
				bool duplicate = false;
				for (int j = 0; j < refreshRateOptions.Count; ++j)
				{
					if (Mathf.Approximately(ToHz(refreshRateOptions[j]), ToHz(rate)))
					{
						duplicate = true;
						break;
					}
				}
				if (!duplicate)
				{
					refreshRateOptions.Add(rate);
				}
			}

			refreshRateOptions.Sort((a, b) => ToHz(a).CompareTo(ToHz(b)));

			if (refreshRateOptions.Count == 0)
			{
				refreshRateOptions.Add(Screen.currentResolution.refreshRateRatio);
			}
		}

		/// <summary>Converts a refresh-rate ratio to hertz.</summary>
		private static float ToHz(RefreshRate rate)
		{
			return rate.denominator == 0 ? 0.0f : (float)rate.numerator / rate.denominator;
		}

		/// <summary>Builds the display labels for the resolution dropdown.</summary>
		private List<string> BuildResolutionLabels()
		{
			List<string> labels = new List<string>(resolutionOptions.Count);
			for (int i = 0; i < resolutionOptions.Count; ++i)
			{
				labels.Add($"{resolutionOptions[i].x} x {resolutionOptions[i].y}");
			}
			return labels;
		}

		/// <summary>Builds the display labels for the refresh rate dropdown.</summary>
		private List<string> BuildRefreshRateLabels()
		{
			List<string> labels = new List<string>(refreshRateOptions.Count);
			for (int i = 0; i < refreshRateOptions.Count; ++i)
			{
				labels.Add($"{ToHz(refreshRateOptions[i]):F0} Hz");
			}
			return labels;
		}

		/// <summary>
		/// Reads the saved resolution, falling back to something sensible when it is absent or unsupported.
		/// </summary>
		/// <remarks>
		/// The old fallback was index 0. <c>Screen.resolutions</c> is ordered smallest first, so a
		/// saved resolution the display no longer offers — a monitor swap, a config typo, a fresh
		/// install with no entry at all — dropped the client to the smallest mode the hardware
		/// supports. The current window size is a far better guess, and the largest supported mode
		/// is a better last resort than the smallest.
		/// </remarks>
		private Vector2Int ResolveSavedResolution()
		{
			Configuration.GlobalSettings.TryGetInt(ResolutionWidthKey, out int width, 0);
			Configuration.GlobalSettings.TryGetInt(ResolutionHeightKey, out int height, 0);

			Vector2Int saved = new Vector2Int(width, height);
			if (resolutionOptions.Contains(saved))
			{
				return saved;
			}

			Vector2Int current = new Vector2Int(Screen.width, Screen.height);
			if (resolutionOptions.Contains(current))
			{
				return current;
			}

			return resolutionOptions[resolutionOptions.Count - 1];
		}

		/// <summary>
		/// Reads the saved refresh rate, falling back to the highest the resolution offers.
		/// </summary>
		private RefreshRate ResolveSavedRefreshRate()
		{
			Configuration.GlobalSettings.TryGetInt(RefreshRateKey, out int savedHz, 0);

			for (int i = 0; i < refreshRateOptions.Count; ++i)
			{
				if (Mathf.RoundToInt(ToHz(refreshRateOptions[i])) == savedHz)
				{
					return refreshRateOptions[i];
				}
			}
			return refreshRateOptions[refreshRateOptions.Count - 1];
		}

		/// <summary>
		/// Reads the saved fullscreen mode, rejecting anything this platform does not offer.
		/// </summary>
		/// <remarks>
		/// The stored value is the <see cref="FullScreenMode"/> value, not a dropdown index —
		/// which is what its own default, <c>(int)FullScreenMode.FullScreenWindow</c>, always
		/// implied, and what the old code contradicted by writing the index back into the same key
		/// and then casting it to the enum.
		/// </remarks>
		private FullScreenMode ResolveSavedFullscreen()
		{
			Configuration.GlobalSettings.TryGetInt(FullscreenKey, out int saved, (int)FullScreenMode.FullScreenWindow);

			FullScreenMode mode = (FullScreenMode)saved;
			if (fullscreenOptions.Contains(mode))
			{
				return mode;
			}
			return fullscreenOptions[0];
		}

		/// <summary>Index of a resolution in the dropdown, or 0.</summary>
		private int IndexOfResolution(Vector2Int size)
		{
			int index = resolutionOptions.IndexOf(size);
			return index >= 0 ? index : 0;
		}

		/// <summary>Index of a refresh rate in the dropdown, or the last entry.</summary>
		private int IndexOfRefreshRate(RefreshRate rate)
		{
			for (int i = 0; i < refreshRateOptions.Count; ++i)
			{
				if (Mathf.Approximately(ToHz(refreshRateOptions[i]), ToHz(rate)))
				{
					return i;
				}
			}
			return refreshRateOptions.Count - 1;
		}

		/// <summary>
		/// Rebuilds the refresh-rate list when a different resolution is selected.
		/// </summary>
		private void OnResolutionSelectionChanged(ChangeEvent<string> evt)
		{
			if (resolutionDropdown == null || refreshRateDropdown == null)
			{
				return;
			}

			int index = Mathf.Clamp(resolutionDropdown.index, 0, resolutionOptions.Count - 1);
			RefreshRate previous = SelectedRefreshRate();

			BuildRefreshRateOptions(resolutionOptions[index]);
			refreshRateDropdown.choices = BuildRefreshRateLabels();
			refreshRateDropdown.index = IndexOfRefreshRate(previous);
		}

		/// <summary>The resolution the dropdowns currently describe.</summary>
		private Vector2Int SelectedResolution()
		{
			int index = resolutionDropdown != null
				? Mathf.Clamp(resolutionDropdown.index, 0, resolutionOptions.Count - 1)
				: IndexOfResolution(committedResolution);
			return resolutionOptions[index];
		}

		/// <summary>The refresh rate the dropdowns currently describe.</summary>
		private RefreshRate SelectedRefreshRate()
		{
			int index = refreshRateDropdown != null
				? Mathf.Clamp(refreshRateDropdown.index, 0, refreshRateOptions.Count - 1)
				: 0;
			return refreshRateOptions[index];
		}

		/// <summary>The fullscreen mode the dropdowns currently describe.</summary>
		private FullScreenMode SelectedFullscreen()
		{
			int index = fullscreenDropdown != null
				? Mathf.Clamp(fullscreenDropdown.index, 0, fullscreenOptions.Count - 1)
				: 0;
			return fullscreenOptions[index];
		}

		/// <summary>
		/// Applies the staged display selection and arms the auto-revert countdown.
		/// </summary>
		private void OnScreenApply()
		{
			if (displayRevertArmed)
			{
				// Already waiting on a confirmation; do not stack a second one.
				return;
			}

			ApplyDisplayMode(SelectedResolution(), SelectedRefreshRate(), SelectedFullscreen());

			displayRevertArmed = true;
			displayRevertDeadline = Time.unscaledTime + DisplayRevertSeconds;
			RefreshDisplayPrompt();
		}

		/// <summary>
		/// Confirms the applied display mode and writes it to the configuration file.
		/// </summary>
		/// <remarks>
		/// This is the only place the display keys are written. A mode the player never confirmed
		/// is never persisted, so a restart cannot put an unusable mode back.
		/// </remarks>
		private void OnScreenKeep()
		{
			if (!displayRevertArmed)
			{
				return;
			}

			displayRevertArmed = false;

			committedResolution = SelectedResolution();
			committedRefreshRate = SelectedRefreshRate();
			committedFullscreen = SelectedFullscreen();

			Configuration.GlobalSettings.Set(ResolutionWidthKey, committedResolution.x);
			Configuration.GlobalSettings.Set(ResolutionHeightKey, committedResolution.y);
			Configuration.GlobalSettings.Set(RefreshRateKey, Mathf.RoundToInt(ToHz(committedRefreshRate)));
			Configuration.GlobalSettings.Set(FullscreenKey, (int)committedFullscreen);
			RequestSave();

			SetScreenStatus("Display settings saved.");
			RefreshDisplayPrompt();
		}

		/// <summary>
		/// Puts the previous display mode back, from the Revert button.
		/// </summary>
		private void OnScreenRevert()
		{
			RevertDisplaySettings();
			SetScreenStatus("Display settings restored.");
		}

		/// <summary>
		/// Restores the last committed display mode and resets the dropdowns to match.
		/// </summary>
		private void RevertDisplaySettings()
		{
			displayRevertArmed = false;

			ApplyDisplayMode(committedResolution, committedRefreshRate, committedFullscreen);

			if (resolutionDropdown != null)
			{
				resolutionDropdown.SetValueWithoutNotify(
					$"{committedResolution.x} x {committedResolution.y}");
			}
			BuildRefreshRateOptions(committedResolution);
			if (refreshRateDropdown != null)
			{
				refreshRateDropdown.choices = BuildRefreshRateLabels();
				refreshRateDropdown.index = IndexOfRefreshRate(committedRefreshRate);
			}
			if (fullscreenDropdown != null)
			{
				fullscreenDropdown.index = Mathf.Max(0, fullscreenOptions.IndexOf(committedFullscreen));
			}

			RefreshDisplayPrompt();
		}

		/// <summary>
		/// Binds the frame rate limit dropdown and applies the saved value.
		/// </summary>
		/// <remarks>
		/// Applied at start-up, not merely displayed. The saved preference used to be read only to
		/// position a dropdown, so a player who had chosen a cap got the bootstrap default until
		/// they reopened Options and pressed Apply — every session.
		/// </remarks>
		private void InitializeFrameRateLimit()
		{
			if (frameRateDropdown == null)
			{
				Log.Error("UITKOptions", "Frame rate dropdown is missing.");
				return;
			}

			List<int> choices = BuildFrameRateChoices();
			List<string> labels = new List<string>(choices.Count);
			for (int i = 0; i < choices.Count; ++i)
			{
				labels.Add(choices[i] + " FPS");
			}

			frameRateDropdown.choices = labels;

			int saved = ResolveSavedFrameRate(choices);
			frameRateDropdown.index = Mathf.Max(0, choices.IndexOf(saved));

			Client.ApplyTargetFrameRate(saved);

			frameRateDropdown.RegisterValueChangedCallback(evt =>
			{
				int index = frameRateDropdown.index;
				if (index < 0 || index >= choices.Count)
				{
					return;
				}

				int selected = choices[index];
				Configuration.GlobalSettings.Set(FrameRateKey, selected);
				Client.ApplyTargetFrameRate(selected);
			});
		}

		/// <summary>
		/// Builds the selectable frame rate caps, dropping any below the tick rate.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The floor is the network tick rate, not a cosmetic minimum: FishNet derives ticks from
		/// the update loop, so a frame rate below the tick rate cannot deliver them on schedule
		/// and the client falls behind the server's timeline. Offering such a value would let a
		/// player break their own connection from a settings menu.
		/// </para>
		/// <para>
		/// The ceiling is the display's fastest mode. Frames produced faster than the panel can
		/// present them are discarded at scan-out, so offering them would only sell the player
		/// heat and fan noise.
		/// </para>
		/// <para>
		/// The monitor's own rate is always included even when it is not one of the standard
		/// ladder values — 165 Hz and 59.94 Hz panels both exist, and the player should be able to
		/// pick their actual refresh rate rather than the nearest round number below it.
		/// </para>
		/// </remarks>
		/// <returns>The frame rate caps to offer, ascending.</returns>
		private static List<int> BuildFrameRateChoices()
		{
			int minimum = Client.ResolveMinimumFrameRate();
			int maximum = Mathf.Max(minimum, Client.ResolveMaximumFrameRate());

			List<int> choices = new List<int>(frameRateChoices.Length + 1);
			for (int i = 0; i < frameRateChoices.Length; ++i)
			{
				int rate = frameRateChoices[i];
				if (rate >= minimum && rate <= maximum)
				{
					choices.Add(rate);
				}
			}

			// The panel's exact rate, when the ladder does not already contain it.
			if (!choices.Contains(maximum))
			{
				choices.Add(maximum);
				choices.Sort();
			}

			// Never present an empty dropdown, whatever the display and tick rate report.
			if (choices.Count == 0)
			{
				choices.Add(Mathf.Clamp(minimum, Client.MinimumTargetFrameRate, Client.MaximumTargetFrameRate));
			}

			return choices;
		}

		/// <summary>
		/// Reads the saved frame rate cap, falling back to the display's own refresh rate.
		/// </summary>
		/// <remarks>
		/// A saved value that is no longer offered falls back to the fastest available rather than
		/// being honoured. That is the case where the player has moved the game to a slower
		/// monitor, or changed the tick rate: the old number is meaningless on the new hardware,
		/// and the closest honest answer is the best this display can do.
		/// </remarks>
		/// <param name="choices">The available caps, ascending.</param>
		/// <returns>The cap to apply.</returns>
		private static int ResolveSavedFrameRate(List<int> choices)
		{
			Configuration.GlobalSettings.TryGetInt(FrameRateKey, out int saved, 0);

			if (saved > 0 && choices.Contains(saved))
			{
				return saved;
			}

			// Default to the display's own refresh rate, which is the last (highest) entry.
			return choices[choices.Count - 1];
		}

		/// <summary>
		/// Pushes a display mode to the screen.
		/// </summary>
		/// <remarks>
		/// One call sets resolution, mode and refresh rate together. Setting them one at a time —
		/// which is what the old separate resolution and refresh-rate paths did — means each call
		/// reads back whatever the previous one left, and they undo one another.
		/// </remarks>
		private static void ApplyDisplayMode(Vector2Int size, RefreshRate rate, FullScreenMode mode)
		{
#if !UNITY_WEBGL
			Screen.SetResolution(size.x, size.y, mode, rate);

			/* The render cap is deliberately NOT set from the display refresh rate here.
			 *
			 * Display refresh rate and render frame rate are separate settings with separate
			 * controls, and deriving one from the other capped every player at their monitor's
			 * rate — so a 144 Hz owner could never reach the 500 FPS the game supports, which is
			 * exactly what an uncapped competitive player wants. The Frame Rate Limit dropdown
			 * owns the cap; changing display mode no longer silently overwrites it. */
#endif
			/* WebGL is excluded deliberately: the browser drives presentation through
			 * requestAnimationFrame, and forcing targetFrameRate there causes stutter. */
		}

		/// <summary>
		/// Shows the Apply button or the Keep/Revert prompt, whichever the current state calls for.
		/// </summary>
		private void RefreshDisplayPrompt()
		{
			if (screenApplyButton != null)
			{
				screenApplyButton.style.display = displayRevertArmed ? DisplayStyle.None : DisplayStyle.Flex;
			}
			if (screenKeepButton != null)
			{
				screenKeepButton.style.display = displayRevertArmed ? DisplayStyle.Flex : DisplayStyle.None;
			}
			if (screenRevertButton != null)
			{
				screenRevertButton.style.display = displayRevertArmed ? DisplayStyle.Flex : DisplayStyle.None;
			}
			if (!displayRevertArmed && screenStatus != null &&
				!string.IsNullOrEmpty(screenStatus.text) && screenStatus.text.StartsWith("Keep"))
			{
				screenStatus.text = string.Empty;
			}
		}

		/// <summary>Writes the display section's status line.</summary>
		private void SetScreenStatus(string text)
		{
			if (screenStatus != null)
			{
				screenStatus.text = text;
			}
		}

		// ── Controls ────────────────────────────────────────────────

		/// <summary>
		/// Builds one row per rebindable binding in the Player action map.
		/// </summary>
		/// <remarks>
		/// There was no keybinding UI at all before this: <c>PlayerInputController</c> already had
		/// both halves of the persistence — <c>SaveBindingOverrides</c> and
		/// <c>LoadBindingOverrides</c>, reading and writing the <c>InputBindingOverrides</c>
		/// configuration key — and nothing anywhere could produce an override for them to carry.
		/// <para>
		/// Composite parts get their own row (Move / Up, Move / Down, …) because that is where the
		/// keys a player actually wants to change live. The composite header itself is not a
		/// binding and is skipped.
		/// </para>
		/// </remarks>
		private void InitializeControlsSection()
		{
			if (controlsList == null)
			{
				Log.Error("UITKOptions", "Controls container is missing.");
				return;
			}

			controlsList.Clear();
			SetControlsStatus(string.Empty);

			InputActionMap map = ResolvePlayerActionMap();
			if (map == null)
			{
				/* Controls are created on world entry. Opening the options panel from the login
				 * screen is legitimate, and silently showing an empty section there would read as
				 * a broken panel — so say why instead. Deliberately does NOT create the controls:
				 * doing that here would enable the Player action map on the login screen. */
				SetControlsStatus("Key bindings are available once you are in the world.");
				return;
			}

			foreach (InputAction action in map.actions)
			{
				for (int i = 0; i < action.bindings.Count; ++i)
				{
					InputBinding binding = action.bindings[i];
					if (binding.isComposite)
					{
						// A header, not a binding. Its parts follow and get their own rows.
						continue;
					}
					if (IsNonRebindable(binding))
					{
						continue;
					}

					controlsList.Add(BuildBindingRow(action, i, binding));
				}
			}

			RefreshConflictHighlighting();
		}

		/// <summary>
		/// True for bindings that cannot meaningfully be rebound to a key.
		/// </summary>
		/// <remarks>
		/// Pointer position and delta are continuously changing analogue controls; an interactive
		/// rebind would capture one the instant the mouse moved. They are also not something a
		/// player rebinds.
		/// </remarks>
		private static bool IsNonRebindable(InputBinding binding)
		{
			string path = binding.effectivePath;
			if (string.IsNullOrEmpty(path))
			{
				// An unbound binding is still worth a row — that is the row you use to bind it.
				return false;
			}
			return path.EndsWith("/position") || path.EndsWith("/delta");
		}

		/// <summary>
		/// Resolves the editable action map, or null when input has not been created yet.
		/// </summary>
		private static InputActionMap ResolvePlayerActionMap()
		{
			PlayerControls controls = PlayerInputController.Controls;
			if (controls == null || controls.asset == null)
			{
				return null;
			}
			return controls.asset.FindActionMap(PlayerActionMapName, throwIfNotFound: false);
		}

		/// <summary>
		/// Builds the row for a single binding: its name, its key, and a per-binding reset.
		/// </summary>
		private VisualElement BuildBindingRow(InputAction action, int bindingIndex, InputBinding binding)
		{
			VisualElement row = new VisualElement();
			row.AddToClassList("options-row");

			string caption = binding.isPartOfComposite && !string.IsNullOrEmpty(binding.name)
				? $"{action.name} / {binding.name}"
				: action.name;

			Label label = new Label(caption);
			label.AddToClassList("fish-label");
			label.AddToClassList("options-row-label");
			row.Add(label);

			Button bindButton = new Button { name = $"bind-{action.name}-{bindingIndex}" };
			bindButton.AddToClassList("fish-button");
			bindButton.AddToClassList("options-bind-btn");
			bindButton.text = DisplayStringFor(action, bindingIndex);

			InputAction capturedAction = action;
			int capturedIndex = bindingIndex;
			bindButton.clicked += () => BeginRebind(capturedAction, capturedIndex, bindButton);
			row.Add(bindButton);

			Button resetButton = new Button { text = "↺" };
			resetButton.AddToClassList("fish-button");
			resetButton.AddToClassList("options-bind-reset");
			resetButton.clicked += () =>
			{
				capturedAction.RemoveBindingOverride(capturedIndex);
				bindButton.text = DisplayStringFor(capturedAction, capturedIndex);
				PersistBindings();
				RefreshConflictHighlighting();
			};
			row.Add(resetButton);

			return row;
		}

		/// <summary>
		/// Human-readable name of the key a binding currently resolves to.
		/// </summary>
		private static string DisplayStringFor(InputAction action, int bindingIndex)
		{
			string display = action.GetBindingDisplayString(bindingIndex);
			return string.IsNullOrEmpty(display) ? "Unbound" : display;
		}

		/// <summary>
		/// Starts listening for the key that should drive a binding.
		/// </summary>
		/// <remarks>
		/// The action is disabled for the duration. An enabled action fires its callbacks while
		/// the rebind is capturing, so pressing a key to bind it also performs whatever it is
		/// currently bound to — pressing I to rebind something opens the inventory over the
		/// options panel.
		/// </remarks>
		private void BeginRebind(InputAction action, int bindingIndex, Button bindButton)
		{
			if (activeRebind != null)
			{
				// A second press on the listening row cancels; anywhere else is ignored.
				CancelActiveRebind();
				if (ReferenceEquals(bindButton, activeRebindButton))
				{
					return;
				}
			}

			activeRebindButton = bindButton;
			bindButton.AddToClassList("options-bind-btn--listening");
			bindButton.text = "Press a key…";
			SetControlsStatus("Listening. Press a key, or wait to cancel.");

			bool wasEnabled = action.enabled;
			action.Disable();

			InputActionRebindingExtensions.RebindingOperation operation = action.PerformInteractiveRebinding(bindingIndex)
				.WithTimeout(RebindTimeoutSeconds);

			for (int i = 0; i < ExcludedRebindControls.Length; ++i)
			{
				operation = operation.WithControlsExcluding(ExcludedRebindControls[i]);
			}

			operation
				.OnCancel(op => FinishRebind(action, bindingIndex, bindButton, wasEnabled, op, canceled: true))
				.OnComplete(op => FinishRebind(action, bindingIndex, bindButton, wasEnabled, op, canceled: false))
				.Start();

			activeRebind = operation;
		}

		/// <summary>
		/// Completes or cancels a rebind, applies conflict detection, and restores the row.
		/// </summary>
		private void FinishRebind(InputAction action, int bindingIndex, Button bindButton,
			bool wasEnabled, InputActionRebindingExtensions.RebindingOperation operation, bool canceled)
		{
			activeRebind = null;
			activeRebindButton = null;

			bindButton.RemoveFromClassList("options-bind-btn--listening");

			if (!canceled)
			{
				/* Conflict detection runs after the override is applied, because the effective
				 * path is what has to be compared and that is only known once it is. A collision
				 * puts the previous binding straight back — a rebind that silently leaves two
				 * actions on one key produces a client where one of them appears to have stopped
				 * working. */
				if (TryFindConflict(action, bindingIndex, out string conflictName))
				{
					action.RemoveBindingOverride(bindingIndex);
					SetControlsStatus($"That key is already used by {conflictName}.");
				}
				else
				{
					SetControlsStatus(string.Empty);
					PersistBindings();
				}
			}
			else
			{
				SetControlsStatus("Rebinding cancelled.");
			}

			bindButton.text = DisplayStringFor(action, bindingIndex);

			operation.Dispose();
			if (wasEnabled)
			{
				action.Enable();
			}

			RefreshConflictHighlighting();
		}

		/// <summary>
		/// Cancels an in-progress rebind, if there is one.
		/// </summary>
		private void CancelActiveRebind()
		{
			if (activeRebind == null)
			{
				return;
			}

			InputActionRebindingExtensions.RebindingOperation operation = activeRebind;
			activeRebind = null;

			// Cancel raises OnCancel, which does the restoration and the Dispose.
			operation.Cancel();
		}

		/// <summary>
		/// Looks for another binding in the Player map resolving to the same control.
		/// </summary>
		/// <param name="action">The action that was just rebound.</param>
		/// <param name="bindingIndex">The binding within it.</param>
		/// <param name="conflictName">Name of the action already using that control.</param>
		/// <returns>True when the new binding collides with an existing one.</returns>
		private static bool TryFindConflict(InputAction action, int bindingIndex, out string conflictName)
		{
			conflictName = null;

			InputActionMap map = action.actionMap;
			if (map == null)
			{
				return false;
			}

			string path = action.bindings[bindingIndex].effectivePath;
			if (string.IsNullOrEmpty(path))
			{
				return false;
			}

			foreach (InputAction other in map.actions)
			{
				for (int i = 0; i < other.bindings.Count; ++i)
				{
					if (ReferenceEquals(other, action) && i == bindingIndex)
					{
						continue;
					}

					InputBinding binding = other.bindings[i];
					if (binding.isComposite)
					{
						continue;
					}
					if (!string.Equals(binding.effectivePath, path, System.StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					conflictName = binding.isPartOfComposite && !string.IsNullOrEmpty(binding.name)
						? $"{other.name} / {binding.name}"
						: other.name;
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Marks every binding button that shares its key with another one.
		/// </summary>
		/// <remarks>
		/// A rebind cannot introduce a conflict — it is rejected — but a set of bindings shipped
		/// with the asset, or restored from a configuration file written by an older build, can
		/// already contain one. Highlighting them is the only way a player can find out which.
		/// </remarks>
		private void RefreshConflictHighlighting()
		{
			if (controlsList == null)
			{
				return;
			}

			InputActionMap map = ResolvePlayerActionMap();
			if (map == null)
			{
				return;
			}

			foreach (InputAction action in map.actions)
			{
				for (int i = 0; i < action.bindings.Count; ++i)
				{
					if (action.bindings[i].isComposite || IsNonRebindable(action.bindings[i]))
					{
						continue;
					}

					Button button = controlsList.Q<Button>($"bind-{action.name}-{i}");
					if (button == null)
					{
						continue;
					}
					button.EnableInClassList("options-bind-btn--conflict", TryFindConflict(action, i, out _));
				}
			}
		}

		/// <summary>
		/// Clears every keybinding override and rebuilds the section.
		/// </summary>
		private void ResetAllBindings()
		{
			PlayerControls controls = PlayerInputController.Controls;
			if (controls == null || controls.asset == null)
			{
				return;
			}

			CancelActiveRebind();
			controls.asset.RemoveAllBindingOverrides();
			PersistBindings();
			InitializeControlsSection();
			SetControlsStatus("Key bindings restored to defaults.");
		}

		/// <summary>
		/// Writes the current binding overrides into the configuration and schedules a save.
		/// </summary>
		private void PersistBindings()
		{
			PlayerInputController.SaveBindingOverrides();
			RequestSave();
		}

		/// <summary>Writes the Controls section's status line.</summary>
		private void SetControlsStatus(string text)
		{
			if (controlsStatus != null)
			{
				controlsStatus.text = text;
			}
		}
	}
}
