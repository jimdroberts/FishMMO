using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit implementation of the reconnect display. Shows reconnect attempt progress
	/// and allows cancelling the reconnect attempt.
	/// </summary>
	public class UITKReconnectDisplay : UITKControl
	{
		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.System;

		/// <summary>
		/// The name of the cancel button in the UI.
		/// </summary>
		private const string CANCEL_BUTTON_NAME = "reconnect-cancel-btn";
		/// <summary>
		/// The name of the attempt counter Label in the UI.
		/// </summary>
		private const string ATTEMPT_COUNTER_NAME = "reconnect-attempt-counter";

		private Button cancelButton;
		private Label attemptCounterText;

		/// <summary>
		/// Resolves cached elements and wires up the cancel button.
		/// </summary>
		public override void OnStarting()
		{
			if (Root == null)
			{
				return;
			}

			cancelButton = Root.Q<Button>(CANCEL_BUTTON_NAME);
			attemptCounterText = Root.Q<Label>(ATTEMPT_COUNTER_NAME);

			if (cancelButton != null)
			{
				cancelButton.clicked += OnCancelClicked;
			}
		}

		/// <summary>
		/// Subscribes to reconnect and connection events when the client is injected.
		/// </summary>
		public override void OnClientSet()
		{
			Client.OnReconnectAttempt += OnReconnectAttemptsChanged;
			Client.OnConnectionSuccessful += OnCloseScreen;
			Client.OnReconnectFailed += OnCloseScreen;
		}

		/// <summary>
		/// Unsubscribes from reconnect and connection events when the client is cleared.
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
		/// Updates the UI and shows/hides controls when the reconnect attempt count changes.
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
				if (attemptCounterText != null)
				{
					attemptCounterText.style.display = attempts > 1 ? DisplayStyle.Flex : DisplayStyle.None;
					attemptCounterText.text = $"Attempt {attempts} of {maxAttempts}...";
				}

				if (cancelButton != null)
				{
					cancelButton.style.display = attempts > 1 ? DisplayStyle.Flex : DisplayStyle.None;
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
		/// Cancels the reconnect attempt and hides the UI.
		/// </summary>
		public void OnCancelClicked()
		{
			Client.ReconnectCancel();
			Hide();
		}

		/// <summary>
		/// Hides the reconnect screen.
		/// </summary>
		public void OnCloseScreen()
		{
			Hide();
		}
	}
}
