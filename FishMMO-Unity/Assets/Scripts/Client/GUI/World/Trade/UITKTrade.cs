using System;
using System.Collections.Generic;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// The player-to-player trade window (issue #144): the local player's table on the left,
	/// the partner's on the right, one Accept and one Cancel.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Display only.</b> Every change the player makes here is a request to the server —
	/// an inventory slot and a quantity, a currency amount, an accept that quotes the state
	/// version it saw — and everything drawn here comes back from the server in a
	/// <see cref="TradeStateBroadcast"/>. The window never assumes a request succeeded; it
	/// repaints from the next state message, which is also what makes it correct after a
	/// tree rebuild or a missed message.
	/// </para>
	/// <para>
	/// <b>Offered slots are locked locally</b> from the authoritative state, so the bag greys
	/// them out and refuses to drag, split, equip or consume them — the same lock the server
	/// holds, derived from the same message, which keeps prediction deterministic.
	/// </para>
	/// <para>
	/// <b>The range check runs here too</b>, against the partner's observed position, so the
	/// window closes the instant the players separate; the server's own check is the one
	/// that decides, and a partner who is no longer observed at all counts as out of range.
	/// </para>
	/// </remarks>
	public class UITKTrade : UITKCharacterControl
	{
		/// <summary>
		/// The currency attribute template, resolved by id like the other currency-aware
		/// panels. Zero means "no currency" and the field is disabled.
		/// </summary>
		public int CurrencyTemplateID;

		[Tooltip("Whether opening the trade also opens the inventory, so there is something to drag from.")]
		[SerializeField] private bool openInventoryOnShow = true;

		protected override bool OpensInventoryOnShow => openInventoryOnShow;

		// Element names.
		private const string SUBTITLE_NAME = "trade-subtitle";
		private const string RANGE_NAME = "trade-range";
		private const string CLOSE_NAME = "close-button";
		private const string OWN_NAME_NAME = "trade-own-name";
		private const string OWN_STATUS_NAME = "trade-own-status";
		private const string OWN_GRID_NAME = "trade-own-grid";
		private const string OWN_CURRENCY_NAME = "trade-own-currency";
		private const string OWN_BALANCE_NAME = "trade-own-balance";
		private const string OWN_HINT_NAME = "trade-own-hint";
		private const string PARTNER_NAME_NAME = "trade-partner-name";
		private const string PARTNER_STATUS_NAME = "trade-partner-status";
		private const string PARTNER_GRID_NAME = "trade-partner-grid";
		private const string PARTNER_CURRENCY_NAME = "trade-partner-currency";
		private const string STATUS_NAME = "trade-status";
		private const string ACCEPT_NAME = "trade-accept-btn";
		private const string CANCEL_NAME = "trade-cancel-btn";

		// USS classes.
		private const string SLOT_CLASS = "fish-slot";
		private const string SLOT_ICON_CLASS = "fish-slot__icon";
		private const string SLOT_AMOUNT_CLASS = "fish-slot__amount";
		private const string SLOT_EMPTY_CLASS = "fish-slot--empty";
		private const string TRADE_SLOT_CLASS = "trade-slot";
		private const string TRADE_SLOT_ICON_CLASS = "trade-slot__icon";
		private const string TRADE_SLOT_AMOUNT_CLASS = "trade-slot__amount";
		private const string TRADE_SLOT_READONLY_CLASS = "trade-slot--readonly";
		private const string STATUS_ACCEPTED_CLASS = "trade-column__status--accepted";
		private const string BADGE_GOOD_CLASS = "fish-badge--good";

		private const string DRAG_OBJECT_NAME = "UIDragObject";
		private const string TOOLTIP_NAME = "UITooltip";
		private const string DIALOG_NAME = "UIDialogBox";
		private const string TOAST_NAME = "UIToast";

		/// <summary>Seconds between local range checks. The server checks on its own clock.</summary>
		private const float RANGE_CHECK_INTERVAL = 0.25f;

		/// <summary>One drawn slot on either table.</summary>
		private sealed class SlotView
		{
			public VisualElement Root;
			public VisualElement Icon;
			public Label Amount;

			/// <summary>The entry drawn in it, or null when empty.</summary>
			public TradeOfferEntry? Entry;

			/// <summary>
			/// A transient item for the tooltip of a partner's entry — an item this client does
			/// not hold, rebuilt from template and seed so its stats read the same on both
			/// screens. Null for own entries, which use the live item in the bag.
			/// </summary>
			public Item TooltipItem;
		}

		private Label subtitleLabel;
		private Label rangeLabel;
		private Label ownNameLabel;
		private Label ownStatusLabel;
		private VisualElement ownGrid;
		private IntegerField ownCurrencyField;
		private Label ownBalanceLabel;
		private Label ownHintLabel;
		private Label partnerNameLabel;
		private Label partnerStatusLabel;
		private VisualElement partnerGrid;
		private Label partnerCurrencyLabel;
		private Label statusLabel;
		private Button acceptButton;
		private Button cancelButton;

		private readonly List<SlotView> ownSlots = new List<SlotView>();
		private readonly List<SlotView> partnerSlots = new List<SlotView>();

		/// <summary>Inventory slots this window has locked locally, so they can all be released.</summary>
		private readonly HashSet<int> lockedInventorySlots = new HashSet<int>();

		/// <summary>The other party of the open session. Zero when no session is open.</summary>
		public long PartnerCharacterID { get; private set; }

		/// <summary>The other party's display name.</summary>
		public string PartnerName { get; private set; } = string.Empty;

		/// <summary>The range the server enforces for the open session.</summary>
		public float MaxDistance { get; private set; } = TradeRules.DefaultMaxDistance;

		/// <summary>True between <see cref="TradeOpenedBroadcast"/> and <see cref="TradeClosedBroadcast"/>.</summary>
		public bool SessionOpen { get; private set; }

		/// <summary>The last table the server sent, or default before the first.</summary>
		public TradeStateBroadcast State { get; private set; }

		private bool hasState;

		/// <summary>Number of slots each table draws: the larger of the default grid and what the server sent.</summary>
		private int gridSlotCount = TradeRules.DefaultMaxOfferSlots;

		/// <summary>The requester of the invitation currently on screen, or zero.</summary>
		private long pendingRequesterID;
		private float pendingInviteExpiresAt;

		private float nextRangeCheck;

		/// <summary>True while the window is being hidden by a server close, so no cancel is sent.</summary>
		private bool closingFromServer;

		// ── Lifecycle ───────────────────────────────────────────────────────────────────────

		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			subtitleLabel = root.Q<Label>(SUBTITLE_NAME);
			rangeLabel = root.Q<Label>(RANGE_NAME);
			ownNameLabel = root.Q<Label>(OWN_NAME_NAME);
			ownStatusLabel = root.Q<Label>(OWN_STATUS_NAME);
			ownGrid = root.Q<VisualElement>(OWN_GRID_NAME);
			ownCurrencyField = root.Q<IntegerField>(OWN_CURRENCY_NAME);
			ownBalanceLabel = root.Q<Label>(OWN_BALANCE_NAME);
			ownHintLabel = root.Q<Label>(OWN_HINT_NAME);
			partnerNameLabel = root.Q<Label>(PARTNER_NAME_NAME);
			partnerStatusLabel = root.Q<Label>(PARTNER_STATUS_NAME);
			partnerGrid = root.Q<VisualElement>(PARTNER_GRID_NAME);
			partnerCurrencyLabel = root.Q<Label>(PARTNER_CURRENCY_NAME);
			statusLabel = root.Q<Label>(STATUS_NAME);
			acceptButton = root.Q<Button>(ACCEPT_NAME);
			cancelButton = root.Q<Button>(CANCEL_NAME);

			// Resolved locally each run: OnStarting re-runs on a fresh tree.
			Button closeButton = root.Q<Button>(CLOSE_NAME);
			if (closeButton != null)
			{
				closeButton.clicked -= OnCloseClicked;
				closeButton.clicked += OnCloseClicked;
			}

			if (acceptButton != null)
			{
				acceptButton.clicked -= OnAcceptClicked;
				acceptButton.clicked += OnAcceptClicked;
			}

			if (cancelButton != null)
			{
				cancelButton.clicked -= OnCloseClicked;
				cancelButton.clicked += OnCloseClicked;
			}

			if (ownCurrencyField != null)
			{
				ownCurrencyField.isDelayed = true;
				ownCurrencyField.UnregisterValueChangedCallback(OnCurrencyFieldChanged);
				ownCurrencyField.RegisterValueChangedCallback(OnCurrencyFieldChanged);
			}

			BuildGrids();
			RebuildAll();
		}

		public override void OnDestroying()
		{
			ReleaseLocalLocks();
			ownSlots.Clear();
			partnerSlots.Clear();
		}

		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<TradeInviteBroadcast>(OnClientTradeInviteReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<TradeRequestResultBroadcast>(OnClientTradeRequestResultReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<TradeOpenedBroadcast>(OnClientTradeOpenedReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<TradeStateBroadcast>(OnClientTradeStateReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<TradeClosedBroadcast>(OnClientTradeClosedReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<TradeRefusedBroadcast>(OnClientTradeRefusedReceived);
		}

		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<TradeInviteBroadcast>(OnClientTradeInviteReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<TradeRequestResultBroadcast>(OnClientTradeRequestResultReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<TradeOpenedBroadcast>(OnClientTradeOpenedReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<TradeStateBroadcast>(OnClientTradeStateReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<TradeClosedBroadcast>(OnClientTradeClosedReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<TradeRefusedBroadcast>(OnClientTradeRefusedReceived);
		}

		public override void OnPreUnsetCharacter()
		{
			// The character is going away; its bag's locks go with it.
			ReleaseLocalLocks();
			ResetSession();
		}

		protected override void OnAfterShow()
		{
			RebuildAll();
		}

		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			RebuildAll();
		}

		/// <summary>
		/// Closing the window is cancelling the trade. The override is on <c>Hide(bool)</c>
		/// because quit-to-login calls that form directly.
		/// </summary>
		public override void Hide(bool overrideIsAlwaysOpen)
		{
			bool wasVisible = Visible;
			base.Hide(overrideIsAlwaysOpen);

			if (overrideIsAlwaysOpen || Document == null)
			{
				return;
			}

			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip) && Root != null)
			{
				tooltip.HideFor(Root);
			}

			if (SessionOpen && !closingFromServer && wasVisible)
			{
				Client.Broadcast(new TradeCancelBroadcast(), Channel.Reliable);
			}

			ReleaseLocalLocks();
			ResetSession();
		}

		protected override void OnTick()
		{
			float now = Time.unscaledTime;

			if (pendingRequesterID != 0 && now >= pendingInviteExpiresAt)
			{
				// The server has lapsed it; an answer now would be ignored, so stop offering one.
				pendingRequesterID = 0;
				if (UIManager.TryGetTK(DIALOG_NAME, out UITKDialogBox dialog))
				{
					dialog.DismissWithoutAnswer();
				}
			}

			if (!SessionOpen || !Visible || now < nextRangeCheck)
			{
				return;
			}
			nextRangeCheck = now + RANGE_CHECK_INTERVAL;

			if (!IsPartnerInRange())
			{
				/* Closed here first for immediacy; the server's own tick closes the session
				 * on its side and its TradeClosedBroadcast finds nothing left to close. The
				 * cancel is still sent so the partner's window closes without waiting for it. */
				Toast("Trade cancelled: out of range.", ToastSeverity.Warning);
				Hide();
			}
		}

		// ── Public entry points (also used by the render harness and tests) ─────────────────

		/// <summary>
		/// Opens the window for a session with <paramref name="partnerID"/>.
		/// </summary>
		public void OpenWith(long partnerID, string partnerName, float maxDistance)
		{
			PartnerCharacterID = partnerID;
			PartnerName = partnerName ?? string.Empty;
			MaxDistance = maxDistance > 0.0f ? maxDistance : TradeRules.DefaultMaxDistance;
			SessionOpen = true;
			hasState = false;
			State = default;
			closingFromServer = false;
			nextRangeCheck = Time.unscaledTime + RANGE_CHECK_INTERVAL;

			Show();
			RebuildAll();
		}

		/// <summary>
		/// Repaints both tables from the server's view of the session.
		/// </summary>
		public void ApplyState(TradeStateBroadcast state)
		{
			if (!SessionOpen)
			{
				return;
			}

			State = state;
			hasState = true;

			int longest = Mathf.Max(state.OwnOffer?.Length ?? 0, state.PartnerOffer?.Length ?? 0);
			if (longest > gridSlotCount)
			{
				gridSlotCount = longest;
				BuildGrids();
			}

			SyncLocalLocks(state.OwnOffer);
			RebuildAll();
		}

		/// <summary>
		/// Ends the session from the server's side: no cancel is sent back.
		/// </summary>
		public void ApplyClosed(TradeCloseReason reason)
		{
			if (SessionOpen)
			{
				Toast(DescribeClose(reason), reason == TradeCloseReason.Completed ? ToastSeverity.Success : ToastSeverity.Info);
			}

			closingFromServer = true;
			try
			{
				Hide();
			}
			finally
			{
				closingFromServer = false;
			}
		}

		/// <summary>
		/// Asks the server to open a trade with <paramref name="targetCharacterID"/>.
		/// </summary>
		/// <remarks>
		/// The entry point the context menus call. The local pre-checks only save a round
		/// trip; the server applies the same rules and more.
		/// </remarks>
		public void RequestTrade(long targetCharacterID)
		{
			if (Character == null || targetCharacterID <= 0 || targetCharacterID == Character.ID)
			{
				return;
			}

			if (SessionOpen)
			{
				Toast("You are already trading.", ToastSeverity.Warning);
				return;
			}

			if (!CharacterStateValidation.CanAct(Character))
			{
				Toast("You cannot trade right now.", ToastSeverity.Warning);
				return;
			}

			if (BaseCharacter.ClientCharacters.TryGetValue(targetCharacterID, out ICharacter target) &&
				target?.Transform != null && Character.Transform != null &&
				!TradeRules.IsWithinRange(Character.Transform.position, target.Transform.position, MaxDistance))
			{
				Toast("That player is too far away to trade with.", ToastSeverity.Warning);
				return;
			}

			Client.Broadcast(new TradeRequestBroadcast { TargetCharacterID = targetCharacterID }, Channel.Reliable);
			Toast("Trade request sent.", ToastSeverity.Info);
		}

		/// <summary>
		/// Puts an inventory item on the table. The bag calls this on right-click while a
		/// trade is open; the drop handler calls it for a drag.
		/// </summary>
		/// <returns>True when a request was sent.</returns>
		public bool TryOfferInventorySlot(int slot, uint amount = 0)
		{
			if (!SessionOpen || Character == null || !CharacterStateValidation.CanAct(Character))
			{
				return false;
			}

			if (!Character.TryGet(out IInventoryController inventory) ||
				!inventory.IsValidSlot(slot) ||
				inventory.IsSlotLocked(slot) ||
				!inventory.TryGetItem(slot, out Item item) ||
				item == null)
			{
				return false;
			}

			if (hasState && State.OwnOffer != null && State.OwnOffer.Length >= gridSlotCount)
			{
				SetStatus("Your table is full.");
				return false;
			}

			Client.Broadcast(new TradeOfferItemBroadcast { Slot = slot, Amount = amount }, Channel.Reliable);
			return true;
		}

		/// <summary>Takes an offered inventory slot back off the table.</summary>
		public void WithdrawInventorySlot(int slot)
		{
			if (!SessionOpen)
			{
				return;
			}
			Client.Broadcast(new TradeWithdrawItemBroadcast { Slot = slot }, Channel.Reliable);
		}

		// ── Broadcast handlers ──────────────────────────────────────────────────────────────

		private void OnClientTradeInviteReceived(TradeInviteBroadcast msg, Channel channel)
		{
			if (Character == null)
			{
				return;
			}

			pendingRequesterID = msg.RequesterCharacterID;
			pendingInviteExpiresAt = Time.unscaledTime + Mathf.Max(1.0f, msg.ExpiresInSeconds);

			long requesterID = msg.RequesterCharacterID;
			string name = string.IsNullOrEmpty(msg.RequesterName) ? "Someone" : msg.RequesterName;

			if (!UIManager.TryGetTK(DIALOG_NAME, out UITKDialogBox dialog) ||
				!dialog.Open($"{name} wants to trade with you.",
					() => AnswerInvite(requesterID, true),
					() => AnswerInvite(requesterID, false)))
			{
				// No dialog to ask with: decline rather than leave the requester waiting.
				AnswerInvite(requesterID, false);
			}
		}

		/// <summary>
		/// Answers the invitation the dialog was opened for — and only that one. A dialog
		/// that outlived its invitation answers nothing.
		/// </summary>
		private void AnswerInvite(long requesterID, bool accept)
		{
			if (pendingRequesterID != requesterID)
			{
				return;
			}
			pendingRequesterID = 0;

			Client.Broadcast(new TradeRequestResponseBroadcast
			{
				RequesterCharacterID = requesterID,
				Accept = accept,
			}, Channel.Reliable);
		}

		private void OnClientTradeRequestResultReceived(TradeRequestResultBroadcast msg, Channel channel)
		{
			Toast(DescribeRequestFailure(msg.Failure), ToastSeverity.Warning);
		}

		private void OnClientTradeOpenedReceived(TradeOpenedBroadcast msg, Channel channel)
		{
			// An invitation we had up is moot once a trade is open.
			pendingRequesterID = 0;
			OpenWith(msg.PartnerCharacterID, msg.PartnerName, msg.MaxDistance);
		}

		private void OnClientTradeStateReceived(TradeStateBroadcast msg, Channel channel)
		{
			ApplyState(msg);
		}

		/// <summary>
		/// A refused change: the reason goes on the status line (the window is up) and as a
		/// toast, and the table that follows it repaints the truth.
		/// </summary>
		private void OnClientTradeRefusedReceived(TradeRefusedBroadcast msg, Channel channel)
		{
			string text = DescribeRefusal(msg.Reason);
			SetStatus(text);
			Toast(text, ToastSeverity.Warning);
		}

		private void OnClientTradeClosedReceived(TradeClosedBroadcast msg, Channel channel)
		{
			if (!SessionOpen)
			{
				/* No session: this is the server dropping an invitation prompt — the requester
				 * withdrew, or the trade could not open after we accepted. */
				if (pendingRequesterID != 0)
				{
					pendingRequesterID = 0;
					if (UIManager.TryGetTK(DIALOG_NAME, out UITKDialogBox dialog))
					{
						dialog.DismissWithoutAnswer();
					}
				}
				if (msg.Reason != TradeCloseReason.PartnerCancelled)
				{
					Toast(DescribeClose(msg.Reason), ToastSeverity.Info);
				}
				return;
			}

			ApplyClosed(msg.Reason);
		}

		// ── Building ────────────────────────────────────────────────────────────────────────

		/// <summary>Builds both grids to <see cref="gridSlotCount"/> slots. Idempotent.</summary>
		private void BuildGrids()
		{
			BuildGrid(ownGrid, ownSlots, readOnly: false);
			BuildGrid(partnerGrid, partnerSlots, readOnly: true);
		}

		private void BuildGrid(VisualElement grid, List<SlotView> views, bool readOnly)
		{
			views.Clear();
			if (grid == null)
			{
				return;
			}
			grid.Clear();

			for (int i = 0; i < gridSlotCount; ++i)
			{
				var view = new SlotView();

				view.Root = new VisualElement();
				view.Root.AddToClassList(SLOT_CLASS);
				view.Root.AddToClassList(TRADE_SLOT_CLASS);
				view.Root.AddToClassList(SLOT_EMPTY_CLASS);
				if (readOnly)
				{
					view.Root.AddToClassList(TRADE_SLOT_READONLY_CLASS);
				}

				view.Icon = new VisualElement();
				view.Icon.AddToClassList(SLOT_ICON_CLASS);
				view.Icon.AddToClassList(TRADE_SLOT_ICON_CLASS);
				view.Icon.pickingMode = PickingMode.Ignore;
				view.Root.Add(view.Icon);

				view.Amount = new Label(string.Empty);
				view.Amount.AddToClassList(SLOT_AMOUNT_CLASS);
				view.Amount.AddToClassList(TRADE_SLOT_AMOUNT_CLASS);
				view.Amount.pickingMode = PickingMode.Ignore;
				view.Amount.style.display = DisplayStyle.None;
				view.Root.Add(view.Amount);

				SlotView captured = view;
				view.Root.RegisterCallback<PointerEnterEvent>(evt => OnSlotPointerEnter(captured));
				view.Root.RegisterCallback<PointerLeaveEvent>(evt => OnSlotPointerLeave(captured));
				if (!readOnly)
				{
					view.Root.RegisterCallback<PointerDownEvent>(evt => OnOwnSlotPointerDown(evt, captured));
					view.Root.RegisterCallback<PointerUpEvent>(evt => OnOwnSlotPointerUp(evt, captured));
				}

				grid.Add(view.Root);
				views.Add(view);
			}
		}

		/// <summary>Repaints everything from <see cref="State"/>.</summary>
		private void RebuildAll()
		{
			if (Root == null)
			{
				return;
			}

			if (subtitleLabel != null)
			{
				subtitleLabel.text = SessionOpen ? $"with {PartnerName}" : string.Empty;
			}
			if (rangeLabel != null)
			{
				rangeLabel.text = SessionOpen ? $"{MaxDistance:0}m" : string.Empty;
			}
			if (ownNameLabel != null)
			{
				ownNameLabel.text = Character != null && !string.IsNullOrEmpty(Character.CharacterName) ? Character.CharacterName : "You";
			}
			if (partnerNameLabel != null)
			{
				partnerNameLabel.text = string.IsNullOrEmpty(PartnerName) ? "Partner" : PartnerName;
			}

			TradeOfferEntry[] own = hasState ? State.OwnOffer : null;
			TradeOfferEntry[] partner = hasState ? State.PartnerOffer : null;

			PaintGrid(ownSlots, own, ownSide: true);
			PaintGrid(partnerSlots, partner, ownSide: false);

			PaintStatus(ownStatusLabel, hasState && State.OwnAccepted);
			PaintStatus(partnerStatusLabel, hasState && State.PartnerAccepted);

			if (ownCurrencyField != null)
			{
				ownCurrencyField.SetValueWithoutNotify((int)Mathf.Clamp(hasState ? State.OwnCurrency : 0, 0, int.MaxValue));
				ownCurrencyField.SetEnabled(SessionOpen && CurrencyTemplateID != 0 && !(hasState && State.OwnAccepted && State.PartnerAccepted));
			}
			if (ownBalanceLabel != null)
			{
				ownBalanceLabel.text = TryGetOwnBalance(out long balance) ? $"of {balance}" : string.Empty;
			}
			if (partnerCurrencyLabel != null)
			{
				partnerCurrencyLabel.text = (hasState ? State.PartnerCurrency : 0).ToString();
			}

			bool bothAccepted = hasState && State.OwnAccepted && State.PartnerAccepted;
			if (acceptButton != null)
			{
				acceptButton.text = hasState && State.OwnAccepted ? "Unaccept" : "Accept";
				acceptButton.SetEnabled(SessionOpen && hasState && !bothAccepted);
			}
			if (cancelButton != null)
			{
				cancelButton.SetEnabled(SessionOpen && !bothAccepted);
			}

			if (ownHintLabel != null)
			{
				ownHintLabel.text = "Drag items from your bag, or right-click them. Right-click here to take one back.";
			}

			if (bothAccepted)
			{
				SetStatus("Both accepted — completing…");
			}
			else if (hasState && State.OwnAccepted)
			{
				SetStatus($"Waiting for {PartnerName} to accept.");
			}
			else if (hasState && State.PartnerAccepted)
			{
				SetStatus($"{PartnerName} has accepted. Review their offer, then accept.");
			}
			else if (SessionOpen)
			{
				SetStatus("Any change to either side clears both acceptances.");
			}
			else
			{
				SetStatus(string.Empty);
			}
		}

		private void PaintGrid(List<SlotView> views, TradeOfferEntry[] entries, bool ownSide)
		{
			for (int i = 0; i < views.Count; ++i)
			{
				SlotView view = views[i];
				bool filled = entries != null && i < entries.Length;

				view.Entry = filled ? entries[i] : (TradeOfferEntry?)null;
				view.TooltipItem = null;

				if (!filled)
				{
					UITKItemIcon.Clear(view.Icon);
					view.Amount.style.display = DisplayStyle.None;
					view.Root.AddToClassList(SLOT_EMPTY_CLASS);
					continue;
				}

				TradeOfferEntry entry = entries[i];
				BaseItemTemplate template = BaseItemTemplate.Get<BaseItemTemplate>(entry.TemplateID);

				UITKItemIcon.Apply(view.Icon, template != null ? template.Icon : null);
				view.Root.RemoveFromClassList(SLOT_EMPTY_CLASS);

				bool showAmount = entry.Amount > 1 || (template != null && template.MaxStackSize > 1);
				view.Amount.text = entry.Amount.ToString();
				view.Amount.style.display = showAmount ? DisplayStyle.Flex : DisplayStyle.None;

				if (!ownSide && template != null)
				{
					// Built once per paint, not per hover. See SlotView.TooltipItem.
					view.TooltipItem = new Item(entry.ItemID, entry.Seed, template, Math.Max(1u, entry.Amount));
				}
			}
		}

		private static void PaintStatus(Label label, bool accepted)
		{
			if (label == null)
			{
				return;
			}
			label.text = accepted ? "Accepted" : "Not accepted";
			if (accepted)
			{
				label.AddToClassList(STATUS_ACCEPTED_CLASS);
				label.AddToClassList(BADGE_GOOD_CLASS);
			}
			else
			{
				label.RemoveFromClassList(STATUS_ACCEPTED_CLASS);
				label.RemoveFromClassList(BADGE_GOOD_CLASS);
			}
		}

		private void SetStatus(string text)
		{
			if (statusLabel != null)
			{
				statusLabel.text = text ?? string.Empty;
			}
		}

		// ── Interactions ────────────────────────────────────────────────────────────────────

		private void OnCloseClicked()
		{
			Hide();
		}

		private void OnAcceptClicked()
		{
			if (!SessionOpen || !hasState)
			{
				return;
			}

			bool accept = !State.OwnAccepted;

			/* The same estimate the server makes when it receives the accept, made first so a
			 * player short of bag space is told without a round trip. The server's answer is
			 * the one that counts; this only saves the wait. */
			if (accept && Character != null && Character.TryGet(out IInventoryController inventory) &&
				!TradeRules.HasRoomFor(inventory, State.PartnerOffer, State.OwnOffer, out int required, out int available))
			{
				string text = $"{DescribeRefusal(TradeRefusalReason.NoRoom)} ({required} needed, {available} free)";
				SetStatus(text);
				Toast(text, ToastSeverity.Warning);
				return;
			}

			Client.Broadcast(new TradeAcceptBroadcast { Accept = accept, Version = State.Version }, Channel.Reliable);
			SetStatus(accept ? "Accepting…" : "Withdrawing acceptance…");
		}

		private void OnCurrencyFieldChanged(ChangeEvent<int> evt)
		{
			if (ownCurrencyField == null)
			{
				return;
			}

			if (!SessionOpen)
			{
				ownCurrencyField.SetValueWithoutNotify(0);
				return;
			}

			long requested = evt.newValue < 0 ? 0 : evt.newValue;
			long balance = TryGetOwnBalance(out long held) ? held : 0;
			long clamped = Math.Min(requested, balance);

			// The field shows what will be sent, never what was typed past the balance.
			ownCurrencyField.SetValueWithoutNotify((int)Math.Min(clamped, int.MaxValue));

			Client.Broadcast(new TradeSetCurrencyBroadcast { Amount = clamped }, Channel.Reliable);
		}

		private bool TryGetOwnBalance(out long balance)
		{
			balance = 0;
			if (Character == null || CurrencyTemplateID == 0)
			{
				return false;
			}
			CharacterAttributeTemplate template = CharacterAttributeTemplate.Get<CharacterAttributeTemplate>(CurrencyTemplateID);
			return template != null && CharacterCurrency.TryGetBalance(Character, template, out balance);
		}

		/// <summary>
		/// Left button: a drag from the bag lands here as an offer. Right button: an offered
		/// item is taken back.
		/// </summary>
		private void OnOwnSlotPointerDown(PointerDownEvent evt, SlotView view)
		{
			if (!SessionOpen)
			{
				return;
			}

			if (evt.button == 1)
			{
				evt.StopPropagation();
				if (view.Entry.HasValue)
				{
					WithdrawInventorySlot(view.Entry.Value.Slot);
				}
				return;
			}

			if (evt.button == 0)
			{
				TryCompleteDrop();
			}
		}

		/// <summary>A held drag released over the table is a drop too.</summary>
		private void OnOwnSlotPointerUp(PointerUpEvent evt, SlotView view)
		{
			if (!SessionOpen || evt.button != 0)
			{
				return;
			}
			TryCompleteDrop();
		}

		/// <summary>
		/// Turns whatever the drag object holds into an offer, if it is an inventory item.
		/// </summary>
		private void TryCompleteDrop()
		{
			if (!UIManager.TryGetTK(DRAG_OBJECT_NAME, out UITKDragObject dragObject) || !dragObject.IsDragging)
			{
				return;
			}

			if (dragObject.Type != ReferenceButtonType.Inventory)
			{
				// Bank, equipment, ability: none of those can go on a trade table.
				dragObject.Clear();
				return;
			}

			int slot = (int)dragObject.ReferenceID;
			uint amount = dragObject.SplitAmount;

			if (Character != null &&
				Character.TryGet(out IInventoryController inventory) &&
				inventory.TryGetItem(slot, out Item item) &&
				!dragObject.MatchesSource(item))
			{
				// The bag changed under the drag; the drag is stale.
				dragObject.Clear();
				return;
			}

			TryOfferInventorySlot(slot, amount);
			dragObject.Clear();
		}

		private void OnSlotPointerEnter(SlotView view)
		{
			if (!view.Entry.HasValue || !UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				return;
			}

			Item item = view.TooltipItem;
			if (item == null && Character != null && Character.TryGet(out IInventoryController inventory))
			{
				inventory.TryGetItem(view.Entry.Value.Slot, out item);
			}

			if (item != null)
			{
				tooltip.Open(item, view.Root);
			}
		}

		private void OnSlotPointerLeave(SlotView view)
		{
			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				tooltip.HideFor(view.Root);
			}
		}

		// ── Range ───────────────────────────────────────────────────────────────────────────

		/// <summary>
		/// True when the partner is observed and within <see cref="MaxDistance"/>.
		/// </summary>
		/// <remarks>
		/// A partner this client no longer observes has, from here, walked out of the world —
		/// the observer range is far larger than the trade range, so the only way that
		/// happens within range is a scene change, which the server closes on anyway.
		/// </remarks>
		private bool IsPartnerInRange()
		{
			if (Character?.Transform == null)
			{
				return false;
			}

			if (!BaseCharacter.ClientCharacters.TryGetValue(PartnerCharacterID, out ICharacter partner) ||
				partner?.Transform == null)
			{
				return false;
			}

			return TradeRules.IsWithinRange(Character.Transform.position, partner.Transform.position, MaxDistance);
		}

		// ── Local locks ─────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Makes the bag's lock set equal to the offered slots: the bag greys them, refuses
		/// to drag or split them, and the consumable finder skips them — deterministically
		/// with the server, which locked the same slots from the same state.
		/// </summary>
		private void SyncLocalLocks(TradeOfferEntry[] ownOffer)
		{
			if (Character == null || !Character.TryGet(out IInventoryController inventory))
			{
				return;
			}

			var wanted = new HashSet<int>();
			if (ownOffer != null)
			{
				for (int i = 0; i < ownOffer.Length; ++i)
				{
					wanted.Add(ownOffer[i].Slot);
				}
			}

			List<int> release = null;
			foreach (int slot in lockedInventorySlots)
			{
				if (!wanted.Contains(slot))
				{
					(release ??= new List<int>()).Add(slot);
				}
			}
			if (release != null)
			{
				for (int i = 0; i < release.Count; ++i)
				{
					inventory.UnlockSlot(release[i]);
					lockedInventorySlots.Remove(release[i]);
				}
			}

			foreach (int slot in wanted)
			{
				if (lockedInventorySlots.Add(slot))
				{
					inventory.LockSlot(slot);
				}
			}
		}

		private void ReleaseLocalLocks()
		{
			if (lockedInventorySlots.Count == 0)
			{
				return;
			}

			if (Character != null && Character.TryGet(out IInventoryController inventory))
			{
				foreach (int slot in lockedInventorySlots)
				{
					inventory.UnlockSlot(slot);
				}
			}
			lockedInventorySlots.Clear();
		}

		private void ResetSession()
		{
			SessionOpen = false;
			hasState = false;
			State = default;
			PartnerCharacterID = 0;
			PartnerName = string.Empty;
		}

		// ── Wording ─────────────────────────────────────────────────────────────────────────

		private void Toast(string text, ToastSeverity severity)
		{
			if (UIManager.TryGetTK(TOAST_NAME, out UITKToast toast))
			{
				toast.Show(text, severity);
			}
			else
			{
				Log.Debug("UITKTrade", text);
			}
		}

		/// <summary>Player wording for a refused invitation.</summary>
		public static string DescribeRequestFailure(TradeRequestFailure failure)
		{
			switch (failure)
			{
				case TradeRequestFailure.TargetUnavailable: return "That player cannot trade right now.";
				case TradeRequestFailure.TargetBusy: return "That player is already trading.";
				case TradeRequestFailure.SelfBusy: return "You already have a trade or invitation open.";
				case TradeRequestFailure.OutOfRange: return "That player is too far away to trade with.";
				case TradeRequestFailure.Declined: return "Your trade request was declined.";
				case TradeRequestFailure.Expired: return "Your trade request was not answered.";
				case TradeRequestFailure.Throttled: return "Please wait before sending another trade request.";
				case TradeRequestFailure.CannotAct: return "You cannot trade right now.";
				default: return "The trade could not be started.";
			}
		}

		/// <summary>Player wording for a refused change to the table.</summary>
		public static string DescribeRefusal(TradeRefusalReason reason)
		{
			switch (reason)
			{
				case TradeRefusalReason.NotOpen: return "The trade is no longer open for changes.";
				case TradeRefusalReason.CannotAct: return "You cannot trade right now.";
				case TradeRefusalReason.InvalidSlot: return "There is nothing in that slot to offer.";
				case TradeRefusalReason.ItemLocked: return "That item is busy and cannot be offered right now.";
				case TradeRefusalReason.ItemNotReady: return "That item is still being saved; try again in a moment.";
				case TradeRefusalReason.BadAmount: return "You cannot offer that many.";
				case TradeRefusalReason.AlreadyOffered: return "That item is already on the table.";
				case TradeRefusalReason.TableFull: return "Your side of the table is full.";
				case TradeRefusalReason.NotOffered: return "That item is not on the table.";
				case TradeRefusalReason.InsufficientCurrency: return "You do not have that much currency.";
				case TradeRefusalReason.NoCurrency: return "Currency cannot be traded here.";
				case TradeRefusalReason.StaleVersion: return "The offer changed; review it and accept again.";
				case TradeRefusalReason.NoRoom: return "You do not have enough bag space for their offer.";
				case TradeRefusalReason.PartnerNoRoom: return "They do not have enough bag space for your offer.";
				default: return "The trade could not be changed.";
			}
		}

		/// <summary>Player wording for a closed session.</summary>
		public static string DescribeClose(TradeCloseReason reason)
		{
			switch (reason)
			{
				case TradeCloseReason.Completed: return "Trade complete.";
				case TradeCloseReason.Cancelled: return "Trade cancelled.";
				case TradeCloseReason.PartnerCancelled: return "The other player cancelled the trade.";
				case TradeCloseReason.OutOfRange: return "Trade cancelled: out of range.";
				case TradeCloseReason.PartnerLeft: return "Trade cancelled: the other player left.";
				case TradeCloseReason.CannotAct: return "Trade cancelled: one of you cannot act right now.";
				case TradeCloseReason.NoRoom: return "Trade cancelled: not enough bag space.";
				case TradeCloseReason.ServerShutdown: return "Trade cancelled by the server.";
				default: return "Trade cancelled.";
			}
		}
	}
}
