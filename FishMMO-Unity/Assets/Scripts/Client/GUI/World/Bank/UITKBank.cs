using System.Collections.Generic;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit implementation of the bank panel.
	/// Binds to <c>UIBank.uxml</c> / <c>UIBank.uss</c> and renders the character's bank as a
	/// grid of slot buttons. Item drag-and-drop and tooltips reuse the shared
	/// <see cref="UITKDragObject"/> and <see cref="UITKTooltip"/> overlays via <see cref="UIManager"/>.
	/// </summary>
	/// <remarks>
	/// The grid is a view of the replicated container and nothing else: no click here writes to a
	/// slot. A request goes out, the slot is marked as waiting, and the container being replicated
	/// back is what changes what the player sees.
	/// </remarks>
	public class UITKBank : UITKCharacterControl
	{
		// ── UXML element names ────────────────────────────────────────────────

		private const string SLOT_GRID_NAME = "slot-grid";
		/// <summary>Name of the header subtitle showing slot usage.</summary>
		private const string SUBTITLE_NAME = "header-subtitle";
		/// <summary>Name of the footer label counting occupied slots.</summary>
		private const string USED_NAME = "bank-used";
		/// <summary>Name of the footer label counting free slots.</summary>
		private const string FREE_NAME = "bank-free";
		/// <summary>Name of the footer capacity bar fill.</summary>
		private const string CAPACITY_FILL_NAME = "bank-capacity-fill";
		/// <summary>Name of the label shown when nothing is stored.</summary>
		private const string EMPTY_NAME = "bank-empty";
		private const string CLOSE_BTN_NAME = "close-button";

		// ── Shared UI overlay names (panels resolved by GameObject name via UIManager) ──

		private const string DRAG_OBJECT_NAME = "UIDragObject";
		private const string TOOLTIP_NAME = "UITooltip";

		// ── USS class names ───────────────────────────────────────────────────

		private const string CSS_SLOT = "fish-slot";
		private const string CSS_SLOT_GRID = "bank-slot";
		private const string CSS_SLOT_ICON = "fish-slot__icon";
		private const string CSS_SLOT_ICON_LAYOUT = "bank-slot__icon";
		private const string CSS_SLOT_AMOUNT = "fish-slot__amount";
		private const string CSS_SLOT_AMOUNT_LAYOUT = "bank-slot__amount";
		private const string CSS_SLOT_LOCK = "fish-slot__lock";
		private const string CSS_SLOT_LOCK_LAYOUT = "bank-slot__lock";
		/// <summary>USS class marking a slot as waiting on the server.</summary>
		private const string CSS_LOCK_PENDING = "bank-slot__lock--pending";
		private const string CSS_HIDDEN = "bank-hidden";

		// ── Per-slot view data ────────────────────────────────────────────────

		private struct SlotView
		{
			/// <summary>Root VisualElement of the slot.</summary>
			public VisualElement Root;
			/// <summary>Icon element displaying the item sprite.</summary>
			public VisualElement Icon;
			/// <summary>Stack-count label.</summary>
			public Label Amount;
			/// <summary>Lock overlay element.</summary>
			public VisualElement Lock;
		}

		// ── Private state ─────────────────────────────────────────────────────

		/// <summary>Slot views indexed by bank slot index.</summary>
		private readonly List<SlotView> slotViews = new List<SlotView>();
		/// <summary>Header line showing slot usage.</summary>
		private Label subtitleLabel;
		/// <summary>Footer label counting occupied slots.</summary>
		private Label usedLabel;
		/// <summary>Footer label counting free slots.</summary>
		private Label freeLabel;
		/// <summary>Footer capacity bar fill.</summary>
		private VisualElement capacityFill;
		/// <summary>Label shown in place of the grid when nothing is stored.</summary>
		private Label emptyLabel;

		/// <summary>Cached item sprite per slot.</summary>
		/// <remarks>
		/// No longer the source of the capacity readout — see <see cref="RefreshCapacity"/>. A
		/// sprite says whether an item has an icon, which is not the same question as whether a
		/// slot holds one.
		/// </remarks>
		private readonly List<Sprite> slotSprites = new List<Sprite>();

		/// <summary>The slot grid container element.</summary>
		private VisualElement slotGrid;

		/// <summary>True while this panel holds a subscription on the shared operation tracker.</summary>
		private bool trackerSubscribed;

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
			usedLabel = root.Q<Label>(USED_NAME);
			freeLabel = root.Q<Label>(FREE_NAME);
			capacityFill = root.Q(CAPACITY_FILL_NAME);
			emptyLabel = root.Q<Label>(EMPTY_NAME);

			Button closeBtn = root.Q<Button>(CLOSE_BTN_NAME);
			if (closeBtn != null)
			{
				closeBtn.clicked += Hide;
			}
		}

		/// <summary>
		/// Rebuilds the grid after the visual tree has been replaced.
		/// </summary>
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			ApplyPerOpenContent();
		}

		/// <summary>
		/// Fills the grid on every show, including the very first one.
		/// </summary>
		/// <remarks>
		/// THE CONTRACT: enabling the document re-clones the UXML, so anything written before
		/// <c>Show()</c> is discarded. <c>OnAfterStarting</c> covers later opens, but on the first
		/// ever open <c>hasStarted</c> is still false and <c>ReinitializeIfTreeReplaced</c> bails
		/// out before calling it — and this panel is opened by a banker broadcast, so its first
		/// open is triggered by something the player did rather than at startup. Both hooks do the
		/// work, and both are idempotent.
		/// </remarks>
		protected override void OnAfterShow()
		{
			ApplyPerOpenContent();
		}

		/// <summary>
		/// Registers the banker broadcast handler and joins the shared operation tracker.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<BankerBroadcast>(OnClientBankerBroadcastReceived);
			SubscribeTracker();
		}

		/// <summary>
		/// Unregisters the banker broadcast handler and leaves the shared operation tracker.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<BankerBroadcast>(OnClientBankerBroadcastReceived);
			UnsubscribeTracker();
		}

		/// <summary>
		/// Times out item operations whose reply never arrived.
		/// </summary>
		protected override void OnTick()
		{
			ItemOperationTracker.Tick();
		}

		/// <summary>
		/// Destroys all runtime slot elements and drops every subscription when the control is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			UnsubscribeTracker();
			ReleaseAndClearDrag();

			if (Character != null && Character.TryGet(out IBankController bankController))
			{
				bankController.OnSlotUpdated -= OnBankSlotUpdated;
				bankController.OnSlotLockChanged -= OnBankSlotLockChanged;
			}

			DestroySlots();
			base.OnDestroying();
		}

		/// <summary>
		/// Hides the panel and abandons anything it had in flight.
		/// </summary>
		/// <remarks>
		/// Closing the bank is not a neutral act: walking out of range of the banker is one of the
		/// ways the server refuses an operation, so a bank slot left marked as waiting after the
		/// panel closes has a very good chance of never being answered.
		/// </remarks>
		/// <param name="overrideIsAlwaysOpen">When true, the call is a no-op.</param>
		public override void Hide(bool overrideIsAlwaysOpen)
		{
			base.Hide(overrideIsAlwaysOpen);

			if (!Visible)
			{
				ReleaseAndClearDrag();
			}
		}

		// ── Broadcast handling ────────────────────────────────────────────────

		/// <summary>
		/// Shows the bank panel when a banker interaction succeeds, otherwise hides it.
		/// </summary>
		private void OnClientBankerBroadcastReceived(BankerBroadcast msg, Channel channel)
		{
			if (Character == null ||
				!Character.TryGet(out IBankController bankController))
			{
				Hide();
				return;
			}
			Show();
		}

		// ── Character control ─────────────────────────────────────────────────

		/// <summary>
		/// Unsubscribes from bank slot events before the character is replaced.
		/// </summary>
		public override void OnPreSetCharacter()
		{
			if (Character != null &&
				Character.TryGet(out IBankController bankController))
			{
				bankController.OnSlotUpdated -= OnBankSlotUpdated;
				bankController.OnSlotLockChanged -= OnBankSlotLockChanged;
			}
		}

		/// <summary>
		/// Builds the bank grid for the newly set character and subscribes to slot events.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			DestroySlots();

			if (Character == null ||
				!Character.TryGet(out IBankController bankController))
			{
				return;
			}

			/* Subscribed before the grid is considered, and no longer behind a slotGrid check.
			 * The character is set on world entry, which is usually before this panel has ever
			 * been opened — so its UXML has not been cloned and slotGrid is still null. Bailing
			 * out here used to skip the subscriptions as well as the build, and nothing else
			 * subscribes, so the panel spent the rest of the session deaf to slot updates: it
			 * drew whatever the container held at the moment it was opened and never changed
			 * again. Only the grid needs the tree. */
			bankController.OnSlotUpdated -= OnBankSlotUpdated;
			bankController.OnSlotLockChanged -= OnBankSlotLockChanged;
			bankController.OnSlotUpdated += OnBankSlotUpdated;
			bankController.OnSlotLockChanged += OnBankSlotLockChanged;

			if (slotGrid == null)
			{
				// Built on the first open instead — see ApplyPerOpenContent.
				return;
			}

			BuildSlots(bankController);
		}

		/// <summary>
		/// Drops every subscription and in-flight operation before the character goes away.
		/// </summary>
		public override void OnPreUnsetCharacter()
		{
			if (Character != null && Character.TryGet(out IBankController bankController))
			{
				bankController.OnSlotUpdated -= OnBankSlotUpdated;
				bankController.OnSlotLockChanged -= OnBankSlotLockChanged;
			}

			ReleaseAndClearDrag();
		}

		// ── Bank slot callbacks ───────────────────────────────────────────────

		/// <summary>
		/// Called when the lock state of a bank slot changes.
		/// </summary>
		public void OnBankSlotLockChanged(IItemContainer container, int slot, bool isLocked)
		{
			if (slot >= 0 && slot < slotViews.Count)
			{
				ApplySlotLockVisual(slot, IsSlotBlocked(slot));
			}
		}

		/// <summary>
		/// Called when a bank slot's item changes.
		/// </summary>
		/// <remarks>
		/// The slot arriving from the server IS the acknowledgement of whatever this panel asked
		/// for, so the pending mark is released here rather than on a separate reply message.
		/// </remarks>
		public void OnBankSlotUpdated(IItemContainer container, Item item, int bankIndex)
		{
			if (container == null || bankIndex < 0)
			{
				return;
			}

			/* An index the grid does not have means the grid is smaller than the container —
			 * built before the container was sized, or against a tree that has been replaced.
			 * Rebuilding is the recovery; dropping the update silently is what left a panel
			 * showing fewer slots than the character actually has. */
			if (bankIndex >= slotViews.Count)
			{
				if (Character == null ||
					!Character.TryGet(out IBankController rebuildIBankController) ||
					!EnsureSlots(rebuildIBankController) ||
					bankIndex >= slotViews.Count)
				{
					return;
				}
			}

			ItemOperationTracker.Release(ReferenceButtonType.Bank, bankIndex);

			bool empty = container.IsSlotEmpty(bankIndex);
			if (!empty)
			{
				SetSlotItem(bankIndex, item);
			}
			else
			{
				ClearSlot(bankIndex);
			}

			// A drag started from this slot no longer refers to what it was started from.
			if (UIManager.TryGetTK(DRAG_OBJECT_NAME, out UITKDragObject dragObject))
			{
				dragObject.NotifySlotChanged(ReferenceButtonType.Bank, bankIndex, empty ? null : item);
			}
		}

		// ── Shared operation tracker ──────────────────────────────────────────

		/// <summary>
		/// Joins the shared item-operation tracker, once.
		/// </summary>
		private void SubscribeTracker()
		{
			if (trackerSubscribed)
			{
				return;
			}
			trackerSubscribed = true;

			/* -= before += on a static event: OnClientSet runs again after a quit to login, and a
			 * static event outlives this component. */
			ItemOperationTracker.SlotPendingChanged -= OnTrackerSlotPendingChanged;
			ItemOperationTracker.SlotPendingChanged += OnTrackerSlotPendingChanged;
			ItemOperationTracker.ResyncRequested -= OnTrackerResyncRequested;
			ItemOperationTracker.ResyncRequested += OnTrackerResyncRequested;
			ItemOperationTracker.Attach();
		}

		/// <summary>
		/// Leaves the shared item-operation tracker, once.
		/// </summary>
		private void UnsubscribeTracker()
		{
			if (!trackerSubscribed)
			{
				return;
			}
			trackerSubscribed = false;

			ItemOperationTracker.SlotPendingChanged -= OnTrackerSlotPendingChanged;
			ItemOperationTracker.ResyncRequested -= OnTrackerResyncRequested;
			ItemOperationTracker.Detach();
		}

		/// <summary>
		/// Repaints a slot when it starts or stops waiting on the server.
		/// </summary>
		private void OnTrackerSlotPendingChanged(ReferenceButtonType type, int slot, bool pending)
		{
			if (type != ReferenceButtonType.Bank || slot < 0 || slot >= slotViews.Count)
			{
				return;
			}

			ApplySlotLockVisual(slot, IsSlotBlocked(slot));
		}

		/// <summary>
		/// Re-renders every slot from the replicated container.
		/// </summary>
		private void OnTrackerResyncRequested(ReferenceButtonType type)
		{
			if (type != ReferenceButtonType.Bank)
			{
				return;
			}

			if (Character != null && Character.TryGet(out IBankController bankController))
			{
				RefreshAllSlots(bankController);
			}
		}

		// ── Slot element construction ─────────────────────────────────────────

		/// <summary>
		/// Re-reads the whole grid from the character, rebuilding it only if it is stale.
		/// </summary>
		private void ApplyPerOpenContent()
		{
			if (Character == null ||
				!Character.TryGet(out IBankController bankController))
			{
				return;
			}

			if (EnsureSlots(bankController))
			{
				// Rebuilt, which repaints every slot on the way out.
				return;
			}

			RefreshAllSlots(bankController);
		}

		/// <summary>
		/// Guarantees the grid holds one element per container slot, rebuilding it when it does
		/// not. Returns true if it rebuilt, in which case every slot has already been repainted.
		/// </summary>
		/// <remarks>
		/// The grid is sized from the container's slot COUNT, never from how many of those slots
		/// hold something. Bank capacity is 100 slots whether it is full or empty, and all 100
		/// frames are drawn either way — an empty slot is the thing the player drops an item onto
		/// and the thing that shows them the space they have, so a grid that renders only
		/// occupied slots has nothing to aim at and no way to convey that it is empty rather than
		/// broken.
		/// <para>
		/// Two ways the grid goes stale. The container changed size — including the case that
		/// matters most here, a grid built at zero because the panel was opened before the
		/// character had one, which nothing else would ever correct. And the elements belong to a
		/// tree that has since been replaced: <c>UIDocument</c> re-clones the UXML on every
		/// enable, and a slot whose parent is not the current grid is drawn nowhere at all while
		/// still looking perfectly valid from C#.
		/// </para>
		/// </remarks>
		private bool EnsureSlots(IBankController bankController)
		{
			if (slotGrid == null || bankController == null)
			{
				return false;
			}

			bool stale = slotViews.Count != bankController.Items.Count ||
						 (slotViews.Count > 0 && slotViews[0].Root != null && slotViews[0].Root.parent != slotGrid);

			if (!stale)
			{
				return false;
			}

			DestroySlots();
			BuildSlots(bankController);
			return true;
		}

		/// <summary>
		/// Creates one element per container slot and fills it from the container.
		/// </summary>
		private void BuildSlots(IBankController bankController)
		{
			int slotCount = bankController.Items.Count;
			for (int i = 0; i < slotCount; ++i)
			{
				SlotView view = CreateSlot(i);
				slotViews.Add(view);
				slotSprites.Add(null);
			}

			RefreshAllSlots(bankController);
		}

		/// <summary>
		/// Repaints every slot's item and lock state from the container.
		/// </summary>
		private void RefreshAllSlots(IBankController bankController)
		{
			int slotCount = Mathf.Min(slotViews.Count, bankController.Items.Count);
			for (int i = 0; i < slotCount; ++i)
			{
				if (bankController.TryGetItem(i, out Item item))
				{
					SetSlotItem(i, item);
				}
				else
				{
					ClearSlot(i);
				}
				ApplySlotLockVisual(i, IsSlotBlocked(i));
			}
			RefreshCapacity();
		}

		/// <summary>
		/// Creates a single bank slot element, registers its interaction callbacks,
		/// and appends it to the slot grid.
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
			slotRoot.AddToClassList(CSS_SLOT_GRID);

			VisualElement icon = new VisualElement();
			icon.AddToClassList(CSS_SLOT_ICON);
			icon.AddToClassList(CSS_SLOT_ICON_LAYOUT);
			slotRoot.Add(icon);

			Label amount = new Label();
			amount.AddToClassList(CSS_SLOT_AMOUNT);
			amount.AddToClassList(CSS_SLOT_AMOUNT_LAYOUT);
			amount.AddToClassList(CSS_HIDDEN);
			slotRoot.Add(amount);

			VisualElement lockOverlay = new VisualElement();
			lockOverlay.AddToClassList(CSS_SLOT_LOCK);
			lockOverlay.AddToClassList(CSS_SLOT_LOCK_LAYOUT);
			lockOverlay.AddToClassList(CSS_HIDDEN);
			slotRoot.Add(lockOverlay);

			int captured = slotIndex;
			slotRoot.RegisterCallback<PointerDownEvent>(evt => OnSlotPointerDown(evt, captured));
			slotRoot.RegisterCallback<PointerEnterEvent>(evt => OnSlotPointerEnter(captured, slotRoot));
			slotRoot.RegisterCallback<PointerLeaveEvent>(evt => OnSlotPointerLeave(slotRoot));

			slotGrid.Add(slotRoot);

			SlotView view;
			view.Root = slotRoot;
			view.Icon = icon;
			view.Amount = amount;
			view.Lock = lockOverlay;
			return view;
		}

		/// <summary>
		/// Removes all runtime slot elements and clears cached state.
		/// </summary>
		/// <remarks>
		/// <c>RemoveFromHierarchy</c>, not <c>slotGrid.Remove</c>.
		/// <c>VisualElement.Remove</c> THROWS when the element is not its child, and after the
		/// document re-clones the UXML these roots belong to the previous tree while
		/// <c>slotGrid</c> is the new one — so the old code threw on the first slot, abandoning
		/// the rebuild and leaving the vault permanently empty on screen.
		/// </remarks>
		private void DestroySlots()
		{
			for (int i = 0; i < slotViews.Count; ++i)
			{
				slotViews[i].Root?.RemoveFromHierarchy();
			}
			slotViews.Clear();
			slotSprites.Clear();
		}

		// ── Slot visuals ──────────────────────────────────────────────────────

		/// <summary>
		/// Populates a slot's icon and stack-count badge from an item.
		/// </summary>
		private void SetSlotItem(int slotIndex, Item item)
		{
			if (item == null || slotIndex < 0 || slotIndex >= slotViews.Count)
			{
				return;
			}

			SlotView view = slotViews[slotIndex];
			if (view.Root == null)
			{
				return;
			}

			Sprite sprite = item.Template != null ? item.Template.Icon : null;
			slotSprites[slotIndex] = sprite;
			RefreshCapacity();

			// Placeholder when the template has no icon: an occupied slot must look occupied.
			UITKItemIcon.Apply(view.Icon, sprite);

			if (view.Amount != null)
			{
				if (item.IsStackable && item.Stackable != null)
				{
					view.Amount.text = item.Stackable.Amount.ToString();
					view.Amount.RemoveFromClassList(CSS_HIDDEN);
				}
				else
				{
					view.Amount.text = "";
					view.Amount.AddToClassList(CSS_HIDDEN);
				}
			}
		}

		/// <summary>
		/// Recomputes the header subtitle, footer counts and capacity bar.
		/// </summary>
		/// <remarks>
		/// Occupancy comes from the controller, which is the only thing that knows it. It used to
		/// be counted from <c>slotSprites</c> on the reasoning that the sprite array is the view's
		/// own record of which slots are filled — but a sprite records whether an item has an
		/// ICON, not whether a slot holds an item. Every item whose template has no icon assigned,
		/// or whose icon has not finished loading, left a null in that array and was counted as an
		/// empty slot, so the totals read low and drifted as icons resolved.
		///
		/// The inventory panel had the same defect and was fixed the same way; the bank was missed.
		/// </remarks>
		private void RefreshCapacity()
		{
			int total = slotViews.Count;
			int used = 0;
			if (Character != null && Character.TryGet(out IBankController bankController))
			{
				for (int i = 0; i < total; ++i)
				{
					if (!bankController.IsSlotEmpty(i))
					{
						++used;
					}
				}
			}
			int free = total - used;

			if (subtitleLabel != null)
			{
				subtitleLabel.text = $"{used} / {total} slots";
			}
			if (usedLabel != null)
			{
				usedLabel.text = $"{used} used";
			}
			if (freeLabel != null)
			{
				freeLabel.text = $"{free} free";
			}
			if (capacityFill != null)
			{
				float fraction = total > 0 ? (float)used / total : 0.0f;
				capacityFill.style.width = new StyleLength(Length.Percent(fraction * 100.0f));
				// Near-full is worth flagging before the player finds out by failing to loot.
				capacityFill.EnableInClassList("fish-bar__fill--hp", total > 0 && free <= 2);
			}
			if (emptyLabel != null)
			{
				emptyLabel.style.display = used == 0 && total > 0 ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}

		/// <summary>
		/// Clears a slot's icon and hides its stack-count badge.
		/// </summary>
		private void ClearSlot(int slotIndex)
		{
			if (slotIndex < 0 || slotIndex >= slotViews.Count)
			{
				return;
			}

			SlotView view = slotViews[slotIndex];
			if (view.Root == null)
			{
				return;
			}

			slotSprites[slotIndex] = null;
			RefreshCapacity();

			UITKItemIcon.Clear(view.Icon);
			if (view.Amount != null)
			{
				view.Amount.text = "";
				view.Amount.AddToClassList(CSS_HIDDEN);
			}
		}

		/// <summary>
		/// Shows or hides the lock overlay on a slot.
		/// </summary>
		private void ApplySlotLockVisual(int slotIndex, bool isLocked)
		{
			if (slotIndex < 0 || slotIndex >= slotViews.Count)
			{
				return;
			}

			VisualElement lockEl = slotViews[slotIndex].Lock;
			if (lockEl == null)
			{
				return;
			}

			lockEl.EnableInClassList(CSS_HIDDEN, !isLocked);
			lockEl.EnableInClassList(CSS_LOCK_PENDING,
				isLocked && ItemOperationTracker.IsPending(ReferenceButtonType.Bank, slotIndex));
		}

		/// <summary>
		/// Reports whether a slot is unavailable for a new request, for any reason.
		/// </summary>
		private bool IsSlotBlocked(int slotIndex)
		{
			if (ItemOperationTracker.IsPending(ReferenceButtonType.Bank, slotIndex))
			{
				return true;
			}

			return Character != null &&
				   Character.TryGet(out IBankController bankController) &&
				   bankController.IsSlotLocked(slotIndex);
		}

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
				HandleSlotLeftClick(slotIndex);
			}
		}

		/// <summary>
		/// Left-click: completes an in-progress drag (swap or unequip to bank) or begins
		/// dragging the item currently in this slot.
		/// </summary>
		private void HandleSlotLeftClick(int slotIndex)
		{
			if (!UIManager.TryGetTK(DRAG_OBJECT_NAME, out UITKDragObject dragObject))
			{
				return;
			}

			if (!Character.TryGet(out IBankController bankController))
			{
				return;
			}

			if (dragObject.IsDragging)
			{
				CompleteDropOntoSlot(dragObject, bankController, slotIndex);
				return;
			}

			BeginDragFromSlot(dragObject, bankController, slotIndex);
		}

		/// <summary>
		/// Drops whatever the drag is carrying onto <paramref name="slotIndex"/>.
		/// </summary>
		/// <remarks>
		/// This replaced a call to <c>IBankController.CanSwapItemSlots</c>, whose entire body is
		/// <c>return !(fromInventory == InventoryType.Inventory &amp;&amp; from == to)</c>. It does
		/// not look at the containers at all, so it approved deposits out of empty slots, out of
		/// locked slots, and into indices past the end of the vault; and because the one case it
		/// does check names <c>InventoryType.Inventory</c>, it never even caught a bank slot
		/// dropped on itself. The server rejects all of it, so nothing was corrupted — what it
		/// cost was a round trip and, before <c>ItemOperationFailedBroadcast</c>, silence.
		/// </remarks>
		private void CompleteDropOntoSlot(UITKDragObject dragObject, IBankController bankController, int slotIndex)
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
				// An ability or hotkey drag has no business landing in a vault.
				dragObject.Clear();
				return;
			}

			InventoryType sourceInventory = dragObject.Type == ReferenceButtonType.Bank
				? InventoryType.Bank
				: InventoryType.Inventory;

			IItemContainer sourceContainer = ResolveContainer(dragObject.Type);

			if (sourceContainer == null ||
				!sourceContainer.CanManipulate() ||
				!bankController.CanManipulate() ||
				!sourceContainer.IsValidSlot(sourceSlot) ||
				!bankController.IsValidSlot(slotIndex) ||
				(sourceInventory == InventoryType.Bank && sourceSlot == slotIndex) ||
				!sourceContainer.TryGetItem(sourceSlot, out Item sourceItem) ||
				!dragObject.MatchesSource(sourceItem) ||
				sourceContainer.IsSlotLocked(sourceSlot) ||
				bankController.IsSlotLocked(slotIndex))
			{
				dragObject.Clear();
				return;
			}

			// Claim both ends, or neither: a slot marked as waiting for an unsent request never unlocks.
			if (!ItemOperationTracker.TryBegin(dragObject.Type, sourceSlot))
			{
				dragObject.Clear();
				return;
			}
			if (!ItemOperationTracker.TryBegin(ReferenceButtonType.Bank, slotIndex))
			{
				ItemOperationTracker.Release(dragObject.Type, sourceSlot);
				dragObject.Clear();
				return;
			}

			Client.Broadcast(new BankSwapItemSlotsBroadcast()
			{
				From = sourceSlot,
				To = slotIndex,
				FromInventory = sourceInventory,
			}, Channel.Reliable);

			dragObject.Clear();
		}

		/// <summary>
		/// Sends an unequip whose destination is this container.
		/// </summary>
		private void CompleteUnequipInto(UITKDragObject dragObject, int equipmentSlot)
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

			Client.Broadcast(new EquipmentUnequipItemBroadcast()
			{
				Slot = (byte)equipmentSlot,
				ToInventory = InventoryType.Bank,
			}, Channel.Reliable);

			dragObject.Clear();
		}

		/// <summary>
		/// Starts a drag from an occupied bank slot.
		/// </summary>
		private void BeginDragFromSlot(UITKDragObject dragObject, IBankController bankController, int slotIndex)
		{
			if (IsSlotBlocked(slotIndex) ||
				!bankController.TryGetItem(slotIndex, out Item item) ||
				item == null)
			{
				return;
			}

			// Same as the inventory: a missing icon must not prevent the item being moved.
			Sprite sprite = item.Template != null ? item.Template.Icon : null;

			/* Carry the item, not just the slot number: the slot index stops being true the moment
			 * anything else writes to that slot, and the drop would then move the wrong item. */
			dragObject.SetItemReference(sprite, slotIndex, ReferenceButtonType.Bank, item);
		}

		/// <summary>
		/// Shows the item tooltip when the pointer enters a slot that contains an item.
		/// </summary>
		private void OnSlotPointerEnter(int slotIndex, VisualElement owner)
		{
			if (Character == null ||
				!Character.TryGet(out IBankController bankController) ||
				!bankController.TryGetItem(slotIndex, out Item item))
			{
				return;
			}

			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				// With an owner, so the tooltip closes itself if this slot is rebuilt under it.
				tooltip.Open(item.Tooltip(), owner);
			}
		}

		/// <summary>
		/// Hides the item tooltip when the pointer leaves a slot.
		/// </summary>
		private void OnSlotPointerLeave(VisualElement owner)
		{
			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				// HideFor, so a stale leave cannot close a tooltip another slot has since opened.
				tooltip.HideFor(owner);
			}
		}

		/// <summary>
		/// Resolves the character's container for a drag source type.
		/// </summary>
		private IItemContainer ResolveContainer(ReferenceButtonType type)
		{
			if (Character == null)
			{
				return null;
			}

			switch (type)
			{
				case ReferenceButtonType.Inventory:
					return Character.TryGet(out IInventoryController inventoryController) ? inventoryController : null;
				case ReferenceButtonType.Bank:
					return Character.TryGet(out IBankController bankController) ? bankController : null;
				case ReferenceButtonType.Equipment:
					return Character.TryGet(out IEquipmentController equipmentController) ? equipmentController : null;
				default:
					return null;
			}
		}

		/// <summary>
		/// Abandons this panel's in-flight operations and any drag that started here.
		/// </summary>
		private void ReleaseAndClearDrag()
		{
			ItemOperationTracker.ReleaseAll(ReferenceButtonType.Bank);

			if (UIManager.TryGetTK(DRAG_OBJECT_NAME, out UITKDragObject dragObject) &&
				dragObject.IsDragging &&
				dragObject.Type == ReferenceButtonType.Bank)
			{
				dragObject.Clear();
			}
		}
	}
}
