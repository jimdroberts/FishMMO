using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	/// <summary>
	/// UI control for displaying reconnect attempt progress when the client
	/// loses connection. Shows an attempt counter, cancel button, and manages
	/// mouse mode for user interaction during reconnection.
	/// </summary>
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
		/// Whether the reconnect currently running is a deliberate scene handoff rather than a
		/// dropped connection.
		/// </summary>
		/// <remarks>
		/// A zone change, a channel switch and a cross-scene bind-point respawn are all
		/// implemented as a deliberate drop, so this panel was raised — and mouse mode forced
		/// on, taking the camera out of the player's hands — on every routine teleport. The
		/// counter and cancel button are hidden on a first attempt, so what appeared was a
		/// bare panel over the loading overlay announcing a connection loss that had not
		/// happened. The loading screen already makes exactly this distinction; see
		/// <see cref="ClientConnectionManager.IsSceneHandoffReconnect"/>.
		/// <para>
		/// Only the first attempt is exempt: a handoff succeeds on its first retry, so anything
		/// past that is a genuine failure the player should be told about.
		/// </para>
		/// </remarks>
		private bool IsSceneHandoff() => Client?.Connection?.IsSceneHandoffReconnect ?? false;

		/// <summary>
		/// Event handler for when the reconnect attempt count changes. Updates UI and shows/hides controls.
		/// </summary>
		/// <param name="attempts">The current attempt number.</param>
		/// <param name="maxAttempts">The maximum number of allowed attempts.</param>
		public void OnReconnectAttemptsChanged(int attempts, int maxAttempts)
		{
			// A deliberate scene handoff is not an outage — see IsSceneHandoff.
			if (attempts <= 1 && IsSceneHandoff())
			{
				return;
			}

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
				PlayerInputController.MouseMode = true;
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