using System;
using TMPro;

namespace FishMMO.Client
{
	public class UIConfirmationTooltip : UIControl
	{
		public TMP_Text DialogueLabel;

		private Action onAccept;
		private Action onCancel;

		public override void OnStarting()
		{
		}

		public override void OnDestroying()
		{
		}

		public void Open(string text, Action onAccept, Action onCancel = null)
		{
			if (Visible)
			{
				return;
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