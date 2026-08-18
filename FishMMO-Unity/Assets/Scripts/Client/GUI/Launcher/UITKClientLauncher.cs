using System;
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
			if (TryResolveElements())
			{
				ApplyAll();
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
				return false;
			}

			this.titleLabel = this.root.Q<Label>(TITLE_NAME);
			this.newsContainer = this.root.Q<VisualElement>(NEWS_NAME);
			this.newsScroll = this.root.Q<ScrollView>(NEWS_SCROLL_NAME);
			this.statusLabel = this.root.Q<Label>(STATUS_NAME);
			this.progressGroup = this.root.Q<VisualElement>(PROGRESS_NAME);
			this.progressFill = this.root.Q<VisualElement>(PROGRESS_FILL_NAME);
			this.progressTextLabel = this.root.Q<Label>(PROGRESS_TEXT_NAME);
			this.playButton = this.root.Q<Button>(PLAY_BUTTON_NAME);
			this.quitButton = this.root.Q<Button>(QUIT_BUTTON_NAME);
			this.settingsButton = this.root.Q<Button>(SETTINGS_BUTTON_NAME);
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
				this.timeoutSlider.SetValueWithoutNotify(LauncherSettings.GetRequestTimeout(10));
				this.timeoutSlider.RegisterValueChangedCallback(evt =>
				{
					LauncherSettings.SetRequestTimeout(evt.newValue);
					LauncherSettings.Save();
				});
			}

			if (this.retriesSlider != null)
			{
				this.retriesSlider.SetValueWithoutNotify(LauncherSettings.GetMaxRetries(3));
				this.retriesSlider.RegisterValueChangedCallback(evt =>
				{
					LauncherSettings.SetMaxRetries(evt.newValue);
					LauncherSettings.Save();
				});
			}

			if (this.retryDelaySlider != null)
			{
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
			if (this.diskSizeLabel != null)
			{
				this.diskSizeLabel.text = this.pendingDiskSizeText;
			}
		}

		#region ILauncherView

		/// <inheritdoc />
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
			if (this.titleLabel != null)
			{
				this.titleLabel.text = this.pendingTitle;
			}
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
			this.statusLabel.text = this.pendingStatus;
			// Collapsed rather than merely blank so the footer does not reserve a gap for a
			// message that is not there.
			SetHidden(this.statusLabel, string.IsNullOrEmpty(this.pendingStatus));
		}

		private void ApplyNewsVisible()
		{
			SetHidden(this.newsScroll, !this.pendingNewsVisible);
		}

		private void ApplyNewsBody()
		{
			if (this.newsContainer == null)
			{
				return;
			}

			if (this.pendingNewsMessage != null)
			{
				this.newsContainer.Clear();
				Label message = new Label(this.pendingNewsMessage);
				message.AddToClassList("launcher-news__text");
				this.newsContainer.Add(message);
				return;
			}

			UITKHtmlContentRenderer.Render(this.pendingNewsContent, this.newsContainer, LauncherLinkPolicy.OpenIfSafe);
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
