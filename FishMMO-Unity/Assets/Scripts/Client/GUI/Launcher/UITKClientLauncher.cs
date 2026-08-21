using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using HtmlAgilityPack;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// Renders the launcher through a UI Toolkit <see cref="UIDocument"/>.
	/// </summary>
	/// <remarks>
	/// Deliberately a plain <see cref="MonoBehaviour"/> rather than a <see cref="UITKControl"/>,
	/// matching <see cref="ClientLauncher"/> itself, which is not a <c>UIControl</c>.
	/// <c>UITKControl</c> exists to be a game UI panel: it registers with the static
	/// <c>UIManager</c> in Awake, takes an injected <c>Client</c>, and responds to
	/// quit-to-login. None of that applies before a <c>Client</c> exists, and registering the
	/// launcher into a manager that outlives the launcher scene would leave a stale entry
	/// behind across the handoff to post-boot.
	/// <para>
	/// Every setter records its value whether or not the visual tree has been built yet, and
	/// the recorded state is re-applied in <see cref="OnEnable"/>. Component initialisation
	/// order is not guaranteed, so <see cref="ClientLauncher"/> may well set the title before
	/// this component's <c>UIDocument</c> has produced a root — without buffering, those early
	/// calls would silently go nowhere and the launcher would come up blank.
	/// </para>
	/// </remarks>
	public class UITKClientLauncher : MonoBehaviour, ILauncherView
	{
		private const string TITLE_NAME = "launcher-title";
		private const string NEWS_NAME = "launcher-news";
		private const string NEWS_SCROLL_NAME = "launcher-news-scroll";
		private const string STATUS_NAME = "launcher-status";
		private const string PROGRESS_NAME = "launcher-progress";
		private const string PROGRESS_FILL_NAME = "launcher-progress-fill";
		private const string PROGRESS_TEXT_NAME = "launcher-progress-text";
		private const string PLAY_BUTTON_NAME = "launcher-play-btn";
		private const string QUIT_BUTTON_NAME = "launcher-quit-btn";
		private const string HERO_NAME = "launcher-hero";
		private const string SETTINGS_BUTTON_NAME = "launcher-settings-btn";
		private const string SETTINGS_PANEL_NAME = "launcher-settings";
		private const string SETTING_AUTOUPDATE_NAME = "launcher-setting-autoupdate";
		private const string SETTING_TIMEOUT_NAME = "launcher-setting-timeout";
		private const string SETTING_RETRIES_NAME = "launcher-setting-retries";
		private const string SETTING_RETRYDELAY_NAME = "launcher-setting-retrydelay";
		private const string SETTINGS_NOTE_NAME = "launcher-settings-note";
		private const string DISK_SIZE_NAME = "launcher-disk-size";
		private const string INSTALL_PATH_NAME = "launcher-install-path";
		private const string SETTING_PATCHDIR_NAME = "launcher-setting-patchdir";
		private const string PATCHDIR_NOTE_NAME = "launcher-patchdir-note";
		private const string PATCHDIR_BROWSE_NAME = "launcher-patchdir-browse";

		/// <summary>USS class that hides an element. Defined in FishMMO-Theme.uss.</summary>
		private const string HIDDEN_CLASS = "fish-hidden";

		/// <summary>
		/// The UIDocument that owns the launcher's visual tree. Assign in the Inspector.
		/// </summary>
		[Tooltip("UIDocument backing the launcher UI.")]
		public UIDocument Document;

		private VisualElement root;
		private Label titleLabel;
		private VisualElement newsContainer;
		private ScrollView newsScroll;
		private Label statusLabel;
		private VisualElement progressGroup;
		private VisualElement progressFill;
		private Label progressTextLabel;
		private Button playButton;
		private Button quitButton;
		/// <summary>Brand banner across the top of the panel.</summary>
		private VisualElement hero;

		private Button settingsButton;
		private VisualElement settingsPanel;
		private Toggle autoUpdateToggle;
		private SliderInt timeoutSlider;
		private SliderInt retriesSlider;
		private Slider retryDelaySlider;
		private Label settingsNote;
		private Label diskSizeLabel;
		private Label installPathLabel;
		private TextField patchDirField;
		private Label patchDirNote;
		private Button patchDirBrowseButton;

		private bool elementsResolved;
		private bool settingsOpen;
		/// <summary>
		/// Latched once the launcher has handed off to the game scene. One-way: nothing may put
		/// the launcher back on screen afterwards.
		/// </summary>
		/// <remarks>
		/// The handoff is not a state the launcher can usefully return from — the scene it lives
		/// in is being unloaded — so anything that re-shows it is a bug rather than a feature.
		/// Making that explicit is what lets <see cref="OnEnable"/> and <see cref="Update"/> stop
		/// re-applying recorded state into a panel the login screen now owns.
		/// </remarks>
		private bool dismissed;
		/// <summary>Frames spent waiting for the visual tree before giving up.</summary>
		private int resolveAttempts;
		/// <summary>Set once resolution has been abandoned, so the error is logged only once.</summary>
		private bool resolveFailed;
		/// <summary>
		/// How many frames to wait for UIDocument to clone its tree. Generous — this only ever
		/// needs one or two, and the cost of waiting is a null check per frame.
		/// </summary>
		private const int MaxResolveAttempts = 300;
		private string pendingDiskSizeText = "Installation size: measuring...";

		// Recorded state, applied whenever the visual tree becomes available.
		private string pendingTitle = string.Empty;
		private string pendingButtonText = string.Empty;
		private bool pendingButtonInteractable = true;
		private Action buttonAction;
		private Action quitAction;
		private float pendingProgress;
		private string pendingProgressText = string.Empty;
		private bool pendingProgressVisible;
		private string pendingStatus = string.Empty;
		private bool pendingNewsVisible = true;
		private string pendingNewsMessage;
		private HtmlNode pendingNewsContent;

		private void Awake()
		{
			TryResolveElements();
		}

		private void OnEnable()
		{
			/* A re-enable after the handoff must not rebuild anything. UIDocument re-inserts its
			 * root into the shared panel whenever it is enabled, and ApplyAll would repopulate a
			 * tree that is now sitting on top of the login screen — so the teardown is simply run
			 * again rather than the view coming back to life. */
			if (this.dismissed)
			{
				DetachFromPanel();
				return;
			}

			if (TryResolveElements())
			{
				ApplyAll();
			}

			UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnAnySceneLoaded;
			UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnAnySceneLoaded;
		}

		private void OnDisable()
		{
			UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnAnySceneLoaded;
		}

		/// <summary>
		/// Last-resort detach, so a destroyed launcher can never leave a tree behind on screen.
		/// </summary>
		/// <remarks>
		/// Unloading the launcher scene destroys this component and its GameObject, but not the
		/// <see cref="VisualElement"/>s it inserted: those are managed objects held by reference
		/// from a panel that belongs to a PanelSettings asset, and the asset outlives the scene.
		/// Without this the only thing between a mis-sequenced teardown and a launcher frozen
		/// over the login screen is UIDocument's own cleanup, which is exactly the assumption
		/// that failed here.
		/// </remarks>
		private void OnDestroy()
		{
			DetachFromPanel();
		}

		/// <summary>
		/// Dismisses the launcher as soon as the game scene arrives, whoever loaded it.
		/// </summary>
		/// <param name="scene">The scene that finished loading.</param>
		/// <param name="mode">How it was loaded.</param>
		/// <remarks>
		/// The controller already hides this view when its own load callback runs, and that is
		/// the path a shipped build takes. This is a second, independent guarantee, because the
		/// callback chain has several ways to be skipped: AddressableLoadProcessor returns early
		/// from EnqueueLoad for a scene it already tracks, an editor session may have the postboot
		/// scene open before Play is ever pressed, and a scene loaded outside Addressables never
		/// enters its bookkeeping at all. Any of those leaves the launcher drawing over the login
		/// screen for the rest of the session.
		///
		/// Watching for the scene itself does not care how it got there. The launcher's own UI is
		/// the right place for the rule "stop drawing once the game is up", since it is the thing
		/// that suffers when the rule is missed.
		/// </remarks>
		private void OnAnySceneLoaded(UnityEngine.SceneManagement.Scene scene,
			UnityEngine.SceneManagement.LoadSceneMode mode)
		{
			if (scene.name != ClientLauncher.PostbootSceneName)
			{
				return;
			}

			/* Only a usable scene ends the launcher.
			 *
			 * ClientLauncher treats a postboot scene that reports as loaded but is not valid as a
			 * launch failure and deliberately keeps the launcher up to show it. This handler runs
			 * first — it is driven from SceneManager.sceneLoaded, ahead of the Addressables
			 * completion callback — so dismissing unconditionally took the UI away before that
			 * error could be drawn, leaving the player with a blank screen and no way to retry.
			 * The guard here mirrors the one in ClientLauncher.OnPostbootSceneLoaded. */
			if (!scene.IsValid() || !scene.isLoaded)
			{
				Debug.LogWarning($"[UITKClientLauncher] {ClientLauncher.PostbootSceneName} reported as loaded but is not usable; keeping the launcher up so the failure can be shown.");
				return;
			}

			/* See ClientLauncher for why this uses UnityEngine.Debug: FishMMO.Logging is not
			 * initialised when the editor opens the launcher scene directly. */
			Debug.Log($"[UITKClientLauncher] {ClientLauncher.PostbootSceneName} loaded; dismissing the launcher UI.");
			SetVisible(false);
		}

		/// <summary>
		/// Keeps retrying resolution until the visual tree exists, then stops.
		/// </summary>
		/// <remarks>
		/// Deliberately not relying on OnEnable being late enough. UIDocument clones the tree in
		/// its own OnEnable, and which component gets there first depends on their order on the
		/// GameObject — an ordering the scene can change without anyone noticing. Polling for a
		/// couple of frames costs nothing and removes the dependency entirely.
		/// <para>
		/// Gives up loudly rather than spinning forever, because a permanent failure here means
		/// a blank launcher, and silence is what made this expensive to diagnose the first time.
		/// </para>
		/// </remarks>
		private void Update()
		{
			// Resolving after the handoff would clone a fresh tree straight back into the panel
			// the login screen is now drawing into.
			if (this.dismissed || this.elementsResolved || this.resolveFailed)
			{
				return;
			}

			if (TryResolveElements())
			{
				ApplyAll();
				return;
			}

			this.resolveAttempts++;
			if (this.resolveAttempts >= MaxResolveAttempts)
			{
				this.resolveFailed = true;
				Log.Error("UITKClientLauncher", $"Gave up resolving the launcher UI after {MaxResolveAttempts} frames; it will not render. Check the UIDocument's Source Asset and Panel Settings.");
			}
		}

		/// <summary>
		/// Resolves and caches the elements this view drives, wiring the button callbacks once.
		/// </summary>
		/// <returns>True when the visual tree is available and elements are cached.</returns>
		private bool TryResolveElements()
		{
			if (this.elementsResolved)
			{
				return true;
			}

			if (this.Document == null)
			{
				Log.Error("UITKClientLauncher", "No UIDocument assigned; the launcher cannot render.");
				return false;
			}

			this.root = this.Document.rootVisualElement;
			if (this.root == null)
			{
				// Normal before the document's own OnEnable has run. OnEnable retries.
				Log.Debug("UITKClientLauncher", "rootVisualElement is null; will retry.");
				return false;
			}

			/* An empty root is "not ready yet", not "nothing to find".
			 *
			 * UIDocument allocates rootVisualElement early but only clones the UXML into it in
			 * its own OnEnable, which runs after every component's Awake. Awake therefore sees a
			 * real, empty root. Treating that as resolved — which this did — meant every Q<>
			 * returned null, the resolved flag latched, and the retry in OnEnable took the
			 * early-out and never looked again. The launcher then ran its entire flow correctly
			 * against a set of null fields and drew nothing. */
			if (this.root.childCount == 0)
			{
				Log.Debug("UITKClientLauncher", "Visual tree not cloned yet; will retry.");
				return false;
			}

			/* Logged unconditionally, not only on failure.
			 *
			 * A silent success and a silent failure looked identical from the outside: every
			 * setter null-checks, so an unresolved tree makes the launcher run perfectly and
			 * draw nothing, which is indistinguishable from a layout that renders blank. That
			 * cost a whole build/test round trip to tell apart. */
			Log.Info("UITKClientLauncher", $"Resolving UI. sourceAsset={(this.Document.visualTreeAsset != null ? this.Document.visualTreeAsset.name : "NULL")} rootChildren={this.root.childCount} panelSettings={(this.Document.panelSettings != null ? this.Document.panelSettings.name : "NULL")}");

			this.titleLabel = this.root.Q<Label>(TITLE_NAME);
			this.newsContainer = this.root.Q<VisualElement>(NEWS_NAME);
			this.newsScroll = this.root.Q<ScrollView>(NEWS_SCROLL_NAME);
			this.statusLabel = this.root.Q<Label>(STATUS_NAME);
			this.progressGroup = this.root.Q<VisualElement>(PROGRESS_NAME);
			this.progressFill = this.root.Q<VisualElement>(PROGRESS_FILL_NAME);
			this.progressTextLabel = this.root.Q<Label>(PROGRESS_TEXT_NAME);
			this.playButton = this.root.Q<Button>(PLAY_BUTTON_NAME);
			this.quitButton = this.root.Q<Button>(QUIT_BUTTON_NAME);
			this.hero = this.root.Q<VisualElement>(HERO_NAME);
			this.settingsButton = this.root.Q<Button>(SETTINGS_BUTTON_NAME);
			AttachHeroAspect();
			this.settingsPanel = this.root.Q<VisualElement>(SETTINGS_PANEL_NAME);
			this.autoUpdateToggle = this.root.Q<Toggle>(SETTING_AUTOUPDATE_NAME);
			this.timeoutSlider = this.root.Q<SliderInt>(SETTING_TIMEOUT_NAME);
			this.retriesSlider = this.root.Q<SliderInt>(SETTING_RETRIES_NAME);
			this.retryDelaySlider = this.root.Q<Slider>(SETTING_RETRYDELAY_NAME);
			this.settingsNote = this.root.Q<Label>(SETTINGS_NOTE_NAME);
			this.diskSizeLabel = this.root.Q<Label>(DISK_SIZE_NAME);
			this.installPathLabel = this.root.Q<Label>(INSTALL_PATH_NAME);
			this.patchDirField = this.root.Q<TextField>(SETTING_PATCHDIR_NAME);
			this.patchDirNote = this.root.Q<Label>(PATCHDIR_NOTE_NAME);
			this.patchDirBrowseButton = this.root.Q<Button>(PATCHDIR_BROWSE_NAME);

			// Names the elements that did not resolve, so a UXML/controller mismatch reports
			// which one rather than silently drawing an incomplete screen.
			string missing = string.Join(", ", new[]
			{
				this.titleLabel == null ? TITLE_NAME : null,
				this.newsContainer == null ? NEWS_NAME : null,
				this.newsScroll == null ? NEWS_SCROLL_NAME : null,
				this.statusLabel == null ? STATUS_NAME : null,
				this.progressGroup == null ? PROGRESS_NAME : null,
				this.playButton == null ? PLAY_BUTTON_NAME : null,
				this.quitButton == null ? QUIT_BUTTON_NAME : null,
				this.settingsButton == null ? SETTINGS_BUTTON_NAME : null,
				this.settingsPanel == null ? SETTINGS_PANEL_NAME : null,
			}.Where(n => n != null));

			if (!string.IsNullOrEmpty(missing))
			{
				/* Not latched. A partial resolve means the tree is still being built or the
				 * UXML genuinely does not match this controller; either way, committing to it
				 * would bind callbacks to whatever did resolve and permanently give up on the
				 * rest. Retrying costs nothing and the caller reports if it never succeeds. */
				Log.Warning("UITKClientLauncher", $"UXML elements not found (will retry): {missing}");
				return false;
			}

			Log.Info("UITKClientLauncher", "All launcher UI elements resolved.");

			BindSettings();

			// Registered once and dispatched through the fields, so replacing an action is a
			// field assignment. Re-registering per state change would accumulate callbacks and
			// fire every action the launcher had ever been in.
			if (this.playButton != null)
			{
				this.playButton.clicked += () => this.buttonAction?.Invoke();
			}
			if (this.quitButton != null)
			{
				this.quitButton.clicked += () => this.quitAction?.Invoke();
			}

			this.elementsResolved = true;
			return true;
		}

		/// <summary>
		/// Seeds the settings controls from stored values and persists any change.
		/// </summary>
		/// <remarks>
		/// Values are read through <see cref="LauncherSettings"/> rather than from the
		/// configuration store directly, so the clamping applied to a hand-edited config file
		/// is the same clamping the sliders enforce. Reading raw would let the UI display an
		/// out-of-range value that the download path then silently ignores.
		/// <para>
		/// Each change is saved immediately. The launcher has no OK button, and it is routinely
		/// terminated by the updater taking over the process — deferring the write to teardown
		/// would lose it exactly when an update is happening.
		/// </para>
		/// </remarks>
		private void BindSettings()
		{
			if (this.settingsButton != null)
			{
				this.settingsButton.clicked += ToggleSettings;
			}

			if (this.autoUpdateToggle != null)
			{
				this.autoUpdateToggle.SetValueWithoutNotify(LauncherSettings.AutoUpdate);
				this.autoUpdateToggle.RegisterValueChangedCallback(evt =>
				{
					LauncherSettings.AutoUpdate = evt.newValue;
					LauncherSettings.Save();
					RefreshSettingsNote();
				});
			}

			if (this.timeoutSlider != null)
			{
				/* Range driven from the same constants LauncherSettings clamps against, rather
				 * than the literals in the UXML. Two copies of a bound drift: raise the cap in
				 * code and the slider silently keeps refusing the values the setting now accepts.
				 * The label carries the range too, so the player can see what is allowed instead
				 * of discovering it by dragging into a wall. */
				this.timeoutSlider.lowValue = LauncherSettings.MinRequestTimeout;
				this.timeoutSlider.highValue = LauncherSettings.MaxRequestTimeout;
				this.timeoutSlider.label =
					$"Request timeout ({LauncherSettings.MinRequestTimeout}-{LauncherSettings.MaxRequestTimeout}s)";
				this.timeoutSlider.SetValueWithoutNotify(LauncherSettings.GetRequestTimeout(10));
				this.timeoutSlider.RegisterValueChangedCallback(evt =>
				{
					LauncherSettings.SetRequestTimeout(evt.newValue);
					LauncherSettings.Save();
				});
			}

			if (this.retriesSlider != null)
			{
				this.retriesSlider.lowValue = LauncherSettings.MinRetries;
				this.retriesSlider.highValue = LauncherSettings.MaxRetriesLimit;
				this.retriesSlider.label =
					$"Retry attempts ({LauncherSettings.MinRetries}-{LauncherSettings.MaxRetriesLimit})";
				this.retriesSlider.SetValueWithoutNotify(LauncherSettings.GetMaxRetries(3));
				this.retriesSlider.RegisterValueChangedCallback(evt =>
				{
					LauncherSettings.SetMaxRetries(evt.newValue);
					LauncherSettings.Save();
				});
			}

			if (this.retryDelaySlider != null)
			{
				this.retryDelaySlider.lowValue = LauncherSettings.MinRetryDelay;
				this.retryDelaySlider.highValue = LauncherSettings.MaxRetryDelay;
				this.retryDelaySlider.label =
					$"Retry delay ({LauncherSettings.MinRetryDelay:0}-{LauncherSettings.MaxRetryDelay:0}s)";
				this.retryDelaySlider.SetValueWithoutNotify(LauncherSettings.GetRetryDelay(1.0f));
				this.retryDelaySlider.RegisterValueChangedCallback(evt =>
				{
					LauncherSettings.SetRetryDelay(evt.newValue);
					LauncherSettings.Save();
				});
			}

			if (this.installPathLabel != null)
			{
				this.installPathLabel.text = $"Installed at: {Constants.GetWorkingDirectory()}";
			}

			if (this.patchDirField != null)
			{
				this.patchDirField.SetValueWithoutNotify(LauncherSettings.PatchDirectoryOverride);
				/* Committed on blur or Enter rather than per keystroke. RegisterValueChangedCallback
				 * fires on every character, so a path typed by hand would be written to disk —
				 * and validated — dozens of times, most of them against a prefix that is not yet
				 * a real directory. */
				this.patchDirField.RegisterCallback<FocusOutEvent>(_ => CommitPatchDirectory());
				this.patchDirField.RegisterCallback<KeyDownEvent>(evt =>
				{
					if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
					{
						CommitPatchDirectory();
					}
				});
			}

			if (this.patchDirBrowseButton != null)
			{
				// Hidden rather than disabled where no native dialog exists. A permanently
				// greyed-out button reads as something broken; the text field beside it is the
				// real control on every platform, so its absence costs nothing.
				SetHidden(this.patchDirBrowseButton, !NativeFolderPicker.IsSupported);
				this.patchDirBrowseButton.clicked += BrowseForPatchDirectory;
			}

			RefreshSettingsNote();
			RefreshPatchDirectoryNote();
		}

		/// <summary>
		/// Opens the OS folder dialog and stores the chosen directory.
		/// </summary>
		/// <remarks>
		/// A cancelled dialog returns null and is left alone rather than clearing the field —
		/// backing out of a picker should not silently reset the setting to the default.
		/// </remarks>
		private void BrowseForPatchDirectory()
		{
			string chosen = NativeFolderPicker.PickFolder("Choose where patch downloads are stored");
			if (string.IsNullOrWhiteSpace(chosen))
			{
				return;
			}

			this.patchDirField?.SetValueWithoutNotify(chosen);
			LauncherSettings.PatchDirectoryOverride = chosen;
			LauncherSettings.Save();
			RefreshPatchDirectoryNote();
		}

		/// <summary>
		/// Stores the typed patch directory and reports where patches will actually land.
		/// </summary>
		private void CommitPatchDirectory()
		{
			if (this.patchDirField == null)
			{
				return;
			}

			LauncherSettings.PatchDirectoryOverride = this.patchDirField.value?.Trim() ?? string.Empty;
			LauncherSettings.Save();
			RefreshPatchDirectoryNote();
		}

		/// <summary>
		/// Reports the directory patches will actually be written to.
		/// </summary>
		/// <remarks>
		/// Shows the resolved path rather than echoing what was typed. An unusable override
		/// falls back to the install's own folder, and a player who is not told that will
		/// reasonably believe their setting took effect.
		/// </remarks>
		private void RefreshPatchDirectoryNote()
		{
			if (this.patchDirNote == null)
			{
				return;
			}

			string fallback = Constants.GetPatchesDirectory();
			string resolved = LauncherSettings.ResolvePatchDirectory(fallback);

			this.patchDirNote.text = string.IsNullOrWhiteSpace(LauncherSettings.PatchDirectoryOverride)
				? $"Leave empty to use the install folder. Currently: {resolved}"
				: (resolved == fallback
					? $"That path could not be used — falling back to {resolved}"
					: $"Patches will download to {resolved}");
		}

		/// <summary>
		/// Shows or hides the settings section.
		/// </summary>
		private void ToggleSettings()
		{
			this.settingsOpen = !this.settingsOpen;
			SetHidden(this.settingsPanel, !this.settingsOpen);
		}

		/// <summary>
		/// Explains the consequence of the current auto-update choice, so the toggle is not
		/// just a label the player has to guess the meaning of.
		/// </summary>
		private void RefreshSettingsNote()
		{
			if (this.settingsNote == null)
			{
				return;
			}

			this.settingsNote.text = LauncherSettings.AutoUpdate
				? "Updates start downloading as soon as one is found."
				: "The launcher will wait and show an Update button instead.";
		}

		/// <summary>
		/// Pushes all recorded state into the visual tree.
		/// </summary>
		private void ApplyAll()
		{
			ApplyTitle();
			ApplyButtonText();
			ApplyButtonInteractable();
			ApplyProgress();
			ApplyProgressText();
			ApplyProgressVisible();
			ApplyStatus();
			ApplyNewsVisible();
			ApplyNewsBody();
			ApplyDiskSize();
		}

		private void ApplyDiskSize()
		{
			// Locally computed, but it costs nothing to keep every label this view writes on
			// the same footing — one rule is easier to keep than an exception list.
			RemoteText.SetText(this.diskSizeLabel, this.pendingDiskSizeText);
		}

		#region ILauncherView

		/// <inheritdoc />
		public void SetVisible(bool visible)
		{
			if (!visible)
			{
				Dismiss();
				return;
			}

			/* Refused after the handoff. See <see cref="dismissed"/>: the launcher scene is being
			 * unloaded by the time anything could ask for this, so honouring it would put a tree
			 * with no GameObject behind it back over the login screen. */
			if (this.dismissed)
			{
				return;
			}

			if (this.Document != null)
			{
				this.Document.enabled = true;
			}

			/* Re-enabling a UIDocument makes it clone a *new* tree, so every cached element the
			 * setters write through belongs to the discarded one. Dropping the resolved flag
			 * sends Update round again to re-cache against the tree that is actually on screen. */
			this.elementsResolved = false;
			this.resolveAttempts = 0;
			this.root = null;
		}

		/// <summary>
		/// Takes the launcher off screen for good and unhooks it from the shared UI panel.
		/// </summary>
		/// <remarks>
		/// Disabling the <see cref="UIDocument"/> is not sufficient on its own, which is what this
		/// used to rely on. A <see cref="VisualElement"/> is a plain managed object living in a
		/// runtime panel owned by <c>Assets/UI Toolkit/PanelSettings.asset</c> — an asset shared
		/// with ClientPreboot, ClientLoginGUI and ClientWorldGUI, so the panel outlives the
		/// launcher scene entirely. Unloading that scene destroys GameObjects, not the tree they
		/// inserted; anything the panel still references keeps drawing with nothing behind it,
		/// and the launcher root is a full-screen element with a full-screen backdrop.
		/// <para>
		/// The tree can also be put back. Every <c>UITKControl</c> writes
		/// <c>UIDocument.sortingOrder</c> on Show and on focus, and PanelSettings answers a
		/// sorting-order change by re-seating the roots of the documents attached to it — so the
		/// first login panel to appear is enough to re-insert a launcher document that was only
		/// disabled. That is the "launcher over the login screen" symptom.
		/// </para>
		/// <para>
		/// So the root is detached explicitly, the document is unhooked from the shared
		/// PanelSettings so nothing can re-seat it, and <see cref="dismissed"/> latches.
		/// </para>
		/// </remarks>
		private void Dismiss()
		{
			if (this.dismissed)
			{
				return;
			}
			this.dismissed = true;

			// Nothing left to watch for; the scene this lives in is on its way out.
			UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnAnySceneLoaded;

			bool stillAttached = DetachFromPanel();

			/* Kept permanently, and on UnityEngine.Debug for the reason given in
			 * OnAnySceneLoaded. Whether the launcher actually left the panel is not otherwise
			 * observable from the console — the previous logs only proved the dismissal was
			 * *asked for*, which was never the part in doubt. */
			Debug.Log($"[UITKClientLauncher] Launcher dismissed and detached from the shared UI panel. stillAttached={stillAttached}");
		}

		/// <summary>
		/// Removes the launcher's visual tree from whatever panel holds it and stops the document
		/// from re-attaching.
		/// </summary>
		/// <returns>True if any tree is still attached to a panel afterwards, which is a bug.</returns>
		private bool DetachFromPanel()
		{
			/* Read live, and read it before the document is disabled. UIDocument drops its
			 * rootVisualElement in OnDisable, so afterwards there is nothing left to detach; and
			 * the cached copy is not necessarily the tree the panel holds, because a document
			 * that has been disabled and re-enabled clones a new one. Both are handled. */
			VisualElement live = this.Document != null ? this.Document.rootVisualElement : null;

			DetachTree(live);
			DetachTree(this.root);

			if (this.Document != null)
			{
				this.Document.enabled = false;

				/* Cleared so a later sorting-order change cannot re-seat this document into the
				 * shared panel. Guarded because assigning the value it already holds is the one
				 * branch of UIDocument.panelSettings that re-attaches rather than detaches. */
				if (this.Document.panelSettings != null)
				{
					this.Document.panelSettings = null;
				}
			}

			return (live != null && live.panel != null) ||
			       (this.root != null && this.root.panel != null);
		}

		/// <summary>
		/// Makes one visual tree inert and removes it from its panel.
		/// </summary>
		/// <param name="element">The tree root, or null.</param>
		/// <remarks>
		/// <c>RemoveFromHierarchy</c> is the line that matters — everything before it is a
		/// property of an element the panel still owns, and a full-screen root left in a panel
		/// draws and picks regardless of how the component that created it is configured. The
		/// display, picking and enabled writes are belt and braces for the case where the element
		/// is somehow re-parented: a panel that still receives pointer events swallows the clicks
		/// meant for the login screen underneath, which is a worse failure than a stale image
		/// because it looks like the game has hung.
		/// </remarks>
		private static void DetachTree(VisualElement element)
		{
			if (element == null)
			{
				return;
			}

			element.style.display = DisplayStyle.None;
			element.pickingMode = PickingMode.Ignore;
			element.SetEnabled(false);
			element.RemoveFromHierarchy();
		}

		public void SetTitle(string title)
		{
			this.pendingTitle = title;
			ApplyTitle();
		}

		/// <inheritdoc />
		public void SetButtonText(string text)
		{
			this.pendingButtonText = text;
			ApplyButtonText();
		}

		/// <inheritdoc />
		public void SetButtonInteractable(bool interactable)
		{
			this.pendingButtonInteractable = interactable;
			ApplyButtonInteractable();
		}

		/// <inheritdoc />
		public void SetButtonAction(Action action)
		{
			this.buttonAction = action;
		}

		/// <inheritdoc />
		public void SetQuitAction(Action action)
		{
			this.quitAction = action;
		}

		/// <inheritdoc />
		public void SetProgress(DownloadStats stats)
		{
			this.pendingProgress = Mathf.Clamp01(stats.NormalizedProgress);
			this.pendingProgressText = stats.ToDisplayString();
			ApplyProgress();
			ApplyProgressText();
		}

		/// <inheritdoc />
		public void SetProgressVisible(bool visible)
		{
			this.pendingProgressVisible = visible;
			if (!visible)
			{
				this.pendingProgress = 0f;
				this.pendingProgressText = string.Empty;
			}
			ApplyProgressVisible();
			ApplyProgress();
			ApplyProgressText();
		}

		/// <inheritdoc />
		public void ShowStatus(string message)
		{
			this.pendingStatus = message;
			ApplyStatus();
		}

		/// <inheritdoc />
		public void ClearStatus()
		{
			this.pendingStatus = string.Empty;
			ApplyStatus();
		}

		/// <inheritdoc />
		public void SetNewsVisible(bool visible)
		{
			this.pendingNewsVisible = visible;
			ApplyNewsVisible();
		}

		/// <inheritdoc />
		public void SetNewsMessage(string message)
		{
			this.pendingNewsMessage = message;
			this.pendingNewsContent = null;
			ApplyNewsBody();
		}

		/// <inheritdoc />
		public void SetNewsContent(HtmlNode content)
		{
			this.pendingNewsContent = content;
			this.pendingNewsMessage = null;
			ApplyNewsBody();
		}

		/// <inheritdoc />
		public void SetInstallSize(long? sizeBytes)
		{
			// "Unavailable" rather than "0 B" when unknown: a zero would read as an empty
			// installation, which is a different and alarming claim.
			this.pendingDiskSizeText = sizeBytes.HasValue
				? $"Installation size: {DownloadStats.FormatBytes((ulong)sizeBytes.Value)}"
				: "Installation size: unavailable";
			ApplyDiskSize();
		}

		/// <inheritdoc />
		public void Teardown()
		{
			// Nothing to release. Every callback this view registers lives on an element owned
			// by the UIDocument, which is destroyed together with this component.
		}

		#endregion

		#region Apply

		private void ApplyTitle()
		{
			// The title embeds the installed version string, which is read from disk and can
			// have been written by a patch. Same treatment as the status line.
			RemoteText.SetText(this.titleLabel, this.pendingTitle);
		}

		private void ApplyButtonText()
		{
			if (this.playButton != null)
			{
				this.playButton.text = this.pendingButtonText;
			}
		}

		private void ApplyButtonInteractable()
		{
			if (this.playButton != null)
			{
				this.playButton.SetEnabled(this.pendingButtonInteractable);
			}
		}

		private void ApplyProgress()
		{
			if (this.progressFill != null)
			{
				this.progressFill.style.width = Length.Percent(this.pendingProgress * 100f);
			}
		}

		private void ApplyProgressText()
		{
			if (this.progressTextLabel != null)
			{
				this.progressTextLabel.text = this.pendingProgressText;
			}
		}

		private void ApplyProgressVisible()
		{
			SetHidden(this.progressGroup, !this.pendingProgressVisible);
		}

		private void ApplyStatus()
		{
			if (this.statusLabel == null)
			{
				return;
			}

			/* S6: this label carries server-controlled text.
			 *
			 * ClientLauncher composes its detail strings from the version string the update
			 * server returned and from the error strings the request layer produced — so
			 * "Version {latest} is available" and "Error fetching latest version: {error}" are
			 * both remote content in a Label whose enableRichText defaults to true. Routed
			 * through RemoteText so it renders literally and cannot carry control characters. */
			RemoteText.SetText(this.statusLabel, this.pendingStatus);

			// Collapsed rather than merely blank so the footer does not reserve a gap for a
			// message that is not there.
			SetHidden(this.statusLabel, string.IsNullOrEmpty(this.pendingStatus));
		}

		private void ApplyNewsVisible()
		{
			SetHidden(this.newsScroll, !this.pendingNewsVisible);
		}

		/// <summary>
		/// Longest news message rendered as plain text.
		/// </summary>
		/// <remarks>
		/// Much larger than <see cref="RemoteText.MaxLength"/>, because the fallback summary is
		/// genuinely a few paragraphs — but still bounded, because this path also carries
		/// fetch-error text that originated off-machine.
		/// </remarks>
		private const int MaxNewsMessageLength = 16384;

		/// <summary>
		/// The in-flight incremental news render, so a second one can cancel the first.
		/// </summary>
		private Coroutine newsRenderRoutine;

		private void ApplyNewsBody()
		{
			if (this.newsContainer == null)
			{
				return;
			}

			/* Any previous render is abandoned before a new one starts. RenderIncremental spans
			 * frames, so without this a fetch that lands while an earlier render is still
			 * walking would interleave two documents into one container. */
			if (this.newsRenderRoutine != null)
			{
				StopCoroutine(this.newsRenderRoutine);
				this.newsRenderRoutine = null;
			}

			if (this.pendingNewsMessage != null)
			{
				this.newsContainer.Clear();
				/* Plain Label, rich text OFF. This path carries the built-in summary (safe) AND
				 * fetch-error strings built from remote input (not), and there is no way to tell
				 * them apart at this point — so both go through the same helper. See RemoteText
				 * for why "it's only assigned to a Label" is not protection. */
				this.newsContainer.Add(RemoteText.CreateLabel(
					this.pendingNewsMessage,
					"launcher-news__text",
					allowNewlines: true,
					maxLength: MaxNewsMessageLength));
				return;
			}

			/* Incremental, and only when this component can host a coroutine. A disabled or
			 * destroyed MonoBehaviour cannot, and StartCoroutine throws rather than no-opping —
			 * so the synchronous form is the fallback. Both share the same budgets. */
			if (isActiveAndEnabled)
			{
				this.newsRenderRoutine = StartCoroutine(RenderNewsIncremental(this.pendingNewsContent));
			}
			else
			{
				UITKHtmlContentRenderer.Render(this.pendingNewsContent, this.newsContainer, LauncherLinkPolicy.OpenIfSafe);
			}
		}

		/// <summary>
		/// Runs the bounded incremental news render and clears its handle when it finishes.
		/// </summary>
		private System.Collections.IEnumerator RenderNewsIncremental(HtmlNode content)
		{
			yield return UITKHtmlContentRenderer.RenderIncremental(
				content,
				this.newsContainer,
				LauncherLinkPolicy.OpenIfSafe);

			this.newsRenderRoutine = null;
		}

		/// <summary>
		/// Keeps the banner's height matched to its own aspect ratio as the panel resizes.
		/// </summary>
		/// <remarks>
		/// USS has no aspect-ratio property, so a fixed height on a full-width element can only be
		/// wrong: <c>scale-and-crop</c> fills the strip but slices the top and bottom off the
		/// artwork, and <c>scale-to-fit</c> keeps it whole but leaves the panel background showing
		/// down both sides. The banner is 3.2:1 against a panel that is nowhere near that shape,
		/// so either choice was visible.
		///
		/// Driving the height from the resolved width removes the tradeoff — the strip is exactly
		/// as tall as the art needs, so it spans the full width, is never cropped and is never
		/// stretched, at any panel size.
		///
		/// The ratio is read from the assigned image rather than hardcoded, so replacing the
		/// artwork with a differently proportioned banner needs no code change.
		/// </remarks>
		private void AttachHeroAspect()
		{
			if (this.hero == null)
			{
				return;
			}
			this.hero.UnregisterCallback<GeometryChangedEvent>(OnHeroGeometryChanged);
			this.hero.RegisterCallback<GeometryChangedEvent>(OnHeroGeometryChanged);
		}

		/// <summary>
		/// Recomputes the banner height when its width changes.
		/// </summary>
		/// <param name="evt">The geometry change.</param>
		private void OnHeroGeometryChanged(GeometryChangedEvent evt)
		{
			if (this.hero == null)
			{
				return;
			}

			float width = this.hero.resolvedStyle.width;
			if (float.IsNaN(width) || width <= 1.0f)
			{
				return;
			}

			float aspect = ResolveHeroAspect();
			if (aspect <= 0.0f)
			{
				return;
			}

			float height = width / aspect;

			/* Guarded against re-entry. Writing a height triggers another GeometryChangedEvent,
			 * and an unconditional write would bounce between two rounding neighbours forever. */
			float current = this.hero.resolvedStyle.height;
			if (!float.IsNaN(current) && Mathf.Abs(current - height) < 0.5f)
			{
				return;
			}

			this.hero.style.height = height;
		}

		/// <summary>
		/// Width-over-height of the banner image currently assigned to the hero element.
		/// </summary>
		/// <returns>The aspect ratio, or a fallback when the image cannot be measured.</returns>
		private float ResolveHeroAspect()
		{
			Background background = this.hero.resolvedStyle.backgroundImage;

			if (background.sprite != null && background.sprite.rect.height > 0.0f)
			{
				return background.sprite.rect.width / background.sprite.rect.height;
			}
			if (background.texture != null && background.texture.height > 0)
			{
				return (float)background.texture.width / background.texture.height;
			}
			/* No image resolved yet — the stylesheet may not have applied on the first layout
			 * pass. Collapsing to zero would make the banner vanish, so hold the authored height
			 * by reporting the ratio it already has. */
			float width = this.hero.resolvedStyle.width;
			float height = this.hero.resolvedStyle.height;
			return (!float.IsNaN(width) && !float.IsNaN(height) && height > 0.0f) ? width / height : 0.0f;
		}

		/// <summary>
		/// Adds or removes the shared hidden class on <paramref name="element"/>.
		/// </summary>
		private static void SetHidden(VisualElement element, bool hidden)
		{
			if (element == null)
			{
				return;
			}
			element.EnableInClassList(HIDDEN_CLASS, hidden);
		}

		#endregion
	}
}
