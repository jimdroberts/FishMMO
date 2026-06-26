using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet;
using FishNet.Transporting;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit hotkey bar. Replaces the legacy UGUI <see cref="UIHotkeyBar"/> /
	/// <see cref="UIHotkeyButton"/> / <see cref="UIHotkeyGroup"/> trio with dynamically
	/// generated VisualElement slots. Preserves all network behaviour:
	/// <see cref="HotkeySetBroadcast"/> / <see cref="HotkeySetMultipleBroadcast"/> assignment,
	/// cooldown sweeps via <see cref="ICooldownController"/>, drag-assign / drag-clear via the
	/// shared UGUI <c>UIDragObject</c> overlay, and Input System activation.
	/// </summary>
	public class UITKHotkeyBar : UITKCharacterControl
	{
		/// <summary>Name of the container element that holds the generated hotkey slots.</summary>
		private const string LIST_NAME = "hotkey-list";

		/// <summary>USS class applied to each generated hotkey slot root.</summary>
		private const string SLOT_CLASS = "hotkey-slot";

		/// <summary>USS class applied to each slot's icon element.</summary>
		private const string ICON_CLASS = "hotkey-slot__icon";

		/// <summary>USS class applied to each slot's cooldown sweep overlay.</summary>
		private const string COOLDOWN_CLASS = "hotkey-slot__cooldown";

		/// <summary>USS class applied to each slot's key-map label.</summary>
		private const string LABEL_CLASS = "hotkey-slot__label";

		/// <summary>Name of the shared drag overlay registered with the UIManager.</summary>
		private const string DRAG_OBJECT_NAME = "UIDragObject";

		/// <summary>Name of the shared tooltip overlay registered with the UIManager.</summary>
		private const string TOOLTIP_NAME = "UITooltip";

		/// <summary>
		/// Runtime state and visual elements for a single hotkey slot.
		/// </summary>
		private sealed class HotkeySlot
		{
			/// <summary>Root container for the slot.</summary>
			public VisualElement Root;
			/// <summary>Icon element showing the assigned item/ability sprite.</summary>
			public VisualElement Icon;
			/// <summary>Cooldown sweep overlay (height driven from C#).</summary>
			public VisualElement Cooldown;
			/// <summary>Key-map label (e.g. "1", "LMB").</summary>
			public Label Label;
			/// <summary>The fixed hotkey slot index.</summary>
			public int Index;
			/// <summary>The reference type currently assigned to the slot.</summary>
			public ReferenceButtonType Type = ReferenceButtonType.None;
			/// <summary>The reference ID currently assigned to the slot.</summary>
			public long ReferenceID = UIReferenceButton.NULL_REFERENCE_ID;
		}

		/// <summary>All created hotkey slots in index order.</summary>
		private readonly List<HotkeySlot> slots = new List<HotkeySlot>();
		/// <summary>The container element that holds the generated hotkey slot roots.</summary>
		private VisualElement list;

		/// <summary>
		/// Queries the list container, builds the hotkey slots and subscribes to cooldown events.
		/// </summary>
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root != null)
			{
				list = root.Q(LIST_NAME);
			}

			BuildSlots(Constants.Configuration.MaximumPlayerHotkeys);

			ICooldownController.OnAddCooldown += CooldownController_OnAddOrUpdateCooldown;
			ICooldownController.OnUpdateCooldown += CooldownController_OnAddOrUpdateCooldown;
			ICooldownController.OnRemoveCooldown += CooldownController_OnRemoveCooldown;
		}

		/// <summary>
		/// Unsubscribes from cooldown events when the control is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			ICooldownController.OnAddCooldown -= CooldownController_OnAddOrUpdateCooldown;
			ICooldownController.OnUpdateCooldown -= CooldownController_OnAddOrUpdateCooldown;
			ICooldownController.OnRemoveCooldown -= CooldownController_OnRemoveCooldown;
		}

		/// <summary>
		/// Registers the hotkey assignment broadcasts when the client connection is set.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<HotkeySetBroadcast>(OnClientHotkeySetBroadcastReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<HotkeySetMultipleBroadcast>(OnClientHotkeySetMultipleBroadcastReceived);
		}

		/// <summary>
		/// Unregisters the hotkey assignment broadcasts when the client connection is unset.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<HotkeySetBroadcast>(OnClientHotkeySetBroadcastReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<HotkeySetMultipleBroadcast>(OnClientHotkeySetMultipleBroadcastReceived);
		}

		/// <summary>
		/// Builds the requested number of hotkey slots, capped to the configured maximum.
		/// </summary>
		/// <param name="amount">The number of hotkey slots to create.</param>
		private void BuildSlots(int amount)
		{
			if (list == null)
			{
				return;
			}

			for (int i = 0; i < amount && i < Constants.Configuration.MaximumPlayerHotkeys; ++i)
			{
				HotkeySlot slot = CreateSlot(i);
				list.Add(slot.Root);
				slots.Add(slot);
			}
		}

		/// <summary>
		/// Creates the visual elements and registers interaction callbacks for a single slot.
		/// </summary>
		/// <param name="index">The hotkey slot index.</param>
		/// <returns>The populated <see cref="HotkeySlot"/>.</returns>
		private HotkeySlot CreateSlot(int index)
		{
			VisualElement slotRoot = new VisualElement();
			slotRoot.AddToClassList(SLOT_CLASS);

			VisualElement icon = new VisualElement();
			icon.AddToClassList(ICON_CLASS);
			slotRoot.Add(icon);

			VisualElement cooldown = new VisualElement();
			cooldown.AddToClassList(COOLDOWN_CLASS);
			cooldown.style.height = Length.Percent(0.0f);
			slotRoot.Add(cooldown);

			string keyMap = UIHotkeyBar.GetHotkeyIndexKeyMap(index)
				.Replace("Hotkey ", string.Empty)
				.Replace("Left Mouse", "LMB")
				.Replace("Right Mouse", "RMB");
			Label label = new Label(keyMap);
			label.AddToClassList(LABEL_CLASS);
			slotRoot.Add(label);

			HotkeySlot slot = new HotkeySlot
			{
				Root = slotRoot,
				Icon = icon,
				Cooldown = cooldown,
				Label = label,
				Index = index,
			};

			slotRoot.RegisterCallback<PointerDownEvent>(evt => OnSlotPointerDown(evt, slot));
			slotRoot.RegisterCallback<PointerEnterEvent>(evt => OnSlotPointerEnter(slot));
			slotRoot.RegisterCallback<PointerLeaveEvent>(evt => OnSlotPointerLeave());

			return slot;
		}

		/// <summary>
		/// Handles a broadcast that assigns or clears a single hotkey slot.
		/// </summary>
		/// <param name="msg">The broadcast message.</param>
		/// <param name="channel">The network channel.</param>
		private void OnClientHotkeySetBroadcastReceived(HotkeySetBroadcast msg, Channel channel)
		{
			if (msg.HotkeyData.Slot < 0 || msg.HotkeyData.Slot >= slots.Count)
			{
				return;
			}

			HotkeySlot slot = slots[msg.HotkeyData.Slot];
			if (msg.HotkeyData.Type == 0)
			{
				ClearSlot(slot);
			}
			else
			{
				slot.Type = (ReferenceButtonType)msg.HotkeyData.Type;
				slot.ReferenceID = msg.HotkeyData.ReferenceID;
				RefreshSlotIcon(slot);
			}
		}

		/// <summary>
		/// Handles a broadcast that assigns or clears multiple hotkey slots.
		/// </summary>
		/// <param name="msg">The broadcast message.</param>
		/// <param name="channel">The network channel.</param>
		private void OnClientHotkeySetMultipleBroadcastReceived(HotkeySetMultipleBroadcast msg, Channel channel)
		{
			foreach (HotkeySetBroadcast subMsg in msg.Hotkeys)
			{
				OnClientHotkeySetBroadcastReceived(subMsg, channel);
			}
		}

		/// <summary>
		/// Called every frame. Validates hotkey assignments and polls the Input System for activation.
		/// </summary>
		private void Update()
		{
			ValidateHotkeys();
			UpdateInput();
		}

		/// <summary>
		/// Clears hotkeys whose referenced item/ability no longer exists and refreshes icons.
		/// </summary>
		private void ValidateHotkeys()
		{
			if (Character == null)
			{
				return;
			}

			for (int i = 0; i < slots.Count; ++i)
			{
				HotkeySlot slot = slots[i];

				switch (slot.Type)
				{
					case ReferenceButtonType.None:
						break;
					case ReferenceButtonType.Inventory:
						if (!Character.TryGet(out IInventoryController inventoryController) ||
							inventoryController.IsSlotEmpty((int)slot.ReferenceID))
						{
							ClearSlot(slot);
						}
						else
						{
							RefreshSlotIcon(slot);
						}
						break;
					case ReferenceButtonType.Equipment:
						if (!Character.TryGet(out IEquipmentController equipmentController) ||
							equipmentController.IsSlotEmpty((int)slot.ReferenceID))
						{
							ClearSlot(slot);
						}
						else
						{
							RefreshSlotIcon(slot);
						}
						break;
					case ReferenceButtonType.Ability:
						if (!Character.TryGet(out IAbilityController abilityController) ||
							!abilityController.KnownAbilities.ContainsKey(slot.ReferenceID))
						{
							ClearSlot(slot);
						}
						else
						{
							RefreshSlotIcon(slot);
						}
						break;
					default:
						break;
				}
			}
		}

		/// <summary>
		/// Polls the Input System and activates the first pressed hotkey slot.
		/// </summary>
		private void UpdateInput()
		{
			if (Character == null || slots.Count < 1)
			{
				return;
			}

			if (PlayerInputHandler.MouseMode || UIManager.InputControlHasFocus())
			{
				return;
			}

			for (int i = 0; i < slots.Count; ++i)
			{
				if (IsHotkeyPressed(i))
				{
					ActivateSlot(slots[i]);
					return;
				}
			}
		}

		/// <summary>
		/// Handles pointer-down on a slot: assigns a dragged reference (left), activates (left, no drag),
		/// or removes the assignment (right).
		/// </summary>
		/// <param name="evt">The pointer-down event.</param>
		/// <param name="slot">The slot that was pressed.</param>
		private void OnSlotPointerDown(PointerDownEvent evt, HotkeySlot slot)
		{
			if (evt.button == 0)
			{
				HandleSlotLeftClick(slot);
			}
			else if (evt.button == 1)
			{
				HandleSlotRightClick(slot);
			}
		}

		/// <summary>
		/// Assigns a dragged reference to the slot (broadcasting the change) or activates it.
		/// </summary>
		/// <param name="slot">The slot that was left-clicked.</param>
		private void HandleSlotLeftClick(HotkeySlot slot)
		{
			if (UIManager.TryGetTK(DRAG_OBJECT_NAME, out UITKDragObject dragObject) && dragObject.Visible)
			{
				if (dragObject.Type != ReferenceButtonType.Bank)
				{
					slot.Type = dragObject.Type;
					slot.ReferenceID = dragObject.ReferenceID;
					if (dragObject.IconSprite != null)
					{
						SetSlotIconSprite(slot, dragObject.IconSprite);
					}

					Client.Broadcast(new HotkeySetBroadcast()
					{
						HotkeyData = new HotkeyData()
						{
							Type = (byte)slot.Type,
							Slot = slot.Index,
							ReferenceID = slot.ReferenceID,
						}
					}, Channel.Reliable);
				}

				dragObject.Clear();
			}
			else
			{
				ActivateSlot(slot);
			}
		}

		/// <summary>
		/// Removes the slot's assignment, seeds the drag overlay, and broadcasts the change.
		/// </summary>
		/// <param name="slot">The slot that was right-clicked.</param>
		private void HandleSlotRightClick(HotkeySlot slot)
		{
			if (slot.ReferenceID == UIReferenceButton.NULL_REFERENCE_ID)
			{
				return;
			}

			if (UIManager.TryGetTK(DRAG_OBJECT_NAME, out UITKDragObject dragObject))
			{
				Sprite sprite = slot.Icon.style.backgroundImage.value.sprite;
				dragObject.SetReference(sprite, slot.ReferenceID, slot.Type);

				ClearSlot(slot);

				Client.Broadcast(new HotkeySetBroadcast()
				{
					HotkeyData = new HotkeyData()
					{
						Type = 0,
						Slot = slot.Index,
						ReferenceID = UIReferenceButton.NULL_REFERENCE_ID,
					}
				}, Channel.Reliable);
			}
		}

		/// <summary>
		/// Shows the tooltip for the slot's assigned reference when hovered.
		/// </summary>
		/// <param name="slot">The hovered slot.</param>
		private void OnSlotPointerEnter(HotkeySlot slot)
		{
			if (Character == null || slot.ReferenceID < 0)
			{
				return;
			}

			if (!UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				return;
			}

			switch (slot.Type)
			{
				case ReferenceButtonType.Inventory:
					if (Character.TryGet(out IInventoryController inventoryController) &&
						inventoryController.TryGetItem((int)slot.ReferenceID, out Item inventoryItem))
					{
						tooltip.Open(inventoryItem.Tooltip());
					}
					break;
				case ReferenceButtonType.Equipment:
					if (Character.TryGet(out IEquipmentController equipmentController) &&
						equipmentController.TryGetItem((int)slot.ReferenceID, out Item equippedItem))
					{
						tooltip.Open(equippedItem.Tooltip());
					}
					break;
				case ReferenceButtonType.Ability:
					if (Character.TryGet(out IAbilityController abilityController) &&
						abilityController.KnownAbilities.TryGetValue(slot.ReferenceID, out Ability ability))
					{
						tooltip.Open(ability.Tooltip());
					}
					break;
				default:
					break;
			}
		}

		/// <summary>
		/// Hides the tooltip when the pointer leaves a slot.
		/// </summary>
		private void OnSlotPointerLeave()
		{
			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				tooltip.Hide();
			}
		}

		/// <summary>
		/// Activates the slot's assigned action based on its reference type.
		/// </summary>
		/// <param name="slot">The slot to activate.</param>
		private void ActivateSlot(HotkeySlot slot)
		{
			if (Character == null)
			{
				return;
			}

			switch (slot.Type)
			{
				case ReferenceButtonType.Inventory:
					if (Character.TryGet(out IInventoryController inventoryController))
					{
						inventoryController.Activate((int)slot.ReferenceID);
					}
					break;
				case ReferenceButtonType.Equipment:
					if (Character.TryGet(out IEquipmentController equipmentController))
					{
						equipmentController.Activate((int)slot.ReferenceID);
					}
					break;
				case ReferenceButtonType.Ability:
					if (!UIManager.ControlHasFocus() &&
						Character.TryGet(out IAbilityController abilityController))
					{
						abilityController.Activate(slot.ReferenceID, true);
					}
					break;
				default:
					break;
			}
		}

		/// <summary>
		/// Refreshes a slot's icon from its currently assigned reference.
		/// </summary>
		/// <param name="slot">The slot to refresh.</param>
		private void RefreshSlotIcon(HotkeySlot slot)
		{
			if (Character == null)
			{
				return;
			}

			Sprite sprite = null;
			switch (slot.Type)
			{
				case ReferenceButtonType.Inventory:
					if (Character.TryGet(out IInventoryController inventoryController) &&
						inventoryController.TryGetItem((int)slot.ReferenceID, out Item inventoryItem) &&
						inventoryItem.Template != null)
					{
						sprite = inventoryItem.Template.Icon;
					}
					break;
				case ReferenceButtonType.Equipment:
					if (Character.TryGet(out IEquipmentController equipmentController) &&
						equipmentController.TryGetItem((int)slot.ReferenceID, out Item equippedItem) &&
						equippedItem.Template != null)
					{
						sprite = equippedItem.Template.Icon;
					}
					break;
				case ReferenceButtonType.Ability:
					if (Character.TryGet(out IAbilityController abilityController) &&
						abilityController.KnownAbilities.TryGetValue(slot.ReferenceID, out Ability ability) &&
						ability.Template != null)
					{
						sprite = ability.Template.Icon;
					}
					break;
				default:
					break;
			}

			if (sprite != null)
			{
				SetSlotIconSprite(slot, sprite);
			}
		}

		/// <summary>
		/// Sets a slot's icon sprite, showing or hiding the icon as appropriate.
		/// </summary>
		/// <param name="slot">The slot to update.</param>
		/// <param name="sprite">The sprite to display, or null to clear.</param>
		private void SetSlotIconSprite(HotkeySlot slot, Sprite sprite)
		{
			if (sprite != null)
			{
				slot.Icon.style.backgroundImage = new StyleBackground(sprite);
				slot.Icon.style.display = DisplayStyle.Flex;
			}
			else
			{
				slot.Icon.style.backgroundImage = new StyleBackground();
				slot.Icon.style.display = DisplayStyle.None;
			}
		}

		/// <summary>
		/// Clears a slot's assignment, icon and cooldown sweep.
		/// </summary>
		/// <param name="slot">The slot to clear.</param>
		private void ClearSlot(HotkeySlot slot)
		{
			slot.Type = ReferenceButtonType.None;
			slot.ReferenceID = UIReferenceButton.NULL_REFERENCE_ID;
			SetSlotIconSprite(slot, null);
			slot.Cooldown.style.height = Length.Percent(0.0f);
		}

		/// <summary>
		/// Updates the cooldown sweep for whichever slot references the cooled-down entry.
		/// </summary>
		/// <param name="referenceID">The reference ID on cooldown.</param>
		/// <param name="cooldown">The cooldown instance.</param>
		private void CooldownController_OnAddOrUpdateCooldown(long referenceID, CooldownInstance cooldown)
		{
			uint currentTick = GetCurrentCooldownTick();
			float remaining = cooldown.RemainingTime(currentTick);
			float fraction = remaining > 0.0f && cooldown.TotalTime > 0.0f
				? remaining / cooldown.TotalTime
				: 0.0f;

			for (int i = 0; i < slots.Count; ++i)
			{
				HotkeySlot slot = slots[i];
				if (slot.ReferenceID == referenceID)
				{
					slot.Cooldown.style.height = Length.Percent(Mathf.Clamp01(fraction) * 100.0f);
				}
			}
		}

		/// <summary>
		/// Clears the cooldown sweep for whichever slot references the finished cooldown.
		/// </summary>
		/// <param name="referenceID">The reference ID whose cooldown ended.</param>
		private void CooldownController_OnRemoveCooldown(long referenceID)
		{
			for (int i = 0; i < slots.Count; ++i)
			{
				HotkeySlot slot = slots[i];
				if (slot.ReferenceID == referenceID)
				{
					slot.Cooldown.style.height = Length.Percent(0.0f);
				}
			}
		}

		/// <summary>
		/// Resolves the authoritative cooldown tick for remaining-time calculations.
		/// </summary>
		/// <returns>The authoritative tick.</returns>
		private uint GetCurrentCooldownTick()
		{
			uint localTick = InstanceFinder.TimeManager != null ? InstanceFinder.TimeManager.LocalTick : 0u;
			if (Character != null && Character.TryGet(out ICooldownController cooldownController))
			{
				return cooldownController.ResolveAuthoritativeTick(localTick);
			}
			return localTick;
		}

		/// <summary>
		/// Returns whether the hotkey at the given index is currently pressed via the Input System.
		/// </summary>
		/// <param name="hotkeyIndex">The hotkey index.</param>
		/// <returns>True if the corresponding input is pressed.</returns>
		private static bool IsHotkeyPressed(int hotkeyIndex)
		{
			if (PlayerInputHandler.Controls == null)
			{
				return false;
			}

			switch (hotkeyIndex)
			{
				case 0:
					return Mouse.current != null && Mouse.current.leftButton.isPressed;
				case 1:
					return Mouse.current != null && Mouse.current.rightButton.isPressed;
				case 2:
					return PlayerInputHandler.Controls.Player.Hotkey1.IsPressed();
				case 3:
					return PlayerInputHandler.Controls.Player.Hotkey2.IsPressed();
				case 4:
					return PlayerInputHandler.Controls.Player.Hotkey3.IsPressed();
				case 5:
					return PlayerInputHandler.Controls.Player.Hotkey4.IsPressed();
				case 6:
					return PlayerInputHandler.Controls.Player.Hotkey5.IsPressed();
				case 7:
					return PlayerInputHandler.Controls.Player.Hotkey6.IsPressed();
				case 8:
					return PlayerInputHandler.Controls.Player.Hotkey7.IsPressed();
				case 9:
					return PlayerInputHandler.Controls.Player.Hotkey8.IsPressed();
				case 10:
					return PlayerInputHandler.Controls.Player.Hotkey9.IsPressed();
				case 11:
					return PlayerInputHandler.Controls.Player.Hotkey0.IsPressed();
				default:
					return false;
			}
		}
	}
}
