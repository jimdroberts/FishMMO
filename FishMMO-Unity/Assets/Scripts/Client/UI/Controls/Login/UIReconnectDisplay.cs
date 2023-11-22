using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	public class UIReconnectDisplay : UIControl
	{
		[Header("Reconnect Screen Parameters")]
		public Button CancelButton;
		public TMP_Text CancelButtonText;
		public TMP_Text AttemptCounterText;

		public override void OnStarting()
		{
			Client.OnReconnectAttempt += OnReconnectAttemptsChanged;
			Client.OnConnectionSuccessful += OnCloseScreen;
			Client.OnReconnectFailed += OnCloseScreen;
		}

		public override void OnDestroying()
		{
			Client.OnReconnectAttempt -= OnReconnectAttemptsChanged;
			Client.OnConnectionSuccessful -= OnCloseScreen;
			Client.OnReconnectFailed -= OnCloseScreen;
		}

		public void OnReconnectAttemptsChanged(byte attempts, byte maxAttempts)
		{
			AttemptCounterText.text = $"Attempt {attempts} of {maxAttempts}";
			Show();

			if (attempts >= maxAttempts)
			{
				Client.QuitToLogin();
			}
		}

		public void OnCancelClicked()
		{
			Client.ReconnectCancel();
			Hide();
		}

		public void OnCloseScreen()
		{
			Hide();
		}
	}
}