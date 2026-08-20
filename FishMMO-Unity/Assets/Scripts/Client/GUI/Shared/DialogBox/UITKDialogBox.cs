using System;
using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit confirmation dialog. Shows a message with accept/cancel buttons and
	/// invokes optional callbacks. Mirrors the legacy UGUI <c>UIDialogBox</c> API.
	/// </summary>
	public class UITKDialogBox : UITKControl
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

			if (acceptButton != null)
			{
				acceptButton.clicked += OnClick_Accept;
			}
			if (cancelButton != null)
			{
				cancelButton.clicked += OnClick_Cancel;
			}
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
		public void Open(string text, Action onAccept = null, Action onCancel = null)
		{
			if (dialogLabel != null)
			{
				dialogLabel.text = text;
			}

			onAcceptCallback = onAccept;
			onCancelCallback = onCancel;

			bool acceptVisible = onAccept != null;
			SetButtonVisible(acceptButton, acceptVisible);

			if (acceptVisible)
			{
				bool cancelVisible = onCancel != null;
				SetButtonVisible(cancelButton, cancelVisible);
				if (cancelButton != null)
				{
					cancelButton.text = "Cancel";
				}
			}
			else
			{
				SetButtonVisible(cancelButton, true);
				if (cancelButton != null)
				{
					cancelButton.text = "Close";
				}
			}

			Show();
		}

		/// <summary>
		/// Replaces the message without reopening the dialog.
		/// </summary>
		/// <remarks>
		/// For content that updates while the box stays on screen — a queue position counting
		/// down, say. Going through <see cref="Open"/> for that would re-evaluate the buttons
		/// and re-Show on every tick. Mirrors the UGUI <c>UIDialogBox.SetText</c>.
		/// </remarks>
		/// <param name="text">The new message.</param>
		public void SetText(string text)
		{
			if (dialogLabel != null)
			{
				dialogLabel.text = text;
			}
		}

		/// <summary>
		/// Handles accept button clicks, invoking the accept callback and closing the dialog.
		/// </summary>
		private void OnClick_Accept()
		{
			Action callback = onAcceptCallback;

			/* Cleared before invoking, not after. These outlive the dialog otherwise: Hide()
			 * only switches the panel off, so anything that shows it again without going
			 * through Open — and these callbacks do things like quit to login — would fire the
			 * previous dialog's answer. The UGUI UIDialogBox clears for the same reason. */
			onAcceptCallback = null;
			onCancelCallback = null;

			callback?.Invoke();
			Hide();
		}

		/// <summary>
		/// Handles cancel button clicks, invoking the cancel callback and closing the dialog.
		/// </summary>
		private void OnClick_Cancel()
		{
			Action callback = onCancelCallback;

			onAcceptCallback = null;
			onCancelCallback = null;

			callback?.Invoke();
			Hide();
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
