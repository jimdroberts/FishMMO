using System;
using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit input dialog. Shows a message with a text field and accept/cancel buttons,
	/// invoking a callback with the entered text.
	/// </summary>
	/// <remarks>
	/// Shares the open/close callback rules with <see cref="UITKDialogBox"/> through
	/// <see cref="UITKCallbackDialog"/>. This panel is where getting them wrong hurt most: it
	/// used to hide on Accept with an empty field and invoke neither callback, and its two live
	/// callers — the account-verification prompt and the TOTP prompt in <c>UITKLogin</c> — have
	/// hidden the login panel and locked sign-in by the time it opens. An answer that never
	/// arrives left the client with no visible panel and no way back to the login screen.
	/// </remarks>
	public class UITKDialogInputBox : UITKCallbackDialog
	{
		/// <summary>
		/// Name of the dialog message label element.
		/// </summary>
		private const string DIALOG_LABEL_NAME = "dialog-input-label";
		/// <summary>
		/// Name of the text input field element.
		/// </summary>
		private const string INPUT_FIELD_NAME = "dialog-input-field";
		/// <summary>
		/// Name of the accept button element.
		/// </summary>
		private const string ACCEPT_BUTTON_NAME = "dialog-input-accept-btn";
		/// <summary>
		/// Name of the cancel button element.
		/// </summary>
		private const string CANCEL_BUTTON_NAME = "dialog-input-cancel-btn";

		/// <summary>
		/// The dialog message label.
		/// </summary>
		private Label dialogLabel;
		/// <summary>
		/// The text input field.
		/// </summary>
		private TextField inputField;
		/// <summary>
		/// The accept button.
		/// </summary>
		private Button acceptButton;
		/// <summary>
		/// The cancel button.
		/// </summary>
		private Button cancelButton;

		/// <summary>
		/// Callback invoked with the entered text when the accept button is clicked.
		/// </summary>
		private Action<string> onAcceptCallback;
		/// <summary>
		/// Callback invoked when the cancel button is clicked.
		/// </summary>
		private Action onCancelCallback;

		/// <summary>
		/// Message the current request wants displayed.
		/// </summary>
		private string pendingText = string.Empty;

		/// <summary>
		/// Whether the current request's answer is a secret and must be masked on screen.
		/// </summary>
		/// <remarks>
		/// Carried as request state rather than set directly on the field, for the same reason the
		/// message is: <see cref="UITKControl.Show()"/> re-clones the UXML, so anything written to
		/// the old <see cref="TextField"/> is discarded. It is applied in
		/// <see cref="ApplyRequest"/> against the live tree, and — importantly — cleared in
		/// <see cref="ClearRequest"/>, so a masked prompt cannot leave the shared dialog masked
		/// for the next caller that asks for an ordinary line of text.
		/// </remarks>
		private bool pendingMasked;

		/// <summary>
		/// Resolves cached elements and wires button callbacks.
		/// </summary>
		public override void OnStarting()
		{
			if (Root == null)
			{
				return;
			}

			dialogLabel = Root.Q<Label>(DIALOG_LABEL_NAME);
			inputField = Root.Q<TextField>(INPUT_FIELD_NAME);
			acceptButton = Root.Q<Button>(ACCEPT_BUTTON_NAME);
			cancelButton = Root.Q<Button>(CANCEL_BUTTON_NAME);

			if (acceptButton != null)
			{
				acceptButton.clicked += OnClick_Accept;
			}
			if (cancelButton != null)
			{
				cancelButton.clicked += OnClick_Cancel;
			}

			AttachDialogKeys(Root);
		}

		/// <summary>
		/// Opens the input dialog with the specified message and optional callbacks.
		/// </summary>
		/// <param name="text">Message to display.</param>
		/// <param name="onAccept">Optional callback invoked with the entered text when accepted.</param>
		/// <param name="onCancel">Optional callback invoked when cancelled.</param>
		/// <returns>
		/// False when a prompt was already on screen and this one was refused. A refused request
		/// is answered immediately through <paramref name="onCancel"/>.
		/// </returns>
		public bool Open(string text, Action<string> onAccept = null, Action onCancel = null)
		{
			return Open(text, false, onAccept, onCancel);
		}

		/// <summary>
		/// Opens the prompt, optionally masking what the player types.
		/// </summary>
		/// <param name="text">The message shown above the field.</param>
		/// <param name="masked">
		/// True when the answer is a secret — an account password, a recovery passphrase — and must
		/// not be rendered in clear text.
		/// </param>
		/// <param name="onAccept">Optional callback invoked with the entered text when accepted.</param>
		/// <param name="onCancel">Optional callback invoked when cancelled.</param>
		/// <returns>
		/// False when a prompt was already on screen and this one was refused. A refused request
		/// is answered immediately through <paramref name="onCancel"/>.
		/// </returns>
		/// <remarks>
		/// This overload exists because the recovery-code store needs the account password to
		/// derive its decryption key, and the only prompt available rendered it in clear text on
		/// screen. Masking is off by default so every existing caller keeps its current behaviour.
		/// </remarks>
		public bool Open(string text, bool masked, Action<string> onAccept = null, Action onCancel = null)
		{
			if (!TryClaim())
			{
				onCancel?.Invoke();
				return false;
			}

			pendingText = text ?? string.Empty;
			pendingMasked = masked;
			onAcceptCallback = onAccept;
			onCancelCallback = onCancel;

			Show();
			return true;
		}

		/// <summary>
		/// Writes the current request's message into the live tree and empties the field.
		/// </summary>
		/// <remarks>
		/// The field is cleared here rather than in <see cref="Open"/> so the previous
		/// prompt's answer — a verification code, a password — cannot survive into the next one
		/// on the re-cloned tree.
		/// </remarks>
		protected override void ApplyRequest()
		{
			if (dialogLabel != null)
			{
				dialogLabel.text = pendingText;
			}
			if (inputField != null)
			{
				inputField.value = string.Empty;

				/* Applied here, against the tree the player will actually see. Setting it in Open
				 * would write to the tree Show() is about to discard, and the field would come
				 * back unmasked. */
				inputField.isPasswordField = pendingMasked;
			}
		}

		/// <summary>
		/// Drops the callbacks, the message and the typed text this request was opened with.
		/// </summary>
		protected override void ClearRequest()
		{
			onAcceptCallback = null;
			onCancelCallback = null;
			pendingText = string.Empty;

			// Codes and passwords are typed in here; do not leave one sitting in the field.
			if (inputField != null)
			{
				inputField.value = string.Empty;

				// The shared dialog is reused: an unreset mask would silently hide the next
				// caller's ordinary text input.
				inputField.isPasswordField = false;
			}
			pendingMasked = false;
		}

		/// <summary>
		/// Puts the caret in the field, which is the only thing the player can usefully do here.
		/// </summary>
		protected override void FocusDefault()
		{
			inputField?.Focus();
		}

		/// <summary>
		/// Enter submits, which is what a player typing a code expects.
		/// </summary>
		protected override void OnSubmitKey()
		{
			OnClick_Accept();
		}

		/// <summary>
		/// Handles accept button clicks.
		/// </summary>
		/// <remarks>
		/// An empty field is a refusal, not a no-op. This used to hide the dialog and invoke
		/// neither callback, which is the unrecoverable case described on the class.
		/// </remarks>
		private void OnClick_Accept()
		{
			string value = inputField != null ? inputField.value : string.Empty;

			if (string.IsNullOrWhiteSpace(value))
			{
				CancelRequest();
				return;
			}

			Action<string> callback = onAcceptCallback;
			Resolve(() => callback?.Invoke(value));
		}

		/// <summary>
		/// Handles cancel button clicks, invoking the cancel callback and closing the dialog.
		/// </summary>
		private void OnClick_Cancel()
		{
			CancelRequest();
		}

		/// <summary>
		/// Answers the request down its cancel path. Escape, quit-to-login, an empty accept and
		/// a bare <see cref="UITKControl.Hide()"/> all arrive here.
		/// </summary>
		protected override void CancelRequest()
		{
			Action callback = onCancelCallback;
			Resolve(() => callback?.Invoke());
		}
	}
}
