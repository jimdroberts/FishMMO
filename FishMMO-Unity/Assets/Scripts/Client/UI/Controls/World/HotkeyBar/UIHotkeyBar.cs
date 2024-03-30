using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIHotkeyBar : UICharacterControl
	{
		private const int MAX_HOTKEYS = 10;

		public RectTransform parent;
		public UIHotkeyButton buttonPrefab;

		public List<UIHotkeyButton> hotkeys = new List<UIHotkeyButton>();

		public override void OnStarting()
		{
			AddHotkeys(MAX_HOTKEYS);
		}

		public override void SetCharacter(Character character)
		{
			base.SetCharacter(character);

			foreach (UIHotkeyButton hotkey in hotkeys)
			{
				if (hotkey != null)
				{
					hotkey.Character = character;
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
				if (hotkeys[i] == null) continue;

				switch (hotkeys[i].Type)
				{
					case ReferenceButtonType.None:
						break;
					case ReferenceButtonType.Inventory:
						if (Character.TryGet(out IInventoryController inventoryController) &&
							inventoryController.IsSlotEmpty((int)hotkeys[i].ReferenceID))
						{
							hotkeys[i].Clear();
						}
						break;
					case ReferenceButtonType.Equipment:
						if (Character.TryGet(out IEquipmentController equipmentController) &&
							equipmentController.IsSlotEmpty((int)hotkeys[i].ReferenceID))
						{
							hotkeys[i].Clear();
						}
						break;
					case ReferenceButtonType.Ability:
						if (Character.TryGet(out IAbilityController abilityController) &&
							!abilityController.KnownAbilities.ContainsKey(hotkeys[i].ReferenceID))
						{
							hotkeys[i].Clear();
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
				string keyMap = GetHotkeyIndexKeyMap(i);
				if (string.IsNullOrWhiteSpace(keyMap)) return;

				if (hotkeys[i] != null && InputManager.GetKey(keyMap))
				{
					hotkeys[i].Activate();
					return;
				}
			}
		}

		public void AddHotkeys(int amount)
		{
			if (parent == null || buttonPrefab == null) return;


			for (int i = 0; i < amount && i < MAX_HOTKEYS; ++i)
			{
				UIHotkeyButton button = Instantiate(buttonPrefab, parent);
				button.Character = Character;
				button.KeyMap = GetHotkeyIndexKeyMap(i);
				button.ReferenceID = UIReferenceButton.NULL_REFERENCE_ID;
				button.Type = ReferenceButtonType.None;
				hotkeys.Add(button);
			}
		}
	}
}