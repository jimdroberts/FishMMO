using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// Shared behaviour for a panel that draws a character's item container as a grid of slots.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The bank and the inventory were separate implementations of the same panel — 87% identical
	/// after allowing for their vocabulary, sharing twenty-four identically named private methods.
	/// That is why a defect fixed in one stayed in the other: the capacity readout counted icons
	/// rather than items, was found and corrected in the inventory, and went on being wrong in the
	/// bank until it was reported again from play. A second copy does not double the work of a
	/// fix, it halves the chance of one.
	/// </para>
	/// <para>
	/// What made unifying them straightforward is that the domain model had already done it:
	/// <see cref="IBankController"/> and <see cref="IInventoryController"/> both derive from
	/// <see cref="IItemContainer"/>, which exposes everything this view needs — the slot list, the
	/// emptiness test, the item accessor and the two change events. The panels simply were not
	/// using it. Nothing here knows which container it is drawing.
	/// </para>
	/// <para>
	/// The grid is a view of the replicated container and nothing else: no click writes to a slot.
	/// A request goes out, the slot is marked as waiting, and the container being replicated back
	/// is what changes what the player sees.
	/// </para>
	/// <para>
	/// The same argument reached one panel further later on. The equipment panel was a third copy
	/// of everything a SLOT does — the tracker subscription, the lock overlay, the painters, the
	/// tooltip, the drag start — and it had already drifted from this one in three places. All of
	/// that now lives in <see cref="UITKSlotPanelBase"/>, which this class and the equipment panel
	/// both derive from; read its remarks for what had diverged and how each divergence was
	/// settled. What stayed here is the grid itself, and the half of the item protocol a grid
	/// speaks: swap, split, and quick transfer to the other open container.
	/// </para>
	/// </remarks>
	public abstract class UITKItemGridPanel : UITKSlotPanelBase
	{
		// ── What each panel must say about itself ─────────────────────────────

		/* Prefix and DragType are declared by UITKSlotPanelBase now, along with everything a slot
		 * does once it exists: the tracker subscription, the lock overlay, the painters, the
		 * tooltip, the drag start and release. What is left in this file is what makes a GRID a
		 * grid rather than a set of authored sockets — it builds one element per container slot,
		 * counts its capacity, shows a currency chip, and speaks the swap and split half of the
		 * item protocol. See that class for why the split is drawn here and not elsewhere. */

		/// <summary>This panel's container, as the server names it in a request.</summary>
		protected abstract InventoryType OwnInventoryType { get; }

		// ── What the designer configures per panel ────────────────────────────

		/// <summary>
		/// Which character attribute this panel shows in its footer as the currency.
		/// </summary>
		/// <remarks>
		/// Resolved by id like the other currency-aware panels — trade and crafting both take
		/// the same setting — because currency is an ordinary <see cref="CharacterAttribute"/>
		/// rather than a field of its own. See <see cref="CharacterCurrency"/> for why that
		/// matters: the balance is the attribute's base value, and reading the wrong half of it
		/// has already let a buff be spent as money once.
		/// <para>
		/// Zero means this panel shows no currency, and the whole chip is hidden rather than
		/// left standing empty in the footer.
		/// </para>
		/// </remarks>
		[TemplateReference(typeof(CharacterAttributeTemplate))]
		public int CurrencyTemplateID;

		/// <summary>
		/// Asks the server to move an item into one of this panel's slots.
		/// </summary>
		/// <remarks>
		/// The only part of a drop that differs between panels: the request carries the same three
		/// fields either way, but the broadcast type names its destination container.
		/// </remarks>
		/// <param name="fromSlot">Slot the item is leaving.</param>
		/// <param name="toSlot">Slot in this panel it is going to.</param>
		/// <param name="fromInventory">Container the item is leaving.</param>
		protected abstract void SendSwapRequest(int fromSlot, int toSlot, InventoryType fromInventory);

		/// <summary>
		/// Asks the server to split part of a stack into one of this panel's slots. Issue #198.
		/// </summary>
		/// <remarks>
		/// As with <see cref="SendSwapRequest"/>, the request names its DESTINATION container, so
		/// the panel receiving the split half is the one that sends it.
		/// </remarks>
		/// <param name="fromSlot">Slot holding the stack being split.</param>
		/// <param name="toSlot">Slot in this panel the split half is going to.</param>
		/// <param name="fromInventory">Container holding the stack being split.</param>
		/// <param name="amount">How much to take off it.</param>
		protected abstract void SendSplitRequest(int fromSlot, int toSlot, InventoryType fromInventory, uint amount);

		// ── UXML element names ────────────────────────────────────────────────

		private const string SLOT_GRID_NAME = "slot-grid";
		/// <summary>Name of the header subtitle showing slot usage.</summary>
		private const string SUBTITLE_NAME = "header-subtitle";
		private const string CLOSE_BTN_NAME = "close-button";

		/// <summary>Name of the header capacity bar fill.</summary>
		protected virtual string CapacityFillName => Prefix + "-capacity-fill";
		/// <summary>Name of the footer chip showing the character's currency.</summary>
		protected virtual string CurrencyChipName => Prefix + "-currency-chip";
		/// <summary>Name of the label naming the currency attribute.</summary>
		protected virtual string CurrencyNameLabelName => Prefix + "-currency-name";
		/// <summary>Name of the label showing the currency balance.</summary>
		protected virtual string CurrencyValueLabelName => Prefix + "-currency-value";

		// ── Shared UI overlay names (panels resolved by GameObject name via UIManager) ──

		/* DRAG_OBJECT_NAME, TOOLTIP_NAME and TOAST_NAME are on UITKSlotPanelBase. The split prompt
		 * is only reachable from a grid, so its name stays here. */
		protected const string INPUT_DIALOG_NAME = "UIDialogInputBox";

		// ── USS class names ───────────────────────────────────────────────────

		private const string CSS_SLOT = "fish-slot";
		private const string CSS_SLOT_ICON = "fish-slot__icon";
		private const string CSS_SLOT_AMOUNT = "fish-slot__amount";
		private const string CSS_SLOT_LOCK = "fish-slot__lock";

		private string CssSlotGrid => Prefix + "-slot";
		private string CssSlotIconLayout => Prefix + "-slot__icon";
		private string CssSlotAmountLayout => Prefix + "-slot__amount";
		private string CssSlotLockLayout => Prefix + "-slot__lock";

		/* CssHidden and CssLockPending are derived from Prefix by UITKSlotPanelBase, which is where
		 * the slot painters that use them live. The four names above are grid layout classes and
		 * have no counterpart in a socket panel. */

		// ── Private state ─────────────────────────────────────────────────────

		/// <summary>Header line showing slot usage, drawn over the capacity bar.</summary>
		private Label subtitleLabel;
		/// <summary>Header capacity bar fill.</summary>
		private VisualElement capacityFill;

		/// <summary>Footer chip showing the character's currency.</summary>
		private VisualElement currencyChip;
		/// <summary>Label naming the currency attribute.</summary>
		private Label currencyNameLabel;
		/// <summary>Label showing the currency balance.</summary>
		private Label currencyValueLabel;

		/// <summary>
		/// The attribute this panel shows in its footer as the character's currency.
		/// </summary>
		/// <remarks>
		/// Null when no template is configured or the character does not carry that attribute,
		/// in which case the chip is hidden. Held rather than re-resolved on each repaint so the
		/// subscription and the unsubscribe name the same attribute — the character's attribute
		/// set can be replaced under it, and detaching from a re-resolved instance would leave
		/// the original wired to this panel forever.
		/// </remarks>
		private CharacterAttribute currencyAttribute;

		/// <summary>The slot grid container element.</summary>
		private VisualElement slotGrid;

		// ── UITKControl lifecycle ─────────────────────────────────────────────

		/// <summary>
		/// Queries named elements and wires up the close button.
		/// </summary>
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			slotGrid = root.Q(SLOT_GRID_NAME);
			subtitleLabel = root.Q<Label>(SUBTITLE_NAME);
			capacityFill = root.Q(CapacityFillName);

			currencyChip = root.Q(CurrencyChipName);
			currencyNameLabel = root.Q<Label>(CurrencyNameLabelName);
			currencyValueLabel = root.Q<Label>(CurrencyValueLabelName);

			Button closeBtn = root.Q<Button>(CLOSE_BTN_NAME);
			if (closeBtn != null)
			{
				closeBtn.clicked += Hide;
			}
		}

		/* OnAfterStarting, OnAfterShow, OnClientSet, OnClientUnset, OnTick and Hide are all on
		 * UITKSlotPanelBase, which had to gain them because this panel and the equipment panel
		 * had written the same six overrides — including the same first-open trap in the same
		 * comment, twice. */

		/// <summary>
		/// Destroys all runtime slot elements and detaches from the container.
		/// </summary>
		/// <remarks>
		/// The tracker subscription and the drag are released by the base before this runs, which
		/// is the order this panel already used.
		/// </remarks>
		protected override void OnPanelDestroying()
		{
			UnsubscribeContainer();
			DestroySlots();
		}

		// ── Character control ─────────────────────────────────────────────────

		/// <summary>
		/// Unsubscribes from slot events before the character is replaced.
		/// </summary>
		public override void OnPreSetCharacter()
		{
			UnsubscribeContainer();
			UnsubscribeCurrency();
		}

		/// <summary>
		/// Builds the grid for the newly set character and subscribes to slot events.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			/* Before the container is considered, and not behind its null check. The same trap
			 * the subscriptions below carry a warning about: a character with no container yet
			 * would otherwise skip the currency wiring, and nothing else would retry it. */
			SubscribeCurrency();

			DestroySlots();

			IItemContainer container = OwnContainer;
			if (container == null)
			{
				return;
			}

			/* Subscribed before the grid is considered, and not behind a slotGrid check. The
			 * character is set on world entry, which is usually before this panel has ever been
			 * opened — so its UXML has not been cloned and slotGrid is still null. Bailing out
			 * here used to skip the subscriptions as well as the build, and nothing else
			 * subscribes, so the panel spent the rest of the session deaf to slot updates: it drew
			 * whatever the container held at the moment it was opened and never changed again.
			 * Only the grid needs the tree. */
			container.OnSlotUpdated -= OnSlotUpdated;
			container.OnSlotLockChanged -= OnSlotLockChanged;
			container.OnSlotUpdated += OnSlotUpdated;
			container.OnSlotLockChanged += OnSlotLockChanged;

			if (slotGrid == null)
			{
				// Built on the first open instead — see ApplyPerOpenContent.
				return;
			}

			BuildSlots(container);
		}

		/// <summary>
		/// Drops every subscription and in-flight operation before the character goes away.
		/// </summary>
		public override void OnPreUnsetCharacter()
		{
			UnsubscribeContainer();
			UnsubscribeCurrency();
			ReleaseAndClearDrag();
		}

		/// <summary>Detaches this panel's handlers from the container, if there is one.</summary>
		private void UnsubscribeContainer()
		{
			IItemContainer container = OwnContainer;
			if (container == null)
			{
				return;
			}

			container.OnSlotUpdated -= OnSlotUpdated;
			container.OnSlotLockChanged -= OnSlotLockChanged;
		}

		// ── Slot callbacks ────────────────────────────────────────────────────

		/// <summary>
		/// Called when the lock state of a slot changes.
		/// </summary>
		public void OnSlotLockChanged(IItemContainer container, int slot, bool isLocked)
		{
			if (slot >= 0 && slot < slotViews.Count)
			{
				ApplySlotLockVisual(slot, IsSlotBlocked(slot));
			}
		}

		/// <summary>
		/// Called when a slot's item changes.
		/// </summary>
		/// <remarks>
		/// The slot arriving from the server IS the acknowledgement of whatever this panel asked
		/// for, so the pending mark is released here rather than on a separate reply message.
		/// </remarks>
		public void OnSlotUpdated(IItemContainer container, Item item, int slotIndex)
		{
			ApplySlotUpdate(container, item, slotIndex);
		}

		/// <summary>
		/// Rebuilds the grid when an update names a slot the grid does not have.
		/// </summary>
		/// <remarks>
		/// A grid CAN grow, unlike a set of authored sockets, which is why the base asks rather
		/// than assumes. The index arrives past the end when the grid was built before the
		/// container was sized — a grid of zero because the panel was opened before the character
		/// had a container — and nothing else would ever correct it.
		/// </remarks>
		protected override bool TryGrowSlotsFor(int slotIndex)
		{
			return EnsureSlots(OwnContainer);
		}

		/// <summary>
		/// Recomputes the capacity readout whenever a slot's contents are repainted.
		/// </summary>
		/// <remarks>
		/// The one thing the shared painters had to be told about. A socket panel has no capacity
		/// bar, so the base paints the slot and asks here whether anything counts it.
		/// </remarks>
		protected override void OnSlotContentChanged()
		{
			RefreshCapacity();
		}

		// ── Slot element construction ─────────────────────────────────────────

		/// <summary>
		/// Re-reads the whole grid from the character, rebuilding it only if it is stale.
		/// </summary>
		protected override void ApplyPerOpenContent()
		{
			/* Ahead of the container check. A character with no container yet still has a
			 * currency, and the footer is the only place this panel draws it. */
			RefreshCurrency();

			IItemContainer container = OwnContainer;
			if (container == null)
			{
				return;
			}

			if (EnsureSlots(container))
			{
				// Rebuilt, which repaints every slot on the way out.
				return;
			}

			RefreshAllSlots(container);
		}

		/// <summary>
		/// Guarantees the grid holds one element per container slot, rebuilding it when it does
		/// not. Returns true if it rebuilt, in which case every slot has already been repainted.
		/// </summary>
		/// <remarks>
		/// The grid is sized from the container's slot COUNT, never from how many of those slots
		/// hold something. Capacity is what it is whether the container is full or empty, and all
		/// of those frames are drawn either way — an empty slot is the thing the player drops an
		/// item onto and the thing that shows them the space they have, so a grid that renders
		/// only occupied slots has nothing to aim at and no way to convey that it is empty rather
		/// than broken.
		/// <para>
		/// Two ways the grid goes stale. The container changed size — including the case that
		/// matters most here, a grid built at zero because the panel was opened before the
		/// character had one, which nothing else would ever correct. And the elements belong to a
		/// tree that has since been replaced: <c>UIDocument</c> re-clones the UXML on every
		/// enable, and a slot whose parent is not the current grid is drawn nowhere at all while
		/// still looking perfectly valid from C#.
		/// </para>
		/// </remarks>
		private bool EnsureSlots(IItemContainer container)
		{
			if (slotGrid == null || container == null)
			{
				return false;
			}

			bool stale = slotViews.Count != container.Items.Count ||
						 (slotViews.Count > 0 && slotViews[0].Root != null && slotViews[0].Root.parent != slotGrid);

			if (!stale)
			{
				return false;
			}

			DestroySlots();
			BuildSlots(container);
			return true;
		}

		/// <summary>
		/// Creates one element per container slot and fills it from the container.
		/// </summary>
		private void BuildSlots(IItemContainer container)
		{
			int slotCount = container.Items.Count;
			for (int i = 0; i < slotCount; ++i)
			{
				slotViews.Add(CreateSlot(i));
			}

			RefreshAllSlots(container);
		}

		/// <summary>
		/// Creates a single slot element, registers its interaction callbacks, and appends it to
		/// the slot grid.
		/// </summary>
		/// <remarks>
		/// The callbacks are registered on an element created here and thrown away by
		/// <see cref="DestroySlots"/>, so there is nothing to unregister and no accumulation
		/// across rebuilds. That only holds because every rebuild destroys first; a path that
		/// creates slots without destroying the old ones would give one click N handlers.
		/// </remarks>
		private SlotView CreateSlot(int slotIndex)
		{
			VisualElement slotRoot = new VisualElement();
			slotRoot.AddToClassList(CSS_SLOT);
			slotRoot.AddToClassList(CssSlotGrid);

			VisualElement icon = new VisualElement();
			icon.AddToClassList(CSS_SLOT_ICON);
			icon.AddToClassList(CssSlotIconLayout);
			slotRoot.Add(icon);

			Label amount = new Label();
			amount.AddToClassList(CSS_SLOT_AMOUNT);
			amount.AddToClassList(CssSlotAmountLayout);
			amount.AddToClassList(CssHidden);
			slotRoot.Add(amount);

			VisualElement lockOverlay = new VisualElement();
			lockOverlay.AddToClassList(CSS_SLOT_LOCK);
			lockOverlay.AddToClassList(CssSlotLockLayout);
			lockOverlay.AddToClassList(CssHidden);
			slotRoot.Add(lockOverlay);

			int captured = slotIndex;
			slotRoot.RegisterCallback<PointerDownEvent>(evt => OnSlotPointerDown(evt, captured));
			slotRoot.RegisterCallback<PointerEnterEvent>(evt => OnSlotPointerEnter(captured, slotRoot));
			slotRoot.RegisterCallback<PointerLeaveEvent>(evt => OnSlotPointerLeave(slotRoot));
			RegisterExtraSlotCallbacks(slotRoot, captured);

			slotGrid.Add(slotRoot);

			SlotView view;
			view.Root = slotRoot;
			view.Icon = icon;
			view.Amount = amount;
			view.Lock = lockOverlay;
			return view;
		}

		/// <summary>
		/// Lets a panel register additional callbacks on a freshly created slot.
		/// </summary>
		/// <remarks>
		/// Registered here rather than after the fact because these elements are discarded and
		/// rebuilt, so a panel that attached its handlers elsewhere would lose them on the next
		/// rebuild.
		/// </remarks>
		/// <param name="slotRoot">The slot element being created.</param>
		/// <param name="slotIndex">The slot's index, already captured for closures.</param>
		protected virtual void RegisterExtraSlotCallbacks(VisualElement slotRoot, int slotIndex)
		{
		}

		/// <summary>
		/// Removes all runtime slot elements and clears cached state.
		/// </summary>
		/// <remarks>
		/// <c>RemoveFromHierarchy</c>, not <c>slotGrid.Remove</c>.
		/// <c>VisualElement.Remove</c> THROWS when the element is not its child, and after the
		/// document re-clones the UXML these roots belong to the previous tree while
		/// <c>slotGrid</c> is the new one — so the old code threw on the first slot, abandoning
		/// the rebuild and leaving the panel permanently empty on screen.
		/// </remarks>
		private void DestroySlots()
		{
			for (int i = 0; i < slotViews.Count; ++i)
			{
				slotViews[i].Root?.RemoveFromHierarchy();
			}
			slotViews.Clear();
		}

		// ── Capacity readout ──────────────────────────────────────────────────

		/// <summary>
		/// Recomputes the capacity count and the bar it is drawn on.
		/// </summary>
		/// <remarks>
		/// Occupancy comes from the container, which is the only thing that knows it. Both panels
		/// once counted it from a cached sprite per slot, on the reasoning that the sprite array
		/// is the view's own record of which slots are filled — but a sprite records whether an
		/// item has an ICON, not whether a slot holds an item. Every item whose template has no
		/// icon assigned, or whose icon had not finished loading, left a null there and was
		/// counted as an empty slot, so the totals read low and drifted as icons resolved.
		/// <para>
		/// One number, stated once. The count and the bar used to be one of three separate
		/// readouts of the same fact, the other two being a "used" and a "free" figure in the
		/// footer; the footer now shows currency instead, and the count sits on the bar.
		/// </para>
		/// </remarks>
		private void RefreshCapacity()
		{
			int total = slotViews.Count;
			int used = 0;

			IItemContainer container = OwnContainer;
			if (container != null)
			{
				for (int i = 0; i < total; ++i)
				{
					if (!container.IsSlotEmpty(i))
					{
						++used;
					}
				}
			}

			int free = total - used;

			if (subtitleLabel != null)
			{
				/* No noun: the label is drawn on the capacity bar now, under the panel's own
				 * title, which says what is being counted. A resource bar reads "100 / 100"
				 * and this one should read the same way. */
				subtitleLabel.text = $"{used} / {total}";
			}
			if (capacityFill != null)
			{
				float fraction = total > 0 ? (float)used / total : 0.0f;
				capacityFill.style.width = new StyleLength(Length.Percent(fraction * 100.0f));
				// Near-full is worth flagging before the player finds out by failing to loot.
				capacityFill.EnableInClassList("fish-bar__fill--hp", total > 0 && free <= 2);
			}
		}

		// ── Footer currency ───────────────────────────────────────────────────

		/// <summary>
		/// Resolves this panel's currency attribute for the current character and subscribes to it.
		/// </summary>
		/// <remarks>
		/// Every exit path repaints. A character switch, or a template that stops resolving, has
		/// to take the old balance off the screen — leaving the previous character's money in the
		/// footer is worse than leaving it blank.
		/// </remarks>
		private void SubscribeCurrency()
		{
			currencyAttribute = null;

			if (CurrencyTemplateID == 0 || Character == null)
			{
				// Nothing configured: the chip is hidden rather than filled with a guess.
				RefreshCurrency();
				return;
			}

			CharacterAttributeTemplate template =
				CharacterAttributeTemplate.Get<CharacterAttributeTemplate>(CurrencyTemplateID);

			if (template == null ||
				!Character.TryGet(out ICharacterAttributeController attributeController) ||
				!attributeController.TryGetAttribute(template, out CharacterAttribute currency))
			{
				RefreshCurrency();
				return;
			}

			currencyAttribute = currency;
			currencyAttribute.OnAttributeUpdated -= OnCurrencyUpdated;
			currencyAttribute.OnAttributeUpdated += OnCurrencyUpdated;

			RefreshCurrency();
		}

		/// <summary>
		/// Detaches this panel from the currency attribute it resolved.
		/// </summary>
		private void UnsubscribeCurrency()
		{
			if (currencyAttribute == null)
			{
				return;
			}

			currencyAttribute.OnAttributeUpdated -= OnCurrencyUpdated;
			currencyAttribute = null;
		}

		/// <summary>
		/// Repaints the footer when the currency balance changes.
		/// </summary>
		private void OnCurrencyUpdated(CharacterAttribute attribute)
		{
			RefreshCurrency();
		}

		/// <summary>
		/// Paints the footer currency chip, or hides it when there is no currency to show.
		/// </summary>
		/// <remarks>
		/// Reads <see cref="CharacterAttribute.Value"/>, the base value, and not
		/// <c>FinalValue</c>. That is the number spending is checked against, so showing the
		/// final value would offer the player money a currency-boosting buff has lent them and
		/// <see cref="CharacterCurrency.TrySpend"/> would refuse to spend — which is exactly the
		/// defect that helper was written to close.
		/// </remarks>
		private void RefreshCurrency()
		{
			if (currencyChip == null)
			{
				return;
			}

			if (currencyAttribute == null || currencyNameLabel == null || currencyValueLabel == null)
			{
				currencyChip.AddToClassList(CssHidden);
				return;
			}

			/* Template is resolved from the cache when the attribute is constructed, so it is
			 * null for an attribute whose template was never registered — a real possibility
			 * for anything built from an asset that did not go through the boot loader. The
			 * value is still the character's money and still worth showing; only the name is
			 * lost, and it is lost as a blank rather than as an exception in a repaint. */
			CharacterAttributeTemplate template = currencyAttribute.Template;

			currencyChip.RemoveFromClassList(CssHidden);
			currencyNameLabel.text = template != null ? template.Name : string.Empty;
			currencyValueLabel.text = currencyAttribute.Value.ToString();
		}

		/* SetSlotItem, ClearSlot, RefreshSlotTooltip, ApplySlotLockVisual and IsSlotBlocked are all
		 * on UITKSlotPanelBase. They were the largest single block of duplication between this
		 * panel and the equipment panel, and two of them had already diverged — see that class for
		 * which, and which way the divergence was settled. */

		// ── Slot interaction ──────────────────────────────────────────────────

		/// <summary>
		/// Routes pointer-down events on a slot to the left-click handler.
		/// </summary>
		private void OnSlotPointerDown(PointerDownEvent evt, int slotIndex)
		{
			if (Character == null || Client == null)
			{
				return;
			}

			if (evt.button == 0)
			{
				/* Shift is read from the event rather than from global input state: the modifier
				 * that matters is the one held when this click happened, and a poll can answer for
				 * a moment either side of it. */
				if (evt.shiftKey)
				{
					TryQuickTransfer(slotIndex);
				}
				else
				{
					HandleSlotLeftClick(slotIndex);
				}
			}
			else if (evt.button == 1)
			{
				HandleSlotRightClick(slotIndex);
			}
		}

		/// <summary>
		/// The container a shift-click sends an item to, or null when this panel has nowhere to
		/// send one.
		/// </summary>
		protected virtual ReferenceButtonType? QuickTransferTarget => null;

		/// <summary>
		/// Sends the request that moves an item out of this panel and into
		/// <see cref="QuickTransferTarget"/>.
		/// </summary>
		/// <remarks>
		/// The broadcast belongs to the DESTINATION container, not this one — moving into the bank
		/// is a bank swap whoever asked for it — so the panel losing the item is the one that has
		/// to name the other panel's request.
		/// </remarks>
		/// <param name="fromSlot">Slot in this panel the item is leaving.</param>
		/// <param name="toSlot">Slot in the target container it is going to.</param>
		protected virtual void SendQuickTransferRequest(int fromSlot, int toSlot)
		{
		}

		/// <summary>
		/// Moves an item to the other open container without a drag.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Banking a bagful is the operation players repeat most, and doing it by drag means one
		/// press, one aim and one release per item across two panels.
		/// </para>
		/// <para>
		/// The destination is the first free slot this client can see, and the request is the same
		/// swap a drag onto that slot would send. That is deliberate: it carries exactly the risk
		/// the drag it replaces already carries, no more. A slot this client believes is empty and
		/// the server does not would swap two items rather than move one — but the player aiming a
		/// drag at that same slot gets the same outcome, so quick transfer is not introducing a
		/// class of mistake, only saving the aiming.
		/// </para>
		/// </remarks>
		/// <param name="slotIndex">Slot holding the item to send.</param>
		protected void TryQuickTransfer(int slotIndex)
		{
			if (QuickTransferTarget == null)
			{
				return;
			}

			IItemContainer source = OwnContainer;
			IItemContainer target = ResolveContainer(QuickTransferTarget.Value);

			// No target container means the other panel is not something this character has.
			if (source == null || target == null)
			{
				return;
			}

			if (IsSlotBlocked(slotIndex) ||
				!source.TryGetItem(slotIndex, out Item item) ||
				item == null)
			{
				return;
			}

			if (!source.CanManipulate() || !target.CanManipulate())
			{
				return;
			}

			int destination = FirstFreeSlot(target);
			if (destination < 0)
			{
				/* Said out loud. A shift-click that silently does nothing is indistinguishable from
				 * one the game did not register, and the player's next move is to try it again. */
				Notify($"No room in your {QuickTransferTargetName}.", ToastSeverity.Warning);
				return;
			}

			// Claim both ends, or neither: a slot marked as waiting for an unsent request never unlocks.
			if (!ItemOperationTracker.TryBegin(DragType, slotIndex))
			{
				return;
			}
			if (!ItemOperationTracker.TryBegin(QuickTransferTarget.Value, destination))
			{
				ItemOperationTracker.Release(DragType, slotIndex);
				return;
			}

			SendQuickTransferRequest(slotIndex, destination);
		}

		/// <summary>Player-facing name of the quick transfer destination, for a refusal.</summary>
		private string QuickTransferTargetName
		{
			get
			{
				switch (QuickTransferTarget)
				{
					case ReferenceButtonType.Bank: return "bank";
					case ReferenceButtonType.Inventory: return "inventory";
					default: return "bags";
				}
			}
		}

		/// <summary>
		/// The first slot of a container that can take an item, or -1 when none can.
		/// </summary>
		/// <remarks>
		/// A locked slot is skipped rather than treated as free: it is waiting on a request of its
		/// own, and aiming a second one at it would be refused.
		/// </remarks>
		private static int FirstFreeSlot(IItemContainer container)
		{
			for (int i = 0; i < container.Items.Count; ++i)
			{
				if (container.IsSlotEmpty(i) && !container.IsSlotLocked(i))
				{
					return i;
				}
			}

			return -1;
		}

		/// <summary>Shows a transient notice, if the toast panel is up.</summary>
		private static void Notify(string text, ToastSeverity severity)
		{
			if (UIManager.TryGetTK(TOAST_NAME, out UITKToast toast))
			{
				toast.Show(text, severity);
			}
		}

		/// <summary>
		/// Right-click on a slot: offers to split the stack in it. Issue #198.
		/// </summary>
		/// <remarks>
		/// A panel with a better use for the click (the inventory wears what can be worn)
		/// overrides this and falls back to it for everything else, so a stack of arrows splits
		/// the same way in the bag and in the bank.
		/// </remarks>
		/// <param name="slotIndex">The slot that was clicked.</param>
		protected virtual void HandleSlotRightClick(int slotIndex)
		{
			TryPromptSplit(slotIndex);
		}

		/// <summary>
		/// Asks how much of the stack in <paramref name="slotIndex"/> to pick up, then starts a
		/// drag carrying just that much.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Shift-click already means "send to the other container" (issue #197) and press-drag
		/// already means "move the whole stack", so the split is asked for another way: a
		/// right-click on a stack of two or more opens a quantity prompt, and accepting it picks
		/// the split half up as a drag. The drop then names its destination exactly as a drag of
		/// the whole stack would, which is what lets the split reuse every check a drop makes.
		/// </para>
		/// <para>
		/// Nothing is written on this client at any point. The prompt starts a drag, the drop
		/// sends a request, and the two slots repaint when the server sends them back — the same
		/// contract as every other item operation here.
		/// </para>
		/// </remarks>
		/// <param name="slotIndex">Slot holding the stack to split.</param>
		protected void TryPromptSplit(int slotIndex)
		{
			if (Character == null || Client == null || IsSlotBlocked(slotIndex))
			{
				return;
			}

			IItemContainer container = OwnContainer;
			if (container == null ||
				!container.CanManipulate() ||
				!CharacterStateValidation.CanAct(Character) ||
				!container.TryGetItem(slotIndex, out Item item) ||
				item == null ||
				!item.IsStackable ||
				item.Stackable.Amount < 2)
			{
				// One of something cannot be split, and a right-click on it is not an error.
				return;
			}

			/* A right-click while carrying something is not a drop, and starting a second drag
			 * under the first would lose whichever one the player thought they were holding. */
			if (UIManager.TryGetTK(DRAG_OBJECT_NAME, out UITKDragObject dragObject) && dragObject.IsDragging)
			{
				return;
			}

			if (!UIManager.TryGetTK(INPUT_DIALOG_NAME, out UITKDialogInputBox prompt))
			{
				return;
			}

			uint held = item.Stackable.Amount;
			prompt.Open(
				$"Split {item.Name}: how many to pick up? (1 to {held - 1}, or {held} for the whole stack)",
				answer => OnSplitAmountEntered(slotIndex, item, answer));
		}

		/// <summary>
		/// Turns the prompt's answer into a drag, or says why it cannot.
		/// </summary>
		/// <remarks>
		/// Every boundary has a defined answer rather than whatever the arithmetic would do: zero,
		/// a non-number and more than the stack holds are refused out loud; exactly the whole
		/// stack is an ordinary drag of the item, because moving everything is a move, not a
		/// split — and the server refuses a split of the whole stack for the same reason.
		/// </remarks>
		private void OnSplitAmountEntered(int slotIndex, Item item, string answer)
		{
			if (!uint.TryParse(answer?.Trim(), out uint amount) || amount < 1)
			{
				Notify("Enter a whole number of at least 1.", ToastSeverity.Warning);
				return;
			}

			/* Re-read the slot. The prompt was modal but the container was not frozen: a loot
			 * broadcast or the identity write-back can have replaced the item since. The drag has
			 * to start from what is there NOW, or the drop's source check fails for a reason the
			 * player never saw. */
			IItemContainer container = OwnContainer;
			if (container == null ||
				IsSlotBlocked(slotIndex) ||
				!container.TryGetItem(slotIndex, out Item current) ||
				!ReferenceEquals(current, item) ||
				!item.IsStackable)
			{
				Notify("That stack changed; nothing was split.", ToastSeverity.Warning);
				return;
			}

			uint held = item.Stackable.Amount;
			if (amount > held)
			{
				Notify($"There are only {held} there.", ToastSeverity.Warning);
				return;
			}

			if (!UIManager.TryGetTK(DRAG_OBJECT_NAME, out UITKDragObject dragObject) || dragObject.IsDragging)
			{
				return;
			}

			// The whole stack is a move, so it is carried as an ordinary drag: split amount 0.
			Sprite sprite = item.Template != null ? item.Template.Icon : null;
			dragObject.SetItemReference(sprite, slotIndex, DragType, item, amount < held ? amount : 0u);
		}

		/// <summary>
		/// Left-click: completes an in-progress drag or begins dragging this slot's item.
		/// </summary>
		protected virtual void HandleSlotLeftClick(int slotIndex)
		{
			if (!UIManager.TryGetTK(DRAG_OBJECT_NAME, out UITKDragObject dragObject))
			{
				return;
			}

			IItemContainer container = OwnContainer;
			if (container == null)
			{
				return;
			}

			if (dragObject.IsDragging)
			{
				CompleteDropOntoSlot(dragObject, container, slotIndex);
				return;
			}

			BeginDragFromSlot(dragObject, container, slotIndex);
		}

		/// <summary>
		/// Drops whatever the drag is carrying onto <paramref name="slotIndex"/>.
		/// </summary>
		/// <remarks>
		/// This replaced a call to the containers' own <c>CanSwapItemSlots</c>, whose entire body
		/// is <c>return !(fromInventory == InventoryType.Inventory &amp;&amp; from == to)</c>. It
		/// does not look at the containers at all, so it approved moves out of empty slots, out of
		/// locked slots, and into indices past the end; and because the one case it does check
		/// names <c>InventoryType.Inventory</c>, it never even caught a bank slot dropped on
		/// itself. The server rejects all of it, so nothing was corrupted — what it cost was a
		/// round trip and, before <c>ItemOperationFailedBroadcast</c>, silence.
		/// </remarks>
		protected void CompleteDropOntoSlot(UITKDragObject dragObject, IItemContainer container, int slotIndex)
		{
			int sourceSlot = (int)dragObject.ReferenceID;

			/* Equipment drags are an unequip, not a swap: EquipmentUnequipItemBroadcast names the
			 * destination CONTAINER and lets the server choose the slot within it, so the slot the
			 * player aimed at is not part of the request and must not be locked as if it were. */
			if (dragObject.Type == ReferenceButtonType.Equipment)
			{
				CompleteUnequipInto(dragObject, sourceSlot);
				return;
			}

			if (dragObject.Type != ReferenceButtonType.Inventory &&
				dragObject.Type != ReferenceButtonType.Bank)
			{
				// An ability or hotkey drag has no business landing in an item grid.
				dragObject.Clear();
				return;
			}

			InventoryType sourceInventory = dragObject.Type == ReferenceButtonType.Bank
				? InventoryType.Bank
				: InventoryType.Inventory;

			IItemContainer sourceContainer = ResolveContainer(dragObject.Type);

			if (sourceContainer == null ||
				!sourceContainer.CanManipulate() ||
				!container.CanManipulate() ||
				!CharacterStateValidation.CanAct(Character) ||
				!sourceContainer.IsValidSlot(sourceSlot) ||
				!container.IsValidSlot(slotIndex) ||
				(sourceInventory == OwnInventoryType && sourceSlot == slotIndex) ||
				!sourceContainer.TryGetItem(sourceSlot, out Item sourceItem) ||
				!dragObject.MatchesSource(sourceItem) ||
				sourceContainer.IsSlotLocked(sourceSlot) ||
				container.IsSlotLocked(slotIndex))
			{
				dragObject.Clear();
				return;
			}

			// A drag carrying a quantity is a split, not a swap. Same checks so far; different request.
			if (dragObject.SplitAmount > 0)
			{
				CompleteSplitOntoSlot(dragObject, sourceInventory, sourceSlot, sourceItem, container, slotIndex);
				return;
			}

			// Claim both ends, or neither: a slot marked as waiting for an unsent request never unlocks.
			if (!ItemOperationTracker.TryBegin(dragObject.Type, sourceSlot))
			{
				dragObject.Clear();
				return;
			}
			if (!ItemOperationTracker.TryBegin(DragType, slotIndex))
			{
				ItemOperationTracker.Release(dragObject.Type, sourceSlot);
				dragObject.Clear();
				return;
			}

			SendSwapRequest(sourceSlot, slotIndex, sourceInventory);

			dragObject.Clear();
		}

		/// <summary>
		/// Sends the split a quantity-carrying drag was started for. Issue #198.
		/// </summary>
		/// <remarks>
		/// The destination is checked against the rule the server applies — empty, or a matching
		/// stack with room for the whole amount — because a request the client already knows
		/// will be refused costs a round trip and two locked slots. The amount is checked again
		/// too: the stack may have shrunk under the drag, and a split of everything it now holds
		/// is a move, which is not what the player picked up. Either refusal drops the drag and
		/// says why, so a click that did nothing is never mistaken for one the game missed.
		/// </remarks>
		private void CompleteSplitOntoSlot(UITKDragObject dragObject, InventoryType sourceInventory,
			int sourceSlot, Item sourceItem, IItemContainer container, int slotIndex)
		{
			uint amount = dragObject.SplitAmount;

			if (!ItemStackTransfer.IsValidSplitAmount(sourceItem, amount))
			{
				Notify("That stack changed; nothing was split.", ToastSeverity.Warning);
				dragObject.Clear();
				return;
			}

			container.TryGetItem(slotIndex, out Item occupant);
			if (!ItemStackTransfer.CanSplitOnto(occupant, sourceItem, amount))
			{
				Notify("A split stack can only go to an empty slot, or onto a matching stack with room.", ToastSeverity.Warning);
				dragObject.Clear();
				return;
			}

			// Claim both ends, or neither: a slot marked as waiting for an unsent request never unlocks.
			if (!ItemOperationTracker.TryBegin(dragObject.Type, sourceSlot))
			{
				dragObject.Clear();
				return;
			}
			if (!ItemOperationTracker.TryBegin(DragType, slotIndex))
			{
				ItemOperationTracker.Release(dragObject.Type, sourceSlot);
				dragObject.Clear();
				return;
			}

			SendSplitRequest(sourceSlot, slotIndex, sourceInventory, amount);

			dragObject.Clear();
		}

		/// <summary>
		/// Sends an unequip whose destination is this container.
		/// </summary>
		protected void CompleteUnequipInto(UITKDragObject dragObject, int equipmentSlot)
		{
			IItemContainer equipmentContainer = ResolveContainer(ReferenceButtonType.Equipment);

			if (equipmentContainer == null ||
				!equipmentContainer.CanManipulate() ||
				!equipmentContainer.IsValidSlot(equipmentSlot) ||
				equipmentSlot < byte.MinValue || equipmentSlot > byte.MaxValue ||
				!equipmentContainer.TryGetItem(equipmentSlot, out Item equipped) ||
				!dragObject.MatchesSource(equipped) ||
				equipmentContainer.IsSlotLocked(equipmentSlot) ||
				!ItemOperationTracker.TryBegin(ReferenceButtonType.Equipment, equipmentSlot))
			{
				dragObject.Clear();
				return;
			}

			/* Queued on the controller and applied inside the owner's next replicate tick, on both
			 * peers at once — see IEquipmentController. The destination CONTAINER is part of the
			 * request; the slot within it is chosen by the container, and the server reports the
			 * slot it chose if the two copies of the container disagreed. */
			if (!Character.TryGet(out IEquipmentController equipmentController) ||
				!equipmentController.RequestUnequip((ItemSlot)equipmentSlot, OwnInventoryType))
			{
				ItemOperationTracker.Release(ReferenceButtonType.Equipment, equipmentSlot);
			}

			dragObject.Clear();
		}

		/* BeginDragFromSlot, OnSlotPointerEnter, OnSlotPointerLeave and ReleaseAndClearDrag are on
		 * UITKSlotPanelBase. Starting a drag, showing a tooltip and abandoning what is in flight
		 * are the same acts in a bag slot and in a socket — it is only the DROP that differs, a
		 * swap here and an equip there, which is why the completion half stayed behind. */
	}
}
