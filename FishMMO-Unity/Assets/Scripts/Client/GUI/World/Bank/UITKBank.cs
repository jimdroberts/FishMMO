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
	/// Binds to <c>UIBank.uxml</c> / <c>UIBank.uss</c> and replicates all behaviour of the
	/// legacy UGUI <see cref="UIBank"/> and <see cref="UIBankButton"/> classes without modifying
	/// them. Item drag-and-drop and tooltips reuse the existing UGUI <see cref="UIDragObject"/>
	/// and <see cref="UITooltip"/> overlays via <see cref="UIManager"/>.
	/// </summary>
	public class UITKBank : UITKCharacterControl
	{
		// ── UXML element names ────────────────────────────────────────────────

		private const string SLOT_GRID_NAME = "slot-grid";
		private const string CLOSE_BTN_NAME = "close-button";

		// ── Shared UI overlay names (legacy UGUI controls reused via UIManager) ──

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

		/// <summary>Cached item sprite per slot, used to seed the drag object.</summary>
		private readonly List<Sprite> slotSprites = new List<Sprite>();

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

			Button closeBtn = root.Q<Button>(CLOSE_BTN_NAME);
			if (closeBtn != null)
			{
				closeBtn.clicked += Hide;
			}
		}

		/// <summary>
		/// Registers the banker broadcast handler when the client is injected.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<BankerBroadcast>(OnClientBankerBroadcastReceived);
		}

		/// <summary>
		/// Unregisters the banker broadcast handler when the client is cleared.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<BankerBroadcast>(OnClientBankerBroadcastReceived);
		}

		/// <summary>
		/// Destroys all runtime slot elements when the control is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			DestroySlots();
			base.OnDestroying();
		}

		// ── Broadcast handling ────────────────────────────────────────────────

		/// <summary>
		/// Shows the bank panel when a banker interaction succeeds, otherwise hides it.
		/// Mirrors <see cref="UIBank.OnClientBankerBroadcastReceived"/>.
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
				slotGrid == null ||
				!Character.TryGet(out IBankController bankController))
			{
				return;
			}

			bankController.OnSlotUpdated -= OnBankSlotUpdated;
			bankController.OnSlotLockChanged -= OnBankSlotLockChanged;

			int slotCount = bankController.Items.Count;
			for (int i = 0; i < slotCount; ++i)
			{
				SlotView view = CreateSlot(i);
				slotViews.Add(view);
				slotSprites.Add(null);

				if (bankController.TryGetItem(i, out Item item))
				{
					SetSlotItem(i, item);
				}
				SetSlotLocked(i, bankController.IsSlotLocked(i));
			}

			bankController.OnSlotUpdated += OnBankSlotUpdated;
			bankController.OnSlotLockChanged += OnBankSlotLockChanged;
		}

		// ── Bank slot callbacks ───────────────────────────────────────────────

		/// <summary>
		/// Called when the lock state of a bank slot changes.
		/// </summary>
		public void OnBankSlotLockChanged(IItemContainer container, int slot, bool isLocked)
		{
			if (slot >= 0 && slot < slotViews.Count)
			{
				SetSlotLocked(slot, isLocked);
			}
		}

		/// <summary>
		/// Called when a bank slot's item changes.
		/// </summary>
		public void OnBankSlotUpdated(IItemContainer container, Item item, int bankIndex)
		{
			if (container == null || bankIndex < 0 || bankIndex >= slotViews.Count)
			{
				return;
			}

			if (!container.IsSlotEmpty(bankIndex))
			{
				SetSlotItem(bankIndex, item);
			}
			else
			{
				ClearSlot(bankIndex);
			}
		}

		// ── Slot element construction ─────────────────────────────────────────

		/// <summary>
		/// Creates a single bank slot element, registers its interaction callbacks,
		/// and appends it to the slot grid.
		/// </summary>
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
			slotRoot.RegisterCallback<PointerEnterEvent>(evt => OnSlotPointerEnter(captured));
			slotRoot.RegisterCallback<PointerLeaveEvent>(evt => OnSlotPointerLeave());

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
		private void DestroySlots()
		{
			if (slotGrid != null)
			{
				for (int i = 0; i < slotViews.Count; ++i)
				{
					if (slotViews[i].Root != null)
					{
						slotGrid.Remove(slotViews[i].Root);
					}
				}
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
		private void SetSlotLocked(int slotIndex, bool isLocked)
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

			if (isLocked)
			{
				lockEl.RemoveFromClassList(CSS_HIDDEN);
			}
			else
			{
				lockEl.AddToClassList(CSS_HIDDEN);
			}
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
		/// dragging the item currently in this slot. Mirrors <see cref="UIBankButton.OnLeftClick"/>.
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

			if (dragObject.Visible)
			{
				InventoryType inventoryType = dragObject.Type == ReferenceButtonType.Bank ? InventoryType.Bank :
											  dragObject.Type == ReferenceButtonType.Inventory ? InventoryType.Inventory :
											  InventoryType.Equipment;

				if (inventoryType != InventoryType.Equipment)
				{
					int from = (int)dragObject.ReferenceID;
					int to = slotIndex;

					if (bankController.CanSwapItemSlots(from, to, inventoryType))
					{
						Client.Broadcast(new BankSwapItemSlotsBroadcast()
						{
							From = from,
							To = to,
							FromInventory = inventoryType,
						}, Channel.Reliable);
					}
				}
				else if (dragObject.ReferenceID >= byte.MinValue &&
						 dragObject.ReferenceID <= byte.MaxValue)
				{
					Client.Broadcast(new EquipmentUnequipItemBroadcast()
					{
						Slot = (byte)dragObject.ReferenceID,
						ToInventory = InventoryType.Bank,
					}, Channel.Reliable);
				}

				dragObject.Clear();
			}
			else if (!bankController.IsSlotEmpty(slotIndex))
			{
				Sprite sprite = slotSprites[slotIndex];
				if (sprite != null)
				{
					dragObject.SetReference(sprite, slotIndex, ReferenceButtonType.Bank);
				}
			}
		}

		/// <summary>
		/// Shows the item tooltip when the pointer enters a slot that contains an item.
		/// </summary>
		private void OnSlotPointerEnter(int slotIndex)
		{
			if (Character == null ||
				!Character.TryGet(out IBankController bankController) ||
				!bankController.TryGetItem(slotIndex, out Item item))
			{
				return;
			}

			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				tooltip.Open(item.Tooltip());
			}
		}

		/// <summary>
		/// Hides the item tooltip when the pointer leaves a slot.
		/// </summary>
		private void OnSlotPointerLeave()
		{
			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				tooltip.Hide();
			}
		}
	}
}
