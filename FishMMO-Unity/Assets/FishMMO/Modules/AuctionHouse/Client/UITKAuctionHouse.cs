using System;
using System.Collections.Generic;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// The auction house window (issue #299): Browse, Sell and My Listings. Binds to <c>UIAuctionHouse.uxml</c> /
	/// <c>UIAuctionHouse.uss</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A view of server state, like the mail and the merchant. Nothing is changed locally: posting, buying and cancelling
	/// send a request, the buttons lock until its one answer arrives, and the lists are fetched again. What is bought or
	/// handed back arrives in the mailbox, so the window never claims the player has something they may not.
	/// </para>
	/// <para>
	/// Selling names an inventory <em>slot</em>: right-click an item in the bags while this window is open (the
	/// <see cref="InventorySlotClaims"/> seam, <see cref="AuctionHouseClient"/>). Spending money takes two presses, like
	/// giving up land: one stray click must not buy something.
	/// </para>
	/// </remarks>
	public class UITKAuctionHouse : UITKCharacterControl
	{
		/// <summary>The control's registered name.</summary>
		public const string ControlName = "UIAuctionHouse";

		/// <summary>The currency attribute template, by id like the other currency-aware panels. Zero: no balance shown.</summary>
		public int CurrencyTemplateID;

		[Tooltip("Open the inventory with the auction house, so there is something to right-click to sell.")]
		[SerializeField] private bool openInventoryOnShow = true;

		/// <inheritdoc />
		protected override bool OpensInventoryOnShow => openInventoryOnShow;

		private enum Tab { Browse, Sell, Mine }

		private const string TOOLTIP_NAME = "UITooltip";
		private const string CSS_HIDDEN = "ah-hidden";
		private const string CSS_TAB_ACTIVE = "fish-tab--active";

		/// <summary>Seconds a first press of Buy or Cancel stays armed.</summary>
		private const float ArmSeconds = 5f;

		/// <summary>Seconds before an unanswered request stops locking the window.</summary>
		private const float RequestTimeoutSeconds = 15f;

		private static readonly string[] SortLabels = { "Price each", "Total price", "Time left", "Name" };
		private static readonly string[] DurationLabels = { "12 hours", "24 hours", "48 hours" };

		// ── State ─────────────────────────────────────────────────────────────

		private long auctioneerID;
		private Tab tab = Tab.Browse;

		private AuctionListingEntry[] results = Array.Empty<AuctionListingEntry>();
		private int page;
		private int totalCount;
		private bool searched;
		private bool descending;

		private AuctionListingEntry[] mine;
		private int maxListings = AuctionRules.DefaultMaxActiveListings;

		private int sellSlot = -1;
		private int hintTemplateID;
		private long hintLowest;
		private long hintListed;
		private bool hintKnown;
		private float depositPercent = AuctionRules.DefaultDepositPercent;
		private long minimumDeposit = 1;
		private float cutPercent = AuctionRules.DefaultCutPercent;

		private bool requestInFlight;
		private float requestAge;
		private long armedListing;
		private float armedFor;
		private string status = string.Empty;

		// ── Elements ──────────────────────────────────────────────────────────

		private Button tabBrowse, tabSell, tabMine;
		private VisualElement paneBrowse, paneSell, paneMine;
		private TextField searchField;
		private DropdownField categoryField, sortField;
		private Button directionButton, prevButton, nextButton;
		private ScrollView resultList, mineList;
		private Label resultEmpty, pageLabel, mineEmpty, mineCount, statusLabel, balanceLabel;
		private VisualElement sellIcon;
		private Label sellName, sellTotal, sellDeposit, sellProceeds, sellHint;
		private TextField sellQuantity, sellPrice;
		private DropdownField sellDuration;
		private Button postButton;

		// ── Lifecycle ─────────────────────────────────────────────────────────

		/// <inheritdoc />
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			Button close = root.Q<Button>("close-button");
			if (close != null)
			{
				close.clicked += Hide;
			}

			tabBrowse = root.Q<Button>("ah-tab-browse");
			tabSell = root.Q<Button>("ah-tab-sell");
			tabMine = root.Q<Button>("ah-tab-mine");
			if (tabBrowse != null) tabBrowse.clicked += () => SetTab(Tab.Browse);
			if (tabSell != null) tabSell.clicked += () => SetTab(Tab.Sell);
			if (tabMine != null) tabMine.clicked += () => SetTab(Tab.Mine);

			paneBrowse = root.Q<VisualElement>("ah-browse");
			paneSell = root.Q<VisualElement>("ah-sell");
			paneMine = root.Q<VisualElement>("ah-mine");

			searchField = root.Q<TextField>("ah-search");
			if (searchField != null)
			{
				searchField.maxLength = AuctionRules.MaxSearchLength;
				searchField.RegisterCallback<KeyDownEvent>(evt =>
				{
					// keyCode only: the twin event that carries the newline character has KeyCode.None.
					if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
					{
						Search(0);
					}
				}, TrickleDown.TrickleDown);
			}
			categoryField = root.Q<DropdownField>("ah-category");
			if (categoryField != null)
			{
				var choices = new List<string>();
				for (AuctionCategory c = AuctionCategory.All; c <= AuctionCategory.Other; c++)
				{
					choices.Add(AuctionRules.Label(c));
				}
				categoryField.choices = choices;
				categoryField.index = 0;
				categoryField.RegisterValueChangedCallback(_ => Search(0));
			}
			sortField = root.Q<DropdownField>("ah-sort");
			if (sortField != null)
			{
				sortField.choices = new List<string>(SortLabels);
				sortField.index = 0;
				sortField.RegisterValueChangedCallback(_ => Search(0));
			}
			directionButton = root.Q<Button>("ah-direction");
			if (directionButton != null)
			{
				directionButton.clicked += () =>
				{
					descending = !descending;
					Search(0);
				};
			}
			Button searchButton = root.Q<Button>("ah-search-button");
			if (searchButton != null) searchButton.clicked += () => Search(0);
			prevButton = root.Q<Button>("ah-prev");
			nextButton = root.Q<Button>("ah-next");
			if (prevButton != null) prevButton.clicked += () => Search(page - 1);
			if (nextButton != null) nextButton.clicked += () => Search(page + 1);
			resultList = root.Q<ScrollView>("ah-results");
			resultEmpty = root.Q<Label>("ah-results-empty");
			pageLabel = root.Q<Label>("ah-page");

			sellIcon = root.Q<VisualElement>("ah-sell-icon");
			sellName = root.Q<Label>("ah-sell-name");
			sellQuantity = root.Q<TextField>("ah-sell-quantity");
			sellPrice = root.Q<TextField>("ah-sell-price");
			sellDuration = root.Q<DropdownField>("ah-sell-duration");
			sellTotal = root.Q<Label>("ah-sell-total");
			sellDeposit = root.Q<Label>("ah-sell-deposit");
			sellProceeds = root.Q<Label>("ah-sell-proceeds");
			sellHint = root.Q<Label>("ah-sell-hint");
			postButton = root.Q<Button>("ah-post");
			if (sellDuration != null)
			{
				sellDuration.choices = new List<string>(DurationLabels);
				sellDuration.index = (int)AuctionDuration.Medium;
				sellDuration.RegisterValueChangedCallback(_ => FillSell());
			}
			sellQuantity?.RegisterValueChangedCallback(_ => FillSell());
			sellPrice?.RegisterValueChangedCallback(_ => FillSell());
			if (postButton != null) postButton.clicked += Post;

			mineList = root.Q<ScrollView>("ah-mine-list");
			mineEmpty = root.Q<Label>("ah-mine-empty");
			mineCount = root.Q<Label>("ah-mine-count");

			statusLabel = root.Q<Label>("ah-status");
			balanceLabel = root.Q<Label>("ah-balance");
		}

		/// <inheritdoc />
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			FillAll();
		}

		/// <inheritdoc />
		protected override void OnAfterShow()
		{
			FillAll();
		}

		/// <inheritdoc />
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<AuctioneerBroadcast>(OnOpen);
			Client.NetworkManager.ClientManager.RegisterBroadcast<AuctionSearchResultBroadcast>(OnResults);
			Client.NetworkManager.ClientManager.RegisterBroadcast<AuctionMyListingsBroadcast>(OnMine);
			Client.NetworkManager.ClientManager.RegisterBroadcast<AuctionPriceHintBroadcast>(OnHint);
			Client.NetworkManager.ClientManager.RegisterBroadcast<AuctionResultBroadcast>(OnResult);
		}

		/// <inheritdoc />
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<AuctioneerBroadcast>(OnOpen);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<AuctionSearchResultBroadcast>(OnResults);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<AuctionMyListingsBroadcast>(OnMine);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<AuctionPriceHintBroadcast>(OnHint);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<AuctionResultBroadcast>(OnResult);
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
			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				tooltip.Hide();
			}
			auctioneerID = 0;
			sellSlot = -1;
			hintKnown = false;
			requestInFlight = false;
			armedListing = 0;
		}

		/// <inheritdoc />
		protected override void OnTick()
		{
			if (!Visible)
			{
				return;
			}
			if (requestInFlight)
			{
				requestAge += Time.deltaTime;
				if (requestAge > RequestTimeoutSeconds)
				{
					requestInFlight = false;
					SetStatus("No answer from the auction house. Try again.");
					FillAll();
				}
			}
			if (armedListing != 0)
			{
				armedFor -= Time.deltaTime;
				if (armedFor <= 0f)
				{
					armedListing = 0;
					FillResults();
					FillMine();
				}
			}
			// Cheap, and the balance moves when a purchase or a deposit is taken.
			FillBalance();
		}

		// ── Public: the inventory's right-click ───────────────────────────────

		/// <summary>True when the window is open at an Auctioneer, so a right-clicked slot is offered for sale.</summary>
		public bool IsOpenAtAuctioneer => Visible && auctioneerID != 0;

		/// <summary>Puts an inventory slot on the Sell tab.</summary>
		public void SelectSellSlot(int slot)
		{
			if (Character == null || !Character.TryGet(out IInventoryController inventory) ||
				!inventory.TryGetItem(slot, out Item item) || item?.Template == null)
			{
				return;
			}
			sellSlot = slot;
			armedListing = 0;
			if (sellQuantity != null)
			{
				sellQuantity.SetValueWithoutNotify((item.IsStackable ? item.Stackable.Amount : 1).ToString());
			}
			if (sellPrice != null)
			{
				sellPrice.SetValueWithoutNotify(Math.Max(1, item.Template.Price).ToString());
			}
			hintKnown = false;
			hintTemplateID = item.Template.ID;
			Client.Broadcast(new AuctionPriceHintRequestBroadcast { InteractableID = auctioneerID, TemplateID = item.Template.ID }, Channel.Reliable);
			SetStatus(string.Empty);
			SetTab(Tab.Sell);
		}

		// ── Broadcasts ────────────────────────────────────────────────────────

		private void OnOpen(AuctioneerBroadcast msg, Channel channel)
		{
			auctioneerID = msg.InteractableID;
			requestInFlight = false;
			armedListing = 0;
			sellSlot = -1;
			SetStatus(string.Empty);
			if (!Visible)
			{
				Show();
			}
			SetTab(tab);
			Search(0);
			RequestMine();
		}

		private void OnResults(AuctionSearchResultBroadcast msg, Channel channel)
		{
			results = msg.Entries ?? Array.Empty<AuctionListingEntry>();
			page = msg.Page;
			totalCount = msg.TotalCount;
			searched = true;
			FillResults();
		}

		private void OnMine(AuctionMyListingsBroadcast msg, Channel channel)
		{
			mine = msg.Entries ?? Array.Empty<AuctionListingEntry>();
			maxListings = Math.Max(1, msg.MaxListings);
			FillMine();
			FillSell();
		}

		private void OnHint(AuctionPriceHintBroadcast msg, Channel channel)
		{
			if (msg.TemplateID != hintTemplateID)
			{
				return;
			}
			hintKnown = true;
			hintLowest = msg.LowestUnitPrice;
			hintListed = msg.ListedQuantity;
			depositPercent = msg.DepositPercent;
			minimumDeposit = msg.MinimumDeposit;
			cutPercent = msg.CutPercent;
			// Start the asking price at the going rate when one is known.
			if (hintLowest > 0 && sellPrice != null)
			{
				sellPrice.SetValueWithoutNotify(hintLowest.ToString());
			}
			FillSell();
		}

		private void OnResult(AuctionResultBroadcast msg, Channel channel)
		{
			requestInFlight = false;
			armedListing = 0;
			string text = AuctionRules.Describe(msg.Operation, msg.Result);
			if (msg.Result == AuctionResult.NotEnough && msg.Quantity > 0)
			{
				text = $"Only {msg.Quantity} left.";
			}
			else if (msg.Result == AuctionResult.Success && msg.Operation == AuctionOperation.Post && msg.Amount > 0)
			{
				text = $"Listed. Deposit paid: {msg.Amount:N0}.";
			}
			SetStatus(text);

			if (msg.Operation == AuctionOperation.Post && msg.Result == AuctionResult.Success)
			{
				sellSlot = -1;
				hintKnown = false;
			}
			if (msg.Operation == AuctionOperation.Buy || msg.Operation == AuctionOperation.Cancel)
			{
				Search(page);
			}
			RequestMine();
			FillAll();
		}

		// ── Requests ──────────────────────────────────────────────────────────

		private void Search(int toPage)
		{
			if (auctioneerID == 0)
			{
				return;
			}
			int pages = Math.Max(1, (totalCount + AuctionRules.PageSize - 1) / AuctionRules.PageSize);
			toPage = searched ? Math.Max(0, Math.Min(toPage, pages - 1)) : Math.Max(0, toPage);
			Client.Broadcast(new AuctionSearchBroadcast
			{
				InteractableID = auctioneerID,
				Name = AuctionRules.CleanSearch(searchField?.value),
				Category = categoryField != null ? (AuctionCategory)Math.Max(0, categoryField.index) : AuctionCategory.All,
				Sort = sortField != null ? (AuctionSort)Math.Max(0, sortField.index) : AuctionSort.UnitPrice,
				Descending = descending,
				Page = toPage,
			}, Channel.Reliable);
		}

		private void RequestMine()
		{
			if (auctioneerID != 0)
			{
				Client.Broadcast(new AuctionMyListingsRequestBroadcast { InteractableID = auctioneerID }, Channel.Reliable);
			}
		}

		private bool BeginRequest()
		{
			if (requestInFlight || auctioneerID == 0)
			{
				return false;
			}
			requestInFlight = true;
			requestAge = 0f;
			return true;
		}

		private void Buy(AuctionListingEntry entry, TextField quantityField)
		{
			int quantity = entry.Quantity;
			if (quantityField != null && (!int.TryParse(quantityField.value, out quantity) || quantity < 1 || quantity > entry.Quantity))
			{
				SetStatus($"Buy between 1 and {entry.Quantity}.");
				return;
			}
			if (!AuctionRules.TryTotal(entry.UnitPrice, quantity, out long total))
			{
				SetStatus(AuctionRules.Describe(AuctionOperation.Buy, AuctionResult.InvalidPrice));
				return;
			}
			if (TryGetBalance(out long balance) && balance < total)
			{
				SetStatus(AuctionRules.Describe(AuctionOperation.Buy, AuctionResult.NotEnoughCurrency));
				return;
			}
			if (armedListing != entry.ListingID)
			{
				armedListing = entry.ListingID;
				armedFor = ArmSeconds;
				SetStatus($"Press Confirm to buy {quantity} for {total:N0}.");
				FillResults();
				return;
			}
			if (!BeginRequest())
			{
				return;
			}
			armedListing = 0;
			SetStatus("Buying…");
			Client.Broadcast(new AuctionBuyBroadcast
			{
				InteractableID = auctioneerID,
				ListingID = entry.ListingID,
				Quantity = quantity,
				ExpectedUnitPrice = entry.UnitPrice,
			}, Channel.Reliable);
			FillResults();
		}

		private void Cancel(AuctionListingEntry entry)
		{
			if (armedListing != entry.ListingID)
			{
				armedListing = entry.ListingID;
				armedFor = ArmSeconds;
				SetStatus("Press Confirm to take it down. The deposit is not returned.");
				FillMine();
				return;
			}
			if (!BeginRequest())
			{
				return;
			}
			armedListing = 0;
			SetStatus("Cancelling…");
			Client.Broadcast(new AuctionCancelBroadcast { InteractableID = auctioneerID, ListingID = entry.ListingID }, Channel.Reliable);
			FillMine();
		}

		private void Post()
		{
			if (!TryReadSell(out Item item, out int quantity, out long unitPrice, out AuctionDuration duration, out long total, out long deposit, out string problem))
			{
				SetStatus(problem);
				return;
			}
			if (!BeginRequest())
			{
				return;
			}
			SetStatus("Listing…");
			Client.Broadcast(new AuctionPostBroadcast
			{
				InteractableID = auctioneerID,
				Slot = sellSlot,
				Quantity = quantity,
				UnitPrice = unitPrice,
				Duration = duration,
			}, Channel.Reliable);
			FillSell();
		}

		/// <summary>Reads and checks the Sell tab, as the server will.</summary>
		private bool TryReadSell(out Item item, out int quantity, out long unitPrice, out AuctionDuration duration, out long total, out long deposit, out string problem)
		{
			item = null;
			quantity = 0;
			unitPrice = 0;
			total = 0;
			deposit = 0;
			duration = sellDuration != null ? (AuctionDuration)Math.Max(0, sellDuration.index) : AuctionDuration.Medium;
			problem = "Right-click an item in your bags to sell it.";
			if (sellSlot < 0 || Character == null || !Character.TryGet(out IInventoryController inventory) ||
				!inventory.TryGetItem(sellSlot, out item) || item?.Template == null)
			{
				item = null;
				return false;
			}
			long available = item.IsStackable ? item.Stackable.Amount : 1;
			if (sellQuantity == null || !int.TryParse(sellQuantity.value, out quantity) || quantity < 1 || quantity > available)
			{
				problem = $"Sell between 1 and {available}.";
				return false;
			}
			if (sellPrice == null || !long.TryParse(sellPrice.value, out unitPrice) || !AuctionRules.TryTotal(unitPrice, quantity, out total))
			{
				problem = AuctionRules.Describe(AuctionOperation.Post, AuctionResult.InvalidPrice);
				return false;
			}
			deposit = AuctionRules.Deposit(item.Template.Price, quantity, duration, depositPercent, minimumDeposit);
			if (mine != null && mine.Length >= maxListings)
			{
				problem = AuctionRules.Describe(AuctionOperation.Post, AuctionResult.LimitReached);
				return false;
			}
			if (TryGetBalance(out long balance) && balance < deposit)
			{
				problem = AuctionRules.Describe(AuctionOperation.Post, AuctionResult.NotEnoughCurrency);
				return false;
			}
			problem = string.Empty;
			return true;
		}

		// ── Drawing ───────────────────────────────────────────────────────────

		private void SetTab(Tab to)
		{
			tab = to;
			armedListing = 0;
			tabBrowse?.EnableInClassList(CSS_TAB_ACTIVE, tab == Tab.Browse);
			tabSell?.EnableInClassList(CSS_TAB_ACTIVE, tab == Tab.Sell);
			tabMine?.EnableInClassList(CSS_TAB_ACTIVE, tab == Tab.Mine);
			paneBrowse?.EnableInClassList(CSS_HIDDEN, tab != Tab.Browse);
			paneSell?.EnableInClassList(CSS_HIDDEN, tab != Tab.Sell);
			paneMine?.EnableInClassList(CSS_HIDDEN, tab != Tab.Mine);
			FillAll();
		}

		private void SetStatus(string text)
		{
			status = text ?? string.Empty;
			if (statusLabel != null)
			{
				statusLabel.text = status;
			}
		}

		private void FillAll()
		{
			FillResults();
			FillSell();
			FillMine();
			FillBalance();
			SetStatus(status);
		}

		private void FillBalance()
		{
			if (balanceLabel == null)
			{
				return;
			}
			balanceLabel.text = TryGetBalance(out long balance) ? $"Gold: {balance:N0}" : string.Empty;
		}

		private bool TryGetBalance(out long balance)
		{
			balance = 0;
			if (Character == null || CurrencyTemplateID == 0)
			{
				return false;
			}
			CharacterAttributeTemplate template = CharacterAttributeTemplate.Get<CharacterAttributeTemplate>(CurrencyTemplateID);
			return template != null && CharacterCurrency.TryGetBalance(Character, template, out balance);
		}

		private void FillResults()
		{
			if (resultList == null)
			{
				return;
			}
			resultList.Clear();
			for (int i = 0; i < results.Length; i++)
			{
				resultList.Add(MakeRow(results[i], mineRow: false));
			}
			if (resultEmpty != null)
			{
				resultEmpty.text = !searched ? "Searching…" : "Nothing for sale matches.";
				resultEmpty.EnableInClassList(CSS_HIDDEN, results.Length > 0);
			}
			int pages = Math.Max(1, (totalCount + AuctionRules.PageSize - 1) / AuctionRules.PageSize);
			if (pageLabel != null)
			{
				pageLabel.text = $"Page {page + 1} of {pages}  ({totalCount:N0} listed)";
			}
			prevButton?.SetEnabled(page > 0);
			nextButton?.SetEnabled(page + 1 < pages);
			if (directionButton != null)
			{
				directionButton.text = descending ? "▼" : "▲";
			}
		}

		private void FillMine()
		{
			if (mineList == null)
			{
				return;
			}
			mineList.Clear();
			int count = mine?.Length ?? 0;
			for (int i = 0; i < count; i++)
			{
				mineList.Add(MakeRow(mine[i], mineRow: true));
			}
			if (mineEmpty != null)
			{
				mineEmpty.text = mine == null ? "Loading…" : "You have nothing for sale.";
				mineEmpty.EnableInClassList(CSS_HIDDEN, count > 0);
			}
			if (mineCount != null)
			{
				mineCount.text = $"{count} of {maxListings} listings. What sells, and what does not, comes back by mail.";
			}
		}

		private void FillSell()
		{
			if (sellName == null)
			{
				return;
			}
			Item item = null;
			if (sellSlot >= 0 && Character != null && Character.TryGet(out IInventoryController inventory))
			{
				inventory.TryGetItem(sellSlot, out item);
			}
			if (item?.Template == null)
			{
				sellName.text = "Right-click an item in your bags to sell it.";
				if (sellIcon != null) sellIcon.style.backgroundImage = StyleKeyword.None;
				SetLabel(sellTotal, string.Empty);
				SetLabel(sellDeposit, string.Empty);
				SetLabel(sellProceeds, string.Empty);
				SetLabel(sellHint, string.Empty);
				postButton?.SetEnabled(false);
				return;
			}

			long available = item.IsStackable ? item.Stackable.Amount : 1;
			sellName.text = available > 1 ? $"{item.Template.Name} (you have {available})" : item.Template.Name;
			if (sellIcon != null)
			{
				sellIcon.style.backgroundImage = item.Template.Icon != null ? new StyleBackground(item.Template.Icon) : new StyleBackground(StyleKeyword.None);
			}

			bool ok = TryReadSell(out _, out int quantity, out long unitPrice, out _, out long total, out long deposit, out string problem);
			if (quantity > 0 && unitPrice > 0)
			{
				AuctionRules.TryTotal(unitPrice, quantity, out total);
			}
			long cut = AuctionRules.Cut(total, cutPercent);
			SetLabel(sellTotal, total > 0 ? $"Buyers pay: {total:N0}" : string.Empty);
			SetLabel(sellDeposit, deposit > 0 ? $"Deposit now: {deposit:N0} (not returned)" : string.Empty);
			SetLabel(sellProceeds, total > 0 ? $"You receive if it sells: {total - cut:N0} (the house keeps {cutPercent:0.#}%)" : string.Empty);
			string hint = !hintKnown ? "Checking the going rate…"
				: hintLowest > 0 ? $"Cheapest listed now: {hintLowest:N0} each ({hintListed:N0} for sale)"
				: "None listed now.";
			SetLabel(sellHint, hint);
			postButton?.SetEnabled(ok && !requestInFlight);
			if (!ok && !string.IsNullOrEmpty(problem) && postButton != null)
			{
				postButton.tooltip = problem;
			}
		}

		private static void SetLabel(Label label, string text)
		{
			if (label != null)
			{
				label.text = text;
			}
		}

		/// <summary>One listing row: icon, name, how many, prices, seller or time, and its action.</summary>
		private VisualElement MakeRow(AuctionListingEntry entry, bool mineRow)
		{
			BaseItemTemplate template = BaseItemTemplate.Get<BaseItemTemplate>(entry.TemplateID);

			var row = new VisualElement();
			row.AddToClassList("fish-row");
			row.AddToClassList("ah-row");
			if (entry.Own && !mineRow)
			{
				row.AddToClassList("fish-row--dim");
			}

			var icon = new VisualElement();
			icon.AddToClassList("ah-row__icon");
			if (template?.Icon != null)
			{
				icon.style.backgroundImage = new StyleBackground(template.Icon);
			}
			row.Add(icon);

			row.Add(MakeLabel(template != null ? template.Name : $"Item #{entry.TemplateID}", "fish-row__name", "ah-row__name"));
			row.Add(MakeLabel($"x{entry.Quantity:N0}", "fish-label--numeric", "ah-row__qty"));
			row.Add(MakeLabel(entry.UnitPrice.ToString("N0"), "fish-label--numeric", "ah-row__price"));
			AuctionRules.TryTotal(entry.UnitPrice, entry.Quantity, out long total);
			row.Add(MakeLabel(total.ToString("N0"), "fish-label--numeric", "ah-row__price"));
			row.Add(MakeLabel(mineRow ? string.Empty : entry.SellerName, "fish-row__meta", "ah-row__seller"));
			row.Add(MakeLabel(AuctionRules.FormatTimeLeft(entry.SecondsLeft), "fish-row__meta", "ah-row__time"));

			if (template != null)
			{
				AuctionListingEntry captured = entry;
				row.RegisterCallback<PointerEnterEvent>(_ => ShowTooltip(captured, template, row));
				row.RegisterCallback<PointerLeaveEvent>(_ => HideTooltip(row));
			}

			bool armed = armedListing == entry.ListingID;
			if (mineRow)
			{
				var cancel = new Button(() => Cancel(entry)) { text = armed ? "Confirm" : "Cancel" };
				cancel.AddToClassList("fish-button");
				cancel.AddToClassList("fish-button--danger");
				cancel.AddToClassList("ah-row__button");
				cancel.SetEnabled(!requestInFlight && entry.SecondsLeft > 0);
				if (entry.SecondsLeft <= 0)
				{
					cancel.tooltip = "Expired: it is on its way back to you by mail.";
				}
				row.Add(cancel);
				return row;
			}

			TextField quantity = null;
			if (entry.Quantity > 1 && !entry.Own)
			{
				quantity = new TextField { value = entry.Quantity.ToString(), maxLength = 9 };
				quantity.AddToClassList("fish-input");
				quantity.AddToClassList("fish-input--compact");
				quantity.AddToClassList("ah-row__buyqty");
				quantity.tooltip = "How many to buy";
				row.Add(quantity);
			}
			else
			{
				var spacer = new VisualElement();
				spacer.AddToClassList("ah-row__buyqty");
				row.Add(spacer);
			}
			TextField capturedQuantity = quantity;
			var buy = new Button(() => Buy(entry, capturedQuantity)) { text = armed ? "Confirm" : "Buy" };
			buy.AddToClassList("fish-button");
			buy.AddToClassList("fish-button--primary");
			buy.AddToClassList("ah-row__button");
			buy.SetEnabled(!entry.Own && !requestInFlight);
			if (entry.Own)
			{
				buy.tooltip = "Your own listing.";
			}
			row.Add(buy);
			return row;
		}

		private void ShowTooltip(AuctionListingEntry entry, BaseItemTemplate template, VisualElement owner)
		{
			if (!UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				return;
			}
			// The seed rides along so a generated item shows the attributes it really has.
			Item item = new Item(0, entry.Seed, template, (uint)Math.Max(1, Math.Min(entry.Quantity, (int)Math.Max(1, template.MaxStackSize))));
			TooltipContent content = item.BuildContent();
			if (!entry.Own)
			{
				content.AddHint($"Sold by {entry.SellerName}");
			}
			tooltip.Open(content, owner);
		}

		private static void HideTooltip(VisualElement owner)
		{
			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				tooltip.HideFor(owner);
			}
		}

		private static Label MakeLabel(string text, string themeClass, string layoutClass)
		{
			var label = new Label(text);
			label.AddToClassList("fish-label");
			label.AddToClassList(themeClass);
			label.AddToClassList(layoutClass);
			return label;
		}
	}
}
