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
		/// <remarks>
		/// Deliberately one tier above <see cref="UITKLoadingScreen"/>. The two are on screen
		/// together for the whole of a reconnect and used to share <c>System</c>, where the
		/// tie-break is registration order and the loser receives no pointer events — see
		/// <see cref="UITKPanelLayer.SystemAlert"/>.
		/// </remarks>
		protected override UITKPanelLayer Layer => UITKPanelLayer.SystemAlert;

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

			/* Escape cancels. The scene flags leave CloseOnEscape off for this panel — correctly,
			 * because UIManager.CloseNext would only Hide() it and leave the reconnect running
			 * behind a screen the player can no longer see — so the key is wired here instead and
			 * routed through the same handler the button uses. */
			LoginKeys.Attach(this, Root, onSubmit: null, onCancel: OnCancelClicked);
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
		/// counter is hidden on a first attempt, so what appeared was a nearly bare panel over
		/// the loading overlay announcing a connection loss that had not
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
				/* State first, tree second. Writing straight into the elements here and then
				 * calling Show() lost every write: enabling the UIDocument re-clones the UXML, so
				 * the attempt counter this method had just filled in belonged to a tree that was
				 * discarded microseconds later and the player read the UXML's empty placeholder.
				 * ApplyState runs from OnAfterShow/OnAfterStarting against the live tree. */
				this.shownAttempts = attempts;
				this.shownMaxAttempts = maxAttempts;

				Show();

				// Already visible: Show() is a no-op and would not have called OnAfterShow.
				// ApplyState is also what claims the cursor for the Cancel button.
				ApplyState();
			}
			else
			{
				// If attempts exceed max, quit to login screen.
				Client.QuitToLogin();
			}
		}

		/// <summary>Attempt number the panel is currently reporting.</summary>
		private int shownAttempts;
		/// <summary>Attempt ceiling the panel is currently reporting.</summary>
		private int shownMaxAttempts;

		/// <summary>
		/// Writes the current attempt state into the live visual tree.
		/// </summary>
		/// <remarks>
		/// Runs on every show and after every tree rebuild, so it must be idempotent and tolerate
		/// elements that are still null. See <see cref="UITKControl.OnAfterShow"/>.
		/// </remarks>
		private void ApplyState()
		{
			// The counter appears only past the first attempt; a single retry is over before the
			// player could read it, and a bare "attempt 1 of 10" on a routine hiccup reads worse
			// than nothing.
			if (attemptCounterText != null)
			{
				attemptCounterText.style.display = this.shownAttempts > 1 ? DisplayStyle.Flex : DisplayStyle.None;
				attemptCounterText.text = $"Attempt {this.shownAttempts} of {this.shownMaxAttempts}...";
			}

			/* Cancel, on the other hand, is shown whenever this panel is. It used to be gated on
			 * the same attempts > 1, which produced a state with no way out of it: this panel sits
			 * a tier above the loading overlay and its full-bleed root takes the pointer events, so
			 * for the whole of a first attempt the player had a bare panel with nothing on it
			 * covering the overlay's own "Return to Login" button. A visible panel that offers
			 * nothing is exactly the class of state this pass exists to remove. */
			if (cancelButton != null)
			{
				cancelButton.style.display = DisplayStyle.Flex;
			}

			/* Claim the cursor for as long as this panel is up, rather than merely switching
			 * MouseMode on. The panel is authored ReleasesCursor: 0 and setting the mode directly
			 * did not survive a frame: PlayerInputController.HandleAutoDismiss recaptures the
			 * cursor whenever no VISIBLE panel claims it through ReleasesCursor, so a reconnect
			 * that began during gameplay — where the cursor is locked — put a Cancel button on
			 * screen that the player could not reach, on the one panel whose entire purpose is to
			 * offer a way out. Cleared again in Hide(). */
			if (!ReleasesCursor)
			{
				ReleasesCursor = true;
			}

			if (!PlayerInputController.MouseMode)
			{
				PlayerInputController.MouseMode = true;
			}
		}

		/// <summary>
		/// Hides the panel and hands the cursor back.
		/// </summary>
		/// <remarks>
		/// Overrides <c>Hide(bool)</c>, not <c>Hide()</c>. <c>Hide()</c> is non-virtual and only
		/// forwards here, so this is the one place the cursor claim can be released where every
		/// caller — the cancel button, the reconnect outcome handlers and the quit-to-login
		/// teardown, which calls the bool form directly — reaches it. Leaving the claim set would
		/// hold the cursor released for the rest of the session, because
		/// <c>HandleAutoDismiss</c> asks whether any panel still wants it and this one would keep
		/// saying yes.
		/// </remarks>
		/// <param name="overrideIsAlwaysOpen">When true, the call is a no-op.</param>
		public override void Hide(bool overrideIsAlwaysOpen)
		{
			base.Hide(overrideIsAlwaysOpen);

			if (overrideIsAlwaysOpen || Document == null)
			{
				// The base refused the hide; the panel is still up and still needs the cursor.
				return;
			}

			ReleasesCursor = false;
		}

		/// <inheritdoc/>
		protected override void OnAfterShow()
		{
			base.OnAfterShow();
			ApplyState();
		}

		/// <inheritdoc/>
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			ApplyState();
		}

		/// <summary>
		/// Cancels the reconnect attempt and hides the UI.
		/// </summary>
		/// <remarks>
		/// Cancelling a reconnect abandons the session, so it has to land somewhere the player can
		/// act from. <c>Hide()</c> alone left the loading overlay behind it holding the screen with
		/// its drivers still set and no panel underneath — see the H1 dead-end. Quitting to login
		/// is the established teardown and every panel already implements it.
		/// </remarks>
		public void OnCancelClicked()
		{
			Client.ReconnectCancel();
			Hide();
			Client.QuitToLogin();
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
