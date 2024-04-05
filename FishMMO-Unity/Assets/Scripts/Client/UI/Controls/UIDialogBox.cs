using System;
using TMPro;
using UnityEngine.UI;

namespace FishMMO.Client
{
	public class UIDialogBox : UIControl
	{
		public TMP_Text DialogueLabel;
		public Button AcceptButton;
		public Button CancelButton;
		public TMP_Text CancelButtonLabel;

		private Action onAccept;
		private Action onCancel;

		public override void OnStarting()
		{
			if (CancelButton != null)
			{
				CancelButtonLabel = CancelButton.GetComponentInChildren<TMP_Text>();
			}
		}

		public override void OnDestroying()
		{
		}

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

			// if the accept button is active the cancel button can be enabled or disabled
			if (AcceptButton.gameObject.activeSelf)
			{
				CancelButton.gameObject.SetActive(onCancel != null);

				// validate Cancel buttons label
				if (CancelButtonLabel != null)
				{
					CancelButtonLabel.text = "Cancel";
				}
			}
			// otherwise the cancel button must be available
			else
			{
				CancelButton.gameObject.SetActive(true);

				// rename the Cancel button to Close instead?
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

		public void OnClick_Accept()
		{
			onAccept?.Invoke();

			onAccept = null;
			onCancel = null;

			Hide();
		}

		public void OnClick_Cancel()
		{
			onCancel?.Invoke();

			onAccept = null;
			onCancel = null;

			Hide();
		}
	}
}