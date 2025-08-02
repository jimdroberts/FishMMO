using System;
using TMPro;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Control for displaying a dialog with an input box, allowing user to enter text and accept or cancel the action.
	/// </summary>
	public class UIDialogInputBox : UIControl
	{
		/// <summary>
		/// Label for displaying the dialog prompt text.
		/// </summary>
		public TMP_Text DialogueLabel;

		/// <summary>
		/// Callback invoked when the user accepts the input.
		/// </summary>
		private Action<string> onAccept;
		/// <summary>
		/// Callback invoked when the user cancels the dialog.
		/// </summary>
		private Action onCancel;

		/// <summary>
		/// Called when the control is being destroyed. Clears accept and cancel callbacks.
		/// </summary>
		public override void OnDestroying()
		{
			onAccept = null;
			onCancel = null;
		}

		/// <summary>
		/// Opens the dialog input box with the specified prompt and callbacks.
		/// </summary>
		/// <param name="text">Prompt text to display.</param>
		/// <param name="onAccept">Callback for accept action.</param>
		/// <param name="onCancel">Callback for cancel action.</param>
		public void Open(string text, Action<string> onAccept = null, Action onCancel = null)
		{
			if (Visible)
			{
				return;
			}
			if (DialogueLabel != null)
			{
				DialogueLabel.text = text;
			}
			if (InputField != null)
			{
				InputField.text = "";
			}
			this.onAccept = onAccept;
			this.onCancel = onCancel;
			Show();
		}

		/// <summary>
		/// Called when the accept button is clicked. Invokes accept callback if input is valid, then hides the dialog.
		/// </summary>
		public void OnClick_Accept()
		{
			if (InputField != null &&
				!string.IsNullOrWhiteSpace(InputField.text))
			{
				this.onAccept?.Invoke(InputField.text);
			}

			onAccept = null;
			onCancel = null;

			Hide();
		}

		/// <summary>
		/// Called when the cancel button is clicked. Invokes cancel callback and hides the dialog.
		/// </summary>
		public void OnClick_Cancel()
		{
			onCancel?.Invoke();

			onAccept = null;
			onCancel = null;

			Hide();
		}
	}
}