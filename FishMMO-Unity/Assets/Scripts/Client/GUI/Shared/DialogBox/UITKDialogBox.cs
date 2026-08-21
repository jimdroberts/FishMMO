using System;
using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit confirmation dialog. Shows a message with accept/cancel buttons and
	/// invokes optional callbacks.
	/// </summary>
	/// <remarks>
	/// The open/close callback rules — refuse rather than hijack, exactly one callback per
	/// request on every exit path, callbacks cleared before they are invoked — live in
	/// <see cref="UITKCallbackDialog"/> and are shared with the input dialog and the selector.
	/// </remarks>
	public class UITKDialogBox : UITKCallbackDialog
	{
		/// <summary>
		/// Name of the dialog message label element.
		/// </summary>
		private const string DIALOG_LABEL_NAME = "dialog-label";
		/// <summary>
		/// Name of the accept button element.
		/// </summary>
		private const string ACCEPT_BUTTON_NAME = "dialog-accept-btn";
		/// <summary>
		/// Name of the cancel button element.
		/// </summary>
		private const string CANCEL_BUTTON_NAME = "dialog-cancel-btn";

		/// <summary>
		/// The dialog message label.
		/// </summary>
		private Label dialogLabel;
		/// <summary>
		/// The accept button.
		/// </summary>
		private Button acceptButton;
		/// <summary>
		/// The cancel button.
		/// </summary>
		private Button cancelButton;

		/// <summary>
		/// Callback invoked when the accept button is clicked.
		/// </summary>
		private Action onAcceptCallback;
		/// <summary>
		/// Callback invoked when the cancel button is clicked.
		/// </summary>
		private Action onCancelCallback;

		/// <summary>
		/// Message the current request wants displayed.
		/// </summary>
		/// <remarks>
		/// Held as a field rather than written straight into <see cref="dialogLabel"/> by
		/// <see cref="Open"/>, because the label that exists at that moment is about to be
		/// thrown away — see <see cref="UITKCallbackDialog"/>.
		/// </remarks>
		private string pendingText = string.Empty;

		/// <summary>
		/// Resolves cached elements and wires button callbacks.
		/// </summary>
		public override void OnStarting()
		{
			if (Root == null)
			{
				return;
			}

			dialogLabel = Root.Q<Label>(DIALOG_LABEL_NAME);
			acceptButton = Root.Q<Button>(ACCEPT_BUTTON_NAME);
			cancelButton = Root.Q<Button>(CANCEL_BUTTON_NAME);

			/* Assigned rather than accumulated: this runs again on every visual tree rebuild,
			 * and the elements are new each time, so there is nothing to unsubscribe from — but
			 * a rebuild that reused an element would otherwise stack a second handler on it and
			 * answer the dialog twice per click. */
			if (acceptButton != null)
			{
				acceptButton.clicked += OnClick_Accept;
			}
			if (cancelButton != null)
			{
				cancelButton.clicked += OnClick_Cancel;
			}

			AttachDialogKeys(Root);
		}

		/// <summary>
		/// Opens the dialog with the specified message and optional accept/cancel callbacks.
		/// The accept button is hidden when <paramref name="onAccept"/> is null. When the accept
		/// button is shown, the cancel button is labelled "Cancel" (and hidden when
		/// <paramref name="onCancel"/> is null); otherwise the cancel button acts as a "Close" button.
		/// </summary>
		/// <param name="text">Message to display.</param>
		/// <param name="onAccept">Optional callback invoked when accepted.</param>
		/// <param name="onCancel">Optional callback invoked when cancelled.</param>
		/// <returns>
		/// False when the dialog was already showing another question and this one was refused.
		/// A refused request is answered immediately through <paramref name="onCancel"/>, so a
		/// caller that locked itself while waiting is released either way.
		/// </returns>
		public bool Open(string text, Action onAccept = null, Action onCancel = null)
		{
			if (!TryClaim())
			{
				onCancel?.Invoke();
				return false;
			}

			pendingText = text ?? string.Empty;
			onAcceptCallback = onAccept;
			onCancelCallback = onCancel;

			/* Show first. Everything above is state, not tree writes — the tree is filled in by
			 * ApplyRequest, which Show calls back into once the document has finished cloning. */
			Show();
			return true;
		}

		/// <summary>
		/// Replaces the message without reopening the dialog.
		/// </summary>
		/// <remarks>
		/// For content that updates while the box stays on screen — a queue position counting
		/// down, say. Going through <see cref="Open"/> for that would be refused outright, since
		/// the box is already showing the caller's own question.
		/// </remarks>
		/// <param name="text">The new message.</param>
		public void SetText(string text)
		{
			pendingText = text ?? string.Empty;
			if (dialogLabel != null)
			{
				dialogLabel.text = pendingText;
			}
		}

		/// <summary>
		/// Writes the current request's message and button layout into the live tree.
		/// </summary>
		protected override void ApplyRequest()
		{
			if (dialogLabel != null)
			{
				dialogLabel.text = pendingText;
			}

			bool acceptVisible = onAcceptCallback != null;
			SetButtonVisible(acceptButton, acceptVisible);

			if (acceptVisible)
			{
				bool cancelVisible = onCancelCallback != null;
				SetButtonVisible(cancelButton, cancelVisible);
				if (cancelButton != null)
				{
					cancelButton.text = "Cancel";
				}
			}
			else
			{
				/* No accept handler means the only thing the player can do is dismiss, so the
				 * remaining button says so — and it is always shown, or an informational dialog
				 * opened with no callbacks at all would have no way out. */
				SetButtonVisible(cancelButton, true);
				if (cancelButton != null)
				{
					cancelButton.text = "Close";
				}
			}
		}

		/// <summary>
		/// Drops the callbacks and the message this request was opened with.
		/// </summary>
		protected override void ClearRequest()
		{
			onAcceptCallback = null;
			onCancelCallback = null;
			pendingText = string.Empty;
		}

		/// <summary>
		/// Enter accepts when there is something to accept, and dismisses otherwise.
		/// </summary>
		protected override void OnSubmitKey()
		{
			if (onAcceptCallback != null)
			{
				OnClick_Accept();
				return;
			}
			OnClick_Cancel();
		}

		/// <summary>
		/// Handles accept button clicks, invoking the accept callback and closing the dialog.
		/// </summary>
		private void OnClick_Accept()
		{
			Action callback = onAcceptCallback;
			Resolve(() => callback?.Invoke());
		}

		/// <summary>
		/// Handles cancel button clicks, invoking the cancel callback and closing the dialog.
		/// </summary>
		private void OnClick_Cancel()
		{
			CancelRequest();
		}

		/// <summary>
		/// Answers the request down its cancel path. Escape, quit-to-login and a bare
		/// <see cref="UITKControl.Hide()"/> all arrive here.
		/// </summary>
		protected override void CancelRequest()
		{
			Action callback = onCancelCallback;
			Resolve(() => callback?.Invoke());
		}

		/// <summary>
		/// Toggles the display of a button element.
		/// </summary>
		private static void SetButtonVisible(Button button, bool visible)
		{
			if (button != null)
			{
				button.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}
	}
}
