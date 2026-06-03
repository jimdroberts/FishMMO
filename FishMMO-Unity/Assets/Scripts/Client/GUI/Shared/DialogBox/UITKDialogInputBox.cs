using System;
using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit input dialog. Shows a message with a text field and accept/cancel buttons,
	/// invoking a callback with the entered text. Mirrors the legacy UGUI <c>UIDialogInputBox</c> API.
	/// </summary>
	public class UITKDialogInputBox : UITKControl
	{
		private const string DIALOG_LABEL_NAME = "dialog-input-label";
		private const string INPUT_FIELD_NAME = "dialog-input-field";
		private const string ACCEPT_BUTTON_NAME = "dialog-input-accept-btn";
		private const string CANCEL_BUTTON_NAME = "dialog-input-cancel-btn";

		private Label dialogLabel;
		private TextField inputField;
		private Button acceptButton;
		private Button cancelButton;

		private Action<string> onAcceptCallback;
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
