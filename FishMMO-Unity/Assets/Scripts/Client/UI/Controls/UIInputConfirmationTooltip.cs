using System;
using TMPro;

namespace FishMMO.Client
{
	public class UIInputConfirmationTooltip : UIControl
	{
		public TMP_Text DialogueLabel;

		private Action<string> OnAccept;
		private Action OnCancel;

		public override void OnStarting()
		{
		}

		public override void OnDestroying()
		{
		}

		public void Open(string text, Action<string> onAccept, Action onCancel = null)
		{
			if (Visible)
				return;

			if (DialogueLabel != null)
			{
				DialogueLabel.text = text;
			}
			if (InputField != null)
			{
				InputField.text = "";
			}
			OnAccept = onAccept;
			OnCancel = onCancel;
			Visible = true;
		}

		public void OnClick_Accept()
		{
			if (InputField != null &&
				!string.IsNullOrWhiteSpace(InputField.text))
			{
				OnAccept?.Invoke(InputField.text);
			}
			OnClick_Cancel();
		}

		public void OnClick_Cancel()
		{
			OnCancel?.Invoke();

			OnAccept = null;
			OnCancel = null;

			Visible = false;
		}
	}
}