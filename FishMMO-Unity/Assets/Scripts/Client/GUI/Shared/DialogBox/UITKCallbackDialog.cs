using System;
using UnityEngine.UIElements;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// Base class for the shared, singleton, answer-with-a-callback panels: the confirmation
	/// dialog, the text-input dialog and the list selector.
	/// </summary>
	/// <remarks>
	/// The three of them are one panel each for the whole client, so every caller in the game
	/// writes its callbacks into the same fields. That makes the lifetime of those callbacks the
	/// whole problem, and each of the three used to get a different part of it wrong. The rules
	/// enforced here are:
	/// <para>
	/// <b>1. Refuse, never hijack.</b> An <c>Open</c> that arrives while a request is already on
	/// screen is declined; it does not overwrite the message and the callbacks of the question
	/// the player is currently reading. Without this a guild or party invite arriving on its own
	/// timer replaces the body of an open confirmation while leaving the buttons where they are,
	/// so "Yes" answers a question the player never saw.
	/// </para>
	/// <para>
	/// <b>2. Exactly one callback per request, on every exit path.</b> Accept, cancel, the close
	/// button, Escape, a programmatic <see cref="UITKControl.Hide()"/>, quit-to-login and a
	/// refused <c>Open</c> all resolve the request exactly once. Callers lock themselves while a
	/// dialog is up — <c>UITKLogin</c> hides its own panel and locks sign-in before opening the
	/// verification prompt — so a path that answers with neither callback leaves the client with
	/// no way back.
	/// </para>
	/// <para>
	/// <b>3. Callbacks are cleared before they are invoked, never after.</b> Clearing afterwards
	/// loses the race against a callback that re-opens the dialog, and leaves the previous
	/// caller armed on a shared panel in the meantime.
	/// </para>
	/// <para>
	/// Per-open content is applied from <see cref="ApplyRequest"/>, which runs from both
	/// <see cref="UITKControl.OnAfterShow"/> and <see cref="UITKControl.OnAfterStarting"/>.
	/// Writing it before <c>Show()</c> does not work — the document re-clones the UXML on enable
	/// — and writing it only in <c>OnAfterShow</c> misses the very first open, where the visual
	/// tree does not exist yet and <c>ReinitializeIfTreeReplaced</c> bails out.
	/// </para>
	/// </remarks>
	public abstract class UITKCallbackDialog : UITKControl
	{
		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Modal;

		/// <summary>
		/// True while a caller is waiting for an answer.
		/// </summary>
		/// <remarks>
		/// Tracked separately from <see cref="UITKControl.Visible"/>: a request is armed from the
		/// instant <see cref="TryClaim"/> succeeds, which is before <c>Show()</c> has run, and it
		/// has to stay armed across the frame where the document is enabling.
		/// </remarks>
		protected bool RequestArmed { get; private set; }

		/// <summary>
		/// Guards against a second resolution while one is in flight.
		/// </summary>
		/// <remarks>
		/// <see cref="Resolve"/> hides the panel as its last act, which re-enters
		/// <see cref="Hide(bool)"/>; and a callback is free to open a fresh dialog from inside
		/// its own invocation. Both would otherwise produce a second callback for one request.
		/// </remarks>
		private bool resolving;

		/// <summary>
		/// Claims the panel for a new request, or refuses when one is already outstanding.
		/// </summary>
		/// <returns>True when the caller may proceed to fill in and show the dialog.</returns>
		protected bool TryClaim()
		{
			if (RequestArmed || Visible)
			{
				Log.Debug(GetType().Name, $"[{Name}] Refused a request; one is already on screen.");
				return false;
			}

			RequestArmed = true;
			return true;
		}

		/// <summary>
		/// Answers the current request exactly once, then closes the panel.
		/// </summary>
		/// <param name="answer">
		/// The single callback for this exit path, already captured by the caller. May be null
		/// when the request was opened without one — the request is still consumed.
		/// </param>
		/// <remarks>
		/// <see cref="ClearRequest"/> runs before <paramref name="answer"/> so the panel is
		/// already unarmed when the callback runs: these callbacks quit to login, disconnect and
		/// open further dialogs, and any of that re-entering a still-armed panel would answer the
		/// same request twice.
		/// </remarks>
		protected void Resolve(Action answer)
		{
			if (this.resolving)
			{
				return;
			}

			this.resolving = true;
			try
			{
				RequestArmed = false;
				ClearRequest();

				answer?.Invoke();

				Hide();
			}
			finally
			{
				this.resolving = false;
			}
		}

		/// <summary>
		/// Drops every callback and every piece of per-open state the subclass is holding.
		/// </summary>
		/// <remarks>
		/// Called from <see cref="Resolve"/> before the answer is invoked. Implementations must
		/// not invoke anything from here.
		/// </remarks>
		protected abstract void ClearRequest();

		/// <summary>
		/// Answers the current request down its cancel path.
		/// </summary>
		/// <remarks>
		/// This is what Escape, the close button, quit-to-login and any other dismissal that is
		/// not an explicit accept go through. Implementations capture their cancel callback and
		/// hand it to <see cref="Resolve"/>.
		/// </remarks>
		protected abstract void CancelRequest();

		/// <summary>
		/// Writes this request's content into the live visual tree.
		/// </summary>
		/// <remarks>
		/// Runs on every show and after every tree rebuild, so it must be idempotent and must
		/// tolerate elements that are still null.
		/// </remarks>
		protected abstract void ApplyRequest();

		/// <summary>
		/// Applies the pending request to the tree the player will actually see.
		/// </summary>
		protected override void OnAfterShow()
		{
			ApplyRequest();
			FocusDefault();
		}

		/// <summary>
		/// Re-applies the pending request after the visual tree was rebuilt.
		/// </summary>
		protected override void OnAfterStarting()
		{
			if (!RequestArmed)
			{
				return;
			}
			ApplyRequest();
		}

		/// <summary>
		/// Moves keyboard focus somewhere useful when the dialog opens.
		/// </summary>
		/// <remarks>
		/// Something in the panel has to hold focus or there is no element for a key press to be
		/// dispatched to, and the Enter/Escape handling below never sees anything.
		/// <para>
		/// The default is the panel root rather than a button. A focused UI Toolkit
		/// <c>Button</c> is activated by the space bar, so focusing Accept would let a player
		/// who happens to be holding a movement key answer a confirmation they have not read —
		/// and these dialogs arrive unprompted, on someone else's timing. The root is focusable
		/// but activates nothing, so Enter and Escape go through the explicit handling here and
		/// nothing else does anything at all. Tab still reaches the buttons for a player
		/// deliberately navigating them.
		/// </para>
		/// </remarks>
		protected virtual void FocusDefault()
		{
			Root?.Focus();
		}

		/// <summary>
		/// Wires the shared Enter/Escape handling onto the panel root.
		/// </summary>
		/// <param name="root">The panel root the keys are captured on.</param>
		/// <remarks>
		/// Registered in the trickle-down phase so the keys are read before a focused
		/// <c>TextField</c> or <c>Button</c> consumes them: Return inside a text field would
		/// otherwise be swallowed by the field, which is precisely where the player is typing
		/// when they want to accept.
		/// <para>
		/// Unregister-then-register because this runs again on every tree rebuild, and the
		/// handler is a method group rather than a lambda so the unregister actually matches.
		/// </para>
		/// </remarks>
		protected void AttachDialogKeys(VisualElement root)
		{
			if (root == null)
			{
				return;
			}

			// Focusable so it can hold focus itself; see FocusDefault.
			root.focusable = true;

			root.UnregisterCallback<KeyDownEvent>(OnDialogKeyDown, TrickleDown.TrickleDown);
			root.RegisterCallback<KeyDownEvent>(OnDialogKeyDown, TrickleDown.TrickleDown);
		}

		/// <summary>
		/// Handles Escape, Enter and the arrow keys for a dialog.
		/// </summary>
		private void OnDialogKeyDown(KeyDownEvent evt)
		{
			if (!Visible)
			{
				return;
			}

			switch (evt.keyCode)
			{
				case UnityEngine.KeyCode.Escape:
					evt.StopPropagation();
					CancelRequest();
					return;
				case UnityEngine.KeyCode.Return:
				case UnityEngine.KeyCode.KeypadEnter:
					evt.StopPropagation();
					OnSubmitKey();
					return;
				case UnityEngine.KeyCode.UpArrow:
					if (OnNavigateKey(-1))
					{
						evt.StopPropagation();
					}
					return;
				case UnityEngine.KeyCode.DownArrow:
					if (OnNavigateKey(1))
					{
						evt.StopPropagation();
					}
					return;
			}
		}

		/// <summary>
		/// Called when the player presses Enter. Defaults to accepting the dialog.
		/// </summary>
		protected virtual void OnSubmitKey() { }

		/// <summary>
		/// Called when the player presses an arrow key.
		/// </summary>
		/// <param name="direction">-1 for up, 1 for down.</param>
		/// <returns>True when the key was used and should not travel any further.</returns>
		protected virtual bool OnNavigateKey(int direction)
		{
			return false;
		}

		/// <summary>
		/// Hides the panel, answering any outstanding request on the way out.
		/// </summary>
		/// <param name="overrideIsAlwaysOpen">When true, the call is a no-op.</param>
		/// <remarks>
		/// This is the catch-all that makes rule 2 hold. Escape goes through
		/// <c>UIManager.CloseNext</c> -> <see cref="UITKControl.Hide()"/>, quit-to-login goes
		/// through <c>Hide(false)</c>, and panels call <c>Hide()</c> on these dialogs directly.
		/// None of those knew anything about the callbacks, so all three used to close the box
		/// and leave whoever opened it waiting for an answer that could never arrive.
		/// </remarks>
		public override void Hide(bool overrideIsAlwaysOpen)
		{
			if (overrideIsAlwaysOpen || Document == null)
			{
				/* Refuse the hide exactly the way the base class does, and leave the request
				 * armed: the dialog is still on screen, so the player can still answer it. */
				base.Hide(overrideIsAlwaysOpen);
				return;
			}

			if (RequestArmed && !this.resolving)
			{
				// Dismissed without an explicit answer. Cancel is the answer.
				CancelRequest();
				return;
			}

			base.Hide(overrideIsAlwaysOpen);
		}

		/// <summary>
		/// Answers any outstanding request before the panel goes away.
		/// </summary>
		public override void OnDestroying()
		{
			if (RequestArmed && !this.resolving)
			{
				CancelRequest();
			}
		}
	}
}
