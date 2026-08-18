using System;
using UnityEngine;
using UnityEngine.UIElements;
using HtmlAgilityPack;
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

		private bool elementsResolved;

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
