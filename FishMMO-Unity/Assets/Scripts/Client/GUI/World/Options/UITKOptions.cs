using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// The client's settings panel: five tabs — Display, Audio, Gameplay, Key Bindings and UI —
	/// each built in code and bound directly to <see cref="Configuration.GlobalSettings"/> through
	/// <see cref="ClientSettings"/>.
	/// </summary>
	/// <remarks>
	/// <para><b>Controls are generated, not authored.</b> Every row that belongs to a list — audio
	/// channels, gameplay toggles, theme colours, key bindings — is built from the table that
	/// defines it rather than laid out in the UXML. A configuration key cannot then be lost by
	/// editing a scene, which is how all five gameplay toggles were lost once already.</para>
	///
	/// <para><b>The panel applies nothing at start-up.</b> It used to be the only code that applied
	/// VSync, brightness and the frame-rate cap — from its own <c>OnStarting</c>, which does not
	/// run until the panel is first opened, and the panel ships closed. A player who capped their
	/// frame rate got the bootstrap default every session until they visited the menu. Those
	/// settings are now applied by <see cref="ClientSettingsBootstrap"/> during boot, and this
	/// panel only reads them back to position its controls and writes them when the player changes
	/// one.</para>
	///
	/// <para><b>Display settings are staged, not live.</b> Every other setting here can be undone
	/// by the control that set it. A display mode cannot: pick one the monitor will not show and
	/// the player cannot see the panel that would put it back. So the three display dropdowns write
	/// to a pending selection, Apply commits it, and Apply arms a countdown that restores the
	/// previous mode unless the player presses Keep. Nothing is written to the configuration file
	/// until Keep, so a mode that blacked out the screen is not still there after a restart — and
	/// closing the panel while a mode is unconfirmed puts the old one back immediately rather than
	/// leaving the player waiting out a countdown with no prompt on screen.</para>
	///
	/// <para><b>Writes to disk are debounced.</b> <see cref="Configuration.Save"/> serialises and
	/// rewrites the whole file; a slider bound straight to it rewrites the file once per frame for
	/// as long as it is held. Every write here goes through <see cref="ClientSettings"/>, which
	/// coalesces them, and the panel flushes on close.</para>
	/// </remarks>
	public class UITKOptions : UITKControl
	{
		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Settings;

		/// <summary>The panel's tabs, in the order they appear.</summary>
		private enum OptionsTab
		{
			/// <summary>Resolution, refresh rate, fullscreen, quality, brightness, frame rate, VSync.</summary>
			Display = 0,
			/// <summary>Per-channel volumes and audio behaviour.</summary>
			Audio = 1,
			/// <summary>Toggles that change what the game shows or accepts.</summary>
			Gameplay = 2,
			/// <summary>Every rebindable binding in the Player action map.</summary>
			Controls = 3,
			/// <summary>Window layout, interface scale, theme colours and shareable profiles.</summary>
			Interface = 4,
		}

		// ── Element names ───────────────────────────────────────────

		/// <summary>Names of the tab buttons, indexed by <see cref="OptionsTab"/>.</summary>
		private static readonly string[] TabButtonNames =
		{
			"options-tab-display",
			"options-tab-audio",
			"options-tab-gameplay",
			"options-tab-controls",
			"options-tab-interface",
		};

		/// <summary>Names of the tab pages, indexed by <see cref="OptionsTab"/>.</summary>
		private static readonly string[] TabPageNames =
		{
			"options-page-display",
			"options-page-audio",
			"options-page-gameplay",
			"options-page-controls",
			"options-page-interface",
		};

		/// <summary>USS class that removes a tab page from layout.</summary>
		private const string PAGE_HIDDEN_CLASS = "options-page--hidden";
		/// <summary>USS class marking the selected tab.</summary>
		private const string TAB_ACTIVE_CLASS = "fish-tab--active";

		private const string VSYNC_TOGGLE_NAME = "vsync-toggle";
		private const string BRIGHTNESS_SLIDER_NAME = "brightness-slider";
		private const string BRIGHTNESS_VALUE_NAME = "brightness-value";
		private const string LOOK_SENSITIVITY_SLIDER_NAME = "look-sensitivity-slider";
		private const string LOOK_SENSITIVITY_VALUE_NAME = "look-sensitivity-value";
		private const string RESOLUTION_DROPDOWN_NAME = "resolution-dropdown";
		private const string REFRESHRATE_DROPDOWN_NAME = "refreshrate-dropdown";
		private const string FULLSCREEN_DROPDOWN_NAME = "fullscreen-dropdown";
		private const string QUALITY_DROPDOWN_NAME = "quality-dropdown";
		private const string FRAMERATE_DROPDOWN_NAME = "framerate-dropdown";
		private const string GRAPHICS_HINT_NAME = "options-graphics-hint";
		private const string CLOSE_BUTTON_NAME = "options-close-btn";
		private const string AUDIO_LIST_NAME = "options-audio-list";
		private const string AUDIO_MUTE_TOGGLE_NAME = "audio-mute-unfocused-toggle";
		private const string RESET_AUDIO_NAME = "options-reset-audio-btn";
		private const string GAMEPLAY_LIST_NAME = "options-gameplay-list";
		private const string COLOR_LIST_NAME = "options-color-list";
		private const string RESET_COLORS_NAME = "options-reset-colors-btn";
		private const string CONTROLS_LIST_NAME = "options-controls-list";
		private const string CONTROLS_STATUS_NAME = "options-controls-status";
		private const string RESET_CONTROLS_NAME = "options-reset-controls-btn";
		private const string SCREEN_APPLY_NAME = "options-screen-apply-btn";
		private const string SCREEN_REVERT_NAME = "options-screen-revert-btn";
		private const string SCREEN_KEEP_NAME = "options-screen-keep-btn";
		private const string SCREEN_STATUS_NAME = "options-screen-status";
		private const string SNAP_SLIDER_NAME = "ui-snap-slider";
		private const string SNAP_VALUE_NAME = "ui-snap-value";
		private const string UI_SCALE_SLIDER_NAME = "ui-scale-slider";
		private const string UI_SCALE_VALUE_NAME = "ui-scale-value";
		private const string RESET_LAYOUT_NAME = "options-reset-layout-btn";
		private const string PROFILE_DROPDOWN_NAME = "ui-profile-dropdown";
		private const string PROFILE_LOAD_NAME = "ui-profile-load-btn";
		private const string PROFILE_SAVE_NAME = "ui-profile-save-btn";
		private const string PROFILE_DELETE_NAME = "ui-profile-delete-btn";
		private const string PROFILE_STATUS_NAME = "ui-profile-status";
		private const string PROFILE_PATH_NAME = "ui-profile-path";

		// ── Timings ─────────────────────────────────────────────────

		/// <summary>Seconds of quiet before a pending theme reload is applied.</summary>
		/// <remarks>
		/// Much shorter than the configuration write debounce. A theme reload is visible, so it has
		/// to keep up with the colour picker to be usable; it just must not run on every one of the
		/// sixty change callbacks a second a dragged slider produces.
		/// </remarks>
		private const float ThemeDebounceSeconds = 0.12f;

		/// <summary>Seconds before an applied display mode is put back automatically.</summary>
		/// <remarks>
		/// Long enough to read the prompt and reach the mouse, short enough that a player staring
		/// at a black screen is not doing so for long.
		/// </remarks>
		private const float DisplayRevertSeconds = 12.0f;

		/// <summary>Seconds an interactive rebind listens before giving up.</summary>
		/// <remarks>
		/// A backstop, not the primary way out. The Input System suppresses the events it matches,
		/// so a rebind that could neither be completed nor cancelled would eat the player's input
		/// for as long as it listened. Escape cancels it and a second press on the listening row
		/// cancels it; this covers the case where a device stops reporting entirely.
		/// </remarks>
		private const float RebindTimeoutSeconds = 10.0f;

		/// <summary>Name of the action map whose bindings are editable.</summary>
		private const string PlayerActionMapName = "Player";

		// ── Tables ──────────────────────────────────────────────────

		/// <summary>
		/// Controls that an interactive rebind must never select.
		/// </summary>
		/// <remarks>
		/// <para><b>Escape</b> and <b>Backspace</b> are the rebind prompt's own two controls, and
		/// neither can be bound to anything. Escape cancels the rebind and leaves the binding as it
		/// was; Backspace clears the binding, leaving the row unbound. Both have to be unbindable
		/// for that to be true everywhere — a prompt whose cancel key means something else on one
		/// row out of forty is worse than having no cancel key at all. Escape is additionally the
		/// client's universal "close this": bound to something else, a player who opens a panel
		/// they cannot otherwise dismiss has no way out.</para>
		///
		/// <para>Both stay reachable per row through the row's own reset button, which restores the
		/// binding the game shipped with — that is how an action moved off Escape gets back.</para>
		///
		/// <para><b>The left mouse button</b> is what operates this panel, and excluding it is not
		/// cosmetic. <c>PerformInteractiveRebinding</c> enables event suppression by default: an
		/// event that matches a candidate is consumed and never reaches the UI. With the left
		/// button eligible, the first click after starting a rebind — the click on the listening
		/// row that is supposed to cancel it, the click on Close, a click on any other row — was
		/// swallowed and bound to the action instead. The advertised "press the row again to
		/// cancel" could therefore never work, and the usual result of trying was an action bound
		/// to the mouse button the player uses to click things. The other mouse buttons stay
		/// bindable, because those are genuinely useful for gameplay.</para>
		///
		/// <para><b>Pointer position, delta and scroll</b> are continuously changing analogue
		/// controls; a rebind listening for "any control that actuated" picks one of them the
		/// instant the mouse moves.</para>
		///
		/// <para><b>The keyboard's synthetic <c>anyKey</c></b> is what made Backspace unusable, and
		/// the reason is worth writing down because nothing about it is visible from this file. The
		/// Input System offers <c>anyKey</c> as a real, bindable control that actuates whenever any
		/// key does; it is a <c>ButtonControl</c>, so it survives the "expected control type"
		/// filter, and excluding <c>backspace</c> does not exclude it. Pressing Backspace during a
		/// rebind therefore left exactly one eligible candidate — <c>anyKey</c> — and two things
		/// followed. The rebind completed and bound the row to "Any Key", which is not a key
		/// anybody wants an action on. And because a candidate was found,
		/// <c>RebindingOperation</c> marked the event handled, which stops the device state from
		/// being updated at all: <see cref="PollRebindKeys"/>'s
		/// <c>backspaceKey.wasPressedThisFrame</c> never became true, so the clear the panel
		/// advertises in two places could not fire. Excluding it costs nothing — an action bound to
		/// "any key" fires on every keystroke — and restores both.</para>
		/// </remarks>
		private static readonly string[] ExcludedRebindControls =
		{
			"<Keyboard>/escape",
			"<Keyboard>/backspace",
			"<Keyboard>/anyKey",

			"<Mouse>/leftButton",
			"<Pointer>/press",

			"<Pointer>/position",
			"<Pointer>/delta",
			"<Mouse>/position",
			"<Mouse>/delta",
			"<Mouse>/scroll",
		};

		/// <summary>
		/// Sets of actions that are allowed to share a control with one another.
		/// </summary>
		/// <remarks>
		/// Escape drives three actions at once by design — <c>Cancel</c> interrupts a cast,
		/// <c>CloseLastUI</c> closes the top panel and <c>Menu</c> opens the menu when neither of
		/// the first two had anything to do. That is a deliberate chain, not a mistake, and flagging
		/// it as a conflict would mark three rows red in a fresh install and teach the player to
		/// ignore the warning that matters.
		/// <para>
		/// The exemption covers the <b>shipped</b> bindings only — see
		/// <see cref="SharesBindingByDesign"/>. Duplicates the player creates are never allowed,
		/// and that has to hold for these three actions as well: two of them dragged onto the same
		/// key by hand is an ordinary collision, not the designed chain.
		/// </para>
		/// <para>
		/// Membership is by action name because that is what survives a rebind: the control changes,
		/// the relationship between the actions does not.
		/// </para>
		/// </remarks>
		private static readonly string[][] SharedBindingGroups =
		{
			new[] { "Cancel", "CloseLastUI", "Menu" },
		};

		/// <summary>
		/// Player-facing labels for the themeable colours, indexed alongside
		/// <see cref="UITKTheme.ColorNames"/>.
		/// </summary>
		/// <remarks>
		/// Each label names the thing the colour actually paints, which is decided by
		/// <see cref="UITKThemeManager"/> and not by the colour's internal name. Three of these
		/// used to be wrong in a way the player could only discover by experiment: "Panel
		/// Background" was <c>Primary</c>, which paints the header and footer bars, while the
		/// panel body is <c>Background</c> — so the two obvious choices each changed the other
		/// one's surface.
		/// </remarks>
		private static readonly string[] ColorLabels =
		{
			"Header & Footer",
			"Slot Surface",
			"Accent & Active Tab",
			"Panel Background",
			"Text",
			"Health Bar",
			"Mana Bar",
			"Stamina Bar",
			"Crosshair",
			"Tooltip Text",
		};

		// ── Tab state ───────────────────────────────────────────────

		/// <summary>
		/// The tab shown when the panel is next opened.
		/// </summary>
		/// <remarks>
		/// Static, and deliberately not persisted. It has to outlive the visual tree — hiding the
		/// panel disables its UIDocument and the tree is cloned afresh on the next open, so an
		/// instance field on a rebuilt page would not survive — but which tab a player was last
		/// looking at is not a setting, and writing it to Configuration.cfg would put a line in a
		/// shared settings file every time somebody clicked a tab.
		/// </remarks>
		private static OptionsTab activeTab = OptionsTab.Display;

		/// <summary>Tab buttons, indexed by <see cref="OptionsTab"/>.</summary>
		/// <remarks>
		/// Sized from the name table rather than from a literal, so adding a tab is one entry in
		/// each of the two name arrays and nothing else. A literal length silently truncates the
		/// new tab instead.
		/// </remarks>
		private readonly Button[] tabButtons = new Button[TabButtonNames.Length];

		/// <summary>Tab pages, indexed by <see cref="OptionsTab"/>.</summary>
		private readonly VisualElement[] tabPages = new VisualElement[TabPageNames.Length];

		// ── Display controls ────────────────────────────────────────

		private Toggle vsyncToggle;
		private Slider brightnessSlider;
		private Slider lookSensitivitySlider;
		private Label lookSensitivityValueLabel;
		private Label brightnessValueLabel;
		private DropdownField resolutionDropdown;
		private DropdownField refreshRateDropdown;
		private DropdownField fullscreenDropdown;
		private DropdownField qualityDropdown;
		private DropdownField frameRateDropdown;
		private Label graphicsHint;
		private Button screenApplyButton;
		private Button screenRevertButton;
		private Button screenKeepButton;
		private Label screenStatus;

		// ── Audio controls ──────────────────────────────────────────

		private VisualElement audioList;
		private Toggle muteUnfocusedToggle;

		// ── Other page containers ───────────────────────────────────

		private Button closeButton;
		private VisualElement gameplayList;
		private VisualElement colorList;
		private Button resetColorsButton;
		private VisualElement controlsList;
		private Label controlsStatus;
		private Slider snapSlider;
		private Label snapValueLabel;
		private Slider uiScaleSlider;
		private Label uiScaleValueLabel;
		private DropdownField profileDropdown;
		private Label profileStatus;

		/// <summary>Swatch element for each colour row, indexed alongside UITKTheme.ColorNames.</summary>
		private readonly VisualElement[] colorSwatches = new VisualElement[UITKTheme.ColorNames.Length];

		// ── Display staging ─────────────────────────────────────────

		/// <summary>Distinct screen resolutions, smallest first.</summary>
		private List<Vector2Int> resolutionOptions = new List<Vector2Int>();

		/// <summary>Refresh rates available at the currently selected resolution.</summary>
		private List<RefreshRate> refreshRateOptions = new List<RefreshRate>();

		/// <summary>Fullscreen modes offered on this platform, parallel to the dropdown entries.</summary>
		/// <remarks>
		/// The dropdown's index is not the <see cref="FullScreenMode"/> value: the list is built
		/// conditionally per platform, so on a build without exclusive fullscreen the second entry
		/// is <c>MaximizedWindow</c>. The stored key is always the enum value.
		/// </remarks>
		private List<FullScreenMode> fullscreenOptions = new List<FullScreenMode>();

		/// <summary>The frame-rate caps currently offered, ascending.</summary>
		private List<int> frameRateOptions = new List<int>();

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

		// ── Debounced work ──────────────────────────────────────────

		/// <summary>True when a theme reload is due.</summary>
		private bool themeReloadPending;
		/// <summary>Unscaled time at which a pending theme reload runs.</summary>
		private float themeReloadDeadline;

		// ── Rebinding ───────────────────────────────────────────────

		/*
		 * Change callbacks for the controls whose Initialize* method can run more than once
		 * against the SAME element.
		 *
		 * Most of this panel's controls are safe without this: their Initialize* method runs only
		 * from OnStarting, and OnStarting only ever re-runs against a freshly cloned tree, so each
		 * element is bound exactly once in its life. Four are not — the frame rate dropdown is
		 * rebuilt by Keep, the mute toggle by Reset Audio, and both interface sliders by loading a
		 * UI profile — and RegisterValueChangedCallback appends rather than replaces, so each of
		 * those runs would have added another handler to a live element. The visible result is a
		 * setting that is written twice, then three times, then four; for the interface scale,
		 * where the handler also snaps and writes the value back, it is a feedback loop.
		 *
		 * Detach() is what removes the previous one, and it null-checks. That is not defensive
		 * tidiness: this comment used to claim that "unregistering a callback that was never
		 * registered is a no-op, so the first pass is correct without a null check of its own",
		 * and that is simply false. UnregisterValueChangedCallback(null) throws ArgumentException
		 * — so the FIRST call of InitializeFrameRateLimit, on the first ever open of this panel,
		 * threw out of OnStarting. Everything after it never ran: VSync, audio, gameplay,
		 * interface, colours, profiles, key bindings, and every button binding at the end of
		 * OnStarting, Close included. The panel opened as a half-built window whose controls did
		 * nothing.
		 *
		 * It survived two readings of this file because it is invisible on the page — the claim
		 * reads as true. It was found by rendering the panel with its component actually run.
		 */

		/// <summary>Change handler currently attached to the frame rate dropdown.</summary>
		private EventCallback<ChangeEvent<string>> frameRateChanged;
		/// <summary>Change handler currently attached to the unfocused-mute toggle.</summary>
		private EventCallback<ChangeEvent<bool>> muteUnfocusedChanged;
		/// <summary>Change handler currently attached to the interface scale slider.</summary>
		private EventCallback<ChangeEvent<float>> uiScaleChanged;
		/// <summary>Change handler currently attached to the snap grid slider.</summary>
		private EventCallback<ChangeEvent<float>> snapGridChanged;

		/// <summary>The rebind currently listening for input, or null.</summary>
		private InputActionRebindingExtensions.RebindingOperation activeRebind;
		/// <summary>The button that started the active rebind, so it can be restored.</summary>
		private Button activeRebindButton;

		/// <summary>The action the active rebind is targeting, or null.</summary>
		/// <remarks>
		/// Held alongside the operation because Backspace clears the binding rather than rebinding
		/// it, and the clear happens outside the operation's own callbacks — see
		/// <see cref="ClearActiveBinding"/>. The operation exposes no way to ask which binding it
		/// was started for.
		/// </remarks>
		private InputAction activeRebindAction;

		/// <summary>The binding index the active rebind is targeting.</summary>
		private int activeRebindIndex = -1;

		/// <summary>
		/// Resolves every control, builds every generated list, and binds the callbacks.
		/// </summary>
		public override void OnStarting()
		{
			/* The boot phase normally has this loaded long before the panel exists. Kept as a
			 * fallback for the UI validation scenes, which run a panel with no bootstrap system. */
			ClientSettings.EnsureLoaded();

			if (Root == null)
			{
				return;
			}

			ResolveTabs();

			vsyncToggle = Root.Q<Toggle>(VSYNC_TOGGLE_NAME);
			brightnessSlider = Root.Q<Slider>(BRIGHTNESS_SLIDER_NAME);
			brightnessValueLabel = Root.Q<Label>(BRIGHTNESS_VALUE_NAME);
			lookSensitivitySlider = Root.Q<Slider>(LOOK_SENSITIVITY_SLIDER_NAME);
			lookSensitivityValueLabel = Root.Q<Label>(LOOK_SENSITIVITY_VALUE_NAME);
			resolutionDropdown = Root.Q<DropdownField>(RESOLUTION_DROPDOWN_NAME);
			refreshRateDropdown = Root.Q<DropdownField>(REFRESHRATE_DROPDOWN_NAME);
			fullscreenDropdown = Root.Q<DropdownField>(FULLSCREEN_DROPDOWN_NAME);
			qualityDropdown = Root.Q<DropdownField>(QUALITY_DROPDOWN_NAME);
			frameRateDropdown = Root.Q<DropdownField>(FRAMERATE_DROPDOWN_NAME);
			graphicsHint = Root.Q<Label>(GRAPHICS_HINT_NAME);
			screenApplyButton = Root.Q<Button>(SCREEN_APPLY_NAME);
			screenRevertButton = Root.Q<Button>(SCREEN_REVERT_NAME);
			screenKeepButton = Root.Q<Button>(SCREEN_KEEP_NAME);
			screenStatus = Root.Q<Label>(SCREEN_STATUS_NAME);

			audioList = Root.Q<VisualElement>(AUDIO_LIST_NAME);
			muteUnfocusedToggle = Root.Q<Toggle>(AUDIO_MUTE_TOGGLE_NAME);

			closeButton = Root.Q<Button>(CLOSE_BUTTON_NAME);
			gameplayList = Root.Q<VisualElement>(GAMEPLAY_LIST_NAME);
			colorList = Root.Q<VisualElement>(COLOR_LIST_NAME);
			resetColorsButton = Root.Q<Button>(RESET_COLORS_NAME);
			controlsList = Root.Q<VisualElement>(CONTROLS_LIST_NAME);
			controlsStatus = Root.Q<Label>(CONTROLS_STATUS_NAME);
			snapSlider = Root.Q<Slider>(SNAP_SLIDER_NAME);
			snapValueLabel = Root.Q<Label>(SNAP_VALUE_NAME);
			uiScaleSlider = Root.Q<Slider>(UI_SCALE_SLIDER_NAME);
			uiScaleValueLabel = Root.Q<Label>(UI_SCALE_VALUE_NAME);
			profileDropdown = Root.Q<DropdownField>(PROFILE_DROPDOWN_NAME);
			profileStatus = Root.Q<Label>(PROFILE_STATUS_NAME);

			InitializeDisplaySettings();
			InitializeQualityLevel();
			InitializeBrightness();
			InitializeLookSensitivity();
			InitializeFrameRateLimit();
			InitializeVSync();
			InitializeAudioSettings();
			InitializeGameplayToggles();
			InitializeInterfaceSettings();
			InitializeColorSettings();
			InitializeProfileSection();
			InitializeControlsSection();

			/* Buttons are wired here rather than inside the Initialize* methods above, because
			 * several of those rebuild their rows and are called again — ResetAllBindings calls
			 * InitializeControlsSection, ApplyProfile calls InitializeProfileSection — so a
			 * subscription made inside one would stack another handler onto the same button every
			 * time. The button ELEMENTS are replaced whenever the tree is, which is what makes a
			 * single += per OnStarting correct. */
			Bind(closeButton, Hide);
			Bind(screenApplyButton, OnScreenApply);
			Bind(screenRevertButton, OnScreenRevert);
			Bind(screenKeepButton, OnScreenKeep);
			Bind(Root.Q<Button>(RESET_AUDIO_NAME), ResetAudio);
			Bind(resetColorsButton, ResetColors);
			Bind(Root.Q<Button>(RESET_CONTROLS_NAME), ResetAllBindings);
			Bind(Root.Q<Button>(RESET_LAYOUT_NAME), ResetPanelPositions);
			Bind(Root.Q<Button>(PROFILE_LOAD_NAME), OnProfileLoad);
			Bind(Root.Q<Button>(PROFILE_SAVE_NAME), OnProfileSave);
			Bind(Root.Q<Button>(PROFILE_DELETE_NAME), OnProfileDelete);

			SelectTab(activeTab);
		}

		/// <summary>Attaches a click handler when the button exists.</summary>
		private static void Bind(Button button, System.Action handler)
		{
			if (button != null)
			{
				button.clicked += handler;
			}
		}

		/// <summary>
		/// Removes a change handler from a control, if one is attached.
		/// </summary>
		/// <typeparam name="T">The control's value type.</typeparam>
		/// <param name="control">The control to detach from. May be null.</param>
		/// <param name="handler">The handler to remove, or null when none has been attached yet.</param>
		/// <remarks>
		/// The null check is the whole point. <c>UnregisterValueChangedCallback(null)</c> throws
		/// <c>ArgumentException</c> rather than doing nothing, so passing an unset handler field —
		/// which is exactly what the first pass over each of these controls does — took the whole
		/// of <see cref="OnStarting"/> with it.
		/// </remarks>
		private static void Detach<T>(INotifyValueChanged<T> control, EventCallback<ChangeEvent<T>> handler)
		{
			if (control != null && handler != null)
			{
				control.UnregisterValueChangedCallback(handler);
			}
		}

		/// <summary>
		/// Re-applies state after the visual tree has been rebuilt.
		/// </summary>
		/// <remarks>
		/// The panel starts hidden, so its tree is cloned afresh on every open and every element
		/// cached above belongs to the previous one. <c>OnStarting</c> re-runs from
		/// <c>ReinitializeIfTreeReplaced</c> and rebuilds every page; this only has to put the
		/// display prompt back into whatever state the countdown is in, because that state lives
		/// across an open and close.
		/// </remarks>
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			RefreshDisplayPrompt();
		}

		/// <summary>
		/// Brings the panel up to date with anything that changed while it was closed.
		/// </summary>
		/// <remarks>
		/// The bindings and the profile list are both editable from outside this panel — a key
		/// binding can be reset by a corrupt-override recovery, a profile file can be added to the
		/// folder by hand or dropped in by another player — so both are re-read on every open
		/// rather than only when the tree is rebuilt.
		/// </remarks>
		protected override void OnAfterShow()
		{
			RefreshDisplayPrompt();
			RefreshProfileList();
			InitializeControlsSection();
			RefreshSwatches();
		}

		/// <summary>
		/// Flushes anything pending, cancels an in-progress rebind, and settles an unconfirmed
		/// display mode when the panel closes.
		/// </summary>
		/// <remarks>
		/// A rebind left listening after the panel is gone would capture the next key the player
		/// pressed — in the world, with no prompt on screen — and assign it to whichever action the
		/// row belonged to.
		/// <para>
		/// An unconfirmed display mode is reverted rather than left to time out. The Keep button is
		/// the only thing that can confirm it and closing the panel takes that button away, so
		/// waiting out the countdown means sitting in a mode the player cannot confirm and cannot
		/// see the prompt for. Reverting immediately is the same outcome, sooner and visibly.
		/// </para>
		/// </remarks>
		public override void Hide(bool overrideIsAlwaysOpen)
		{
			if (!overrideIsAlwaysOpen)
			{
				CancelActiveRebind();

				if (displayRevertArmed)
				{
					RevertDisplaySettings();
				}

				FlushThemeReload();
				ClientSettings.Flush();
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
			ClientSettings.Flush();
		}

		/// <summary>
		/// True while a rebind is listening, so Escape cancels it instead of closing this panel.
		/// </summary>
		/// <remarks>
		/// Escape is not suppressed by <c>RebindingOperation</c> — see <see cref="BeginRebind"/> —
		/// so without this the press reaches <c>Player/CloseLastUI</c> and takes the settings
		/// window down with the rebind it was meant to cancel. <see cref="UIManager.CloseNext"/>
		/// consults this before closing anything, and the flag is still set at that point because
		/// the cancel happens in <see cref="PollRebindKeys"/>, which runs in Update — after the
		/// action callbacks that ask the question.
		/// <para>
		/// That ordering is the Input System's <c>ProcessEventsInDynamicUpdate</c>, its default and
		/// what this project uses: events, and so action callbacks, are processed ahead of
		/// <c>MonoBehaviour.Update</c>. Anyone changing <c>InputSettings.updateMode</c> should
		/// re-check this — under a mode that runs actions after Update the rebind would already
		/// have been cancelled by the time the question is asked, and Escape would close the panel
		/// again.
		/// </para>
		/// </remarks>
		public override bool ConsumesEscape => activeRebind != null;

		/// <summary>
		/// Drives the debounced theme reload and the display auto-revert countdown.
		/// </summary>
		protected override void OnTick()
		{
			PollRebindKeys();

			float now = Time.unscaledTime;

			if (themeReloadPending && now >= themeReloadDeadline)
			{
				FlushThemeReload();
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

		// ── Tabs ────────────────────────────────────────────────────

		/// <summary>Resolves the tab buttons and pages and wires the strip.</summary>
		private void ResolveTabs()
		{
			for (int i = 0; i < TabButtonNames.Length; ++i)
			{
				OptionsTab tab = (OptionsTab)i;

				tabButtons[i] = Root.Q<Button>(TabButtonNames[i]);
				tabPages[i] = Root.Q<VisualElement>(TabPageNames[i]);

				if (tabButtons[i] == null)
				{
					Log.Error("UITKOptions", $"Tab button '{TabButtonNames[i]}' is missing from the UXML.");
					continue;
				}
				if (tabPages[i] == null)
				{
					Log.Error("UITKOptions", $"Tab page '{TabPageNames[i]}' is missing from the UXML.");
				}

				// Captured by value; the loop variable would otherwise be shared by every handler.
				OptionsTab captured = tab;
				tabButtons[i].clicked += () => SelectTab(captured);
			}
		}

		/// <summary>
		/// Shows one page and hides the rest.
		/// </summary>
		/// <param name="tab">The tab to select.</param>
		/// <remarks>
		/// A rebind in progress belongs to the page it was started from, and leaving it listening
		/// while the player looks at a different tab means the next key they press is captured with
		/// no prompt anywhere on screen.
		/// </remarks>
		private void SelectTab(OptionsTab tab)
		{
			if (activeTab != tab)
			{
				CancelActiveRebind();
			}

			activeTab = tab;

			for (int i = 0; i < tabPages.Length; ++i)
			{
				bool selected = i == (int)tab;

				if (tabPages[i] != null)
				{
					tabPages[i].EnableInClassList(PAGE_HIDDEN_CLASS, !selected);
				}
				if (tabButtons[i] != null)
				{
					tabButtons[i].EnableInClassList(TAB_ACTIVE_CLASS, selected);
				}
			}
		}

		// ── Theme reload debounce ───────────────────────────────────

		/// <summary>Marks the theme as needing a reload once the player stops adjusting a colour.</summary>
		private void RequestThemeReload()
		{
			themeReloadPending = true;
			themeReloadDeadline = Time.unscaledTime + ThemeDebounceSeconds;
		}

		/// <summary>Applies a pending theme reload.</summary>
		private void FlushThemeReload()
		{
			if (!themeReloadPending)
			{
				return;
			}
			themeReloadPending = false;

			/* Reload rather than poking the one colour: the manager owns which USS classes a colour
			 * maps onto, and re-reading configuration keeps this panel from having to duplicate
			 * that mapping. */
			UITKThemeManager.Reload();
			RefreshSwatches();
		}

		// ── Display ─────────────────────────────────────────────────

		/// <summary>
		/// Populates the three display dropdowns and records the mode currently in force.
		/// </summary>
		/// <remarks>
		/// Nothing here applies a mode. The saved mode is applied during the client's boot phase by
		/// <see cref="ClientDisplaySettings"/>; this only has to describe what is already in effect.
		/// </remarks>
		private void InitializeDisplaySettings()
		{
			resolutionOptions = ClientDisplaySettings.BuildResolutionOptions();
			fullscreenOptions = ClientDisplaySettings.BuildFullscreenOptions();

			committedResolution = ResolveCurrentResolution();
			committedFullscreen = ResolveCurrentFullscreen();

			refreshRateOptions = ClientDisplaySettings.BuildRefreshRateOptions(committedResolution);
			committedRefreshRate = ResolveCurrentRefreshRate();

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
					labels.Add(DescribeFullscreen(fullscreenOptions[i]));
				}
				fullscreenDropdown.choices = labels;
				fullscreenDropdown.index = Mathf.Max(0, fullscreenOptions.IndexOf(committedFullscreen));
			}

			RefreshDisplayPrompt();
		}

		/// <summary>A player-facing name for a fullscreen mode.</summary>
		/// <remarks>
		/// The enum's own names leak Unity's vocabulary into the settings menu —
		/// "FullScreenWindow" is not what a player calls borderless fullscreen, and
		/// "MaximizedWindow" is indistinguishable from "Windowed" to anyone who has not read the
		/// documentation.
		/// </remarks>
		private static string DescribeFullscreen(FullScreenMode mode)
		{
			switch (mode)
			{
				case FullScreenMode.ExclusiveFullScreen: return "Fullscreen (Exclusive)";
				case FullScreenMode.FullScreenWindow:    return "Fullscreen (Borderless)";
				case FullScreenMode.MaximizedWindow:     return "Maximised Window";
				case FullScreenMode.Windowed:            return "Windowed";
				default:                                 return mode.ToString();
			}
		}

		/// <summary>
		/// The resolution the dropdown should start on: the one actually in force, or the saved one.
		/// </summary>
		/// <remarks>
		/// The live window size is preferred over the saved value because the saved value may never
		/// have been applied — a mode the display no longer offers is refused at boot, and showing
		/// the refused value would tell the player they are running at a resolution they are not.
		/// </remarks>
		private Vector2Int ResolveCurrentResolution()
		{
			Vector2Int current = new Vector2Int(Screen.width, Screen.height);
			if (resolutionOptions.Contains(current))
			{
				return current;
			}

			Vector2Int saved = new Vector2Int(
				ClientSettings.GetInt(ClientSettings.ResolutionWidthKey, 0),
				ClientSettings.GetInt(ClientSettings.ResolutionHeightKey, 0));
			if (resolutionOptions.Contains(saved))
			{
				return saved;
			}

			// The largest supported mode is a better last resort than the smallest.
			return resolutionOptions[resolutionOptions.Count - 1];
		}

		/// <summary>The refresh rate currently in force, or the fastest the resolution offers.</summary>
		private RefreshRate ResolveCurrentRefreshRate()
		{
			float currentHz = ClientDisplaySettings.ToHz(Screen.currentResolution.refreshRateRatio);
			for (int i = 0; i < refreshRateOptions.Count; ++i)
			{
				if (Mathf.Approximately(ClientDisplaySettings.ToHz(refreshRateOptions[i]), currentHz))
				{
					return refreshRateOptions[i];
				}
			}
			return refreshRateOptions[refreshRateOptions.Count - 1];
		}

		/// <summary>The fullscreen mode currently in force, if this platform offers it.</summary>
		private FullScreenMode ResolveCurrentFullscreen()
		{
			FullScreenMode current = Screen.fullScreenMode;
			return fullscreenOptions.Contains(current) ? current : fullscreenOptions[0];
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
				labels.Add($"{ClientDisplaySettings.ToHz(refreshRateOptions[i]):F0} Hz");
			}
			return labels;
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
				if (Mathf.Approximately(ClientDisplaySettings.ToHz(refreshRateOptions[i]),
					ClientDisplaySettings.ToHz(rate)))
				{
					return i;
				}
			}
			return refreshRateOptions.Count - 1;
		}

		/// <summary>Rebuilds the refresh-rate list when a different resolution is selected.</summary>
		private void OnResolutionSelectionChanged(ChangeEvent<string> evt)
		{
			if (resolutionDropdown == null || refreshRateDropdown == null)
			{
				return;
			}

			int index = Mathf.Clamp(resolutionDropdown.index, 0, resolutionOptions.Count - 1);
			RefreshRate previous = SelectedRefreshRate();

			refreshRateOptions = ClientDisplaySettings.BuildRefreshRateOptions(resolutionOptions[index]);
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

		/// <summary>Applies the staged display selection and arms the auto-revert countdown.</summary>
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

			ClientSettings.Set(ClientSettings.ResolutionWidthKey, committedResolution.x);
			ClientSettings.Set(ClientSettings.ResolutionHeightKey, committedResolution.y);
			ClientSettings.Set(ClientSettings.RefreshRateKey,
				Mathf.RoundToInt(ClientDisplaySettings.ToHz(committedRefreshRate)));
			ClientSettings.Set(ClientSettings.FullscreenKey, (int)committedFullscreen);

			SetScreenStatus("Display settings saved.");
			RefreshDisplayPrompt();

			/* The frame-rate ceiling is the display's fastest mode, so a mode change can make the
			 * player's saved cap unreachable — or newly reachable. Rebuilt here rather than left
			 * until the panel is next opened, when the dropdown would still be offering caps this
			 * display can no longer present. */
			InitializeFrameRateLimit();
		}

		/// <summary>Puts the previous display mode back, from the Revert button.</summary>
		private void OnScreenRevert()
		{
			RevertDisplaySettings();
			SetScreenStatus("Display settings restored.");
		}

		/// <summary>Restores the last committed display mode and resets the dropdowns to match.</summary>
		private void RevertDisplaySettings()
		{
			displayRevertArmed = false;

			ApplyDisplayMode(committedResolution, committedRefreshRate, committedFullscreen);

			if (resolutionDropdown != null)
			{
				resolutionDropdown.SetValueWithoutNotify($"{committedResolution.x} x {committedResolution.y}");
			}

			refreshRateOptions = ClientDisplaySettings.BuildRefreshRateOptions(committedResolution);
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
		/// Pushes a display mode to the screen.
		/// </summary>
		/// <remarks>
		/// One call sets resolution, mode and refresh rate together. Setting them one at a time
		/// means each call reads back whatever the previous one left, and they undo one another.
		/// <para>
		/// The render cap is deliberately NOT derived from the display refresh rate here. Display
		/// refresh rate and render frame rate are separate settings with separate controls, and
		/// deriving one from the other capped every player at their monitor's rate — so a 144 Hz
		/// owner could never reach the 500 FPS the game supports.
		/// </para>
		/// </remarks>
		private static void ApplyDisplayMode(Vector2Int size, RefreshRate rate, FullScreenMode mode)
		{
#if !UNITY_WEBGL
			Screen.SetResolution(size.x, size.y, mode, rate);
#endif
			/* WebGL is excluded deliberately: the browser drives presentation through
			 * requestAnimationFrame and owns the canvas size. */
		}

		/// <summary>Shows the Apply button or the Keep/Revert prompt, whichever the state calls for.</summary>
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
				!string.IsNullOrEmpty(screenStatus.text) &&
				screenStatus.text.StartsWith("Keep", System.StringComparison.Ordinal))
			{
				// The countdown text is the one message that must not outlive the countdown.
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

		// ── Quality, brightness, frame rate, VSync ──────────────────

		/// <summary>
		/// Binds the quality dropdown to the levels this build ships with.
		/// </summary>
		/// <remarks>
		/// Stored by name, not by index. Quality levels can be reordered or inserted between builds
		/// and an index saved against the old order silently selects a different level — which
		/// looks like the setting having been forgotten, except the player is now running at a
		/// quality they did not choose.
		/// </remarks>
		private void InitializeQualityLevel()
		{
			if (qualityDropdown == null)
			{
				return;
			}

			string[] names = QualitySettings.names;
			if (names == null || names.Length == 0)
			{
				qualityDropdown.SetEnabled(false);
				return;
			}

			qualityDropdown.choices = new List<string>(names);
			qualityDropdown.index = Mathf.Clamp(QualitySettings.GetQualityLevel(), 0, names.Length - 1);

			qualityDropdown.RegisterValueChangedCallback((evt) =>
			{
				int index = qualityDropdown.index;
				if (index < 0 || index >= names.Length)
				{
					return;
				}

				/* applyExpensiveChanges: true, unlike the boot-time apply. The player asked for
				 * this one and is looking at the result, so the texture and render-target reload
				 * is what they are waiting for rather than an unexplained stall during startup.
				 *
				 * Through ClientDisplaySettings rather than QualitySettings directly: it restores
				 * the player's VSync preference over the level's own authored value, and it is
				 * where the editor safeguard lives that stops a play-mode session rewriting the
				 * checked-in QualitySettings asset. */
				ClientDisplaySettings.ApplyQualityLevel(index, true);

				// Re-seed the toggle so it cannot disagree with what was just applied.
				if (vsyncToggle != null)
				{
					vsyncToggle.SetValueWithoutNotify(QualitySettings.vSyncCount > 0);
				}

				ClientSettings.SetString(ClientSettings.QualityLevelKey, names[index]);
				RefreshGraphicsHint();
			});
		}

		/// <summary>
		/// Binds the brightness slider to its saved value.
		/// </summary>
		/// <remarks>
		/// The stored value is clamped on the way in. It is a float in a text file, so it can be
		/// anything at all — and it is fed to <c>RenderSettings.ambientLight</c>, where a large
		/// value blows out the whole scene and a negative one crushes it to black, with the slider
		/// unable to represent either and so unable to put it back.
		/// </remarks>
		private void InitializeBrightness()
		{
			if (brightnessSlider == null)
			{
				Log.Error("UITKOptions", "Brightness slider is missing.");
				return;
			}

			brightnessSlider.lowValue = ClientDisplaySettings.MinimumBrightness;
			brightnessSlider.highValue = ClientDisplaySettings.MaximumBrightness;

			float brightness = ClientSettings.GetFloat(
				ClientSettings.BrightnessKey,
				ClientDisplaySettings.DefaultBrightness,
				ClientDisplaySettings.MinimumBrightness,
				ClientDisplaySettings.MaximumBrightness);

			/* SetValueWithoutNotify, because assigning `value` raises the change callback — and the
			 * callback writes to the configuration and requests a disk write. Seeding a control
			 * from the file it writes back to is how a settings panel rewrites the whole file every
			 * time it is opened. */
			brightnessSlider.SetValueWithoutNotify(brightness);
			UpdatePercentLabel(brightnessValueLabel, brightness);

			brightnessSlider.RegisterValueChangedCallback((evt) =>
			{
				float value = Mathf.Clamp01(evt.newValue);
				ClientSettings.Set(ClientSettings.BrightnessKey, value);
				ClientDisplaySettings.ApplyBrightness(value);
				UpdatePercentLabel(brightnessValueLabel, value);
			});
		}

		/// <summary>
		/// Binds the look sensitivity slider.
		/// </summary>
		/// <remarks>
		/// The stored value is clamped on the way in, for the same reason brightness is: it is a
		/// float in a file the player can edit, and it multiplies raw mouse delta. A large one makes
		/// the view unusable at exactly the moment they would need to reach this menu to undo it,
		/// and zero makes the camera immovable.
		/// </remarks>
		private void InitializeLookSensitivity()
		{
			if (lookSensitivitySlider == null)
			{
				Log.Error("UITKOptions", "Look sensitivity slider is missing.");
				return;
			}

			lookSensitivitySlider.lowValue = ClientCameraSettings.MinimumLookSensitivity;
			lookSensitivitySlider.highValue = ClientCameraSettings.MaximumLookSensitivity;

			float sensitivity = ClientSettings.GetFloat(
				ClientSettings.LookSensitivityKey,
				ClientCameraSettings.DefaultLookSensitivity,
				ClientCameraSettings.MinimumLookSensitivity,
				ClientCameraSettings.MaximumLookSensitivity);

			// Without notify: assigning `value` raises the callback, which writes back to the file
			// this was just read from. See InitializeBrightness.
			lookSensitivitySlider.SetValueWithoutNotify(sensitivity);
			UpdateSensitivityLabel(lookSensitivityValueLabel, sensitivity);

			lookSensitivitySlider.RegisterValueChangedCallback((evt) =>
			{
				float value = Mathf.Clamp(
					evt.newValue,
					ClientCameraSettings.MinimumLookSensitivity,
					ClientCameraSettings.MaximumLookSensitivity);

				ClientSettings.Set(ClientSettings.LookSensitivityKey, value);
				ClientCameraSettings.ApplyLookSensitivity(value);
				UpdateSensitivityLabel(lookSensitivityValueLabel, value);
			});
		}

		/// <summary>
		/// Writes a sensitivity multiplier into its value label.
		/// </summary>
		/// <remarks>
		/// A multiplier rather than a percentage, because that is what it is: the camera turns this
		/// many times as far per unit of mouse movement. Showing "100%" would read as a ceiling.
		/// </remarks>
		private static void UpdateSensitivityLabel(Label label, float value)
		{
			if (label != null)
			{
				label.text = value.ToString("0.00");
			}
		}

		/// <summary>
		/// Binds the frame rate limit dropdown.
		/// </summary>
		/// <remarks>
		/// Rebuilt from scratch whenever it is called, because the choices depend on the display's
		/// fastest mode and on the live network tick rate — both of which can change while the
		/// client is running.
		/// </remarks>
		private void InitializeFrameRateLimit()
		{
			if (frameRateDropdown == null)
			{
				Log.Error("UITKOptions", "Frame rate dropdown is missing.");
				return;
			}

			frameRateOptions = ClientDisplaySettings.BuildFrameRateChoices();

			List<string> labels = new List<string>(frameRateOptions.Count);
			for (int i = 0; i < frameRateOptions.Count; ++i)
			{
				labels.Add(frameRateOptions[i] + " FPS");
			}

			/* Detached BEFORE the choices and the index are rewritten, and that order is the whole
			 * point. Assigning `index` assigns `value`, which raises a change event — and this
			 * method is called again, on the SAME element, from OnScreenKeep. With the previous
			 * handler still attached, confirming a display mode fired it and wrote the re-resolved
			 * fallback back to the configuration file as though the player had chosen it: a saved
			 * 240 FPS cap became a permanent 60 after one session on a 60 Hz screen, and nothing
			 * on screen suggested the preference had been overwritten. */
			Detach(frameRateDropdown, frameRateChanged);

			frameRateDropdown.choices = labels;

			int saved = ClientDisplaySettings.ResolveSavedFrameRate(frameRateOptions);
			frameRateDropdown.index = Mathf.Max(0, frameRateOptions.IndexOf(saved));

			frameRateChanged = (evt) =>
			{
				int index = frameRateDropdown.index;
				if (index < 0 || index >= frameRateOptions.Count)
				{
					return;
				}

				int selected = frameRateOptions[index];

				/* Through ClientSettings, which schedules the write. Writing straight into the
				 * store left the choice in memory only: it applied for the session and was gone
				 * the next time the client started, which reads exactly like a cap that does not
				 * save. */
				ClientSettings.Set(ClientSettings.FrameRateKey, selected);
				Client.ApplyTargetFrameRate(selected);
				RefreshGraphicsHint();
			};
			frameRateDropdown.RegisterValueChangedCallback(frameRateChanged);

			RefreshGraphicsHint();
		}

		/// <summary>Binds the VSync toggle to its saved value.</summary>
		private void InitializeVSync()
		{
			if (vsyncToggle == null)
			{
				Log.Error("UITKOptions", "VSync toggle is missing.");
				return;
			}

			vsyncToggle.SetValueWithoutNotify(ClientSettings.GetBool(ClientSettings.VSyncKey, false));

			vsyncToggle.RegisterValueChangedCallback((evt) =>
			{
				ClientSettings.Set(ClientSettings.VSyncKey, evt.newValue);
				ClientDisplaySettings.ApplyVSync(evt.newValue);
				RefreshGraphicsHint();
			});
		}

		/// <summary>
		/// Explains the interaction between VSync and the frame-rate cap.
		/// </summary>
		/// <remarks>
		/// <c>Application.targetFrameRate</c> is ignored outright while vSync is on, so the cap
		/// dropdown above is inert in that state. Without saying so, a player who turns VSync on
		/// and then finds their 60 FPS cap doing nothing has no way to discover why.
		/// </remarks>
		private void RefreshGraphicsHint()
		{
			if (graphicsHint == null)
			{
				return;
			}

			graphicsHint.text = QualitySettings.vSyncCount > 0
				? "VSync is on, so the frame rate limit is ignored — the display's refresh rate sets the pace."
				: string.Empty;
		}

		// ── Audio ───────────────────────────────────────────────────

		/// <summary>
		/// Builds one row per playable audio channel and binds each to its configuration key.
		/// </summary>
		/// <remarks>
		/// Generated from <see cref="ClientAudioSettings.PlayableChannels"/> — the channels
		/// something in the client actually plays through — and not from the whole
		/// <see cref="AudioChannel"/> enum. Today that is Master alone; the other five levels
		/// persist and apply correctly but have no consumer, so a slider for them would save its
		/// value and change nothing audible. Listing a channel there is what makes its row appear.
		/// </remarks>
		private void InitializeAudioSettings()
		{
			if (audioList == null)
			{
				Log.Error("UITKOptions", "Audio settings container is missing.");
			}
			else
			{
				audioList.Clear();

				for (int i = 0; i < ClientAudioSettings.PlayableChannels.Length; ++i)
				{
					audioList.Add(BuildAudioRow(ClientAudioSettings.PlayableChannels[i]));
				}
			}

			if (muteUnfocusedToggle != null)
			{
				// Detached before re-seeding, for the reason InitializeInterfaceSettings documents.
				Detach(muteUnfocusedToggle, muteUnfocusedChanged);

				muteUnfocusedToggle.SetValueWithoutNotify(ClientAudioSettings.MuteWhenUnfocused);

				muteUnfocusedChanged = (evt) => ClientAudioSettings.MuteWhenUnfocused = evt.newValue;
				muteUnfocusedToggle.RegisterValueChangedCallback(muteUnfocusedChanged);
			}
		}

		/// <summary>Builds the row for a single audio channel.</summary>
		private VisualElement BuildAudioRow(AudioChannel channel)
		{
			VisualElement row = new VisualElement();
			row.AddToClassList("options-row");

			Label caption = new Label(ClientAudioSettings.LabelFor(channel));
			caption.AddToClassList("fish-label");
			caption.AddToClassList("options-row-label");
			row.Add(caption);

			Slider slider = new Slider { name = $"audio-{channel}", lowValue = 0.0f, highValue = 1.0f };
			slider.AddToClassList("options-row-field");
			slider.SetValueWithoutNotify(ClientAudioSettings.GetVolume(channel));
			row.Add(slider);

			Label value = new Label();
			value.AddToClassList("fish-hint");
			value.AddToClassList("options-audio-value");
			UpdatePercentLabel(value, ClientAudioSettings.GetVolume(channel));
			row.Add(value);

			AudioChannel captured = channel;
			slider.RegisterValueChangedCallback((evt) =>
			{
				ClientAudioSettings.SetVolume(captured, evt.newValue);
				UpdatePercentLabel(value, evt.newValue);
			});

			return row;
		}

		/// <summary>Restores every audio channel to its default and re-seeds the rows.</summary>
		private void ResetAudio()
		{
			ClientAudioSettings.ResetToDefaults();
			InitializeAudioSettings();
		}

		/// <summary>Writes a 0..1 value into a label as a whole-number percentage.</summary>
		private static void UpdatePercentLabel(Label label, float value)
		{
			if (label == null)
			{
				return;
			}
			label.text = $"{Mathf.RoundToInt(Mathf.Clamp01(value) * 100.0f)}%";
		}

		// ── Gameplay ────────────────────────────────────────────────

		/// <summary>
		/// Builds one row per gameplay toggle and binds each to its configuration key.
		/// </summary>
		private void InitializeGameplayToggles()
		{
			if (gameplayList == null)
			{
				Log.Error("UITKOptions", "Gameplay settings container is missing.");
				return;
			}

			gameplayList.Clear();

			/* The table lives in ClientSettings, not here. Its defaults have to agree with the
			 * code that acts on each setting, and a copy in the panel is how the display ends up
			 * disagreeing with the box the player is looking at. */
			for (int i = 0; i < ClientSettings.GameplayToggles.Length; ++i)
			{
				(string key, string label, bool _) = ClientSettings.GameplayToggles[i];

				VisualElement row = new VisualElement();
				row.AddToClassList("options-row");

				Label caption = new Label(label);
				caption.AddToClassList("fish-label");
				caption.AddToClassList("options-row-label");
				row.Add(caption);

				Toggle toggle = new Toggle { name = $"toggle-{key}" };
				toggle.AddToClassList("fish-toggle");
				toggle.AddToClassList("options-row-field");
				toggle.SetValueWithoutNotify(ClientSettings.GetGameplayToggle(key));

				// Captured by value; the loop variable would otherwise be shared by every handler.
				string capturedKey = key;
				toggle.RegisterValueChangedCallback((evt) =>
					ClientSettings.SetGameplayToggle(capturedKey, evt.newValue));

				row.Add(toggle);
				gameplayList.Add(row);
			}
		}

		// ── Interface ───────────────────────────────────────────────

		/// <summary>
		/// Binds the interface scale and the window snap grid.
		/// </summary>
		/// <remarks>
		/// Both settings describe the panels themselves rather than anything in the world. The
		/// layout reset is the only way back from an arrangement the player cannot fix by dragging
		/// — a panel left in a corner of a monitor they no longer have, most obviously — so it is
		/// deliberately a plain, always-available button rather than something that only appears
		/// when a saved layout exists.
		/// </remarks>
		private void InitializeInterfaceSettings()
		{
			if (uiScaleSlider != null)
			{
				/* Detached first. This method is called again on the same elements after a UI
				 * profile is loaded, and assigning lowValue/highValue re-clamps the slider's
				 * current value — which raises a change event through whatever handler is still
				 * attached. That handler writes the value back and rescales every panel, so
				 * re-seeding the control could act as though the player had dragged it. */
				Detach(uiScaleSlider, uiScaleChanged);

				uiScaleSlider.lowValue = ClientSettings.MinimumUIScale;
				uiScaleSlider.highValue = ClientSettings.MaximumUIScale;

				float scale = ClientSettings.UIScale;
				uiScaleSlider.SetValueWithoutNotify(scale);
				UpdateScaleLabel(scale);

				uiScaleChanged = (evt) =>
				{
					/* Rounded to the nearest 5%. The slider is continuous and the value is shown
					 * as a percentage, so without this the read-out jitters through 103%, 104%,
					 * 103% under the player's hand — and every one of those is a configuration
					 * write and a relayout of every panel on screen. */
					float snapped = Mathf.Round(evt.newValue * 20.0f) / 20.0f;

					ClientSettings.UIScale = snapped;
					UpdateScaleLabel(snapped);

					if (!Mathf.Approximately(snapped, evt.newValue))
					{
						uiScaleSlider.SetValueWithoutNotify(snapped);
					}
				};
				uiScaleSlider.RegisterValueChangedCallback(uiScaleChanged);
			}

			if (snapSlider != null)
			{
				// Detached first, for the reason above.
				Detach(snapSlider, snapGridChanged);

				snapSlider.lowValue = 0.0f;
				snapSlider.highValue = UITKPanelPositions.MaxSnapGridSize;

				snapSlider.SetValueWithoutNotify(UITKPanelPositions.SnapGridSize);
				UpdateSnapValueLabel(UITKPanelPositions.SnapGridSize);

				snapGridChanged = (evt) =>
				{
					/* Whole points. A grid of 6.37 is not an alignment aid, and the value is shown
					 * to the player as a number of points. */
					float snapped = Mathf.Round(evt.newValue);

					UITKPanelPositions.SnapGridSize = snapped;
					UpdateSnapValueLabel(snapped);

					if (!Mathf.Approximately(snapped, evt.newValue))
					{
						// Put the rounded value back under the handle so it cannot drift.
						snapSlider.SetValueWithoutNotify(snapped);
					}
				};
				snapSlider.RegisterValueChangedCallback(snapGridChanged);
			}
		}

		/// <summary>Writes the interface scale beside its slider.</summary>
		private void UpdateScaleLabel(float scale)
		{
			if (uiScaleValueLabel == null)
			{
				return;
			}
			uiScaleValueLabel.text = $"{Mathf.RoundToInt(scale * 100.0f)}%";
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

		/// <summary>Returns every panel to the position its stylesheet gives it.</summary>
		private void ResetPanelPositions()
		{
			UIManager.ResetAllPanelPositions();
			SetProfileStatus("Window positions reset.");
		}

		// ── Colours ─────────────────────────────────────────────────

		/// <summary>Builds one row per themeable colour, each opening the shared colour picker.</summary>
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

			RefreshSwatches();
		}

		/// <summary>
		/// Opens the shared colour picker for one themeable colour.
		/// </summary>
		/// <param name="name">One of <see cref="UITKTheme.ColorNames"/>.</param>
		/// <param name="index">Index of the colour, for updating its swatch.</param>
		/// <remarks>
		/// The picker reports every change as the player drags, which is once a frame. The swatch —
		/// the one piece of feedback that has to be immediate — is updated inline; the file write
		/// and the theme reload are debounced, because each one is a full file rewrite and a walk
		/// of every registered panel's visual tree respectively.
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
				ClientSettings.RequestSave();

				if (index >= 0 && index < colorSwatches.Length && colorSwatches[index] != null)
				{
					colorSwatches[index].style.backgroundColor = chosen;
					colorSwatches[index].EnableInClassList("options-swatch--unset", false);
				}

				RequestThemeReload();
			});
		}

		/// <summary>Clears every colour override, returning the UI to the stylesheet defaults.</summary>
		private void ResetColors()
		{
			for (int i = 0; i < UITKTheme.ColorNames.Length; ++i)
			{
				UITKTheme.Clear(Configuration.GlobalSettings, UITKTheme.ColorNames[i]);
			}
			ClientSettings.RequestSave();

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
				case "TooltipLabel": return theme.TooltipLabel;
				default:             return Color.white;
			}
		}

		/// <summary>Repaints every colour swatch from the theme currently in force.</summary>
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

		// ── UI profiles ─────────────────────────────────────────────

		/// <summary>Fills in the profile section's static parts.</summary>
		private void InitializeProfileSection()
		{
			Label path = Root.Q<Label>(PROFILE_PATH_NAME);
			if (path != null)
			{
				path.text = UIProfile.ProfileDirectory;
			}

			RefreshProfileList();
			SetProfileStatus(string.Empty);
		}

		/// <summary>Re-reads the profile folder and repopulates the dropdown.</summary>
		private void RefreshProfileList()
		{
			if (profileDropdown == null)
			{
				return;
			}

			string previous = profileDropdown.value;
			List<string> names = UIProfile.List();

			if (names.Count == 0)
			{
				/* A placeholder rather than an empty dropdown. An empty DropdownField renders as a
				 * blank box with no indication of whether it is loading, broken, or simply has
				 * nothing in it. */
				profileDropdown.choices = new List<string> { "(no saved profiles)" };
				profileDropdown.index = 0;
				profileDropdown.SetEnabled(false);
				return;
			}

			profileDropdown.SetEnabled(true);
			profileDropdown.choices = names;

			int index = previous != null ? names.IndexOf(previous) : -1;
			profileDropdown.index = index >= 0 ? index : 0;
		}

		/// <summary>The profile the dropdown is pointing at, or null when there is none.</summary>
		private string SelectedProfile()
		{
			if (profileDropdown == null || !profileDropdown.enabledSelf)
			{
				return null;
			}

			int index = profileDropdown.index;
			List<string> choices = profileDropdown.choices as List<string>;
			if (choices == null || index < 0 || index >= choices.Count)
			{
				return null;
			}
			return choices[index];
		}

		/// <summary>Applies the selected profile to the running client.</summary>
		private void OnProfileLoad()
		{
			string name = SelectedProfile();
			if (string.IsNullOrEmpty(name))
			{
				SetProfileStatus("There is no saved profile to load.");
				return;
			}

			if (!UIProfile.Load(name, out string error))
			{
				SetProfileStatus(error);
				return;
			}

			/* The panel's own controls describe values the profile has just replaced, so they are
			 * re-seeded from the new state rather than left showing the old one. */
			InitializeInterfaceSettings();
			InitializeColorSettings();

			SetProfileStatus($"Loaded '{name}'.");
		}

		/// <summary>Asks for a name, then writes the current interface to a profile.</summary>
		/// <remarks>
		/// Overwriting an existing profile is allowed but confirmed. The dialog offers no file
		/// browser, so the only way to discover that a name is taken is to be told.
		/// </remarks>
		private void OnProfileSave()
		{
			if (!UIManager.TryGetTK("UIDialogInputBox", out UITKDialogInputBox input))
			{
				SetProfileStatus("The name dialog is unavailable.");
				return;
			}

			input.Open("Name this UI profile:", (name) =>
			{
				if (!UIProfile.IsValidName(name, out string reason))
				{
					SetProfileStatus(reason);
					return;
				}

				if (UIProfile.Exists(name) && UIManager.TryGetTK("UIDialogBox", out UITKDialogBox confirm))
				{
					confirm.Open($"'{name}' already exists. Replace it?", () => WriteProfile(name));
					return;
				}

				WriteProfile(name);
			});
		}

		/// <summary>Writes a profile and reports the outcome.</summary>
		private void WriteProfile(string name)
		{
			if (!UIProfile.Save(name, out string error))
			{
				SetProfileStatus(error);
				return;
			}

			RefreshProfileList();

			if (profileDropdown != null && profileDropdown.enabledSelf)
			{
				List<string> choices = profileDropdown.choices as List<string>;
				int index = choices != null ? choices.IndexOf(name) : -1;
				if (index >= 0)
				{
					profileDropdown.index = index;
				}
			}

			SetProfileStatus($"Saved '{name}'.");
		}

		/// <summary>Deletes the selected profile, after confirmation.</summary>
		private void OnProfileDelete()
		{
			string name = SelectedProfile();
			if (string.IsNullOrEmpty(name))
			{
				SetProfileStatus("There is no saved profile to delete.");
				return;
			}

			/* Confirmed, because it deletes a file and there is no undo. Delete sits beside Load in
			 * the same row, which is exactly the arrangement a mis-aimed click punishes. */
			if (!UIManager.TryGetTK("UIDialogBox", out UITKDialogBox confirm))
			{
				SetProfileStatus("The confirmation dialog is unavailable.");
				return;
			}

			confirm.Open($"Delete the UI profile '{name}'?", () =>
			{
				if (!UIProfile.Delete(name, out string error))
				{
					SetProfileStatus(error);
					return;
				}

				RefreshProfileList();
				SetProfileStatus($"Deleted '{name}'.");
			});
		}

		/// <summary>Writes the profile section's status line.</summary>
		private void SetProfileStatus(string text)
		{
			if (profileStatus != null)
			{
				profileStatus.text = text ?? string.Empty;
			}
		}

		// ── Key bindings ────────────────────────────────────────────

		/// <summary>
		/// Builds one row per rebindable binding in the Player action map.
		/// </summary>
		/// <remarks>
		/// Composite parts get their own row (Move / Up, Move / Down, …) because that is where the
		/// keys a player actually wants to change live. The composite header itself is not a
		/// binding and is skipped.
		/// <para>
		/// The action map exists from the client's boot phase onwards, so this works on the login
		/// screen as well as in the world. The "not available yet" message below is a fallback for
		/// a scene that comes up without the boot phase having run.
		/// </para>
		/// </remarks>
		private void InitializeControlsSection()
		{
			if (controlsList == null)
			{
				Log.Error("UITKOptions", "Controls container is missing.");
				return;
			}

			/* A rebind in progress belongs to a row that is about to be destroyed. Left running, it
			 * would complete against a Button that is no longer in any visual tree — the override
			 * would be applied and saved correctly, but the row the player is looking at would go
			 * on showing the old key until something else redrew it. This method is called on every
			 * open and by Reset All Keys, so the case is routine rather than exotic. */
			CancelActiveRebind();

			controlsList.Clear();
			SetControlsStatus(string.Empty);

			InputActionMap map = ResolvePlayerActionMap();
			if (map == null)
			{
				SetControlsStatus("Key bindings are unavailable — the input system has not started.");
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
				/* An unbound binding is still worth a row — that is the row you use to bind it, and
				 * resolving a conflict can leave a binding deliberately unbound. */
				return false;
			}
			return path.EndsWith("/position") || path.EndsWith("/delta");
		}

		/// <summary>Resolves the editable action map, or null when input has not been created yet.</summary>
		private static InputActionMap ResolvePlayerActionMap()
		{
			/* Created on demand rather than reported missing. The boot phase normally builds it,
			 * but the panel is reachable from scenes that come up without one and an empty Key
			 * Bindings tab reads as a broken panel. Creating the asset does not enable any map, so
			 * this cannot make the world's input live from the login screen. */
			PlayerInputController.EnsureControlsCreated();

			PlayerControls controls = PlayerInputController.Controls;
			if (controls == null || controls.asset == null)
			{
				return null;
			}
			return controls.asset.FindActionMap(PlayerActionMapName, throwIfNotFound: false);
		}

		/// <summary>Builds the row for a single binding: its name, its key, and a per-binding reset.</summary>
		private VisualElement BuildBindingRow(InputAction action, int bindingIndex, InputBinding binding)
		{
			VisualElement row = new VisualElement();
			row.AddToClassList("options-row");

			Label label = new Label(DescribeBinding(action, binding));
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
				/* Cancelled first. Resetting a row while ANOTHER row is listening would otherwise
				 * leave that rebind running with its own row still marked as listening. */
				CancelActiveRebind();

				string previousOverride = capturedAction.bindings[capturedIndex].overridePath;
				capturedAction.RemoveBindingOverride(capturedIndex);

				string defaultKey = DisplayStringFor(capturedAction, capturedIndex);

				/* A reset is checked like any other change. The shipped key may be one the player
				 * has since given to something else, and a reset that reintroduced the duplicate
				 * would be a hole in "no duplicates" that is reached by pressing a button labelled
				 * "restore" — the last place a player would look for the cause. Reset All Keys is
				 * the unconditional way back: it restores every row at once, so the state it
				 * produces is the shipped one and cannot collide with itself. */
				if (TryFindConflict(capturedAction, capturedIndex, out string conflictName))
				{
					RejectDuplicate(capturedAction, capturedIndex, previousOverride, defaultKey, conflictName);
					return;
				}

				bindButton.text = defaultKey;
				PersistBindings();
				RefreshConflictHighlighting();
				SetControlsStatus($"{DescribeBinding(capturedAction, capturedAction.bindings[capturedIndex])} restored to {defaultKey}.");
			};
			row.Add(resetButton);

			return row;
		}

		/// <summary>
		/// The player-facing name of a binding.
		/// </summary>
		/// <remarks>
		/// Most actions have two bindings — one keyboard, one gamepad — and both get a row. Without
		/// the device in the caption the two rows are identical, and the player cannot tell which
		/// one they are about to change.
		/// </remarks>
		private static string DescribeBinding(InputAction action, InputBinding binding)
		{
			string name = binding.isPartOfComposite && !string.IsNullOrEmpty(binding.name)
				? $"{action.name} / {binding.name}"
				: action.name;

			return IsGamepadBinding(binding) ? name + "  (Gamepad)" : name;
		}

		/// <summary>
		/// Whether a binding belongs to a gamepad rather than to the keyboard and mouse.
		/// </summary>
		/// <param name="binding">The binding to classify.</param>
		/// <remarks>
		/// The control scheme group is preferred over the path because it survives the binding
		/// being unbound: a row left with no path — which resolving a conflict can produce — still
		/// knows which device it belongs to, and so still restricts what a rebind may capture. The
		/// path is the fallback for a binding authored without a group.
		/// </remarks>
		private static bool IsGamepadBinding(InputBinding binding)
		{
			string groups = binding.groups;
			if (!string.IsNullOrEmpty(groups))
			{
				return groups.IndexOf("Gamepad", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
					groups.IndexOf("Joystick", System.StringComparison.OrdinalIgnoreCase) >= 0;
			}

			string path = binding.effectivePath;
			return !string.IsNullOrEmpty(path) &&
				(path.StartsWith("<Gamepad>", System.StringComparison.Ordinal) ||
				 path.StartsWith("<Joystick>", System.StringComparison.Ordinal));
		}

		/// <summary>Human-readable name of the key a binding currently resolves to.</summary>
		private static string DisplayStringFor(InputAction action, int bindingIndex)
		{
			string display = action.GetBindingDisplayString(bindingIndex);
			return string.IsNullOrEmpty(display) ? "Unbound" : display;
		}

		/// <summary>
		/// Starts listening for the key that should drive a binding.
		/// </summary>
		/// <remarks>
		/// <para>The action is disabled for the duration. An enabled action fires its callbacks
		/// while the rebind is capturing, so pressing a key to bind it also performs whatever it is
		/// currently bound to.</para>
		/// <para>Candidates are restricted to the row's own device. Without that, rebinding the
		/// "(Gamepad)" row of an action with a key on the keyboard silently produced a keyboard
		/// binding sitting in the Gamepad control scheme: it worked, the row's caption still said
		/// Gamepad, and the duplicate was invisible to the player and to control-scheme
		/// switching.</para>
		/// </remarks>
		private void BeginRebind(InputAction action, int bindingIndex, Button bindButton)
		{
			if (activeRebind != null)
			{
				/* The listening button has to be read BEFORE cancelling, because cancelling raises
				 * OnCancel, which clears activeRebindButton to null. Comparing afterwards compared
				 * against null, never matched, and so a second press on the listening row cancelled
				 * the rebind and immediately started a new one — the row could not be switched off. */
				bool sameRow = ReferenceEquals(bindButton, activeRebindButton);

				CancelActiveRebind();

				if (sameRow)
				{
					// A second press on the listening row cancels; anywhere else starts a new one.
					return;
				}
			}

			/* The override in force before this attempt, so a rejected rebind can be undone
			 * exactly. Null means "no override, the asset's own binding"; an empty string means
			 * "deliberately unbound", which is a state resolving a conflict can produce — so this
			 * is distinguished with a null check and NOT with IsNullOrEmpty, which would silently
			 * turn an unbound row back into its shipped default. */
			string previousOverride = action.bindings[bindingIndex].overridePath;
			bool gamepadRow = IsGamepadBinding(action.bindings[bindingIndex]);

			bool wasEnabled = action.enabled;
			action.Disable();

			InputActionRebindingExtensions.RebindingOperation operation = null;
			try
			{
				operation = action.PerformInteractiveRebinding(bindingIndex)
					.WithTimeout(RebindTimeoutSeconds)

					/* No cancel control, deliberately — Escape is polled instead, in
					 * PollRebindKeys. An empty string clears the one PerformInteractiveRebinding
					 * arms by itself for actions whose expected control type is not "Button", so
					 * every row behaves the same way whatever it is bound to.
					 *
					 * The reason is that a cancel control is NOT suppressed. RebindingOperation
					 * only marks an event handled when some control in it became a candidate, and
					 * it breaks out of that loop the moment the cancel control matches — before
					 * anything sets the suppress flag. So Escape would cancel the rebind and then
					 * carry on into the game, where CloseLastUI is bound to it: one press both
					 * cancelled the rebind and closed the settings window the player was still
					 * using, directly contradicting the "Escape cancels" hint on the row.
					 *
					 * Polling puts the cancel in Update, which runs after the action callbacks
					 * have already fired — so the rebind is still live while UIManager decides
					 * what Escape meant, and UITKOptions.ConsumesEscape can claim it. */
					.WithCancelingThrough(string.Empty);

				for (int i = 0; i < ExcludedRebindControls.Length; ++i)
				{
					operation = operation.WithControlsExcluding(ExcludedRebindControls[i]);
				}

				// Repeated calls are OR-ed together: a candidate is accepted if any path matches.
				if (gamepadRow)
				{
					operation = operation
						.WithControlsHavingToMatchPath("<Gamepad>")
						.WithControlsHavingToMatchPath("<Joystick>");
				}
				else
				{
					operation = operation
						.WithControlsHavingToMatchPath("<Keyboard>")
						.WithControlsHavingToMatchPath("<Mouse>");
				}

				operation
					.OnCancel(op => FinishRebind(action, bindingIndex, bindButton, wasEnabled, previousOverride, op, canceled: true))
					.OnComplete(op => FinishRebind(action, bindingIndex, bindButton, wasEnabled, previousOverride, op, canceled: false));

				/* Armed BEFORE Start, so the callbacks above always observe consistent state. They
				 * clear these fields, and setting them afterwards would resurrect a finished
				 * operation as the "active" one if the rebind ever resolved inside Start. */
				activeRebind = operation;
				activeRebindButton = bindButton;
				activeRebindAction = action;
				activeRebindIndex = bindingIndex;

				bindButton.AddToClassList("options-bind-btn--listening");
				bindButton.text = "Press a key…";
				SetControlsStatus(gamepadRow
					? "Listening for a gamepad control.  Escape cancels  ·  Backspace clears this binding."
					: "Listening for a key.  Escape cancels  ·  Backspace clears this binding.");

				operation.Start();
			}
			catch (System.Exception ex)
			{
				/* Anything thrown while arming leaves the action disabled and the row marked as
				 * listening for the rest of the session — the action stops responding in the world
				 * and the only visible symptom is a settings row stuck on "Press a key…". Unwind
				 * completely instead. */
				Log.Error("UITKOptions", $"Could not start rebinding '{action.name}'.", ex);

				operation?.Dispose();
				ClearRebindContext();

				bindButton.RemoveFromClassList("options-bind-btn--listening");
				bindButton.text = DisplayStringFor(action, bindingIndex);
				SetControlsStatus("That binding could not be changed.");

				if (wasEnabled)
				{
					action.Enable();
				}
				return;
			}
		}

		/// <summary>
		/// Completes or cancels a rebind, applies conflict handling, and restores the row.
		/// </summary>
		/// <remarks>
		/// The operation is disposed and the action re-enabled up front, before anything that can
		/// take time. Resolving a conflict asks the player a question, and the answer arrives on a
		/// later frame — deferring the teardown behind that would leave the action disabled, and
		/// therefore dead in the world, for as long as the dialog was on screen.
		/// </remarks>
		private void FinishRebind(InputAction action, int bindingIndex, Button bindButton,
			bool wasEnabled, string previousOverride,
			InputActionRebindingExtensions.RebindingOperation operation, bool canceled)
		{
			ClearRebindContext();

			bindButton.RemoveFromClassList("options-bind-btn--listening");
			bindButton.text = DisplayStringFor(action, bindingIndex);

			operation.Dispose();
			if (wasEnabled)
			{
				action.Enable();
			}

			if (canceled)
			{
				SetControlsStatus("Rebinding cancelled.");
				RefreshConflictHighlighting();
				return;
			}

			string chosenKey = DisplayStringFor(action, bindingIndex);

			/* Conflict detection runs after the override is applied, because the effective path is
			 * what has to be compared and that is only known once it is. */
			if (!TryFindConflict(action, bindingIndex, out string conflictName))
			{
				PersistBindings();
				SetControlsStatus($"{DescribeBinding(action, action.bindings[bindingIndex])} is now {chosenKey}.");
				RefreshConflictHighlighting();
				return;
			}

			RejectDuplicate(action, bindingIndex, previousOverride, chosenKey, conflictName);
		}

		/// <summary>
		/// Undoes a change that would have put two bindings on the same control.
		/// </summary>
		/// <remarks>
		/// <para><b>Duplicates are not allowed.</b> Two actions on one key produces a client where
		/// one of them appears to have stopped working, with nothing on screen saying why — and the
		/// player who created it is the least likely person to suspect the settings screen. The
		/// attempt is undone rather than accepted-with-a-warning, so the state the panel shows is
		/// always a state the game can actually run.</para>
		///
		/// <para>The way through is Backspace, and the message says so. Clearing the binding that
		/// holds the key frees it; the rebind then succeeds. That is also how two keys are swapped,
		/// which is otherwise impossible when duplicates are refused.</para>
		///
		/// <para>What is restored is the override that was in force <em>before this attempt</em>,
		/// which is not the same as clearing the override: the row may well have been customised
		/// already, and dropping it back to the key the game shipped with would discard a rebind
		/// the player never asked to undo.</para>
		///
		/// <para>Shared by the two changes that can create a duplicate: finishing a rebind, and
		/// resetting a single row to its shipped key.</para>
		/// </remarks>
		private void RejectDuplicate(InputAction action, int bindingIndex, string previousOverride,
			string chosenKey, string conflictName)
		{
			RestoreOverride(action, bindingIndex, previousOverride);

			string rowName = DescribeBinding(action, action.bindings[bindingIndex]);

			/* Not persisted: nothing changed. Writing here would rewrite the settings file with
			 * identical content on every refused keypress. */
			InitializeControlsSection();
			SetControlsStatus($"{chosenKey} is already used by {conflictName}, so {rowName} was left as it was. " +
				$"Clear {conflictName} with Backspace to free that key.");
		}

		/// <summary>
		/// Puts a binding back to the override it had before a rebind.
		/// </summary>
		/// <param name="action">The action the binding belongs to.</param>
		/// <param name="bindingIndex">The binding within it.</param>
		/// <param name="previousOverride">
		/// The previous <see cref="InputBinding.overridePath"/>: null for "no override", an empty
		/// string for "deliberately unbound", or a control path.
		/// </param>
		/// <remarks>
		/// The null check is deliberate and is not <c>IsNullOrEmpty</c>. Removing the override and
		/// applying an empty one are different outcomes — the first restores the shipped default,
		/// the second restores an unbound row — and collapsing them would quietly rebind a binding
		/// the player had chosen to leave empty.
		/// </remarks>
		private static void RestoreOverride(InputAction action, int bindingIndex, string previousOverride)
		{
			if (previousOverride == null)
			{
				action.RemoveBindingOverride(bindingIndex);
				return;
			}
			action.ApplyBindingOverride(bindingIndex, previousOverride);
		}

		/// <summary>
		/// Watches for the clear key while a rebind is listening.
		/// </summary>
		/// <remarks>
		/// Polled rather than routed through the rebinding operation. Backspace is in
		/// <see cref="ExcludedRebindControls"/> — it has to be, or it would be bound like any other
		/// key — and an excluded control is never offered to the operation's callbacks at all, so
		/// there is nothing there to hook. Polling also makes the clear work identically on a
		/// gamepad row, where the operation only considers gamepad controls and would never see a
		/// keyboard key.
		/// <para>
		/// One <c>wasPressedThisFrame</c> read per frame, and only while something is listening.
		/// </para>
		/// </remarks>
		private void PollRebindKeys()
		{
			if (activeRebind == null)
			{
				return;
			}

			Keyboard keyboard = Keyboard.current;
			if (keyboard == null)
			{
				return;
			}

			/* Escape first: cancelling and clearing are mutually exclusive, and a player holding
			 * both wants out rather than an unbound row. */
			if (keyboard.escapeKey.wasPressedThisFrame)
			{
				CancelActiveRebind();
				return;
			}

			if (keyboard.backspaceKey.wasPressedThisFrame)
			{
				ClearActiveBinding();
			}
		}

		/// <summary>
		/// Unbinds the row that is currently listening, leaving it with no control at all.
		/// </summary>
		/// <remarks>
		/// This is the only way to free a key, and it exists because duplicates are refused: to
		/// give one action the key another already holds, the player clears the holder first and
		/// then binds. Without it, swapping two keys would be impossible.
		/// <para>
		/// An empty override path is the Input System's "deliberately unbound", which is distinct
		/// from having no override — the row keeps showing as Unbound rather than falling back to
		/// the key the game shipped with. The row's own reset button is what restores that.
		/// </para>
		/// </remarks>
		private void ClearActiveBinding()
		{
			InputAction action = activeRebindAction;
			int bindingIndex = activeRebindIndex;

			if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count)
			{
				return;
			}

			/* Cancelled first, which disposes the operation, re-enables the action and puts the row
			 * back. The override is applied afterwards so it cannot be undone by the teardown. */
			CancelActiveRebind();

			action.ApplyBindingOverride(bindingIndex, string.Empty);
			PersistBindings();

			string rowName = DescribeBinding(action, action.bindings[bindingIndex]);
			InitializeControlsSection();
			SetControlsStatus($"{rowName} is now unbound. Use its reset button to restore the default.");
		}

		/// <summary>Forgets which binding a rebind was targeting.</summary>
		private void ClearRebindContext()
		{
			activeRebind = null;
			activeRebindButton = null;
			activeRebindAction = null;
			activeRebindIndex = -1;
		}

		/// <summary>Cancels an in-progress rebind, if there is one.</summary>
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
		/// <param name="conflictName">Player-facing name of the colliding binding.</param>
		/// <returns>True when the binding collides with an existing one.</returns>
		/// <remarks>
		/// Scoped to the Player map, which is the map this panel lists and therefore the only one
		/// the player can act on. The UI map overlaps it by design — Enter drives both Chat and
		/// UI/Submit, and neither gets in the other's way because the client gates window and
		/// hotkey input on whether a text field has focus.
		/// </remarks>
		private static bool TryFindConflict(InputAction action, int bindingIndex, out string conflictName)
		{
			conflictName = null;

			InputActionMap map = action.actionMap;
			if (map == null)
			{
				return false;
			}

			InputBinding subject = action.bindings[bindingIndex];

			string path = subject.effectivePath;
			if (string.IsNullOrEmpty(path))
			{
				// An unbound binding collides with nothing, including other unbound bindings.
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
					if (SharesBindingByDesign(action.name, subject, other.name, binding))
					{
						continue;
					}

					conflictName = DescribeBinding(other, binding);
					return true;
				}
			}

			return false;
		}

		/// <summary>Whether a binding collides with another one, ignoring which.</summary>
		private static bool HasConflict(InputAction action, int bindingIndex)
		{
			return TryFindConflict(action, bindingIndex, out _);
		}

		/// <summary>
		/// Whether two bindings are allowed to share a control.
		/// </summary>
		/// <param name="first">Name of the first action.</param>
		/// <param name="firstBinding">The first binding.</param>
		/// <param name="second">Name of the second action.</param>
		/// <param name="secondBinding">The second binding.</param>
		/// <returns>
		/// True only when both are bindings the game shipped with, and both actions appear in the
		/// same <see cref="SharedBindingGroups"/> entry.
		/// </returns>
		/// <remarks>
		/// The override check is what keeps "duplicates are not allowed" true. The exemption exists
		/// for one authored arrangement — Escape driving Cancel, CloseLastUI and Menu — and applies
		/// only while those bindings are still the authored ones. The moment a player moves two of
		/// them onto a key of their own choosing, that is an ordinary collision: the chain those
		/// three form depends on the order the client subscribes to them, which is not something a
		/// settings screen should be silently extending to arbitrary keys.
		/// <para>
		/// <c>overridePath</c> is null when a binding has no override at all, and an empty string
		/// when it has been deliberately unbound — so the null test here is exact and
		/// <c>IsNullOrEmpty</c> would be wrong.
		/// </para>
		/// </remarks>
		private static bool SharesBindingByDesign(string first, InputBinding firstBinding,
			string second, InputBinding secondBinding)
		{
			if (firstBinding.overridePath != null || secondBinding.overridePath != null)
			{
				return false;
			}

			for (int g = 0; g < SharedBindingGroups.Length; ++g)
			{
				string[] group = SharedBindingGroups[g];

				bool hasFirst = false;
				bool hasSecond = false;
				for (int i = 0; i < group.Length; ++i)
				{
					hasFirst |= string.Equals(group[i], first, System.StringComparison.Ordinal);
					hasSecond |= string.Equals(group[i], second, System.StringComparison.Ordinal);
				}

				if (hasFirst && hasSecond)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Marks every binding button that shares its key with another one.
		/// </summary>
		/// <remarks>
		/// A rebind can no longer leave a conflict behind: one that would create a duplicate is
		/// undone. What this catches is state that arrived from somewhere else — a configuration
		/// file written by an older build, or hand-edited — which can already contain a duplicate.
		/// Highlighting them is the only way a player can find out which rows are affected.
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
					button.EnableInClassList("options-bind-btn--conflict", HasConflict(action, i));
				}
			}
		}

		/// <summary>Clears every keybinding override and rebuilds the section.</summary>
		/// <remarks>
		/// Confirmed, because it discards every rebind the player has ever made and there is no
		/// undo. It sits at the bottom of a list of forty rows whose own reset buttons undo exactly
		/// one, which is the arrangement a mis-aimed click punishes hardest.
		/// </remarks>
		private void ResetAllBindings()
		{
			PlayerControls controls = PlayerInputController.Controls;
			if (controls == null || controls.asset == null)
			{
				return;
			}

			/* Cancelled before the question is asked, not inside the answer. A rebind left
			 * listening behind the confirmation would go on capturing keys while the player reads
			 * it — so answering with the keyboard, or simply pressing a key, would be swallowed and
			 * bound to whichever row was still listening. */
			CancelActiveRebind();

			if (!UIManager.TryGetTK("UIDialogBox", out UITKDialogBox dialog))
			{
				/* No way to ask, so do not do it. Discarding every rebind the player has made is
				 * not something to do on the strength of one click and a missing dialog. */
				SetControlsStatus("The confirmation dialog is unavailable; nothing was changed.");
				return;
			}

			dialog.Open("Restore every key binding to its default?\n\nAny keys you have changed will be lost.",
				() =>
				{
					controls.asset.RemoveAllBindingOverrides();
					PersistBindings();
					InitializeControlsSection();
					SetControlsStatus("Key bindings restored to defaults.");
				});
		}

		/// <summary>Writes the current binding overrides into the configuration and schedules a save.</summary>
		private void PersistBindings()
		{
			PlayerInputController.SaveBindingOverrides();
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
