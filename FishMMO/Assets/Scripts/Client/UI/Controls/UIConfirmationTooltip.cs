using System;
using TMPro;

namespace Client
{
	public class UIConfirmationTooltip : UIControl
	{
		public TMP_Text dialogueLabel;

		private Action OnAccept;
		private Action OnCancel;

		public override void OnStarting()
		{
		}

		public override void OnDestroying()
		{
		}

		public void Open(string text, Action onAccept, Action onCancel = null)
		{
			if (visible)
				return;

			if (dialogueLabel != null)
			{
				dialogueLabel.text = text;
			}
			OnAccept = onAccept;
			OnCancel = onCancel;
			visible = true;
		}

		public void OnClick_Accept()
		{
			OnAccept?.Invoke();
			visible = false;
		}

		public void OnClick_Cancel()
		{
			OnCancel?.Invoke();
			visible = false;
		}
	}
}