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
			Visible = true;
		}

		public void OnCancelClicked()
		{
			Client.ReconnectCancel();
			Visible = false;
		}

		public void OnCloseScreen()
		{
			Visible = false;
		}
	}

}