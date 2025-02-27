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

		public override void OnClientSet()
		{
			Client.OnReconnectAttempt += OnReconnectAttemptsChanged;
			Client.OnConnectionSuccessful += OnCloseScreen;
			Client.OnReconnectFailed += OnCloseScreen;
		}

		public override void OnClientUnset()
		{
			Client.OnReconnectAttempt -= OnReconnectAttemptsChanged;
			Client.OnConnectionSuccessful -= OnCloseScreen;
			Client.OnReconnectFailed -= OnCloseScreen;
		}

		public void OnReconnectAttemptsChanged(byte attempts, byte maxAttempts)
		{
			if (attempts <= maxAttempts)
			{
				if (AttemptCounterText != null)
				{
					AttemptCounterText.gameObject.SetActive(attempts > 1);
					AttemptCounterText.text = $"Attempt {attempts} of {maxAttempts}...";
				}

				if (CancelButton != null)
				{
					CancelButton.gameObject.SetActive(attempts > 1);
				}

				Show();

				InputManager.MouseMode = true;
			}
			else
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