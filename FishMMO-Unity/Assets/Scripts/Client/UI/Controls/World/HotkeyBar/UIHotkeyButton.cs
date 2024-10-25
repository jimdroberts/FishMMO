using FishMMO.Shared;
using FishNet.Transporting;
using UnityEngine.UI;

namespace FishMMO.Client
{
	public class UIHotkeyButton : UIReferenceButton
	{
		public int HotkeySlot;
		public Slider CooldownMask;
		public string KeyMap = "";

		protected override void Awake()
		{
			ICooldownController.OnAddCooldown += CooldownController_OnAddCooldown;
			ICooldownController.OnUpdateCooldown += CooldownController_OnUpdateCooldown;
			ICooldownController.OnRemoveCooldown += CooldownController_OnRemoveCooldown;
		}

		protected override void OnDestroy()
		{
			ICooldownController.OnAddCooldown -= CooldownController_OnAddCooldown;
			ICooldownController.OnUpdateCooldown -= CooldownController_OnUpdateCooldown;
			ICooldownController.OnRemoveCooldown -= CooldownController_OnRemoveCooldown;
		}

		private void CooldownController_OnAddCooldown(long referenceID, CooldownInstance cooldown)
		{
			if (referenceID != ReferenceID ||
				CooldownMask == null)
			{
				return;
			}

			if (cooldown.RemainingTime > 0 &&
				cooldown.TotalTime > 0)
			{
				CooldownMask.value = cooldown.RemainingTime / cooldown.TotalTime;
			}
		}

		private void CooldownController_OnUpdateCooldown(long referenceID, CooldownInstance cooldown)
		{
			if (referenceID != ReferenceID ||
				CooldownMask == null)
			{
				return;
			}

			if (cooldown.RemainingTime > 0 &&
				cooldown.TotalTime > 0)
			{
				CooldownMask.value = cooldown.RemainingTime / cooldown.TotalTime;
			}
		}

		private void CooldownController_OnRemoveCooldown(long referenceID)
		{
			if (referenceID != ReferenceID ||
				CooldownMask == null)
			{
				return;
			}

			CooldownMask.value = 0.0f;
		}

		public override void OnLeftClick()
		{
			if (UIManager.TryGet("UIDragObject", out UIDragObject dragObject) &&
				dragObject.Visible)
			{
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
						Type = (byte)Type,
						Slot = HotkeySlot,
						ReferenceID = ReferenceID,
					}
				}, Channel.Reliable);
			}
		}

		public void Activate()
		{
			if (Character != null && !string.IsNullOrWhiteSpace(KeyMap))
			{
				switch (Type)
				{
					case ReferenceButtonType.None:
						break;
					case ReferenceButtonType.Inventory:
						if (Character.TryGet(out IInventoryController inventoryController))
						{
							inventoryController.Activate((int)ReferenceID);
						}
						break;
					case ReferenceButtonType.Equipment:
						if (Character.TryGet(out IEquipmentController equipmentController))
						{
							equipmentController.Activate((int)ReferenceID);
						}
						break;
					case ReferenceButtonType.Bank:
						break;
					case ReferenceButtonType.Ability:
						if (Character.TryGet(out IAbilityController abilityController))
						{
							abilityController.Activate(ReferenceID, InputManager.GetKeyCode(KeyMap));
						}
						break;
					default:
						return;
				};
			}
		}

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