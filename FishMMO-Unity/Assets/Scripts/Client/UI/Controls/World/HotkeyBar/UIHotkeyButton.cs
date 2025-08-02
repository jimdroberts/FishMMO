using FishMMO.Shared;
using FishNet.Transporting;
using UnityEngine.UI;

namespace FishMMO.Client
{
	/// <summary>
	/// UI button for a hotkey slot, supporting cooldown display and hotkey assignment.
	/// </summary>
	public class UIHotkeyButton : UIReferenceButton
	{
		/// <summary>
		/// The hotkey slot index this button represents.
		/// </summary>
		public int HotkeySlot;

		/// <summary>
		/// The slider used to visually represent cooldown progress for this hotkey.
		/// </summary>
		public Slider CooldownMask;

		/// <summary>
		/// The key mapping string for this hotkey (e.g., "1", "Q").
		/// </summary>
		public string KeyMap = "";

		/// <summary>
		/// Unity Awake callback. Subscribes to cooldown events.
		/// </summary>
		protected override void Awake()
		{
			ICooldownController.OnAddCooldown += CooldownController_OnAddCooldown;
			ICooldownController.OnUpdateCooldown += CooldownController_OnUpdateCooldown;
			ICooldownController.OnRemoveCooldown += CooldownController_OnRemoveCooldown;
		}

		/// <summary>
		/// Unity OnDestroy callback. Unsubscribes from cooldown events.
		/// </summary>
		protected override void OnDestroy()
		{
			ICooldownController.OnAddCooldown -= CooldownController_OnAddCooldown;
			ICooldownController.OnUpdateCooldown -= CooldownController_OnUpdateCooldown;
			ICooldownController.OnRemoveCooldown -= CooldownController_OnRemoveCooldown;
		}

		/// <summary>
		/// Handles the event when a cooldown is added for a reference ID.
		/// Updates the cooldown mask value if relevant.
		/// </summary>
		/// <param name="referenceID">The reference ID for the cooldown.</param>
		/// <param name="cooldown">The cooldown instance.</param>
		private void CooldownController_OnAddCooldown(long referenceID, CooldownInstance cooldown)
		{
			if (referenceID != ReferenceID ||
				CooldownMask == null)
			{
				return;
			}

			// Only update if cooldown times are valid
			if (cooldown.RemainingTime > 0 &&
				cooldown.TotalTime > 0)
			{
				CooldownMask.value = cooldown.RemainingTime / cooldown.TotalTime;
			}
		}

		/// <summary>
		/// Handles the event when a cooldown is updated for a reference ID.
		/// Updates the cooldown mask value if relevant.
		/// </summary>
		/// <param name="referenceID">The reference ID for the cooldown.</param>
		/// <param name="cooldown">The cooldown instance.</param>
		private void CooldownController_OnUpdateCooldown(long referenceID, CooldownInstance cooldown)
		{
			if (referenceID != ReferenceID ||
				CooldownMask == null)
			{
				return;
			}

			// Only update if cooldown times are valid
			if (cooldown.RemainingTime > 0 &&
				cooldown.TotalTime > 0)
			{
				CooldownMask.value = cooldown.RemainingTime / cooldown.TotalTime;
			}
		}

		/// <summary>
		/// Handles the event when a cooldown is removed for a reference ID.
		/// Resets the cooldown mask value if relevant.
		/// </summary>
		/// <param name="referenceID">The reference ID for the cooldown.</param>
		private void CooldownController_OnRemoveCooldown(long referenceID)
		{
			if (referenceID != ReferenceID ||
				CooldownMask == null)
			{
				return;
			}

			CooldownMask.value = 0.0f;
		}

		/// <summary>
		/// Handles left mouse click events for the hotkey button.
		/// Assigns a hotkey if dragging, otherwise activates the hotkey.
		/// </summary>
		public override void OnLeftClick()
		{
			if (UIManager.TryGet("UIDragObject", out UIDragObject dragObject) &&
				dragObject.Visible)
			{
				// Only allow assignment for non-bank types
				if (dragObject.Type != ReferenceButtonType.Bank)
				{
					Type = dragObject.Type;
					ReferenceID = dragObject.ReferenceID;
					if (Icon != null)
					{
						Icon.sprite = dragObject.Icon.sprite;
					}

					// Tell the server to assign this hotkey
					Client.Broadcast(new HotkeySetBroadcast()
					{
						HotkeyData = new HotkeyData()
						{
							Type = (byte)Type,
							Slot = HotkeySlot,
							ReferenceID = ReferenceID,
						}

					}, Channel.Reliable);
				}

				// Clear the drag object no matter what
				dragObject.Clear();
			}
			else
			{
				Activate();
			}
		}

		/// <summary>
		/// Handles right mouse click events for the hotkey button.
		/// Removes the hotkey assignment and clears the button.
		/// </summary>
		public override void OnRightClick()
		{
			if (UIManager.TryGet("UIDragObject", out UIDragObject dragObject) && ReferenceID != NULL_REFERENCE_ID)
			{
				dragObject.SetReference(Icon.sprite, ReferenceID, Type);

				Clear();

				// Tell server to clear this hotkey
				Client.Broadcast(new HotkeySetBroadcast()
				{
					HotkeyData = new HotkeyData()
					{
						Type = 0,
						Slot = HotkeySlot,
						ReferenceID = UIReferenceButton.NULL_REFERENCE_ID,
					}
				}, Channel.Reliable);
			}
		}

		/// <summary>
		/// Activates the hotkey action based on its type and key mapping.
		/// </summary>
		public void Activate()
		{
			if (Character != null && !string.IsNullOrWhiteSpace(KeyMap))
			{
				switch (Type)
				{
					case ReferenceButtonType.None:
						break;
					case ReferenceButtonType.Inventory:
						// Activate inventory item
						if (Character.TryGet(out IInventoryController inventoryController))
						{
							inventoryController.Activate((int)ReferenceID);
						}
						break;
					case ReferenceButtonType.Equipment:
						// Activate equipment item
						if (Character.TryGet(out IEquipmentController equipmentController))
						{
							equipmentController.Activate((int)ReferenceID);
						}
						break;
					case ReferenceButtonType.Bank:
						break;
					case ReferenceButtonType.Ability:
						// Activate ability if UI does not have focus
						if (!UIManager.ControlHasFocus() &&
							Character.TryGet(out IAbilityController abilityController))
						{
							abilityController.Activate(ReferenceID, InputManager.GetKeyCode(KeyMap));
						}
						break;
					default:
						return;
				}
			}
		}

		/// <summary>
		/// Clears the hotkey button UI and resets the cooldown mask.
		/// </summary>
		public override void Clear()
		{
			base.Clear();

			if (CooldownMask != null)
			{
				CooldownMask.value = 0;
			}
		}
	}
}