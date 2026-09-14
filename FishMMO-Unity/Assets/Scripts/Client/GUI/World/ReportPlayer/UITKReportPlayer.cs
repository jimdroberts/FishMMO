using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishNet.Transporting;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// The report-a-player form, opened from a character's context menu.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A front end for the same filing <c>/report</c> does. It sends one
	/// <see cref="ReportPlayerBroadcast"/> and waits for the server's
	/// <see cref="ReportPlayerResultBroadcast"/>; the server resolves the target, applies the limits
	/// and chooses the words. Nothing here decides whether a report may be filed beyond the two
	/// things the player can see for themselves — a reason and a description.
	/// </para>
	/// <para>
	/// <b>The draft lives on the component, not in the tree.</b> <c>UIDocument</c> re-clones the
	/// UXML on every show, so a reason or a paragraph held only in the fields would be gone the
	/// next time the panel appeared. Every change is copied into the model as it happens and the
	/// fields are rewritten from the model on every show.
	/// </para>
	/// <para>
	/// <b>A refusal keeps the text; a filing closes the form.</b> The most common refusal is the
	/// cooldown, and a player told to wait a minute should not also have to write their report
	/// again. A successful filing writes the server's line — with the ticket number — into chat
	/// before the form closes, so the number is still on screen afterwards.
	/// </para>
	/// </remarks>
	public class UITKReportPlayer : UITKControl
	{
		/// <summary>The name this panel registers under.</summary>
		public const string PanelName = "UIReportPlayer";

		/// <summary>Seconds to wait for the server's answer before giving the form back to the player.</summary>
		public const float ResultTimeoutSeconds = 10f;

		/// <summary>Submit button text at rest.</summary>
		public const string SubmitText = "Submit";

		/// <summary>Submit button text while the server has the report.</summary>
		public const string SendingText = "Sending...";

		private const string TitleName = "report-title";
		private const string ReasonName = "report-reason";
		private const string DescriptionName = "report-description";
		private const string CounterName = "report-counter";
		private const string StatusName = "report-status";
		private const string SubmitName = "report-submit";
		private const string CancelName = "report-cancel";
		private const string CloseName = "close-button";

		/// <summary>Theme class for a status line that reports a refusal.</summary>
		private const string ErrorClass = "fish-label--danger";

		/// <summary>The reason names, in enum order, so a list index is a reason value.</summary>
		private static readonly List<string> ReasonChoices = BuildReasonChoices();

		// ── Model ───────────────────────────────────────────────────────────────────────────

		private long targetCharacterID;
		private string targetName = string.Empty;
		private int reasonIndex = -1;
		private string description = string.Empty;
		private bool awaitingResult;
		private float awaitingSince;
		private string statusText = string.Empty;
		private bool statusIsError;

		// ── View ────────────────────────────────────────────────────────────────────────────

		private Label titleLabel;
		private DropdownField reasonField;
		private TextField descriptionField;
		private Label counterLabel;
		private Label statusLabel;
		private Button submitButton;

		/// <summary>Draws above ordinary windows, with the shared dialogs.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Modal;

		// ── Public surface ──────────────────────────────────────────────────────────────────

		/// <summary>The character being reported, or 0 when only a name is known.</summary>
		public long TargetCharacterID => targetCharacterID;

		/// <summary>The name being reported.</summary>
		public string TargetName => targetName;

		/// <summary>True between sending a report and hearing back about it.</summary>
		public bool AwaitingResult => awaitingResult;

		/// <summary>The status line's current text.</summary>
		public string StatusText => statusText;

		/// <summary>
		/// Whether Submit would send: a reason is chosen, something is written, and nothing is in flight.
		/// </summary>
		public bool CanSubmit =>
			!awaitingResult &&
			reasonIndex >= 0 && reasonIndex < PlayerReportReasons.Count &&
			!string.IsNullOrWhiteSpace(description) &&
			(targetCharacterID > 0 || !string.IsNullOrWhiteSpace(targetName));

		/// <summary>
		/// Opens the registered report panel for a character.
		/// </summary>
		/// <param name="characterID">The character's id, or 0 when only a name is known.</param>
		/// <param name="characterName">The character's name as this client shows it.</param>
		/// <returns>False when no report panel is registered.</returns>
		/// <remarks>
		/// The one-liner every context menu uses, so the lookup by name is written once.
		/// </remarks>
		public static bool TryOpen(long characterID, string characterName)
		{
			if (!UIManager.TryGetTK(PanelName, out UITKReportPlayer panel))
			{
				return false;
			}
			panel.Open(characterID, characterName);
			return true;
		}

		/// <summary>
		/// Opens the form for a character, starting a fresh draft unless it is already for them.
		/// </summary>
		/// <param name="characterID">The character's id, or 0 when only a name is known.</param>
		/// <param name="characterName">The character's name as this client shows it.</param>
		/// <remarks>
		/// While a report is with the server the form stays on it: switching the target underneath
		/// an answer that has not arrived would attribute that answer to the wrong person.
		/// </remarks>
		public void Open(long characterID, string characterName)
		{
			string name = (characterName ?? string.Empty).Trim();

			if (!awaitingResult &&
				(characterID != targetCharacterID || !string.Equals(name, targetName, StringComparison.Ordinal)))
			{
				ResetDraft();
				targetCharacterID = characterID > 0 ? characterID : 0;
				targetName = name;
			}

			if (Visible)
			{
				ApplyModel();
				return;
			}
			Show();
		}

		/// <summary>
		/// Applies the server's answer.
		/// </summary>
		/// <param name="result">The answer.</param>
		/// <remarks>
		/// An answer for a form that is no longer open — the player pressed Escape while it was in
		/// flight — still reaches them, in chat, because the report may well have been filed.
		/// </remarks>
		public void ApplyResult(ReportPlayerResultBroadcast result)
		{
			string message = string.IsNullOrWhiteSpace(result.Message)
				? (result.Filed ? "Your report was filed." : "Your report could not be filed.")
				: result.Message;

			if (!Visible || !awaitingResult)
			{
				ShowSystemMessage(message);
				return;
			}

			awaitingResult = false;

			if (result.Filed)
			{
				ShowSystemMessage(message);
				ResetDraft();
				Hide();
				return;
			}

			SetStatus(message, true);
			ApplyModel();
		}

		/// <summary>
		/// Sends the report, when the form is complete.
		/// </summary>
		public void Submit()
		{
			if (!CanSubmit)
			{
				RefreshControls();
				return;
			}

			if (Client == null)
			{
				SetStatus("Not connected.", true);
				RefreshControls();
				return;
			}

			Client.Broadcast(new ReportPlayerBroadcast()
			{
				TargetCharacterID = targetCharacterID,
				TargetCharacterName = targetName,
				Reason = (PlayerReportReason)reasonIndex,
				Description = description.Trim(),
			}, Channel.Reliable);

			awaitingResult = true;
			awaitingSince = Time.unscaledTime;
			SetStatus("Sending your report...", false);
			RefreshControls();
		}

		// ── Lifecycle ───────────────────────────────────────────────────────────────────────

		/// <summary>Resolves the tree's elements and wires its controls.</summary>
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			titleLabel = root.Q<Label>(TitleName);
			counterLabel = root.Q<Label>(CounterName);
			statusLabel = root.Q<Label>(StatusName);

			reasonField = root.Q<DropdownField>(ReasonName);
			if (reasonField != null)
			{
				reasonField.choices = new List<string>(ReasonChoices);
				reasonField.RegisterValueChangedCallback(OnReasonChanged);
			}

			descriptionField = root.Q<TextField>(DescriptionName);
			if (descriptionField != null)
			{
				descriptionField.multiline = true;
				descriptionField.maxLength = ReportPlayerBroadcast.MaxDescriptionLength;
				descriptionField.verticalScrollerVisibility = ScrollerVisibility.Auto;
				descriptionField.RegisterValueChangedCallback(OnDescriptionChanged);
			}

			submitButton = root.Q<Button>(SubmitName);
			if (submitButton != null)
			{
				submitButton.clicked += Submit;
			}

			Button cancel = root.Q<Button>(CancelName);
			if (cancel != null)
			{
				cancel.clicked += Hide;
			}

			Button close = root.Q<Button>(CloseName);
			if (close != null)
			{
				close.clicked += Hide;
			}

			/* Escape is read here as well as through UIManager's Escape list. A focused multiline
			 * field keeps the key to itself, and the description field is where the player's caret
			 * is for almost all of the time this form is open. Trickle-down so it is seen first;
			 * unregister-then-register because OnStarting runs again on every tree rebuild. */
			root.focusable = true;
			root.UnregisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);
			root.RegisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);
		}

		/// <summary>Writes the draft into a rebuilt tree.</summary>
		protected override void OnAfterStarting()
		{
			ApplyModel();
		}

		/// <summary>Writes the draft into the tree the player is about to see, and puts the caret somewhere useful.</summary>
		protected override void OnAfterShow()
		{
			ApplyModel();

			if (reasonIndex < 0)
			{
				reasonField?.Focus();
			}
			else
			{
				descriptionField?.Focus();
			}
		}

		/// <summary>
		/// Closing the form discards the draft, unless the close was refused.
		/// </summary>
		/// <remarks>
		/// Cancel, the close button, Escape and quit-to-login all arrive here. A report still with
		/// the server is let go rather than waited on; its answer is routed to chat by
		/// <see cref="ApplyResult"/>.
		/// </remarks>
		public override void Hide(bool overrideIsAlwaysOpen)
		{
			base.Hide(overrideIsAlwaysOpen);
			if (!overrideIsAlwaysOpen)
			{
				ResetDraft();
			}
		}

		/// <summary>Gives the form back to the player when the server never answered.</summary>
		protected override void OnTick()
		{
			if (!awaitingResult || Time.unscaledTime - awaitingSince < ResultTimeoutSeconds)
			{
				return;
			}

			awaitingResult = false;
			SetStatus("The server did not answer. Check /tickets before sending this again.", true);
			ApplyModel();
		}

		/// <summary>Registers for the server's answer.</summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<ReportPlayerResultBroadcast>(OnReportResultReceived);
		}

		/// <summary>Unregisters what <see cref="OnClientSet"/> registered.</summary>
		public override void OnClientUnset()
		{
			if (Client != null && Client.NetworkManager != null)
			{
				Client.NetworkManager.ClientManager.UnregisterBroadcast<ReportPlayerResultBroadcast>(OnReportResultReceived);
			}
		}

		// ── Internals ───────────────────────────────────────────────────────────────────────

		private void OnReportResultReceived(ReportPlayerResultBroadcast message, Channel channel) => ApplyResult(message);

		private void OnReasonChanged(ChangeEvent<string> evt)
		{
			reasonIndex = ReasonChoices.IndexOf(evt.newValue ?? string.Empty);
			RefreshControls();
		}

		private void OnDescriptionChanged(ChangeEvent<string> evt)
		{
			/* A refusal's status line is left up while the player edits, so they can still read
			 * why; the next send replaces it. */
			description = evt.newValue ?? string.Empty;
			RefreshControls();
		}

		private void OnRootKeyDown(KeyDownEvent evt)
		{
			if (!Visible || evt.keyCode != KeyCode.Escape)
			{
				return;
			}
			evt.StopPropagation();
			Hide();
		}

		private void ResetDraft()
		{
			reasonIndex = -1;
			description = string.Empty;
			awaitingResult = false;
			statusText = string.Empty;
			statusIsError = false;
		}

		private void SetStatus(string text, bool isError)
		{
			statusText = text ?? string.Empty;
			statusIsError = isError;
		}

		/// <summary>Rewrites every field from the model without raising change events.</summary>
		/// <remarks>
		/// SetValueWithoutNotify, not <c>.value =</c>: a change event raised from inside a dispatch
		/// is queued, and would land after this method and write the old value back over the model.
		/// </remarks>
		private void ApplyModel()
		{
			if (titleLabel != null)
			{
				titleLabel.text = string.IsNullOrEmpty(targetName) ? "Report player" : $"Report {targetName}";
			}
			reasonField?.SetValueWithoutNotify(reasonIndex >= 0 && reasonIndex < ReasonChoices.Count
				? ReasonChoices[reasonIndex]
				: string.Empty);
			descriptionField?.SetValueWithoutNotify(description);
			RefreshControls();
		}

		/// <summary>Updates the counter, the status line and what may be pressed.</summary>
		private void RefreshControls()
		{
			if (counterLabel != null)
			{
				counterLabel.text = $"{description.Length} / {ReportPlayerBroadcast.MaxDescriptionLength}";
			}
			if (statusLabel != null)
			{
				statusLabel.text = statusText;
				statusLabel.EnableInClassList(ErrorClass, statusIsError);
			}
			if (submitButton != null)
			{
				submitButton.SetEnabled(CanSubmit);
				submitButton.text = awaitingResult ? SendingText : SubmitText;
			}
			reasonField?.SetEnabled(!awaitingResult);
			descriptionField?.SetEnabled(!awaitingResult);
		}

		private static List<string> BuildReasonChoices()
		{
			var choices = new List<string>(PlayerReportReasons.Count);
			for (int i = 0; i < PlayerReportReasons.Count; ++i)
			{
				choices.Add(PlayerReportReasons.DisplayName((PlayerReportReason)i));
			}
			return choices;
		}
	}
}
