using System.Collections.Generic;
using FishNet.Transporting;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit implementation of the mailbox panel.
	/// Binds to <c>UIMail.uxml</c> / <c>UIMail.uss</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A view of server state, like every other item-bearing panel here. Nothing is removed
	/// locally: claiming or deleting sends a request, the control is locked until the reply
	/// arrives, and the inbox is re-fetched from the server rather than edited in place. Mail
	/// carries items and currency, so a client that optimistically updated itself would be showing
	/// the player property they might not actually have.
	/// </para>
	/// <para>
	/// Compose names an inventory <em>slot</em>, never an item. The dropdown is built from the
	/// player's own inventory purely so they can pick one by name; what crosses the wire is the
	/// slot index the server then resolves itself.
	/// </para>
	/// </remarks>
	public class UITKMail : UITKCharacterControl
	{
		// ── UXML element names ────────────────────────────────────────────────

		private const string LIST_NAME = "mail-list";
		private const string EMPTY_NAME = "mail-empty";
		private const string TAB_INBOX_NAME = "mail-tab-inbox";
		private const string TAB_COMPOSE_NAME = "mail-tab-compose";
		private const string READ_PANE_NAME = "mail-read";
		private const string COMPOSE_PANE_NAME = "mail-compose";
		private const string READ_SUBJECT_NAME = "mail-read-subject";
		private const string READ_SENDER_NAME = "mail-read-sender";
		private const string READ_BODY_NAME = "mail-read-body";
		private const string ATTACHMENT_ROW_NAME = "mail-read-attachment";
		private const string ATTACHMENT_LABEL_NAME = "mail-attachment-label";
		private const string CLAIM_BTN_NAME = "mail-claim";
		private const string DELETE_BTN_NAME = "mail-delete";
		private const string STATUS_NAME = "mail-status";
		private const string COMPOSE_TO_NAME = "mail-compose-to";
		private const string COMPOSE_SUBJECT_NAME = "mail-compose-subject";
		private const string COMPOSE_BODY_NAME = "mail-compose-body";
		private const string COMPOSE_ITEM_NAME = "mail-compose-item";
		private const string COMPOSE_QUANTITY_NAME = "mail-compose-quantity";
		private const string COMPOSE_CURRENCY_NAME = "mail-compose-currency";
		private const string COMPOSE_STATUS_NAME = "mail-compose-status";
		private const string SEND_BTN_NAME = "mail-send";
		private const string CLOSE_BTN_NAME = "close-button";

		// ── USS class names ───────────────────────────────────────────────────

		private const string CSS_ROW = "fish-row";
		private const string CSS_ROW_LAYOUT = "mail-row";
		private const string CSS_ROW_SELECTED = "fish-row--selected";
		private const string CSS_ROW_DIM = "fish-row--dim";
		private const string CSS_SENDER = "fish-row__name";
		private const string CSS_SENDER_LAYOUT = "mail-row__sender";
		private const string CSS_SUBJECT = "fish-row__meta";
		private const string CSS_SUBJECT_LAYOUT = "mail-row__subject";
		private const string CSS_CLIP = "fish-badge";
		private const string CSS_CLIP_ACCENT = "fish-badge--accent";
		private const string CSS_CLIP_LAYOUT = "mail-row__clip";
		private const string CSS_TAB_ACTIVE = "fish-tab--active";
		private const string CSS_HIDDEN = "mail-hidden";

		/// <summary>The entry the dropdown shows when nothing is attached.</summary>
		private const string NO_ATTACHMENT_CHOICE = "(nothing)";

		// ── Private state ─────────────────────────────────────────────────────

		/// <summary>The mailbox currently in use, or 0 when the panel is closed.</summary>
		private long mailboxID;

		/// <summary>The inbox as the server last reported it.</summary>
		private MailEntryData[] entries = new MailEntryData[0];

		/// <summary>ID of the mail shown in the read pane, or 0 for none.</summary>
		private long selectedMailID;

		/// <summary>True while the compose tab is showing.</summary>
		private bool composing;

		/// <summary>True while a claim, delete or send is awaiting a reply.</summary>
		private bool requestInFlight;

		/// <summary>Inventory slot indices, parallel to the attachment dropdown's choices.</summary>
		private readonly List<int> attachmentSlots = new List<int>();

		/// <summary>Rendered inbox rows, parallel to <see cref="entries"/> order.</summary>
		private readonly List<VisualElement> rowViews = new List<VisualElement>();

		// ── UITKControl lifecycle ─────────────────────────────────────────────

		/// <inheritdoc />
		public override void OnStarting()
		{
			WireControls();
		}

		/// <inheritdoc />
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			WireControls();
			ApplyPerOpenContent();
		}

		/// <summary>
		/// Fills the panel on every show.
		/// </summary>
		/// <remarks>
		/// Enabling the document re-clones the UXML, so both the element references and the button
		/// callbacks have to be re-established — anything wired before <c>Show()</c> is attached to
		/// a tree that no longer exists.
		/// </remarks>
		protected override void OnAfterShow()
		{
			WireControls();
			ApplyPerOpenContent();
		}

		/// <inheritdoc />
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<MailboxBroadcast>(OnClientMailboxBroadcastReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<MailListBroadcast>(OnClientMailListBroadcastReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<MailSendResultBroadcast>(OnClientMailSendResultBroadcastReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<MailClaimResultBroadcast>(OnClientMailClaimResultBroadcastReceived);
		}

		/// <inheritdoc />
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<MailboxBroadcast>(OnClientMailboxBroadcastReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<MailListBroadcast>(OnClientMailListBroadcastReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<MailSendResultBroadcast>(OnClientMailSendResultBroadcastReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<MailClaimResultBroadcast>(OnClientMailClaimResultBroadcastReceived);
		}

		/// <inheritdoc />
		public override void Hide(bool overrideIsAlwaysOpen)
		{
			bool wasVisible = Visible;
			base.Hide(overrideIsAlwaysOpen);

			if (Visible || !wasVisible)
			{
				return;
			}

			mailboxID = 0;
			selectedMailID = 0;
			entries = new MailEntryData[0];
			requestInFlight = false;
			DestroyRows();
		}

		// ── Broadcast handling ────────────────────────────────────────────────

		/// <summary>
		/// Opens the mailbox and asks the server for the inbox.
		/// </summary>
		private void OnClientMailboxBroadcastReceived(MailboxBroadcast msg, Channel channel)
		{
			mailboxID = msg.InteractableID;
			selectedMailID = 0;
			composing = false;
			requestInFlight = false;

			if (!Visible)
			{
				Show();
			}
			else
			{
				ApplyPerOpenContent();
			}

			RequestFetch();
		}

		/// <summary>
		/// Replaces the inbox with the server's snapshot.
		/// </summary>
		private void OnClientMailListBroadcastReceived(MailListBroadcast msg, Channel channel)
		{
			entries = msg.Entries ?? new MailEntryData[0];

			/* Keep the selection if the mail is still there. A refresh follows every claim, and
			 * dropping the selection would close the letter the player just took something from,
			 * which reads as the window losing its place. */
			if (selectedMailID != 0 && FindEntry(selectedMailID) == null)
			{
				selectedMailID = 0;
			}

			ApplyPerOpenContent();
		}

		/// <summary>
		/// Releases the send lock and reports the outcome.
		/// </summary>
		private void OnClientMailSendResultBroadcastReceived(MailSendResultBroadcast msg, Channel channel)
		{
			requestInFlight = false;

			if (msg.Success)
			{
				ClearComposeFields();
				SetComposeStatus("Sent.");
			}
			else
			{
				SetComposeStatus(DescribeFailure(msg.Reason));
			}

			ApplyControlState();
		}

		/// <summary>
		/// Releases the claim lock, reports the outcome, and re-reads the inbox.
		/// </summary>
		private void OnClientMailClaimResultBroadcastReceived(MailClaimResultBroadcast msg, Channel channel)
		{
			requestInFlight = false;
			SetStatus(msg.Success ? "Taken." : DescribeFailure(msg.Reason));

			/* Re-fetch rather than clearing the attachment locally. The server has already zeroed
			 * the row; asking for the truth is one round trip and cannot disagree with it. */
			if (msg.Success)
			{
				RequestFetch();
			}

			ApplyControlState();
		}

		/// <summary>
		/// Turns a refusal into something worth showing the player.
		/// </summary>
		private static string DescribeFailure(MailFailureReason reason)
		{
			switch (reason)
			{
				case MailFailureReason.NoMailbox: return "Step up to the mailbox.";
				case MailFailureReason.NoRecipient: return "No such character.";
				case MailFailureReason.InvalidMessage: return "Subject and message are required.";
				case MailFailureReason.InvalidAttachment: return "That item cannot be attached.";
				case MailFailureReason.NotEnoughCurrency: return "You do not have that much.";
				case MailFailureReason.NothingToClaim: return "Nothing to take.";
				case MailFailureReason.InventoryFull: return "Your inventory is full.";
				case MailFailureReason.ServerError: return "Try again.";
				default: return string.Empty;
			}
		}

		// ── Wiring ────────────────────────────────────────────────────────────

		/// <summary>
		/// Attaches the header, read-pane and compose-pane callbacks to the current tree.
		/// </summary>
		/// <remarks>
		/// Re-run on every show because <c>Show()</c> re-clones the UXML. Callbacks are assigned to
		/// a freshly queried element each time, so there is nothing to unsubscribe — the elements
		/// they were attached to have been discarded with the old tree.
		/// </remarks>
		private void WireControls()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			Button closeBtn = root.Q<Button>(CLOSE_BTN_NAME);
			if (closeBtn != null)
			{
				closeBtn.clicked += Hide;
			}

			Button inboxTab = root.Q<Button>(TAB_INBOX_NAME);
			if (inboxTab != null)
			{
				inboxTab.clicked += () => SetComposing(false);
			}

			Button composeTab = root.Q<Button>(TAB_COMPOSE_NAME);
			if (composeTab != null)
			{
				composeTab.clicked += () => SetComposing(true);
			}

			Button claimBtn = root.Q<Button>(CLAIM_BTN_NAME);
			if (claimBtn != null)
			{
				claimBtn.clicked += RequestClaim;
			}

			Button deleteBtn = root.Q<Button>(DELETE_BTN_NAME);
			if (deleteBtn != null)
			{
				deleteBtn.clicked += RequestDelete;
			}

			Button sendBtn = root.Q<Button>(SEND_BTN_NAME);
			if (sendBtn != null)
			{
				sendBtn.clicked += RequestSend;
			}
		}

		// ── Rendering ─────────────────────────────────────────────────────────

		/// <summary>
		/// Writes everything that has to survive the visual tree being re-cloned.
		/// </summary>
		private void ApplyPerOpenContent()
		{
			if (Root == null)
			{
				return;
			}

			RebuildRows();
			RebuildAttachmentChoices();
			ApplyReadPane();
			ApplyTabs();
			ApplyControlState();
		}

		/// <summary>
		/// Rebuilds the inbox list.
		/// </summary>
		private void RebuildRows()
		{
			DestroyRows();

			VisualElement listRoot = Root?.Q(LIST_NAME);
			if (listRoot == null)
			{
				return;
			}

			for (int i = 0; i < entries.Length; ++i)
			{
				rowViews.Add(CreateRow(listRoot, entries[i]));
			}

			Label empty = Root.Q<Label>(EMPTY_NAME);
			empty?.EnableInClassList(CSS_HIDDEN, entries.Length > 0);
		}

		/// <summary>
		/// Builds one inbox row.
		/// </summary>
		private VisualElement CreateRow(VisualElement listRoot, MailEntryData entry)
		{
			VisualElement rowRoot = new VisualElement();
			rowRoot.AddToClassList(CSS_ROW);
			rowRoot.AddToClassList(CSS_ROW_LAYOUT);
			if (entry.Read)
			{
				// Dimmed rather than hidden: read mail still holds attachments.
				rowRoot.AddToClassList(CSS_ROW_DIM);
			}
			if (entry.ID == selectedMailID)
			{
				rowRoot.AddToClassList(CSS_ROW_SELECTED);
			}

			Label sender = new Label(string.IsNullOrWhiteSpace(entry.SenderName) ? "Unknown" : entry.SenderName);
			sender.AddToClassList(CSS_SENDER);
			sender.AddToClassList(CSS_SENDER_LAYOUT);
			rowRoot.Add(sender);

			Label subject = new Label(entry.Subject ?? string.Empty);
			subject.AddToClassList(CSS_SUBJECT);
			subject.AddToClassList(CSS_SUBJECT_LAYOUT);
			rowRoot.Add(subject);

			if (HasAttachment(entry))
			{
				Label clip = new Label("+");
				clip.AddToClassList(CSS_CLIP);
				clip.AddToClassList(CSS_CLIP_ACCENT);
				clip.AddToClassList(CSS_CLIP_LAYOUT);
				rowRoot.Add(clip);
			}

			// Captured by value: the row is discarded on the next rebuild, so this closure can
			// never outlive the mail it names.
			long capturedID = entry.ID;
			rowRoot.RegisterCallback<PointerDownEvent>(evt => OnRowSelected(capturedID));

			listRoot.Add(rowRoot);
			return rowRoot;
		}

		/// <summary>
		/// Removes every rendered row.
		/// </summary>
		private void DestroyRows()
		{
			for (int i = 0; i < rowViews.Count; ++i)
			{
				rowViews[i]?.RemoveFromHierarchy();
			}
			rowViews.Clear();
		}

		/// <summary>
		/// Fills the read pane from the current selection.
		/// </summary>
		private void ApplyReadPane()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			MailEntryData? selected = FindEntry(selectedMailID);

			Label subject = root.Q<Label>(READ_SUBJECT_NAME);
			Label sender = root.Q<Label>(READ_SENDER_NAME);
			Label body = root.Q<Label>(READ_BODY_NAME);
			VisualElement attachmentRow = root.Q(ATTACHMENT_ROW_NAME);
			Label attachmentLabel = root.Q<Label>(ATTACHMENT_LABEL_NAME);

			if (!selected.HasValue)
			{
				if (subject != null) subject.text = string.Empty;
				if (sender != null) sender.text = string.Empty;
				if (body != null) body.text = "Select a message.";
				attachmentRow?.AddToClassList(CSS_HIDDEN);
				return;
			}

			MailEntryData entry = selected.Value;
			if (subject != null) subject.text = entry.Subject ?? string.Empty;
			if (sender != null) sender.text = "From " + (string.IsNullOrWhiteSpace(entry.SenderName) ? "Unknown" : entry.SenderName);
			if (body != null) body.text = entry.Body ?? string.Empty;

			bool hasAttachment = HasAttachment(entry);
			attachmentRow?.EnableInClassList(CSS_HIDDEN, !hasAttachment);
			if (hasAttachment && attachmentLabel != null)
			{
				attachmentLabel.text = DescribeAttachment(entry);
			}
		}

		/// <summary>
		/// Describes what is attached to a mail.
		/// </summary>
		private static string DescribeAttachment(MailEntryData entry)
		{
			List<string> parts = new List<string>(2);

			if (entry.ItemTemplateID != 0)
			{
				BaseItemTemplate template = BaseItemTemplate.Get<BaseItemTemplate>(entry.ItemTemplateID);
				parts.Add(template != null ? template.Name : "An item");
			}
			if (entry.CurrencyAmount > 0)
			{
				parts.Add(entry.CurrencyAmount.ToString());
			}

			return parts.Count > 0 ? string.Join(" + ", parts) : string.Empty;
		}

		/// <summary>
		/// True when a mail still carries something.
		/// </summary>
		private static bool HasAttachment(MailEntryData entry)
		{
			return entry.ItemTemplateID != 0 || entry.CurrencyAmount > 0;
		}

		/// <summary>
		/// Shows the tab the panel is on and hides the other pane.
		/// </summary>
		private void ApplyTabs()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			root.Q(READ_PANE_NAME)?.EnableInClassList(CSS_HIDDEN, composing);
			root.Q(COMPOSE_PANE_NAME)?.EnableInClassList(CSS_HIDDEN, !composing);

			root.Q<Button>(TAB_INBOX_NAME)?.EnableInClassList(CSS_TAB_ACTIVE, !composing);
			root.Q<Button>(TAB_COMPOSE_NAME)?.EnableInClassList(CSS_TAB_ACTIVE, composing);
		}

		/// <summary>
		/// Enables or disables the controls that submit a request.
		/// </summary>
		/// <remarks>
		/// The lock is what stops a mis-timed double click claiming an attachment twice or sending
		/// a letter twice. It is released by the server's reply — every path sends one — so a
		/// refusal frees the control just as a success does.
		/// </remarks>
		private void ApplyControlState()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			MailEntryData? selected = FindEntry(selectedMailID);
			bool hasSelection = selected.HasValue;
			bool canClaim = hasSelection && HasAttachment(selected.Value) && !requestInFlight;

			root.Q<Button>(CLAIM_BTN_NAME)?.SetEnabled(canClaim);
			root.Q<Button>(DELETE_BTN_NAME)?.SetEnabled(hasSelection && !requestInFlight);
			root.Q<Button>(SEND_BTN_NAME)?.SetEnabled(!requestInFlight);
		}

		/// <summary>
		/// Rebuilds the attachment dropdown from the player's inventory.
		/// </summary>
		/// <remarks>
		/// Names are for the player; the slot index beside each one is what actually gets sent.
		/// The list is rebuilt on every open rather than kept live because the inventory can change
		/// while the panel is up, and a stale entry would name a slot holding something else — which
		/// the server would then attach instead.
		/// </remarks>
		private void RebuildAttachmentChoices()
		{
			attachmentSlots.Clear();

			List<string> choices = new List<string> { NO_ATTACHMENT_CHOICE };
			attachmentSlots.Add(-1);

			if (Character != null &&
				Character.TryGet(out IInventoryController inventoryController) &&
				inventoryController.Items != null)
			{
				for (int i = 0; i < inventoryController.Items.Count; ++i)
				{
					Item item = inventoryController.Items[i];
					if (item == null || item.Template == null)
					{
						continue;
					}

					string amount = item.IsStackable && item.Stackable.Amount > 1
						? " x" + item.Stackable.Amount
						: string.Empty;
					choices.Add(item.Template.Name + amount);
					attachmentSlots.Add(i);
				}
			}

			DropdownField dropdown = Root?.Q<DropdownField>(COMPOSE_ITEM_NAME);
			if (dropdown == null)
			{
				return;
			}

			dropdown.choices = choices;
			dropdown.index = 0;
		}

		// ── Interaction ───────────────────────────────────────────────────────

		/// <summary>
		/// Selects a mail and shows it.
		/// </summary>
		private void OnRowSelected(long mailID)
		{
			selectedMailID = mailID;
			composing = false;
			SetStatus(string.Empty);
			ApplyPerOpenContent();
		}

		/// <summary>
		/// Switches between the inbox and the compose form.
		/// </summary>
		private void SetComposing(bool value)
		{
			composing = value;
			ApplyTabs();
			ApplyControlState();
		}

		// ── Requests ──────────────────────────────────────────────────────────

		/// <summary>
		/// Asks the server for the inbox.
		/// </summary>
		private void RequestFetch()
		{
			if (Client == null || mailboxID == 0)
			{
				return;
			}

			Client.Broadcast(new MailFetchBroadcast()
			{
				InteractableID = mailboxID,
			}, Channel.Reliable);
		}

		/// <summary>
		/// Claims the selected mail's attachment.
		/// </summary>
		private void RequestClaim()
		{
			if (Client == null || mailboxID == 0 || selectedMailID == 0 || requestInFlight)
			{
				return;
			}

			requestInFlight = true;
			ApplyControlState();
			SetStatus(string.Empty);

			Client.Broadcast(new MailClaimAttachmentBroadcast()
			{
				InteractableID = mailboxID,
				MailID = selectedMailID,
			}, Channel.Reliable);
		}

		/// <summary>
		/// Deletes the selected mail.
		/// </summary>
		/// <remarks>
		/// The delete path has no reply of its own, so the lock is released immediately and the
		/// inbox is re-fetched — the refreshed list is the confirmation. Deleting a mail that still
		/// has an attachment destroys it, which is the server's rule, not this panel's: the claim
		/// button sits directly above and is the obvious thing to press first.
		/// </remarks>
		private void RequestDelete()
		{
			if (Client == null || mailboxID == 0 || selectedMailID == 0 || requestInFlight)
			{
				return;
			}

			long deleting = selectedMailID;
			selectedMailID = 0;

			Client.Broadcast(new MailDeleteBroadcast()
			{
				InteractableID = mailboxID,
				MailID = deleting,
			}, Channel.Reliable);

			RequestFetch();
		}

		/// <summary>
		/// Sends the composed mail.
		/// </summary>
		private void RequestSend()
		{
			if (Client == null || mailboxID == 0 || requestInFlight)
			{
				return;
			}

			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			string to = root.Q<TextField>(COMPOSE_TO_NAME)?.value ?? string.Empty;
			string subject = root.Q<TextField>(COMPOSE_SUBJECT_NAME)?.value ?? string.Empty;
			string body = root.Q<TextField>(COMPOSE_BODY_NAME)?.value ?? string.Empty;

			if (string.IsNullOrWhiteSpace(to) ||
				string.IsNullOrWhiteSpace(subject) ||
				string.IsNullOrWhiteSpace(body))
			{
				// Refused locally so an obviously incomplete letter does not cost a round trip.
				// The server enforces the same rule regardless.
				SetComposeStatus("Fill in recipient, subject and message.");
				return;
			}

			int attachmentSlot = -1;
			DropdownField dropdown = root.Q<DropdownField>(COMPOSE_ITEM_NAME);
			if (dropdown != null &&
				dropdown.index >= 0 &&
				dropdown.index < attachmentSlots.Count)
			{
				attachmentSlot = attachmentSlots[dropdown.index];
			}

			int quantity = ParseAmount(root.Q<TextField>(COMPOSE_QUANTITY_NAME)?.value, 1);
			int currency = ParseAmount(root.Q<TextField>(COMPOSE_CURRENCY_NAME)?.value, 0);

			requestInFlight = true;
			ApplyControlState();
			SetComposeStatus(string.Empty);

			Client.Broadcast(new MailSendBroadcast()
			{
				InteractableID = mailboxID,
				RecipientName = to.Trim(),
				Subject = subject,
				Body = body,
				AttachmentSlot = attachmentSlot,
				AttachmentQuantity = quantity,
				CurrencyAttachment = currency,
			}, Channel.Reliable);
		}

		/// <summary>
		/// Reads a non-negative number out of a text field.
		/// </summary>
		/// <remarks>
		/// Never throws and never sends a negative: the field is free text, and the server clamps
		/// whatever arrives anyway, so the only job here is to avoid submitting nonsense.
		/// </remarks>
		private static int ParseAmount(string text, int fallback)
		{
			if (string.IsNullOrWhiteSpace(text) || !int.TryParse(text.Trim(), out int value))
			{
				return fallback;
			}
			return value < 0 ? 0 : value;
		}

		// ── Helpers ───────────────────────────────────────────────────────────

		/// <summary>
		/// Finds a mail in the current snapshot.
		/// </summary>
		private MailEntryData? FindEntry(long mailID)
		{
			if (mailID == 0)
			{
				return null;
			}

			for (int i = 0; i < entries.Length; ++i)
			{
				if (entries[i].ID == mailID)
				{
					return entries[i];
				}
			}
			return null;
		}

		/// <summary>
		/// Empties the compose form after a successful send.
		/// </summary>
		private void ClearComposeFields()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			TextField to = root.Q<TextField>(COMPOSE_TO_NAME);
			if (to != null) to.value = string.Empty;

			TextField subject = root.Q<TextField>(COMPOSE_SUBJECT_NAME);
			if (subject != null) subject.value = string.Empty;

			TextField body = root.Q<TextField>(COMPOSE_BODY_NAME);
			if (body != null) body.value = string.Empty;

			TextField quantity = root.Q<TextField>(COMPOSE_QUANTITY_NAME);
			if (quantity != null) quantity.value = "1";

			TextField currency = root.Q<TextField>(COMPOSE_CURRENCY_NAME);
			if (currency != null) currency.value = "0";

			// The inventory has changed if something was attached, so the choices are stale.
			RebuildAttachmentChoices();
		}

		/// <summary>
		/// Writes the read pane's status line.
		/// </summary>
		private void SetStatus(string text)
		{
			Label status = Root?.Q<Label>(STATUS_NAME);
			if (status != null)
			{
				status.text = text ?? string.Empty;
			}
		}

		/// <summary>
		/// Writes the compose pane's status line.
		/// </summary>
		private void SetComposeStatus(string text)
		{
			Label status = Root?.Q<Label>(COMPOSE_STATUS_NAME);
			if (status != null)
			{
				status.text = text ?? string.Empty;
			}
		}
	}
}
