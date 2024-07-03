using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIHotkeyBar : UICharacterControl
	{
		private const int MAX_HOTKEYS = 10;

		public RectTransform parent;
		public UIHotkeyGroup buttonPrefab;

		public List<UIHotkeyGroup> hotkeys = new List<UIHotkeyGroup>();

		public override void OnStarting()
		{
			AddHotkeys(MAX_HOTKEYS);
		}

		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			foreach (UIHotkeyGroup group in hotkeys)
			{
				if (group != null && group.Button != null)
				{
					group.Button.Character = Character;
				}
			}
		}

		void Update()
		{
			ValidateHotkeys();
			UpdateInput();
		}

		/// <summary>
		/// Get our hotkey virtual key code. Offset by 1.
		/// </summary>
		public static string GetHotkeyIndexKeyMap(int hotkeyIndex)
		{
			switch (hotkeyIndex)
			{
				case 0:
					return "Hotkey 1";
				case 1:
					return "Hotkey 2";
				case 2:
					return "Hotkey 3";
				case 3:
					return "Hotkey 4";
				case 4:
					return "Hotkey 5";
				case 5:
					return "Hotkey 6";
				case 6:
					return "Hotkey 7";
				case 7:
					return "Hotkey 8";
				case 8:
					return "Hotkey 9";
				case 9:
					return "Hotkey 0";
				default:
					return "";
			}
		}

		/// <summary>
		/// Validates all the hotkeys. If an item in your inventory/equipment moves while it's on a hotkey slot it will remove the hotkey.
		/// </summary>
		private void ValidateHotkeys()
		{
			if (Character == null) return;

			for (int i = 0; i < hotkeys.Count; ++i)
			{
				UIHotkeyGroup group = hotkeys[i];
				if (group == null) continue;

				UIHotkeyButton button = group.Button;

				switch (button.Type)
				{
					case ReferenceButtonType.None:
						break;
					case ReferenceButtonType.Inventory:
						if (Character.TryGet(out IInventoryController inventoryController) &&
							inventoryController.IsSlotEmpty((int)button.ReferenceID))
						{
							button.Clear();
						}
						break;
					case ReferenceButtonType.Equipment:
						if (Character.TryGet(out IEquipmentController equipmentController) &&
							equipmentController.IsSlotEmpty((int)button.ReferenceID))
						{
							button.Clear();
						}
						break;
					case ReferenceButtonType.Ability:
						if (Character.TryGet(out IAbilityController abilityController) &&
							!abilityController.KnownAbilities.ContainsKey(button.ReferenceID))
						{
							button.Clear();
						}
						break;
					default:
						break;
				}
			}
		}

		private void UpdateInput()
		{
			if (Character == null || hotkeys == null || hotkeys.Count < 1)
				return;

			for (int i = 0; i < hotkeys.Count; ++i)
			{
				UIHotkeyGroup group = hotkeys[i];
				if (group == null || group.Button == null) continue;

				string keyMap = GetHotkeyIndexKeyMap(i);
				if (string.IsNullOrWhiteSpace(keyMap)) return;

				if (InputManager.GetKey(keyMap))
				{
					group.Button.Activate();
					return;
				}
			}
		}

		public void AddHotkeys(int amount)
		{
			if (parent == null || buttonPrefab == null) return;

			for (int i = 0; i < amount && i < MAX_HOTKEYS; ++i)
			{
				UIHotkeyGroup group = Instantiate(buttonPrefab, parent);
				if (group.Button != null)
				{
					group.Button.Character = Character;
					group.Button.KeyMap = GetHotkeyIndexKeyMap(i);
					group.Button.ReferenceID = UIReferenceButton.NULL_REFERENCE_ID;
					group.Button.Type = ReferenceButtonType.None;
				}
				hotkeys.Add(group);
			}
		}
	}
}