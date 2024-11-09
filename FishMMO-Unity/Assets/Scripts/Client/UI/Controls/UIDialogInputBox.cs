using System;
using TMPro;

namespace FishMMO.Client
{
	public class UIDialogInputBox : UIControl
	{
		public TMP_Text DialogueLabel;

		private Action<string> onAccept;
		private Action onCancel;

		public override void OnStarting()
		{
		}

		public override void OnDestroying()
		{
		}

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

		public void OnClick_Cancel()
		{
			onCancel?.Invoke();

			onAccept = null;
			onCancel = null;

			Hide();
		}
	}
}