using System;
using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit input dialog. Shows a message with a text field and accept/cancel buttons,
	/// invoking a callback with the entered text.
	/// </summary>
	public class UITKDialogInputBox : UITKControl
	{
		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Modal;

		/// <summary>
		/// Name of the dialog message label element.
		/// </summary>
		private const string DIALOG_LABEL_NAME = "dialog-input-label";
		/// <summary>
		/// Name of the text input field element.
		/// </summary>
		private const string INPUT_FIELD_NAME = "dialog-input-field";
		/// <summary>
		/// Name of the accept button element.
		/// </summary>
		private const string ACCEPT_BUTTON_NAME = "dialog-input-accept-btn";
		/// <summary>
		/// Name of the cancel button element.
		/// </summary>
		private const string CANCEL_BUTTON_NAME = "dialog-input-cancel-btn";

		/// <summary>
		/// The dialog message label.
		/// </summary>
		private Label dialogLabel;
		/// <summary>
		/// The text input field.
		/// </summary>
		private TextField inputField;
		/// <summary>
		/// The accept button.
		/// </summary>
		private Button acceptButton;
		/// <summary>
		/// The cancel button.
		/// </summary>
		private Button cancelButton;

		/// <summary>
		/// Callback invoked with the entered text when the accept button is clicked.
		/// </summary>
		private Action<string> onAcceptCallback;
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
			inputField = Root.Q<TextField>(INPUT_FIELD_NAME);
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
		/// Opens the input dialog with the specified message and optional callbacks.
		/// </summary>
		/// <param name="text">Message to display.</param>
		/// <param name="onAccept">Optional callback invoked with the entered text when accepted.</param>
		/// <param name="onCancel">Optional callback invoked when cancelled.</param>
		public void Open(string text, Action<string> onAccept = null, Action onCancel = null)
		{
			if (dialogLabel != null)
			{
				dialogLabel.text = text;
			}
			if (inputField != null)
			{
				inputField.value = string.Empty;
			}

			onAcceptCallback = onAccept;
			onCancelCallback = onCancel;

			Show();
		}

		/// <summary>
		/// Handles accept button clicks, invoking the accept callback when the input is not empty.
		/// </summary>
		private void OnClick_Accept()
		{
			string value = inputField != null ? inputField.value : string.Empty;
			if (!string.IsNullOrWhiteSpace(value))
			{
				onAcceptCallback?.Invoke(value);
			}
			Hide();
		}

		/// <summary>
		/// Handles cancel button clicks, invoking the cancel callback and closing the dialog.
		/// </summary>
		private void OnClick_Cancel()
		{
			onCancelCallback?.Invoke();
			Hide();
		}
	}
}
