using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using HtmlAgilityPack;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Renders the launcher onto the legacy uGUI canvas.
	/// </summary>
	/// <remarks>
	/// A plain class rather than a MonoBehaviour, constructed by <see cref="ClientLauncher"/>
	/// from the element references already serialized on it. That is deliberate: promoting
	/// these fields onto a new component would invalidate every reference the launcher scene
	/// has stored against <see cref="ClientLauncher"/>, silently breaking the working launcher
	/// until someone re-wired it by hand. Adapting in code costs nothing and touches no scene.
	/// </remarks>
	public class UGUILauncherView : ILauncherView
	{
		private readonly TMP_Text title;
		private readonly GameObject htmlView;
		private readonly GameObject progressBarGroup;
		private readonly Slider progressSlider;
		private readonly TMP_Text progressText;
		private readonly Button quitButton;
		private readonly Button playButton;
		private readonly TMP_Text playButtonText;
		private readonly TMP_Text htmlText;
		private readonly TMPro_TextLinkHandler htmlTextLinkHandler;
		private readonly TMP_Text statusText;
		private readonly float pxToTmpSizeFactor;

		/// <summary>
		/// The handler currently attached to the primary button, retained so it can be removed
		/// before the next one is attached.
		/// </summary>
		private UnityEngine.Events.UnityAction primaryButtonHandler;

		/// <summary>
		/// Whether the progress bar is currently meant to be showing.
		/// </summary>
		private bool progressVisible;

		/// <summary>
		/// Whether a status message is currently displayed.
		/// </summary>
		private bool hasStatus;

		/// <summary>
		/// True when status messages have to borrow <see cref="progressText"/> because no
		/// dedicated status element is assigned. In that case the containing progress group
		/// must be visible for the message to be seen at all.
		/// </summary>
		private bool UsesProgressTextForStatus => this.statusText == null && this.progressText != null;

		public UGUILauncherView(
			TMP_Text title,
			GameObject htmlView,
			GameObject progressBarGroup,
			Slider progressSlider,
			TMP_Text progressText,
			Button quitButton,
			Button playButton,
			TMP_Text playButtonText,
			TMP_Text htmlText,
			TMPro_TextLinkHandler htmlTextLinkHandler,
			TMP_Text statusText,
			float pxToTmpSizeFactor)
		{
			this.title = title;
			this.htmlView = htmlView;
			this.progressBarGroup = progressBarGroup;
			this.progressSlider = progressSlider;
			this.progressText = progressText;
			this.quitButton = quitButton;
			this.playButton = playButton;
			this.playButtonText = playButtonText;
			this.htmlText = htmlText;
			this.htmlTextLinkHandler = htmlTextLinkHandler;
			this.statusText = statusText;
			this.pxToTmpSizeFactor = pxToTmpSizeFactor;

			if (this.htmlTextLinkHandler != null)
			{
				this.htmlTextLinkHandler.OnLinkClicked += HandleLinkClicked;
			}
		}

		/// <inheritdoc />
		public void Teardown()
		{
			if (this.htmlTextLinkHandler != null)
			{
				this.htmlTextLinkHandler.OnLinkClicked -= HandleLinkClicked;
			}
			if (this.quitButton != null)
			{
				this.quitButton.onClick.RemoveAllListeners();
			}
			if (this.playButton != null)
			{
				this.playButton.onClick.RemoveAllListeners();
			}
		}

		/// <summary>
		/// Routes a clicked news link through the shared scheme allowlist.
		/// </summary>
		private void HandleLinkClicked(string link)
		{
			LauncherLinkPolicy.OpenIfSafe(link);
		}

		/// <inheritdoc />
		public void SetTitle(string title)
		{
			if (this.title != null)
			{
				this.title.text = title;
			}
		}

		/// <inheritdoc />
		public void SetButtonText(string text)
		{
			if (this.playButtonText != null)
			{
				this.playButtonText.text = text;
			}
		}

		/// <inheritdoc />
		public void SetButtonInteractable(bool interactable)
		{
			if (this.playButton != null)
			{
				this.playButton.interactable = interactable;
			}
		}

		/// <inheritdoc />
		public void SetButtonAction(Action action)
		{
			if (this.playButton == null)
			{
				return;
			}

			// Remove only what this view attached. RemoveAllListeners would also discard
			// listeners configured on the scene object, which is a different contract than
			// "replace the action".
			if (this.primaryButtonHandler != null)
			{
				this.playButton.onClick.RemoveListener(this.primaryButtonHandler);
				this.primaryButtonHandler = null;
			}

			if (action != null)
			{
				this.primaryButtonHandler = () => action.Invoke();
				this.playButton.onClick.AddListener(this.primaryButtonHandler);
			}
		}

		/// <inheritdoc />
		public void SetQuitAction(Action action)
		{
			if (this.quitButton == null)
			{
				return;
			}

			// Wired in code rather than trusted to the scene. The scene's persistent
			// UnityEvent listener had a null target, so the button silently did nothing;
			// a code-side listener cannot be broken by a scene re-save.
			this.quitButton.onClick.RemoveAllListeners();
			if (action != null)
			{
				this.quitButton.onClick.AddListener(() => action.Invoke());
			}
		}

		/// <inheritdoc />
		public void SetProgress(DownloadStats stats)
		{
			if (this.progressSlider != null)
			{
				this.progressSlider.value = stats.NormalizedProgress;
			}
			if (this.progressText != null)
			{
				this.progressText.text = stats.ToDisplayString();
			}
		}

		/// <inheritdoc />
		public void SetProgressVisible(bool visible)
		{
			this.progressVisible = visible;

			if (this.progressSlider != null)
			{
				this.progressSlider.gameObject.SetActive(visible);
				if (!visible)
				{
					this.progressSlider.value = 0f;
				}
			}

			RefreshGroupVisibility();
		}

		/// <inheritdoc />
		public void ShowStatus(string message)
		{
			this.hasStatus = !string.IsNullOrEmpty(message);

			TMP_Text target = this.statusText != null ? this.statusText : this.progressText;
			if (target != null)
			{
				target.text = message;
			}

			if (this.statusText != null)
			{
				this.statusText.gameObject.SetActive(true);
			}

			RefreshGroupVisibility();
		}

		/// <inheritdoc />
		public void ClearStatus()
		{
			this.hasStatus = false;

			TMP_Text target = this.statusText != null ? this.statusText : this.progressText;
			if (target != null)
			{
				target.text = string.Empty;
			}

			RefreshGroupVisibility();
		}

		/// <summary>
		/// Reconciles the progress group's visibility against the two independent reasons it
		/// might need to be on screen.
		/// </summary>
		/// <remarks>
		/// The group holds the progress bar and, when no dedicated status element exists, the
		/// status message too. Deriving its visibility from the progress bar alone wrote every
		/// error message to a deactivated object, leaving the player a two-word button label
		/// and no explanation of what had gone wrong.
		/// </remarks>
		private void RefreshGroupVisibility()
		{
			if (this.progressBarGroup == null)
			{
				return;
			}
			this.progressBarGroup.SetActive(this.progressVisible || (UsesProgressTextForStatus && this.hasStatus));
		}

		/// <inheritdoc />
		public void SetNewsVisible(bool visible)
		{
			if (this.htmlView != null)
			{
				this.htmlView.SetActive(visible);
			}
		}

		/// <inheritdoc />
		public void SetNewsMessage(string message)
		{
			if (this.htmlText != null)
			{
				this.htmlText.text = message;
			}
		}

		/// <inheritdoc />
		public void SetInstallSize(long? sizeBytes)
		{
			// The uGUI launcher has no element for this. It is the legacy fallback view and is
			// not gaining new scene objects — adding one would mean editing the scene that this
			// view exists specifically to leave untouched.
		}

		/// <inheritdoc />
		public void SetNewsContent(HtmlNode content)
		{
			if (this.htmlText != null)
			{
				this.htmlText.text = HtmlToTmpTextConverter.Convert(content, this.pxToTmpSizeFactor);
			}
		}
	}
}
