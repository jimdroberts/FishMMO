using System.Collections.Generic;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit implementation of the inventory panel.
	/// Binds to <c>UIInventory.uxml</c> / <c>UIInventory.uss</c> and renders the character's
	/// inventory as a grid of slot buttons. Item drag-and-drop and tooltips reuse the shared
	/// <see cref="UITKDragObject"/> and <see cref="UITKTooltip"/> overlays via <see cref="UIManager"/>.
	/// </summary>
	/// <remarks>
	/// The grid is a view of the replicated container and nothing else: no click here writes to a
	/// slot. A request goes out, the slot is marked as waiting, and the container being replicated
	/// back is what changes what the player sees. Anything else is a guess that the server is free
	/// to contradict — and until <see cref="ItemOperationFailedBroadcast"/> existed, would
	/// contradict silently.
	/// </remarks>
	public class UITKInventory : UITKCharacterControl
	{
		// ── UXML element names ────────────────────────────────────────────────

		/// <summary>Name of the slot grid element in the UXML.</summary>
		private const string SLOT_GRID_NAME = "slot-grid";
		/// <summary>Name of the header subtitle showing slot usage.</summary>
		private const string SUBTITLE_NAME = "header-subtitle";
		/// <summary>Name of the footer label counting occupied slots.</summary>
		private const string USED_NAME = "inv-used";
		/// <summary>Name of the footer label counting free slots.</summary>
		private const string FREE_NAME = "inv-free";
		/// <summary>Name of the footer capacity bar fill.</summary>
		private const string CAPACITY_FILL_NAME = "inv-capacity-fill";
		/// <summary>Name of the label shown when nothing is stored.</summary>
		private const string EMPTY_NAME = "inv-empty";
		/// <summary>Name of the close button element in the UXML.</summary>
		private const string CLOSE_BTN_NAME = "close-button";

		// ── Shared UI overlay names (panels resolved by GameObject name via UIManager) ──

		/// <summary>Name of the shared drag object overlay.</summary>
		private const string DRAG_OBJECT_NAME = "UIDragObject";
		/// <summary>Name of the shared tooltip overlay.</summary>
		private const string TOOLTIP_NAME = "UITooltip";

		// ── USS class names ───────────────────────────────────────────────────

		/// <summary>USS class applied to any slot container.</summary>
		private const string CSS_SLOT = "fish-slot";
		/// <summary>USS class applied to inventory slot containers.</summary>
		private const string CSS_SLOT_GRID = "inv-slot";
		/// <summary>USS class applied to a slot's icon element.</summary>
		private const string CSS_SLOT_ICON = "fish-slot__icon";
		/// <summary>USS layout class applied to inventory slot icons.</summary>
		private const string CSS_SLOT_ICON_LAYOUT = "inv-slot__icon";
		/// <summary>USS class applied to a slot's stack-count label.</summary>
		private const string CSS_SLOT_AMOUNT = "fish-slot__amount";
		/// <summary>USS layout class applied to inventory slot stack-count labels.</summary>
		private const string CSS_SLOT_AMOUNT_LAYOUT = "inv-slot__amount";
		/// <summary>USS class applied to a slot's lock overlay.</summary>
		private const string CSS_SLOT_LOCK = "fish-slot__lock";
		/// <summary>USS layout class applied to inventory slot lock overlays.</summary>
		private const string CSS_SLOT_LOCK_LAYOUT = "inv-slot__lock";
		/// <summary>USS class marking a slot as waiting on the server.</summary>
		private const string CSS_LOCK_PENDING = "inv-slot__lock--pending";
		/// <summary>USS class for hiding inventory elements.</summary>
		private const string CSS_HIDDEN = "inv-hidden";

		// ── Per-slot view data ────────────────────────────────────────────────

		/// <summary>Runtime view data for a single inventory slot element.</summary>
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

		/// <summary>Slot views indexed by inventory slot index.</summary>
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

		/// <summary>Cached item sprite per slot, used to drive the capacity readout.</summary>
		private readonly List<Sprite> slotSprites = new List<Sprite>();

		/// <summary>The container element that holds the inventory slot elements.</summary>
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
		/// <remarks>
		/// The base implementation re-runs the character pre/post pair, which is what recreates
		/// the slot elements against the new tree. <see cref="ApplyPerOpenContent"/> then repaints
		/// the pending marks, which live in the shared tracker rather than in the tree.
		/// </remarks>
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
		/// out before calling it — so the work has to happen from both hooks. It is idempotent:
		/// the grid is rebuilt only when it is actually stale, otherwise the slots are repainted
		/// in place.
		/// </remarks>
		protected override void OnAfterShow()
		{
			ApplyPerOpenContent();
		}

		/// <summary>
		/// Joins the shared item-operation tracker.
		/// </summary>
		public override void OnClientSet()
		{
			SubscribeTracker();
		}

		/// <summary>
		/// Leaves the shared item-operation tracker.
		/// </summary>
		public override void OnClientUnset()
		{
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

			if (Character != null && Character.TryGet(out IInventoryController inventoryController))
			{
				inventoryController.OnSlotUpdated -= OnInventorySlotUpdated;
				inventoryController.OnSlotLockChanged -= OnInventorySlotLockChanged;
			}

			DestroySlots();
			base.OnDestroying();
		}

		/// <summary>
		/// Hides the panel and abandons anything it had in flight.
		/// </summary>
		/// <remarks>
		/// <c>Hide(bool)</c> and not <c>Hide()</c>: Escape arrives via <c>UIManager.CloseNext</c>
		/// and quit-to-login via <c>Hide(false)</c>, and a drag or a pending mark that outlives the
		/// panel is one the player can neither see nor cancel.
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

		// ── Character control ─────────────────────────────────────────────────

		/// <summary>
		/// Unsubscribes from inventory slot events before the character is replaced.
		/// </summary>
		public override void OnPreSetCharacter()
		{
			if (Character != null &&
				Character.TryGet(out IInventoryController inventoryController))
			{
				inventoryController.OnSlotUpdated -= OnInventorySlotUpdated;
				inventoryController.OnSlotLockChanged -= OnInventorySlotLockChanged;
			}
		}

		/// <summary>
		/// Builds the inventory grid for the newly set character and subscribes to slot events.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			DestroySlots();

			if (Character == null ||
				slotGrid == null ||
				!Character.TryGet(out IInventoryController inventoryController))
			{
				return;
			}

			inventoryController.OnSlotUpdated -= OnInventorySlotUpdated;
			inventoryController.OnSlotLockChanged -= OnInventorySlotLockChanged;

			BuildSlots(inventoryController);

			inventoryController.OnSlotUpdated += OnInventorySlotUpdated;
			inventoryController.OnSlotLockChanged += OnInventorySlotLockChanged;
		}

		/// <summary>
		/// Drops every subscription and in-flight operation before the character goes away.
		/// </summary>
		/// <remarks>
		/// Quit-to-login and a character switch both come through here. Nothing else unsubscribes
		/// on this path, so without it the panel stays wired to a character that is being
		/// destroyed and keeps slots marked as waiting on a server it is no longer talking to.
		/// </remarks>
		public override void OnPreUnsetCharacter()
		{
			if (Character != null && Character.TryGet(out IInventoryController inventoryController))
			{
				inventoryController.OnSlotUpdated -= OnInventorySlotUpdated;
				inventoryController.OnSlotLockChanged -= OnInventorySlotLockChanged;
			}

			ReleaseAndClearDrag();
		}

		// ── Inventory slot callbacks ──────────────────────────────────────────

		/// <summary>
		/// Called when the lock state of an inventory slot changes.
		/// </summary>
		public void OnInventorySlotLockChanged(IItemContainer container, int slot, bool isLocked)
		{
			if (slot >= 0 && slot < slotViews.Count)
			{
				ApplySlotLockVisual(slot, IsSlotBlocked(slot));
			}
		}

		/// <summary>
		/// Called when an inventory slot's item changes.
		/// </summary>
		/// <remarks>
		/// The slot arriving from the server IS the acknowledgement of whatever this panel asked
		/// for, so the pending mark is released here rather than on a separate reply message.
		/// </remarks>
		public void OnInventorySlotUpdated(IItemContainer container, Item item, int inventoryIndex)
		{
			if (container == null || inventoryIndex < 0 || inventoryIndex >= slotViews.Count)
			{
				return;
			}

			ItemOperationTracker.Release(ReferenceButtonType.Inventory, inventoryIndex);

			bool empty = container.IsSlotEmpty(inventoryIndex);
			if (!empty)
			{
				SetSlotItem(inventoryIndex, item);
			}
			else
			{
				ClearSlot(inventoryIndex);
			}

			// A drag started from this slot no longer refers to what it was started from.
			if (UIManager.TryGetTK(DRAG_OBJECT_NAME, out UITKDragObject dragObject))
			{
				dragObject.NotifySlotChanged(ReferenceButtonType.Inventory, inventoryIndex, empty ? null : item);
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

			/* -= before += on a static event. OnClientSet runs again after a quit to login, and a
			 * static event outlives this component: a missed unsubscribe is a handler that keeps
			 * running against a destroyed panel for the rest of the process. */
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
			if (type != ReferenceButtonType.Inventory || slot < 0 || slot >= slotViews.Count)
			{
				return;
			}

			ApplySlotLockVisual(slot, IsSlotBlocked(slot));
		}

		/// <summary>
		/// Re-renders every slot from the replicated container.
		/// </summary>
		/// <remarks>
		/// Raised when the server reported the outcome of an operation as unknown rather than
		/// failed. Nothing is reverted — the container is simply read again.
		/// </remarks>
		private void OnTrackerResyncRequested(ReferenceButtonType type)
		{
			if (type != ReferenceButtonType.Inventory)
			{
				return;
			}

			if (Character != null && Character.TryGet(out IInventoryController inventoryController))
			{
				RefreshAllSlots(inventoryController);
			}
		}

		// ── Slot element construction ─────────────────────────────────────────

		/// <summary>
		/// Re-reads the whole grid from the character, rebuilding it only if it is stale.
		/// </summary>
		private void ApplyPerOpenContent()
		{
			if (slotGrid == null ||
				Character == null ||
				!Character.TryGet(out IInventoryController inventoryController))
			{
				return;
			}

			/* Two ways the grid can be stale: the container changed size, or the elements belong
			 * to a tree that has been replaced since. The second is the one that matters — a slot
			 * whose parent is not the current grid is drawn nowhere and clicking where it used to
			 * be does nothing. */
			bool stale = slotViews.Count != inventoryController.Items.Count ||
						 (slotViews.Count > 0 && slotViews[0].Root != null && slotViews[0].Root.parent != slotGrid);

			if (stale)
			{
				DestroySlots();
				BuildSlots(inventoryController);
				return;
			}

			RefreshAllSlots(inventoryController);
		}

		/// <summary>
		/// Creates one element per container slot and fills it from the container.
		/// </summary>
		private void BuildSlots(IInventoryController inventoryController)
		{
			int slotCount = inventoryController.Items.Count;
			for (int i = 0; i < slotCount; ++i)
			{
				SlotView view = CreateSlot(i);
				slotViews.Add(view);
				slotSprites.Add(null);
			}

			RefreshAllSlots(inventoryController);
		}

		/// <summary>
		/// Repaints every slot's item and lock state from the container.
		/// </summary>
		private void RefreshAllSlots(IInventoryController inventoryController)
		{
			int slotCount = Mathf.Min(slotViews.Count, inventoryController.Items.Count);
			for (int i = 0; i < slotCount; ++i)
			{
				if (inventoryController.TryGetItem(i, out Item item))
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
		/// Creates a single inventory slot element, registers its interaction callbacks,
		/// and appends it to the slot grid.
		/// </summary>
		/// <remarks>
		/// The callbacks are registered on an element created here and thrown away by
		/// <see cref="DestroySlots"/>, so there is nothing to unregister and no accumulation
		/// across rebuilds — the handler dies with the element it was attached to. That only holds
		/// because every rebuild goes through <c>DestroySlots</c> first; adding a path that
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
			slotRoot.RegisterCallback<PointerUpEvent>(evt => OnSlotPointerUp(evt, captured));
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
		/// <c>VisualElement.Remove</c> THROWS when the element is not its child, and that is
		/// exactly the situation after the document re-clones the UXML: these roots belong to the
		/// previous tree while <c>slotGrid</c> is the new one. The old code threw on the first
		/// slot, abandoning the rebuild half-done and leaving the panel with an empty grid for the
		/// rest of the session — the same shape of failure as the roster panels.
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

			if (view.Icon != null)
			{
				view.Icon.style.backgroundImage = sprite != null
					? new StyleBackground(sprite)
					: StyleKeyword.None;
			}

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
		/// </remarks>
		private void RefreshCapacity()
		{
			int total = slotSprites.Count;
			int used = 0;

			if (Character != null && Character.TryGet(out IInventoryController inventoryController))
			{
				for (int i = 0; i < total; ++i)
				{
					if (!inventoryController.IsSlotEmpty(i))
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

			if (view.Icon != null)
			{
				view.Icon.style.backgroundImage = StyleKeyword.None;
			}
			if (view.Amount != null)
			{
				view.Amount.text = "";
				view.Amount.AddToClassList(CSS_HIDDEN);
			}
		}

		/// <summary>
		/// Shows or hides the lock overlay on a slot.
		/// </summary>
		/// <remarks>
		/// One overlay carries two meanings — the container's own lock and a request this panel is
		/// waiting on — because to the player they say the same thing: this slot is busy. The
		/// <c>--pending</c> modifier is what tells a wait apart from a refusal.
		/// </remarks>
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
				isLocked && ItemOperationTracker.IsPending(ReferenceButtonType.Inventory, slotIndex));
		}

		/// <summary>
		/// Reports whether a slot is unavailable for a new request, for any reason.
		/// </summary>
		private bool IsSlotBlocked(int slotIndex)
		{
			if (ItemOperationTracker.IsPending(ReferenceButtonType.Inventory, slotIndex))
			{
				return true;
			}

			return Character != null &&
				   Character.TryGet(out IInventoryController inventoryController) &&
				   inventoryController.IsSlotLocked(slotIndex);
		}

		// ── Slot interaction ──────────────────────────────────────────────────

		/// <summary>
		/// Routes pointer-down events on a slot to left- or right-click handlers.
		/// </summary>
		/// <summary>
		/// Completes a press-and-drag when the pointer is released over a different slot.
		/// </summary>
		/// <remarks>
		/// The panel's drag has always been click-to-pick-up then click-to-drop, which works but
		/// is not what a player tries first — pressing on an item and dragging it did nothing at
		/// all, because nothing was listening for the release. PointerDown already starts the
		/// drag, so a release is the only missing half.
		/// <para>
		/// Releasing over the slot the drag started from deliberately does NOT drop: that is an
		/// ordinary click, and completing there would make click-to-pick-up impossible.
		/// </para>
		/// </remarks>
		private void OnSlotPointerUp(PointerUpEvent evt, int slotIndex)
		{
			if (Character == null || Client == null || evt.button != 0)
			{
				return;
			}

			bool draggingNow = UIManager.TryGetTK(DRAG_OBJECT_NAME, out UITKDragObject dragObject) && dragObject.IsDragging;

			/* Which element receives the release is the whole question. Press-and-drag does
			 * nothing today, and the two explanations — the release never reaching the slot under
			 * the cursor, or reaching it and being refused — are indistinguishable without
			 * seeing it. */
			FishMMO.Logging.Log.Debug("UITKInventory",
				$"PointerUp on slot {slotIndex}: dragging={draggingNow} " +
				$"dragType={(dragObject == null ? "none" : dragObject.Type.ToString())} " +
				$"dragSource={(dragObject == null ? -1 : (int)dragObject.ReferenceID)}.");

			if (!draggingNow)
			{
				return;
			}

			// Same slot the drag came from: this is a click, not a drag. Leave it armed.
			if (dragObject.Type == ReferenceButtonType.Inventory &&
				(int)dragObject.ReferenceID == slotIndex)
			{
				return;
			}

			if (Character.TryGet(out IInventoryController inventoryController))
			{
				CompleteDropOntoSlot(dragObject, inventoryController, slotIndex);
			}
		}

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
			else if (evt.button == 1)
			{
				HandleSlotRightClick(slotIndex);
			}
		}

		/// <summary>
		/// Left-click: completes an in-progress drag (swap or unequip) or begins dragging
		/// the item currently in this slot.
		/// </summary>
		private void HandleSlotLeftClick(int slotIndex)
		{
			if (!UIManager.TryGetTK(DRAG_OBJECT_NAME, out UITKDragObject dragObject))
			{
				return;
			}

			if (!Character.TryGet(out IInventoryController inventoryController))
			{
				return;
			}

			if (dragObject.IsDragging)
			{
				CompleteDropOntoSlot(dragObject, inventoryController, slotIndex);
				return;
			}

			BeginDragFromSlot(dragObject, inventoryController, slotIndex);
		}

		/// <summary>
		/// Drops whatever the drag is carrying onto <paramref name="slotIndex"/>.
		/// </summary>
		/// <remarks>
		/// This replaced a call to <c>IInventoryController.CanSwapItemSlots</c>, whose entire body
		/// is <c>return !(fromInventory == InventoryType.Inventory &amp;&amp; from == to)</c> — it
		/// approves a move out of an empty slot, out of a locked one, from a container the
		/// character does not have, and to an index past the end of the grid. The server rejects
		/// all of those, so nothing was ever corrupted by it; what it cost was a round trip and,
		/// before <c>ItemOperationFailedBroadcast</c>, complete silence afterwards. The checks
		/// below are the pre-flight it was supposed to be. They are still only a pre-flight: the
		/// server re-validates everything and remains the only authority.
		/// </remarks>
		private void CompleteDropOntoSlot(UITKDragObject dragObject, IInventoryController inventoryController, int slotIndex)
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
				// An ability or hotkey drag has no business landing in a bag.
				dragObject.Clear();
				return;
			}

			InventoryType sourceInventory = dragObject.Type == ReferenceButtonType.Bank
				? InventoryType.Bank
				: InventoryType.Inventory;

			IItemContainer sourceContainer = ResolveContainer(dragObject.Type);

			if (sourceContainer == null ||
				!sourceContainer.CanManipulate() ||
				!inventoryController.CanManipulate() ||
				!sourceContainer.IsValidSlot(sourceSlot) ||
				!inventoryController.IsValidSlot(slotIndex) ||
				(sourceInventory == InventoryType.Inventory && sourceSlot == slotIndex) ||
				!sourceContainer.TryGetItem(sourceSlot, out Item sourceItem) ||
				!dragObject.MatchesSource(sourceItem) ||
				sourceContainer.IsSlotLocked(sourceSlot) ||
				inventoryController.IsSlotLocked(slotIndex))
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
			if (!ItemOperationTracker.TryBegin(ReferenceButtonType.Inventory, slotIndex))
			{
				ItemOperationTracker.Release(dragObject.Type, sourceSlot);
				dragObject.Clear();
				return;
			}

			Client.Broadcast(new InventorySwapItemSlotsBroadcast()
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
				ToInventory = InventoryType.Inventory,
			}, Channel.Reliable);

			dragObject.Clear();
		}

		/// <summary>
		/// Starts a drag from an occupied inventory slot.
		/// </summary>
		private void BeginDragFromSlot(UITKDragObject dragObject, IInventoryController inventoryController, int slotIndex)
		{
			bool blocked = IsSlotBlocked(slotIndex);
			bool gotItem = inventoryController.TryGetItem(slotIndex, out Item item);

			/* All three of these returned silently, so a drag that never starts is
			 * indistinguishable from one that started and was dropped. Pending marks in
			 * particular never expire — they are cleared only by an explicit ack — so a slot
			 * whose acknowledgement never arrived stays undraggable for the rest of the session
			 * with nothing to say so. */
			if (blocked || !gotItem || item == null)
			{
				FishMMO.Logging.Log.Debug("UITKInventory",
					$"BeginDrag REFUSED slot {slotIndex}: blocked={blocked} " +
					$"(pending={ItemOperationTracker.IsPending(ReferenceButtonType.Inventory, slotIndex)}) " +
					$"gotItem={gotItem} itemNull={item == null}.");
				return;
			}

			FishMMO.Logging.Log.Debug("UITKInventory", $"BeginDrag OK slot {slotIndex}.");

			/* An item with no icon is still an item. This used to refuse the drag outright when
			 * Template.Icon was null, which meant that on a project whose item art is not in yet
			 * — every icon unassigned — picking anything up was impossible, and so was every
			 * interaction built on it: click-to-pick-up, click-to-drop, and press-and-drag all
			 * dead, with no error to say why. The icon is decoration on the cursor; whether the
			 * player may move the item is not its business. */
			Sprite sprite = item.Template != null ? item.Template.Icon : null;

			/* Carry the item, not just the slot number. A slot index is only true while nothing
			 * writes to that slot, and the server can write to it between the pick-up and the
			 * drop — at which point submitting the index alone moves whatever landed there. */
			dragObject.SetItemReference(sprite, slotIndex, ReferenceButtonType.Inventory, item);
		}

		/// <summary>
		/// Right-click: activates the item in this slot.
		/// </summary>
		/// <remarks>
		/// Activation is entirely client-local today — <c>InventoryController.Activate</c> sends
		/// nothing and the server has no matching handler — so there is no reply to wait for and
		/// no pending mark to take. The guard is still worth having: a slot with a move in flight
		/// is a slot whose contents are about to change, and using the item that is on its way out
		/// is a request built on a stale view.
		/// </remarks>
		private void HandleSlotRightClick(int slotIndex)
		{
			if (IsSlotBlocked(slotIndex))
			{
				return;
			}

			if (!Character.TryGet(out IInventoryController inventoryController))
			{
				return;
			}

			/* Equip, if the item can be worn. InventoryController.Activate is the "use this item"
			 * path and it does nothing at all today — its body is a log line and a commented-out
			 * OnUseItem, and the server has no matching handler — so right-clicking an item was
			 * silently doing nothing. Equipping is the behaviour the slot actually needs, and the
			 * broadcast for it already exists and is already handled server-side; the equipment
			 * panel has been sending it for click-to-drop all along. The destination slot comes
			 * from the item's own template, which is what makes a single right-click meaningful:
			 * a breastplate has exactly one slot it can go to. */
			if (inventoryController.TryGetItem(slotIndex, out Item item) &&
				item.Template is EquippableItemTemplate equippable)
			{
				if (!ItemOperationTracker.TryBegin(ReferenceButtonType.Inventory, slotIndex))
				{
					return;
				}
				if (!ItemOperationTracker.TryBegin(ReferenceButtonType.Equipment, (int)equippable.Slot))
				{
					ItemOperationTracker.Release(ReferenceButtonType.Inventory, slotIndex);
					return;
				}

				Client.Broadcast(new EquipmentEquipItemBroadcast()
				{
					InventoryIndex = slotIndex,
					Slot           = (byte)equippable.Slot,
					FromInventory  = InventoryType.Inventory,
				}, Channel.Reliable);
				return;
			}

			inventoryController.Activate(slotIndex);
		}

		/// <summary>
		/// Shows the item tooltip when the pointer enters a slot that contains an item.
		/// </summary>
		private void OnSlotPointerEnter(int slotIndex, VisualElement owner)
		{
			if (Character == null ||
				!Character.TryGet(out IInventoryController inventoryController) ||
				!inventoryController.TryGetItem(slotIndex, out Item item))
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
			ItemOperationTracker.ReleaseAll(ReferenceButtonType.Inventory);

			if (UIManager.TryGetTK(DRAG_OBJECT_NAME, out UITKDragObject dragObject) &&
				dragObject.IsDragging &&
				dragObject.Type == ReferenceButtonType.Inventory)
			{
				dragObject.Clear();
			}
		}
	}
}
