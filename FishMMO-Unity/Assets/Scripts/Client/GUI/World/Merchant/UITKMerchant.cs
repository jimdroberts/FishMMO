using System.Collections.Generic;
using UnityEngine.UIElements;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit merchant panel.
	/// Receives the merchant template via a server broadcast and renders its items, abilities, and
	/// ability events as tabbed entry slots, plus a Sell tab listing the character's own inventory.
	/// Selecting an entry opens the transaction footer, where a quantity is chosen and the trade
	/// explicitly confirmed.
	/// </summary>
	/// <remarks>
	/// <para><b>Why a footer rather than a click.</b> Buying used to be a bare Ctrl+Left click on a
	/// row: one mis-aimed click spent money, and a double click spent it twice. There was no
	/// quantity at all, and no sell path of any kind. Every trade now goes through the footer,
	/// which names what is being traded, how many, and what it costs, and needs a second,
	/// deliberate press to commit.</para>
	///
	/// <para><b>Double-submit.</b> Both directions hold a <see cref="PendingReplyGuard"/> and
	/// disable the confirm button while a request is outstanding. Without it a double click on
	/// Sell submits the same slot twice: the first sale empties the slot, and by the time the
	/// second arrives the slot may hold something else entirely — the item-loss vector. On the buy
	/// side the same double click simply spends twice.</para>
	///
	/// <para><b>Nothing here is authoritative.</b> The quantity is clamped locally for the
	/// player's benefit and clamped again on the server, which recomputes the price from its own
	/// templates. The client never sends a price, and the totals shown here are display only.</para>
	///
	/// <para><b>Content is written after Show, and again after a tree rebuild.</b> A
	/// <c>UIDocument</c> re-clones its UXML on every enable, so anything written before
	/// <see cref="UITKControl.Show"/> is discarded. The merchant offer arrives in a broadcast, is
	/// kept as plain data, and is rendered from <see cref="OnAfterShow"/> and
	/// <see cref="OnAfterStarting"/> — both, because on a panel's very first open
	/// <c>hasStarted</c> is still false and the re-initialisation path bails out before
	/// <c>OnAfterShow</c> would help.</para>
	/// </remarks>
	public class UITKMerchant : UITKCharacterControl
	{
		/// <summary>Name of the items tab button.</summary>
		private const string ITEMS_TAB_NAME = "merchant-tab-items";

		/// <summary>Name of the header close button element.</summary>
		private const string CLOSE_BTN_NAME = "close-button";

		/// <summary>Name of the abilities tab button.</summary>
		private const string ABILITIES_TAB_NAME = "merchant-tab-abilities";

		/// <summary>Name of the ability events tab button.</summary>
		private const string EVENTS_TAB_NAME = "merchant-tab-events";

		/// <summary>Name of the sell tab button.</summary>
		private const string SELL_TAB_NAME = "merchant-tab-sell";

		/// <summary>Name of the items entry container.</summary>
		private const string ITEMS_LIST_NAME = "merchant-items";

		/// <summary>Name of the abilities entry container.</summary>
		private const string ABILITIES_LIST_NAME = "merchant-abilities";

		/// <summary>Name of the ability events entry container.</summary>
		private const string EVENTS_LIST_NAME = "merchant-events";

		/// <summary>Name of the sell entry container.</summary>
		private const string SELL_LIST_NAME = "merchant-sell";

		/// <summary>Name of the transaction footer.</summary>
		private const string TRANSACTION_NAME = "merchant-transaction";

		/// <summary>Name of the footer's selected-entry label.</summary>
		private const string TRANSACTION_LABEL_NAME = "merchant-transaction-name";

		/// <summary>Name of the footer's running total label.</summary>
		private const string TRANSACTION_TOTAL_NAME = "merchant-transaction-total";

		/// <summary>Name of the footer's quantity field.</summary>
		private const string QUANTITY_FIELD_NAME = "merchant-qty-field";

		/// <summary>Name of the footer's decrement button.</summary>
		private const string QUANTITY_LESS_NAME = "merchant-qty-less";

		/// <summary>Name of the footer's increment button.</summary>
		private const string QUANTITY_MORE_NAME = "merchant-qty-more";

		/// <summary>Name of the footer's maximum-quantity button.</summary>
		private const string QUANTITY_MAX_NAME = "merchant-qty-max";

		/// <summary>Name of the footer's confirm button.</summary>
		private const string CONFIRM_BUTTON_NAME = "merchant-confirm-btn";

		/// <summary>Name of the footer's cancel button.</summary>
		private const string CANCEL_BUTTON_NAME = "merchant-cancel-btn";

		/// <summary>Name of the footer's status line.</summary>
		private const string STATUS_LABEL_NAME = "merchant-status";

		/// <summary>USS class applied to each generated entry slot.</summary>
		private const string ENTRY_CLASS = "merchant-entry";

		/// <summary>USS class applied to an entry's icon element.</summary>
		private const string ENTRY_ICON_CLASS = "merchant-entry__icon";

		/// <summary>USS class applied to an entry's name label.</summary>
		private const string ENTRY_NAME_CLASS = "merchant-entry__name";

		/// <summary>USS class applied to an entry's price label.</summary>
		private const string ENTRY_PRICE_CLASS = "merchant-entry__price";

		/// <summary>USS class marking the currently selected entry.</summary>
		private const string ENTRY_SELECTED_CLASS = "merchant-entry--selected";

		/// <summary>Tooltip hint appended to entry tooltips.</summary>
		private const string PURCHASE_HINT = "\r\n\r\nClick to select, then confirm below.";

		/// <summary>Name of the shared tooltip overlay panel.</summary>
		private const string TOOLTIP_NAME = "UITooltip";

		/// <summary>Name of the shared confirmation dialog.</summary>
		private const string DIALOG_NAME = "UIDialogBox";

		/// <summary>
		/// How long a buy request waits before the confirm button is handed back.
		/// </summary>
		/// <remarks>
		/// A purchase has no dedicated reply broadcast — success arrives as an inventory update and
		/// a refusal arrives as nothing at all — so the guard is a short watchdog rather than a
		/// true request/response pair. Long enough to cover a round trip that waits on a database
		/// write, short enough that a refused purchase does not leave the button dead.
		/// </remarks>
		private const float BUY_TIMEOUT_SECONDS = 5.0f;

		/// <summary>The items tab button.</summary>
		private Button itemsTab;
		/// <summary>The abilities tab button.</summary>
		private Button abilitiesTab;
		/// <summary>The ability events tab button.</summary>
		private Button eventsTab;
		/// <summary>The sell tab button.</summary>
		private Button sellTab;
		/// <summary>The container that holds the item entry slots.</summary>
		private VisualElement itemsList;
		/// <summary>The container that holds the ability entry slots.</summary>
		private VisualElement abilitiesList;
		/// <summary>The container that holds the ability event entry slots.</summary>
		private VisualElement eventsList;
		/// <summary>The container that holds the sellable inventory slots.</summary>
		private VisualElement sellList;

		/// <summary>The transaction footer root.</summary>
		private VisualElement transactionFooter;
		/// <summary>Label naming the selected entry.</summary>
		private Label transactionLabel;
		/// <summary>Label showing the running total.</summary>
		private Label transactionTotal;
		/// <summary>Quantity entry field.</summary>
		private IntegerField quantityField;
		/// <summary>Confirm button.</summary>
		private Button confirmButton;
		/// <summary>Status line.</summary>
		private Label statusLabel;

		/// <summary>The interactable ID of the current merchant.</summary>
		private long lastMerchantID;
		/// <summary>The template ID of the current merchant.</summary>
		private int currentTemplateID;
		/// <summary>The currently visible merchant tab.</summary>
		private MerchantTabType currentTab = MerchantTabType.Item;

		/// <summary>True while the sell tab is the visible one.</summary>
		/// <remarks>
		/// Selling is not a <see cref="MerchantTabType"/> — that enum is part of the purchase wire
		/// format and adding a member to it would change the meaning of a value the server already
		/// switches on. The sell tab is tracked alongside it instead.
		/// </remarks>
		private bool sellTabActive;

		/// <summary>The element of the currently selected entry, or null.</summary>
		private VisualElement selectedElement;

		/// <summary>Tab the current selection belongs to.</summary>
		private MerchantTabType selectedTab;

		/// <summary>Index of the selection within its tab list, or the inventory slot when selling.</summary>
		private int selectedIndex = -1;

		/// <summary>Unit price of the selection, for display only.</summary>
		private int selectedUnitPrice;

		/// <summary>Largest quantity the selection allows.</summary>
		private int selectedMaxQuantity = 1;

		/// <summary>True when the current selection is a sale rather than a purchase.</summary>
		private bool selectionIsSale;

		/// <summary>Watchdog for an outstanding buy request.</summary>
		private readonly PendingReplyGuard buyGuard = new PendingReplyGuard();

		/// <summary>Watchdog for an outstanding sell request.</summary>
		private readonly PendingReplyGuard sellGuard = new PendingReplyGuard();

		/// <summary>True once a sell-list rebuild has been requested for this frame.</summary>
		private bool sellRebuildQueued;

		/// <summary>
		/// Queries the tab buttons, entry containers and transaction footer, and wires them up.
		/// </summary>
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			/* Resolved from the tree rather than cached: OnStarting re-runs on every reopen
			 * against a freshly cloned tree, so this is a new element each time and the
			 * handler cannot accumulate the way a subscription to a static event would. */
			Button closeButton = root.Q<Button>(CLOSE_BTN_NAME);
			if (closeButton != null)
			{
				closeButton.clicked += Hide;
			}

			itemsTab = root.Q<Button>(ITEMS_TAB_NAME);
			abilitiesTab = root.Q<Button>(ABILITIES_TAB_NAME);
			eventsTab = root.Q<Button>(EVENTS_TAB_NAME);
			sellTab = root.Q<Button>(SELL_TAB_NAME);
			itemsList = root.Q(ITEMS_LIST_NAME);
			abilitiesList = root.Q(ABILITIES_LIST_NAME);
			eventsList = root.Q(EVENTS_LIST_NAME);
			sellList = root.Q(SELL_LIST_NAME);

			transactionFooter = root.Q(TRANSACTION_NAME);
			transactionLabel = root.Q<Label>(TRANSACTION_LABEL_NAME);
			transactionTotal = root.Q<Label>(TRANSACTION_TOTAL_NAME);
			quantityField = root.Q<IntegerField>(QUANTITY_FIELD_NAME);
			confirmButton = root.Q<Button>(CONFIRM_BUTTON_NAME);
			statusLabel = root.Q<Label>(STATUS_LABEL_NAME);

			if (itemsTab != null)
			{
				itemsTab.clicked += () => SwitchTab(MerchantTabType.Item);
			}
			if (abilitiesTab != null)
			{
				abilitiesTab.clicked += () => SwitchTab(MerchantTabType.Ability);
			}
			if (eventsTab != null)
			{
				eventsTab.clicked += () => SwitchTab(MerchantTabType.AbilityEvent);
			}
			if (sellTab != null)
			{
				sellTab.clicked += SwitchToSellTab;
			}

			Button less = root.Q<Button>(QUANTITY_LESS_NAME);
			if (less != null)
			{
				less.clicked += () => NudgeQuantity(-1);
			}
			Button more = root.Q<Button>(QUANTITY_MORE_NAME);
			if (more != null)
			{
				more.clicked += () => NudgeQuantity(1);
			}
			Button max = root.Q<Button>(QUANTITY_MAX_NAME);
			if (max != null)
			{
				max.clicked += () => SetQuantity(selectedMaxQuantity);
			}
			if (confirmButton != null)
			{
				confirmButton.clicked += OnConfirmClicked;
			}
			Button cancel = root.Q<Button>(CANCEL_BUTTON_NAME);
			if (cancel != null)
			{
				cancel.clicked += ClearSelection;
			}
			if (quantityField != null)
			{
				quantityField.RegisterValueChangedCallback(OnQuantityFieldChanged);
			}
		}

		/// <summary>
		/// Registers the merchant broadcast handlers when the client is set.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<MerchantBroadcast>(OnClientMerchantBroadcastReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<MerchantSellResultBroadcast>(OnClientMerchantSellResultReceived);
		}

		/// <summary>
		/// Unregisters the merchant broadcast handlers when the client is unset.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<MerchantBroadcast>(OnClientMerchantBroadcastReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<MerchantSellResultBroadcast>(OnClientMerchantSellResultReceived);
		}

		/// <summary>
		/// Drops the outgoing character's inventory subscription.
		/// </summary>
		/// <remarks>
		/// Overridden rather than left to <see cref="OnPreUnsetCharacter"/> because
		/// <c>OnAfterStarting</c> re-runs the Pre/Post pair on every visual tree rebuild and never
		/// touches the Unset half. Subscribing in Post without a matching unsubscribe in Pre is
		/// how a panel ends up with one extra handler per reopen.
		/// </remarks>
		public override void OnPreSetCharacter()
		{
			UnsubscribeInventory();
		}

		/// <summary>
		/// Subscribes to inventory changes so the Sell tab tracks the character's bags.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character != null && Character.TryGet(out IInventoryController inventoryController))
			{
				inventoryController.OnSlotUpdated += Inventory_OnSlotUpdated;
			}
		}

		/// <summary>
		/// Drops the inventory subscription before the character is cleared.
		/// </summary>
		public override void OnPreUnsetCharacter()
		{
			UnsubscribeInventory();
		}

		/// <summary>
		/// Removes the inventory handler from whichever character currently owns it.
		/// </summary>
		private void UnsubscribeInventory()
		{
			if (Character != null && Character.TryGet(out IInventoryController inventoryController))
			{
				inventoryController.OnSlotUpdated -= Inventory_OnSlotUpdated;
			}
		}

		/// <summary>
		/// Clears all entry slots when the control is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			UnsubscribeInventory();
			ClearAll();
			base.OnDestroying();
		}

		/// <summary>
		/// Renders the merchant's offer into the tree the player is about to see.
		/// </summary>
		protected override void OnAfterShow()
		{
			RebuildAll();
		}

		/// <summary>
		/// Renders the merchant's offer again after the visual tree has been rebuilt.
		/// </summary>
		/// <remarks>
		/// Both hooks are needed. <c>OnAfterShow</c> alone does nothing on a panel's first open,
		/// because the tree-replacement check bails out while <c>hasStarted</c> is still false;
		/// <c>OnAfterStarting</c> alone misses every subsequent reopen.
		/// </remarks>
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			RebuildAll();
		}

		/// <summary>
		/// Hides the tooltip along with the panel.
		/// </summary>
		/// <remarks>
		/// Closing the merchant with Escape while the pointer sat over a row left the tooltip on
		/// screen for good: the row's PointerLeave never fires, because the element is not left —
		/// it is destroyed along with the rest of the tree when the document is disabled.
		/// </remarks>
		public override void Hide(bool overrideIsAlwaysOpen)
		{
			/* Guarded on Visible so closing an already-closed merchant cannot dismiss a tooltip
			 * that belongs to some other panel. */
			if (!overrideIsAlwaysOpen && Visible &&
				UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				tooltip.Hide();
			}
			base.Hide(overrideIsAlwaysOpen);
		}

		/// <summary>
		/// Drives the two request watchdogs and the coalesced sell-list rebuild.
		/// </summary>
		protected override void OnTick()
		{
			/* Both are polled, deliberately without short-circuiting: HasExpired is self-clearing
			 * and returns true exactly once per wait, so skipping the second call would leave that
			 * guard latched as pending forever. */
			bool buyTimedOut = buyGuard.HasExpired();
			bool sellTimedOut = sellGuard.HasExpired();
			if (buyTimedOut || sellTimedOut)
			{
				SetStatus("No reply from the server; try again.");
				RefreshConfirmState();
			}

			/* Only while open. The inventory changes constantly during play and this panel is
			 * closed for nearly all of it; rebuilding a hidden panel's list writes rows into a
			 * tree that is discarded on the next open anyway. */
			if (sellRebuildQueued && Visible)
			{
				sellRebuildQueued = false;
				BuildSellEntries();
			}
		}

		/// <summary>
		/// Handles the merchant broadcast: stores the offer and renders it if the panel is open.
		/// </summary>
		/// <param name="msg">The merchant broadcast.</param>
		/// <param name="channel">The network channel.</param>
		private void OnClientMerchantBroadcastReceived(MerchantBroadcast msg, Channel channel)
		{
			lastMerchantID = msg.InteractableID;
			currentTemplateID = msg.TemplateID;

			MerchantTemplate template = MerchantTemplate.Get<MerchantTemplate>(currentTemplateID);
			if (template == null)
			{
				return;
			}

			int abilityCount = template.Abilities != null ? template.Abilities.Count : 0;
			int eventCount = template.AbilityEvents != null ? template.AbilityEvents.Count : 0;
			int itemCount = template.Items != null ? template.Items.Count : 0;

			// Pick the first populated tab, preferring the sell tab only when there is nothing to buy.
			if (itemCount > 0)
			{
				currentTab = MerchantTabType.Item;
				sellTabActive = false;
			}
			else if (abilityCount > 0)
			{
				currentTab = MerchantTabType.Ability;
				sellTabActive = false;
			}
			else if (eventCount > 0)
			{
				currentTab = MerchantTabType.AbilityEvent;
				sellTabActive = false;
			}
			else if (template.BuysItems)
			{
				sellTabActive = true;
			}
			else
			{
				Hide();
				return;
			}

			/* Show first, then render. Enabling the document clones a fresh tree, so anything
			 * written before this line is thrown away — see the class remarks. Show() calls
			 * OnAfterShow, which does the rendering. */
			Show();

			// Already visible: Show is a no-op and OnAfterShow never ran, so render directly.
			RebuildAll();
		}

		/// <summary>
		/// Handles the server's reply to a sell request.
		/// </summary>
		private void OnClientMerchantSellResultReceived(MerchantSellResultBroadcast msg, Channel channel)
		{
			sellGuard.Clear();

			if (msg.Success)
			{
				SetStatus($"Sold {msg.Quantity} for {msg.Payout}.");
				ClearSelection();
			}
			else
			{
				SetStatus("The merchant refused that sale.");
			}

			QueueSellRebuild();
			RefreshConfirmState();
		}

		/// <summary>
		/// Rebuilds every list from the stored merchant template and the character's inventory.
		/// </summary>
		private void RebuildAll()
		{
			if (itemsList == null)
			{
				return;
			}

			MerchantTemplate template = MerchantTemplate.Get<MerchantTemplate>(currentTemplateID);

			int abilityCount = BuildEntries(abilitiesList, template?.Abilities, MerchantTabType.Ability);
			int eventCount = BuildEntries(eventsList, template?.AbilityEvents, MerchantTabType.AbilityEvent);
			int itemCount = BuildEntries(itemsList, template?.Items, MerchantTabType.Item);
			BuildSellEntries();

			SetTabEnabled(abilitiesTab, abilityCount > 0);
			SetTabEnabled(eventsTab, eventCount > 0);
			SetTabEnabled(itemsTab, itemCount > 0);
			SetTabEnabled(sellTab, template != null && template.BuysItems);

			/* The selection lived in the tree that was just replaced, so it cannot survive a
			 * rebuild. Dropping it also closes the footer, which is the correct state for a panel
			 * that has just been re-rendered. */
			ClearSelection();

			ApplyTabVisibility();
		}

		/// <summary>
		/// Builds entry slots for a list of templates into the given container.
		/// </summary>
		/// <typeparam name="T">A tooltip-providing template type.</typeparam>
		/// <param name="container">The container to populate.</param>
		/// <param name="entries">The source entries.</param>
		/// <param name="tab">The tab the entries belong to.</param>
		/// <returns>The number of entries created.</returns>
		private int BuildEntries<T>(VisualElement container, List<T> entries, MerchantTabType tab) where T : class, ITooltip
		{
			if (container == null)
			{
				return 0;
			}

			container.Clear();

			if (entries == null)
			{
				return 0;
			}

			int created = 0;
			for (int i = 0; i < entries.Count; ++i)
			{
				ITooltip entry = entries[i];
				if (entry == null)
				{
					continue;
				}

				CreateEntry(container, entry, tab, i, ResolvePrice(entry), MaxPurchaseQuantity(entry), false);
				++created;
			}
			return created;
		}

		/// <summary>
		/// Builds one row per sellable inventory slot.
		/// </summary>
		/// <remarks>
		/// Rows carry the inventory slot index, not a position in this list, because the server
		/// resolves the sale by slot and a list position would drift the moment the bag changed.
		/// </remarks>
		private void BuildSellEntries()
		{
			if (sellList == null)
			{
				return;
			}

			sellList.Clear();

			MerchantTemplate template = MerchantTemplate.Get<MerchantTemplate>(currentTemplateID);
			if (template == null || !template.BuysItems ||
				Character == null || !Character.TryGet(out IInventoryController inventoryController))
			{
				return;
			}

			List<Item> items = inventoryController.Items;
			if (items == null)
			{
				return;
			}

			for (int slot = 0; slot < items.Count; ++slot)
			{
				Item item = items[slot];
				if (item == null || item.Template == null || item.Template.Price <= 0)
				{
					continue;
				}

				int unitPayout = UnityEngine.Mathf.FloorToInt(item.Template.Price * template.SellPriceMultiplier);
				int stack = item.IsStackable ? (int)item.Stackable.Amount : 1;

				CreateEntry(sellList, item, MerchantTabType.Item, slot, unitPayout, UnityEngine.Mathf.Max(1, stack), true);
			}
		}

		/// <summary>
		/// Reads the price off a merchant entry, whatever concrete template it is.
		/// </summary>
		/// <remarks>
		/// <see cref="ITooltip"/> carries no price, and the three sellable kinds declare it on
		/// three unrelated base types, so this is a type test rather than an interface call. It is
		/// display only — the server takes the price from its own copy of the same template.
		/// </remarks>
		private static int ResolvePrice(ITooltip entry)
		{
			switch (entry)
			{
				case BaseItemTemplate item: return item.Price;
				case BaseAbilityTemplate ability: return ability.Price;
				case AbilityEvent abilityEvent: return abilityEvent.Price;
				case Item instance: return instance.Template != null ? instance.Template.Price : 0;
				default: return 0;
			}
		}

		/// <summary>
		/// Largest quantity a single purchase of this entry may ask for.
		/// </summary>
		/// <remarks>
		/// One stack for items, because the server grants a purchase as a single Item; exactly one
		/// for abilities and ability events, which are learned rather than stacked.
		/// </remarks>
		private static int MaxPurchaseQuantity(ITooltip entry)
		{
			if (entry is BaseItemTemplate item)
			{
				return item.MaxStackSize > 0 ? (int)item.MaxStackSize : 1;
			}
			return 1;
		}

		/// <summary>
		/// Creates a single merchant entry slot with icon, name, price, tooltip and selection.
		/// </summary>
		/// <param name="container">The container to add the slot to.</param>
		/// <param name="entry">The tooltip entry.</param>
		/// <param name="tab">The tab the entry belongs to.</param>
		/// <param name="index">The entry index within its tab list, or the inventory slot when selling.</param>
		/// <param name="unitPrice">Unit price or payout, for display.</param>
		/// <param name="maxQuantity">Largest quantity this entry allows.</param>
		/// <param name="isSale">True when this row sells to the merchant rather than buys from it.</param>
		private void CreateEntry(VisualElement container, ITooltip entry, MerchantTabType tab, int index,
			int unitPrice, int maxQuantity, bool isSale)
		{
			VisualElement slot = new VisualElement();
			slot.AddToClassList(ENTRY_CLASS);

			VisualElement icon = new VisualElement();
			icon.AddToClassList(ENTRY_ICON_CLASS);
			if (entry.Icon != null)
			{
				icon.style.backgroundImage = new StyleBackground(entry.Icon);
			}
			slot.Add(icon);

			string name = entry.Name;
			if (isSale && entry is Item stackItem && stackItem.IsStackable && stackItem.Stackable.Amount > 1)
			{
				name = $"{name} x{stackItem.Stackable.Amount}";
			}

			Label nameLabel = new Label(name);
			nameLabel.AddToClassList(ENTRY_NAME_CLASS);
			slot.Add(nameLabel);

			Label priceLabel = new Label(unitPrice > 0 ? unitPrice.ToString() : "—");
			priceLabel.AddToClassList(ENTRY_PRICE_CLASS);
			slot.Add(priceLabel);

			slot.RegisterCallback<PointerEnterEvent>(evt => OnEntryPointerEnter(entry, slot));
			slot.RegisterCallback<PointerLeaveEvent>(evt => OnEntryPointerLeave(slot));
			slot.RegisterCallback<PointerDownEvent>(evt => OnEntryPointerDown(evt, slot, tab, index, unitPrice, maxQuantity, name, isSale));

			container.Add(slot);
		}

		/// <summary>
		/// Shows the entry tooltip on hover.
		/// </summary>
		/// <param name="entry">The hovered entry.</param>
		/// <param name="owner">The row the tooltip describes.</param>
		private void OnEntryPointerEnter(ITooltip entry, VisualElement owner)
		{
			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				/* Owned by the row. The tooltip closes itself if that row is removed or hidden,
				 * which is what happens when the list is rebuilt underneath the pointer. */
				tooltip.Open(entry.Tooltip() + PURCHASE_HINT, owner);
			}
		}

		/// <summary>
		/// Hides the entry tooltip when the pointer leaves, if it still belongs to this row.
		/// </summary>
		private void OnEntryPointerLeave(VisualElement owner)
		{
			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				tooltip.HideFor(owner);
			}
		}

		/// <summary>
		/// Selects an entry and opens the transaction footer for it.
		/// </summary>
		private void OnEntryPointerDown(PointerDownEvent evt, VisualElement element, MerchantTabType tab,
			int index, int unitPrice, int maxQuantity, string name, bool isSale)
		{
			if (evt.button != 0 || Character == null)
			{
				return;
			}

			SelectEntry(element, tab, index, unitPrice, maxQuantity, name, isSale);
		}

		/// <summary>
		/// Makes an entry the subject of the transaction footer.
		/// </summary>
		private void SelectEntry(VisualElement element, MerchantTabType tab, int index, int unitPrice,
			int maxQuantity, string name, bool isSale)
		{
			selectedElement?.RemoveFromClassList(ENTRY_SELECTED_CLASS);

			selectedElement = element;
			selectedTab = tab;
			selectedIndex = index;
			selectedUnitPrice = unitPrice;
			selectedMaxQuantity = UnityEngine.Mathf.Max(1, maxQuantity);
			selectionIsSale = isSale;

			selectedElement?.AddToClassList(ENTRY_SELECTED_CLASS);

			if (transactionLabel != null)
			{
				transactionLabel.text = name;
			}
			if (confirmButton != null)
			{
				confirmButton.text = isSale ? "Sell" : "Buy";
			}
			SetStatus(string.Empty);
			SetQuantity(1);
			ShowFooter(true);
		}

		/// <summary>
		/// Drops the selection and closes the transaction footer.
		/// </summary>
		private void ClearSelection()
		{
			selectedElement?.RemoveFromClassList(ENTRY_SELECTED_CLASS);
			selectedElement = null;
			selectedIndex = -1;
			selectedUnitPrice = 0;
			selectedMaxQuantity = 1;
			selectionIsSale = false;
			ShowFooter(false);
		}

		/// <summary>
		/// Shows or hides the transaction footer.
		/// </summary>
		private void ShowFooter(bool visible)
		{
			if (transactionFooter != null)
			{
				transactionFooter.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}

		/// <summary>
		/// Moves the quantity by a step, clamped to the selection's limits.
		/// </summary>
		private void NudgeQuantity(int delta)
		{
			SetQuantity(CurrentQuantity() + delta);
		}

		/// <summary>
		/// Writes a clamped quantity into the field and refreshes the total.
		/// </summary>
		private void SetQuantity(int quantity)
		{
			int clamped = UnityEngine.Mathf.Clamp(quantity, 1, selectedMaxQuantity);
			if (quantityField != null && quantityField.value != clamped)
			{
				// SetValueWithoutNotify: the change callback re-enters this method otherwise.
				quantityField.SetValueWithoutNotify(clamped);
			}
			RefreshTotal();
		}

		/// <summary>
		/// Re-clamps a hand-typed quantity.
		/// </summary>
		private void OnQuantityFieldChanged(ChangeEvent<int> evt)
		{
			SetQuantity(evt.newValue);
		}

		/// <summary>
		/// The quantity currently in the field, clamped.
		/// </summary>
		private int CurrentQuantity()
		{
			int value = quantityField != null ? quantityField.value : 1;
			return UnityEngine.Mathf.Clamp(value, 1, selectedMaxQuantity);
		}

		/// <summary>
		/// Updates the footer's running total and the confirm button's enabled state.
		/// </summary>
		private void RefreshTotal()
		{
			if (transactionTotal != null)
			{
				long total = (long)selectedUnitPrice * CurrentQuantity();
				transactionTotal.text = selectionIsSale ? $"+{total}" : $"-{total}";
			}
			RefreshConfirmState();
		}

		/// <summary>
		/// Disables the confirm button whenever a request is already outstanding.
		/// </summary>
		private void RefreshConfirmState()
		{
			if (confirmButton == null)
			{
				return;
			}
			confirmButton.SetEnabled(selectedIndex >= 0 && !buyGuard.IsPending && !sellGuard.IsPending);
		}

		/// <summary>
		/// Asks for confirmation, then submits the trade.
		/// </summary>
		/// <remarks>
		/// The dialog is the second deliberate act; the guard below is what stops the first one
		/// counting twice. Both are needed — a confirmation dialog does nothing about a double
		/// click on its own Accept button.
		/// </remarks>
		private void OnConfirmClicked()
		{
			if (selectedIndex < 0 || Character == null || buyGuard.IsPending || sellGuard.IsPending)
			{
				return;
			}

			int quantity = CurrentQuantity();
			long total = (long)selectedUnitPrice * quantity;
			string name = transactionLabel != null ? transactionLabel.text : "this";
			string question = selectionIsSale
				? $"Sell {quantity} x {name} for {total}?"
				: $"Buy {quantity} x {name} for {total}?";

			if (UIManager.TryGetTK(DIALOG_NAME, out UITKDialogBox dialog) &&
				dialog.Open(question, SubmitTrade))
			{
				return;
			}

			// No dialog available: the footer itself was already an explicit confirmation step.
			SubmitTrade();
		}

		/// <summary>
		/// Sends the trade the footer describes.
		/// </summary>
		private void SubmitTrade()
		{
			if (selectedIndex < 0 || Character == null || buyGuard.IsPending || sellGuard.IsPending)
			{
				return;
			}

			int quantity = CurrentQuantity();

			if (selectionIsSale)
			{
				sellGuard.Begin();
				SetStatus("Selling…");
				RefreshConfirmState();

				Client.Broadcast(new MerchantSellBroadcast()
				{
					InteractableID = lastMerchantID,
					Slot = selectedIndex,
					Quantity = quantity,
				}, Channel.Reliable);
				return;
			}

			buyGuard.Begin(BUY_TIMEOUT_SECONDS);
			SetStatus("Buying…");
			RefreshConfirmState();

			Client.Broadcast(new MerchantPurchaseBroadcast()
			{
				InteractableID = lastMerchantID,
				ID = currentTemplateID,
				Index = selectedIndex,
				Type = selectedTab,
				Quantity = quantity,
			}, Channel.Reliable);
		}

		/// <summary>
		/// Writes the footer's status line.
		/// </summary>
		private void SetStatus(string text)
		{
			if (statusLabel != null)
			{
				statusLabel.text = text;
			}
		}

		/// <summary>
		/// Requests a sell-list rebuild at most once per frame.
		/// </summary>
		/// <remarks>
		/// A single server update can touch several slots, and each one raises
		/// <c>OnSlotUpdated</c>. Rebuilding per event tore the list down and rebuilt it once per
		/// slot; coalescing to the next tick makes it once per update.
		/// </remarks>
		private void QueueSellRebuild()
		{
			sellRebuildQueued = true;
		}

		/// <summary>
		/// Rebuilds the Sell tab when the character's bags change.
		/// </summary>
		private void Inventory_OnSlotUpdated(IItemContainer container, Item item, int slot)
		{
			QueueSellRebuild();
		}

		/// <summary>
		/// Switches the visible merchant tab.
		/// </summary>
		/// <param name="tab">The tab to display.</param>
		private void SwitchTab(MerchantTabType tab)
		{
			currentTab = tab;
			sellTabActive = false;
			ClearSelection();
			ApplyTabVisibility();
		}

		/// <summary>
		/// Switches to the Sell tab.
		/// </summary>
		private void SwitchToSellTab()
		{
			sellTabActive = true;
			ClearSelection();
			BuildSellEntries();
			ApplyTabVisibility();
		}

		/// <summary>
		/// Applies the current tab selection to the lists, the tab buttons and the header chrome.
		/// </summary>
		private void ApplyTabVisibility()
		{
			SetListVisible(itemsList, !sellTabActive && currentTab == MerchantTabType.Item);
			SetListVisible(abilitiesList, !sellTabActive && currentTab == MerchantTabType.Ability);
			SetListVisible(eventsList, !sellTabActive && currentTab == MerchantTabType.AbilityEvent);
			SetListVisible(sellList, sellTabActive);

			SetTabActive(itemsTab, !sellTabActive && currentTab == MerchantTabType.Item);
			SetTabActive(abilitiesTab, !sellTabActive && currentTab == MerchantTabType.Ability);
			SetTabActive(eventsTab, !sellTabActive && currentTab == MerchantTabType.AbilityEvent);
			SetTabActive(sellTab, sellTabActive);

			/* The header count describes the visible tab, so it is re-pointed here rather than
			 * bound once to the item list at startup. */
			VisualElement active =
				sellTabActive ? sellList :
				currentTab == MerchantTabType.Item ? itemsList :
				currentTab == MerchantTabType.Ability ? abilitiesList : eventsList;
			BindListChrome(
				active,
				Root?.Q<Label>("merchant-count"),
				Root?.Q<Label>("merchant-subtitle"),
				Root?.Q<Label>("merchant-empty"),
				sellTabActive ? "item" : "offer",
				sellTabActive ? "items" : "offers");
		}

		/// <summary>
		/// Toggles an entry container's visibility and layout participation.
		/// </summary>
		/// <param name="list">The container.</param>
		/// <param name="visible">Whether the container should be visible.</param>
		private void SetListVisible(VisualElement list, bool visible)
		{
			if (list != null)
			{
				list.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}

		/// <summary>
		/// Toggles a tab button's enabled state and layout participation.
		/// </summary>
		/// <param name="tab">The tab button.</param>
		/// <param name="enabled">Whether the tab should be shown.</param>
		private void SetTabEnabled(Button tab, bool enabled)
		{
			if (tab != null)
			{
				tab.style.display = enabled ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}

		/// <summary>
		/// Applies the active styling class to the selected tab button.
		/// </summary>
		/// <param name="tab">The tab button.</param>
		/// <param name="active">Whether the tab is the active tab.</param>
		private void SetTabActive(Button tab, bool active)
		{
			if (tab != null)
			{
				tab.EnableInClassList("fish-tab--active", active);
			}
		}

		/// <summary>
		/// Clears all entry containers and resets merchant state.
		/// </summary>
		private void ClearAll()
		{
			lastMerchantID = 0;
			currentTemplateID = 0;
			buyGuard.Clear();
			sellGuard.Clear();
			ClearSelection();
			itemsList?.Clear();
			abilitiesList?.Clear();
			eventsList?.Clear();
			sellList?.Clear();
		}
	}
}
