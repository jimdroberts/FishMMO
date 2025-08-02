using System;
using TMPro;
using UnityEngine.UI;

namespace FishMMO.Client
{
	/// <summary>
	/// Manages the display and functionality of a dialog box in the UI.
	/// </summary>
	public class UIDialogBox : UIControl
	{
		/// <summary>
		/// Label for displaying the dialog prompt text.
		/// </summary>
		public TMP_Text DialogueLabel;
		/// <summary>
		/// Button for accepting the dialog.
		/// </summary>
		public Button AcceptButton;
		/// <summary>
		/// Button for cancelling or closing the dialog.
		/// </summary>
		public Button CancelButton;
		/// <summary>
		/// Label for the cancel/close button.
		/// </summary>
		public TMP_Text CancelButtonLabel;

		/// <summary>
		/// Callback invoked when the user accepts the dialog.
		/// </summary>
		private Action onAccept;
		/// <summary>
		/// Callback invoked when the user cancels the dialog.
		/// </summary>
		private Action onCancel;

		/// <summary>
		/// Called when the control is starting. Initializes the cancel button label reference.
		/// </summary>
		public override void OnStarting()
		{
			if (CancelButton != null)
			{
				CancelButtonLabel = CancelButton.GetComponentInChildren<TMP_Text>();
			}
		}

		/// <summary>
		/// Opens the dialog box with the specified prompt and callbacks.
		/// </summary>
		/// <param name="text">Prompt text to display.</param>
		/// <param name="onAccept">Callback for accept action.</param>
		/// <param name="onCancel">Callback for cancel action.</param>
		public void Open(string text, Action onAccept = null, Action onCancel = null)
		{
			if (Visible)
			{
				return;
			}

			if (AcceptButton == null || CancelButton == null)
			{
				return;
			}

			AcceptButton.gameObject.SetActive(onAccept != null);

			// If the accept button is active, the cancel button can be enabled or disabled
			if (AcceptButton.gameObject.activeSelf)
			{
				CancelButton.gameObject.SetActive(onCancel != null);

				// Set Cancel button label to "Cancel"
				if (CancelButtonLabel != null)
				{
					CancelButtonLabel.text = "Cancel";
				}
			}
			// Otherwise, the cancel button must be available and labeled "Close"
			else
			{
				CancelButton.gameObject.SetActive(true);

				// Rename the Cancel button to "Close"
				if (CancelButtonLabel != null)
				{
					CancelButtonLabel.text = "Close";
				}
			}

			if (DialogueLabel != null)
			{
				DialogueLabel.text = text;
			}

			this.onAccept = onAccept;
			this.onCancel = onCancel;
			Show();
		}

		/// <summary>
		/// Called when the accept button is clicked. Invokes accept callback and hides the dialog.
		/// </summary>
		public void OnClick_Accept()
		{
			onAccept?.Invoke();

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