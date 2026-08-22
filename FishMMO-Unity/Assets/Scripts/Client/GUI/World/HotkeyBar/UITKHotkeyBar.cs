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
	/// UI Toolkit hotkey bar. Renders the character's hotkey slots as dynamically generated
	/// VisualElements and owns their network behaviour:
	/// <see cref="HotkeySetBroadcast"/> / <see cref="HotkeySetMultipleBroadcast"/> assignment,
	/// cooldown sweeps via <see cref="ICooldownController"/>, drag-assign / drag-clear via the
	/// shared <see cref="UITKDragObject"/> overlay, and Input System activation.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Model / view split.</b> <see cref="bindings"/> is plain data describing what is bound to
	/// each slot and belongs to the character; <see cref="slots"/> holds the elements currently
	/// rendering it and belongs to ONE visual tree. <c>UIDocument</c> re-clones the UXML on every
	/// enable, so any element cached across a hide/show is a pointer into a discarded tree — the
	/// old code kept its slot state exclusively in those elements and rebuilt them by APPENDING to
	/// a list it never cleared, so a rebuilt tree produced twelve more orphaned slots and a bar
	/// whose bindings had quietly detached from what the player could see.
	/// </para>
	/// <para>
	/// <b>Change-driven, not per-frame.</b> This panel used to rewrite all twelve slots' icons and
	/// display styles on every single frame from <c>Update</c> — twelve <c>TryGet</c> calls,
	/// twelve dictionary probes and up to twenty-four inline style writes at frame rate for a bar
	/// that changes a handful of times per session. Refreshes are now driven by the events that
	/// can actually invalidate a binding (container slot updates, equip/unequip, ability learned,
	/// hotkey broadcast) and coalesced to at most one sweep per frame.
	/// </para>
	/// </remarks>
	public class UITKHotkeyBar : UITKCharacterControl
	{
		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Hud;

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
		/// What a single hotkey slot is bound to. Plain data — no <see cref="VisualElement"/> —
		/// so it survives every rebuild of the visual tree.
		/// </summary>
		private struct HotkeyBinding
		{
			/// <summary>The reference type currently assigned to the slot.</summary>
			public ReferenceButtonType Type;
			/// <summary>The reference ID currently assigned to the slot.</summary>
			public long ReferenceID;
		}

		/// <summary>
		/// Visual elements rendering a single hotkey slot, plus the state needed to avoid
		/// rewriting them when nothing changed.
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

			/// <summary>
			/// The sprite currently written into <see cref="Icon"/>.
			/// </summary>
			/// <remarks>
			/// Kept so the refresh sweep can skip the inline style write when the icon has not
			/// actually changed. Writing an identical <c>StyleBackground</c> still dirties the
			/// element and costs a repaint, which is most of what made the old per-frame refresh
			/// expensive.
			/// </remarks>
			public Sprite AppliedSprite;

			/// <summary>The cooldown sweep fraction currently written into <see cref="Cooldown"/>.</summary>
			public float AppliedCooldownFraction = -1.0f;

			/// <summary>True while this slot's input is held down.</summary>
			public bool IsPressed;

			/// <summary>True when the held activation still owes the controller a Release().</summary>
			public bool AwaitingRelease;
		}

		/// <summary>What each slot is bound to. Index-aligned with <see cref="slots"/>.</summary>
		private HotkeyBinding[] bindings;

		/// <summary>All created hotkey slots in index order. Belongs to the current visual tree.</summary>
		private readonly List<HotkeySlot> slots = new List<HotkeySlot>();

		/// <summary>The container element that holds the generated hotkey slot roots.</summary>
		private VisualElement list;

		/// <summary>
		/// Set when something happened that could have invalidated a binding or an icon.
		/// Consumed by the next <see cref="OnTick"/>.
		/// </summary>
		/// <remarks>
		/// Coalescing matters more than it looks: the server delivers a full bar as one
		/// <see cref="HotkeySetMultipleBroadcast"/> which is unpacked into one set per slot, and
		/// loading a character's inventory raises one slot-updated event per occupied slot. Doing
		/// the sweep inline would run it dozens of times for a single logical change.
		/// </remarks>
		private bool bindingsDirty = true;

		/// <summary>True while at least one slot is showing a cooldown sweep.</summary>
		private bool anyCooldownActive;

		/// <summary>
		/// Queries the list container, builds the hotkey slots and subscribes to cooldown events.
		/// </summary>
		/// <remarks>
		/// Runs again on every tree rebuild, so everything here is written to be idempotent: the
		/// slot list is cleared before it is rebuilt, and the static cooldown subscriptions are
		/// removed before they are added. A bare <c>+=</c> on a static event from a hook that can
		/// re-run is an unbounded handler leak.
		/// </remarks>
		public override void OnStarting()
		{
			EnsureBindings();

			/* The elements in `slots` belong to the tree that was just replaced. Dropping them
			 * first is what stops BuildSlots appending a second set of twelve. */
			slots.Clear();
			list = null;

			VisualElement root = Root;
			if (root != null)
			{
				list = root.Q(LIST_NAME);
			}

			BuildSlots(Constants.Configuration.MaximumPlayerHotkeys);

			ICooldownController.OnAddCooldown -= CooldownController_OnAddOrUpdateCooldown;
			ICooldownController.OnAddCooldown += CooldownController_OnAddOrUpdateCooldown;
			ICooldownController.OnUpdateCooldown -= CooldownController_OnAddOrUpdateCooldown;
			ICooldownController.OnUpdateCooldown += CooldownController_OnAddOrUpdateCooldown;
			ICooldownController.OnRemoveCooldown -= CooldownController_OnRemoveCooldown;
			ICooldownController.OnRemoveCooldown += CooldownController_OnRemoveCooldown;

			bindingsDirty = true;
		}

		/// <summary>
		/// Re-renders the bar from the binding model after the visual tree was rebuilt.
		/// </summary>
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();

			bindingsDirty = true;
			RefreshAllSlots();
		}

		/// <summary>
		/// Unsubscribes from cooldown events when the control is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			ICooldownController.OnAddCooldown -= CooldownController_OnAddOrUpdateCooldown;
			ICooldownController.OnUpdateCooldown -= CooldownController_OnAddOrUpdateCooldown;
			ICooldownController.OnRemoveCooldown -= CooldownController_OnRemoveCooldown;

			UnsubscribeCharacterEvents();

			base.OnDestroying();
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
		/// Drops the previous character's subscriptions before a new character is applied.
		/// </summary>
		public override void OnPreSetCharacter()
		{
			UnsubscribeCharacterEvents();
		}

		/// <summary>
		/// Subscribes to the events that can invalidate a hotkey binding.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			SubscribeCharacterEvents();

			bindingsDirty = true;
			RefreshAllSlots();
		}

		/// <summary>
		/// Drops this character's subscriptions before it is cleared.
		/// </summary>
		public override void OnPreUnsetCharacter()
		{
			base.OnPreUnsetCharacter();

			UnsubscribeCharacterEvents();
		}

		/// <summary>
		/// Clears the binding model so one character's bar cannot appear on the next one's.
		/// </summary>
		/// <remarks>
		/// The model deliberately outlives the visual tree, which means it also outlives the
		/// character unless it is cleared here. A newly selected character with an empty bar
		/// generates no hotkey traffic at all, so nothing would ever overwrite the previous
		/// character's bindings.
		/// </remarks>
		public override void OnPostUnsetCharacter()
		{
			ClearAllBindings();
		}

		/// <inheritdoc />
		public override void OnQuitToLogin()
		{
			ClearAllBindings();

			base.OnQuitToLogin();
		}

		/// <summary>
		/// Allocates the binding model if it does not exist yet.
		/// </summary>
		private void EnsureBindings()
		{
			int count = Constants.Configuration.MaximumPlayerHotkeys;
			if (bindings != null && bindings.Length == count)
			{
				return;
			}

			bindings = new HotkeyBinding[count];
			for (int i = 0; i < count; ++i)
			{
				bindings[i].Type = ReferenceButtonType.None;
				bindings[i].ReferenceID = ReferenceButton.NULL_REFERENCE_ID;
			}
		}

		/// <summary>
		/// Empties every binding and repaints the bar.
		/// </summary>
		private void ClearAllBindings()
		{
			EnsureBindings();

			for (int i = 0; i < bindings.Length; ++i)
			{
				bindings[i].Type = ReferenceButtonType.None;
				bindings[i].ReferenceID = ReferenceButton.NULL_REFERENCE_ID;
			}

			for (int i = 0; i < slots.Count; ++i)
			{
				ApplySlotSprite(slots[i], null);
				ApplyCooldownFraction(slots[i], 0.0f);
				slots[i].IsPressed = false;
				slots[i].AwaitingRelease = false;
			}

			anyCooldownActive = false;
			bindingsDirty = false;
		}

		/// <summary>
		/// Subscribes to the container and ability events that can invalidate a binding.
		/// </summary>
		private void SubscribeCharacterEvents()
		{
			if (Character == null)
			{
				return;
			}

			if (Character.TryGet(out IInventoryController inventoryController))
			{
				inventoryController.OnSlotUpdated += Container_OnSlotUpdated;
			}
			if (Character.TryGet(out IEquipmentController equipmentController))
			{
				equipmentController.OnSlotUpdated += Container_OnSlotUpdated;
				equipmentController.OnItemEquipped += Equipment_OnItemChanged;
				equipmentController.OnItemUnequipped += Equipment_OnItemChanged;
			}
			if (Character.TryGet(out IAbilityController abilityController))
			{
				abilityController.OnAddAbility += Ability_OnAddAbility;
				abilityController.OnRemoveAbility += Ability_OnRemoveAbility;
				abilityController.OnReset += Ability_OnReset;
			}
		}

		/// <summary>
		/// Drops every character-scoped subscription this panel holds.
		/// </summary>
		private void UnsubscribeCharacterEvents()
		{
			if (Character == null)
			{
				return;
			}

			if (Character.TryGet(out IInventoryController inventoryController))
			{
				inventoryController.OnSlotUpdated -= Container_OnSlotUpdated;
			}
			if (Character.TryGet(out IEquipmentController equipmentController))
			{
				equipmentController.OnSlotUpdated -= Container_OnSlotUpdated;
				equipmentController.OnItemEquipped -= Equipment_OnItemChanged;
				equipmentController.OnItemUnequipped -= Equipment_OnItemChanged;
			}
			if (Character.TryGet(out IAbilityController abilityController))
			{
				abilityController.OnAddAbility -= Ability_OnAddAbility;
				abilityController.OnRemoveAbility -= Ability_OnRemoveAbility;
				abilityController.OnReset -= Ability_OnReset;
			}
		}

		/// <summary>Marks the bar for a refresh when a container slot changed.</summary>
		private void Container_OnSlotUpdated(IItemContainer container, Item item, int slotIndex) => bindingsDirty = true;

		/// <summary>Marks the bar for a refresh when equipment changed.</summary>
		private void Equipment_OnItemChanged(Item item, ItemSlot slot) => bindingsDirty = true;

		/// <summary>Marks the bar for a refresh when an ability was learned.</summary>
		private void Ability_OnAddAbility(Ability ability) => bindingsDirty = true;

		/// <summary>Marks the bar for a refresh when an ability was forgotten.</summary>
		private void Ability_OnRemoveAbility(long referenceID) => bindingsDirty = true;

		/// <summary>Marks the bar for a refresh when the ability set was replaced wholesale.</summary>
		private void Ability_OnReset() => bindingsDirty = true;

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

			string keyMap = HotkeyKeyMap.Get(index)
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
			EnsureBindings();

			int index = msg.HotkeyData.Slot;
			if (index < 0 || index >= bindings.Length)
			{
				return;
			}

			if (msg.HotkeyData.Type == 0)
			{
				bindings[index].Type = ReferenceButtonType.None;
				bindings[index].ReferenceID = ReferenceButton.NULL_REFERENCE_ID;
			}
			else
			{
				bindings[index].Type = (ReferenceButtonType)msg.HotkeyData.Type;
				bindings[index].ReferenceID = msg.HotkeyData.ReferenceID;
			}

			bindingsDirty = true;
		}

		/// <summary>
		/// Handles a broadcast that assigns or clears multiple hotkey slots.
		/// </summary>
		/// <param name="msg">The broadcast message.</param>
		/// <param name="channel">The network channel.</param>
		private void OnClientHotkeySetMultipleBroadcastReceived(HotkeySetMultipleBroadcast msg, Channel channel)
		{
			if (msg.Hotkeys == null)
			{
				return;
			}

			foreach (HotkeySetBroadcast subMsg in msg.Hotkeys)
			{
				OnClientHotkeySetBroadcastReceived(subMsg, channel);
			}
		}

		/// <summary>
		/// Per-frame hook. Consumes a pending refresh, animates cooldown sweeps and polls input.
		/// </summary>
		/// <remarks>
		/// This used to be <c>private void Update()</c>. <see cref="UITKControl"/> declares its own
		/// <c>Update</c> and Unity binds only the MOST DERIVED one, so declaring a second in a
		/// subclass silently killed <c>PollLoseFocus</c> and <c>OnTick</c> for this panel — the
		/// exact failure the base class's own comment warns about.
		/// </remarks>
		protected override void OnTick()
		{
			if (bindingsDirty)
			{
				RefreshAllSlots();
			}

			UpdateCooldownSweeps();
			UpdateInput();
		}

		/// <summary>
		/// Validates every binding and repaints any slot whose icon actually changed.
		/// </summary>
		/// <remarks>
		/// A binding whose referenced item or ability no longer exists is dropped here rather than
		/// left pointing at nothing — including on the login path, where the server replays the
		/// stored bar before the client necessarily has the referenced item.
		/// </remarks>
		private void RefreshAllSlots()
		{
			bindingsDirty = false;

			EnsureBindings();

			if (Character == null)
			{
				for (int i = 0; i < slots.Count; ++i)
				{
					ApplySlotSprite(slots[i], null);
				}
				return;
			}

			for (int i = 0; i < slots.Count && i < bindings.Length; ++i)
			{
				HotkeySlot slot = slots[i];

				if (!TryResolveSlotSprite(i, out Sprite sprite))
				{
					// The referenced item or ability is gone; drop the binding rather than
					// leaving a slot that would activate nothing.
					ClearBinding(i, broadcast: false);
					ApplySlotSprite(slot, null);
					ApplyCooldownFraction(slot, 0.0f);
					continue;
				}

				ApplySlotSprite(slot, sprite);
			}
		}

		/// <summary>
		/// Resolves the icon for a binding, reporting whether the binding is still valid.
		/// </summary>
		/// <param name="index">The hotkey slot index.</param>
		/// <param name="sprite">The resolved icon, or null when the slot is empty.</param>
		/// <returns>False when the binding references something that no longer exists.</returns>
		private bool TryResolveSlotSprite(int index, out Sprite sprite)
		{
			sprite = null;

			ref HotkeyBinding binding = ref bindings[index];
			switch (binding.Type)
			{
				case ReferenceButtonType.None:
					return true;
				case ReferenceButtonType.Inventory:
					if (!Character.TryGet(out IInventoryController inventoryController) ||
						!inventoryController.TryGetItem((int)binding.ReferenceID, out Item inventoryItem))
					{
						return false;
					}
					sprite = inventoryItem.Template != null ? inventoryItem.Template.Icon : null;
					return true;
				case ReferenceButtonType.Equipment:
					if (!Character.TryGet(out IEquipmentController equipmentController) ||
						!equipmentController.TryGetItem((int)binding.ReferenceID, out Item equippedItem))
					{
						return false;
					}
					sprite = equippedItem.Template != null ? equippedItem.Template.Icon : null;
					return true;
				case ReferenceButtonType.Ability:
					if (!Character.TryGet(out IAbilityController abilityController) ||
						!abilityController.KnownAbilities.TryGetValue(binding.ReferenceID, out Ability ability))
					{
						return false;
					}
					sprite = ability.Template != null ? ability.Template.Icon : null;
					return true;
				default:
					// Bank and anything else has no meaning on the bar.
					return false;
			}
		}

		/// <summary>
		/// Polls the Input System and drives press / release for every hotkey slot.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Two bugs lived here. First, the poll tested <c>isPressed</c> — a LEVEL, not an edge —
		/// and activated on every frame the key was down, so holding a hotkey re-queued the
		/// ability at frame rate. Second, nothing on the client ever called
		/// <see cref="IAbilityController.Release"/> and the bar hard-coded <c>isHeld: true</c>, so
		/// a charged ability could never fire (the controller waits for the held flag to clear,
		/// then cancels it at the hold cap) and a channel could never be stopped early.
		/// </para>
		/// <para>
		/// <c>isHeld</c> now comes from <see cref="IAbilityController.RequiresHeld"/> — the method
		/// the controller provides for exactly this and which the AI path already used — and the
		/// release edge calls <c>Release()</c>.
		/// </para>
		/// </remarks>
		private void UpdateInput()
		{
			if (slots.Count < 1)
			{
				return;
			}

			bool inputBlocked = Character == null ||
				PlayerInputController.MouseMode ||
				UIManager.InputControlHasFocus();

			for (int i = 0; i < slots.Count; ++i)
			{
				HotkeySlot slot = slots[i];
				bool pressed = !inputBlocked && IsHotkeyPressed(i);

				if (pressed == slot.IsPressed)
				{
					continue;
				}

				slot.IsPressed = pressed;

				if (pressed)
				{
					ActivateSlot(slot);
				}
				else
				{
					ReleaseSlot(slot);
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
				if (dragObject.Type != ReferenceButtonType.Bank &&
					dragObject.Type != ReferenceButtonType.None)
				{
					EnsureBindings();
					bindings[slot.Index].Type = dragObject.Type;
					bindings[slot.Index].ReferenceID = dragObject.ReferenceID;

					if (dragObject.IconSprite != null)
					{
						ApplySlotSprite(slot, dragObject.IconSprite);
					}
					else
					{
						bindingsDirty = true;
					}

					Client.Broadcast(new HotkeySetBroadcast()
					{
						HotkeyData = new HotkeyData()
						{
							Type = (byte)dragObject.Type,
							Slot = slot.Index,
							ReferenceID = dragObject.ReferenceID,
						}
					}, Channel.Reliable);
				}

				dragObject.Clear();
			}
			else
			{
				/* A click activates and immediately releases: there is no pointer-up path that
				 * could deliver the release, and leaving a charged ability holding forever is
				 * what the hold cap exists to clean up — badly. */
				ActivateSlot(slot);
				ReleaseSlot(slot);
			}
		}

		/// <summary>
		/// Removes the slot's assignment, seeds the drag overlay, and broadcasts the change.
		/// </summary>
		/// <param name="slot">The slot that was right-clicked.</param>
		private void HandleSlotRightClick(HotkeySlot slot)
		{
			EnsureBindings();

			if (bindings[slot.Index].ReferenceID == ReferenceButton.NULL_REFERENCE_ID)
			{
				return;
			}

			if (UIManager.TryGetTK(DRAG_OBJECT_NAME, out UITKDragObject dragObject))
			{
				Sprite sprite = slot.AppliedSprite;
				dragObject.SetReference(sprite, bindings[slot.Index].ReferenceID, bindings[slot.Index].Type);

				ClearBinding(slot.Index, broadcast: true);
				ApplySlotSprite(slot, null);
				ApplyCooldownFraction(slot, 0.0f);
			}
		}

		/// <summary>
		/// Empties a binding, optionally telling the server about it.
		/// </summary>
		/// <param name="index">The hotkey slot index.</param>
		/// <param name="broadcast">True to send the clear to the server.</param>
		private void ClearBinding(int index, bool broadcast)
		{
			EnsureBindings();

			bindings[index].Type = ReferenceButtonType.None;
			bindings[index].ReferenceID = ReferenceButton.NULL_REFERENCE_ID;

			if (broadcast && Client != null)
			{
				Client.Broadcast(new HotkeySetBroadcast()
				{
					HotkeyData = new HotkeyData()
					{
						Type = 0,
						Slot = index,
						ReferenceID = HotkeyData.UnsetReferenceID,
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
			EnsureBindings();

			if (Character == null || bindings[slot.Index].ReferenceID < 0)
			{
				return;
			}

			if (!UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				return;
			}

			long referenceID = bindings[slot.Index].ReferenceID;
			switch (bindings[slot.Index].Type)
			{
				case ReferenceButtonType.Inventory:
					if (Character.TryGet(out IInventoryController inventoryController) &&
						inventoryController.TryGetItem((int)referenceID, out Item inventoryItem))
					{
						tooltip.Open(inventoryItem.Tooltip(), slot.Root);
					}
					break;
				case ReferenceButtonType.Equipment:
					if (Character.TryGet(out IEquipmentController equipmentController) &&
						equipmentController.TryGetItem((int)referenceID, out Item equippedItem))
					{
						tooltip.Open(equippedItem.Tooltip(), slot.Root);
					}
					break;
				case ReferenceButtonType.Ability:
					if (Character.TryGet(out IAbilityController abilityController) &&
						abilityController.KnownAbilities.TryGetValue(referenceID, out Ability ability))
					{
						tooltip.Open(ability.Tooltip(), slot.Root);
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
			EnsureBindings();

			if (Character == null)
			{
				return;
			}

			long referenceID = bindings[slot.Index].ReferenceID;
			switch (bindings[slot.Index].Type)
			{
				case ReferenceButtonType.Inventory:
					if (Character.TryGet(out IInventoryController inventoryController))
					{
						inventoryController.Activate((int)referenceID);
					}
					break;
				case ReferenceButtonType.Equipment:
					if (Character.TryGet(out IEquipmentController equipmentController))
					{
						equipmentController.Activate((int)referenceID);
					}
					break;
				case ReferenceButtonType.Ability:
					if (!UIManager.ControlHasFocus() &&
						Character.TryGet(out IAbilityController abilityController))
					{
						/* The controller knows whether this ability is charged or channeled;
						 * the bar does not, and hard-coding true meant a charged ability sat at
						 * full charge until the hold cap cancelled it. */
						bool isHeld = abilityController.RequiresHeld(referenceID);
						abilityController.Activate(referenceID, isHeld);
						slot.AwaitingRelease = isHeld;
					}
					break;
				default:
					break;
			}
		}

		/// <summary>
		/// Releases a held activation started from this slot.
		/// </summary>
		/// <param name="slot">The slot whose input was released.</param>
		/// <remarks>
		/// For a charged ability this is what fires it; for a channel it is what stops it early.
		/// Guarded by <see cref="HotkeySlot.AwaitingRelease"/> so releasing a slot that started a
		/// non-held ability cannot clear the held flag of a DIFFERENT ability that has since
		/// started casting.
		/// </remarks>
		private void ReleaseSlot(HotkeySlot slot)
		{
			if (!slot.AwaitingRelease)
			{
				return;
			}

			slot.AwaitingRelease = false;

			if (Character != null &&
				Character.TryGet(out IAbilityController abilityController))
			{
				abilityController.Release();
			}
		}

		/// <summary>
		/// Writes a slot's icon sprite, skipping the write when it has not changed.
		/// </summary>
		/// <param name="slot">The slot to update.</param>
		/// <param name="sprite">The sprite to display, or null to clear.</param>
		private void ApplySlotSprite(HotkeySlot slot, Sprite sprite)
		{
			if (ReferenceEquals(slot.AppliedSprite, sprite))
			{
				return;
			}

			slot.AppliedSprite = sprite;

			if (slot.Icon == null)
			{
				return;
			}

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
		/// Writes a slot's cooldown sweep height, skipping the write when it has not changed.
		/// </summary>
		/// <param name="slot">The slot to update.</param>
		/// <param name="fraction">The remaining cooldown fraction (0-1).</param>
		private void ApplyCooldownFraction(HotkeySlot slot, float fraction)
		{
			fraction = Mathf.Clamp01(fraction);

			// Quantised to whole percent: the sweep is 48px tall, so anything finer is not a
			// visible change and would repaint the element every frame for nothing.
			if (Mathf.Abs(slot.AppliedCooldownFraction - fraction) < 0.005f)
			{
				return;
			}

			slot.AppliedCooldownFraction = fraction;

			if (slot.Cooldown != null)
			{
				slot.Cooldown.style.height = Length.Percent(fraction * 100.0f);
			}
		}

		/// <summary>
		/// Resolves the cooldown key a binding is tracked under, if it has one.
		/// </summary>
		/// <param name="index">The hotkey slot index.</param>
		/// <param name="key">The cooldown key.</param>
		/// <returns>True when this binding can be on cooldown.</returns>
		/// <remarks>
		/// Slot type matters and the old code ignored it: an ability's cooldown is keyed by the
		/// ability's INSTANCE ID while an inventory binding's reference is a SLOT INDEX, so
		/// matching a raw ID against every slot let a cooldown on ability 3 paint the sweep over
		/// whatever was sitting in inventory slot 3. Consumable cooldowns are keyed by the
		/// consumable's TEMPLATE ID, which is what an item binding has to resolve to.
		/// </remarks>
		private bool TryGetSlotCooldownKey(int index, out long key)
		{
			key = 0;

			if (Character == null)
			{
				return false;
			}

			switch (bindings[index].Type)
			{
				case ReferenceButtonType.Ability:
					key = bindings[index].ReferenceID;
					return key != ReferenceButton.NULL_REFERENCE_ID;
				case ReferenceButtonType.Inventory:
					if (Character.TryGet(out IInventoryController inventoryController) &&
						inventoryController.TryGetItem((int)bindings[index].ReferenceID, out Item inventoryItem) &&
						inventoryItem.Template != null)
					{
						key = inventoryItem.Template.ID;
						return true;
					}
					return false;
				case ReferenceButtonType.Equipment:
					if (Character.TryGet(out IEquipmentController equipmentController) &&
						equipmentController.TryGetItem((int)bindings[index].ReferenceID, out Item equippedItem) &&
						equippedItem.Template != null)
					{
						key = equippedItem.Template.ID;
						return true;
					}
					return false;
				default:
					return false;
			}
		}

		/// <summary>
		/// Flags a cooldown sweep refresh for whichever slot references the cooled-down entry.
		/// </summary>
		/// <param name="referenceID">The reference ID on cooldown.</param>
		/// <param name="cooldown">The cooldown instance.</param>
		private void CooldownController_OnAddOrUpdateCooldown(long referenceID, CooldownInstance cooldown)
		{
			anyCooldownActive = true;
			UpdateCooldownSweeps();
		}

		/// <summary>
		/// Clears the cooldown sweep for whichever slot references the finished cooldown.
		/// </summary>
		/// <param name="referenceID">The reference ID whose cooldown ended.</param>
		private void CooldownController_OnRemoveCooldown(long referenceID)
		{
			UpdateCooldownSweeps();
		}

		/// <summary>
		/// Recomputes every slot's cooldown sweep from the live cooldown controller.
		/// </summary>
		/// <remarks>
		/// Driven per frame rather than from the cooldown events alone. <c>OnUpdateCooldown</c>
		/// only fires when a cooldown is RE-ADDED, so the previous event-only sweep was binary —
		/// it snapped to the starting fraction and stayed there until removal, never animating
		/// down. The loop is skipped entirely once nothing is on cooldown.
		/// </remarks>
		private void UpdateCooldownSweeps()
		{
			if (!anyCooldownActive || Character == null || slots.Count < 1)
			{
				return;
			}

			if (!Character.TryGet(out ICooldownController cooldownController))
			{
				anyCooldownActive = false;
				return;
			}

			uint currentTick = GetCurrentCooldownTick();
			bool stillActive = false;

			for (int i = 0; i < slots.Count && i < bindings.Length; ++i)
			{
				float fraction = 0.0f;

				if (TryGetSlotCooldownKey(i, out long key) &&
					cooldownController.TryGetCooldown(key, currentTick, out float remaining) &&
					remaining > 0.0f)
				{
					float total = ResolveCooldownTotal(cooldownController, key, remaining);
					fraction = total > 0.0f ? remaining / total : 0.0f;
					stillActive = true;
				}

				ApplyCooldownFraction(slots[i], fraction);
			}

			anyCooldownActive = stillActive;
		}

		/// <summary>
		/// Resolves the total duration a cooldown started from, falling back to the remaining
		/// time when the controller cannot report it.
		/// </summary>
		/// <param name="cooldownController">The character's cooldown controller.</param>
		/// <param name="key">The cooldown key.</param>
		/// <param name="remaining">The remaining seconds.</param>
		/// <returns>The total cooldown duration in seconds.</returns>
		private static float ResolveCooldownTotal(ICooldownController cooldownController, long key, float remaining)
		{
			if (cooldownController.TryGetCooldownInstance(key, out CooldownInstance instance) &&
				instance.TotalTime > 0.0f)
			{
				return instance.TotalTime;
			}
			return remaining;
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
			if (PlayerInputController.Controls == null)
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
					return PlayerInputController.Controls.Player.Hotkey1.IsPressed();
				case 3:
					return PlayerInputController.Controls.Player.Hotkey2.IsPressed();
				case 4:
					return PlayerInputController.Controls.Player.Hotkey3.IsPressed();
				case 5:
					return PlayerInputController.Controls.Player.Hotkey4.IsPressed();
				case 6:
					return PlayerInputController.Controls.Player.Hotkey5.IsPressed();
				case 7:
					return PlayerInputController.Controls.Player.Hotkey6.IsPressed();
				case 8:
					return PlayerInputController.Controls.Player.Hotkey7.IsPressed();
				case 9:
					return PlayerInputController.Controls.Player.Hotkey8.IsPressed();
				case 10:
					return PlayerInputController.Controls.Player.Hotkey9.IsPressed();
				case 11:
					return PlayerInputController.Controls.Player.Hotkey0.IsPressed();
				default:
					return false;
			}
		}
	}
}
