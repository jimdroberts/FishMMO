using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	public class UIReconnectDisplay : UIControl
	{
		[Header("Reconnect Screen Parameters")]
		/// <summary>
		/// The button to cancel the reconnect attempt.
		/// </summary>
		public Button CancelButton;
		/// <summary>
		/// The text label for the cancel button.
		/// </summary>
		public TMP_Text CancelButtonText;
		/// <summary>
		/// The text label displaying the current reconnect attempt count.
		/// </summary>
		public TMP_Text AttemptCounterText;

		/// <summary>
		/// Called when the client is set. Subscribes to reconnect and connection events.
		/// </summary>
		public override void OnClientSet()
		{
			Client.OnReconnectAttempt += OnReconnectAttemptsChanged;
			Client.OnConnectionSuccessful += OnCloseScreen;
			Client.OnReconnectFailed += OnCloseScreen;
		}

		/// <summary>
		/// Called when the client is unset. Unsubscribes from reconnect and connection events.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.OnReconnectAttempt -= OnReconnectAttemptsChanged;
			Client.OnConnectionSuccessful -= OnCloseScreen;
			Client.OnReconnectFailed -= OnCloseScreen;
		}

		/// <summary>
		/// Event handler for when the reconnect attempt count changes. Updates UI and shows/hides controls.
		/// </summary>
		/// <param name="attempts">The current attempt number.</param>
		/// <param name="maxAttempts">The maximum number of allowed attempts.</param>
		public void OnReconnectAttemptsChanged(byte attempts, byte maxAttempts)
		{
			if (attempts <= maxAttempts)
			{
				// Show attempt counter and cancel button only if more than one attempt.
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

				// Enable mouse mode for user interaction during reconnect.
				InputManager.MouseMode = true;
			}
			else
			{
				// If attempts exceed max, quit to login screen.
				Client.QuitToLogin();
			}
		}

		/// <summary>
		/// Event handler for when the cancel button is clicked. Cancels reconnect and hides the UI.
		/// </summary>
		public void OnCancelClicked()
		{
			Client.ReconnectCancel();
			Hide();
		}

		/// <summary>
		/// Event handler for when the reconnect screen should be closed. Hides the UI.
		/// </summary>
		public void OnCloseScreen()
		{
			Hide();
		}
	}
}