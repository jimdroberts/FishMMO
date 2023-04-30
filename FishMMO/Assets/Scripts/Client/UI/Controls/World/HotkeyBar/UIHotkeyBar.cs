using System.Collections.Generic;
using UnityEngine;

namespace Client
{
	public class UIHotkeyBar : UIControl
	{
		private const int MAX_HOTKEYS = 10;

		public RectTransform parent;
		public UIHotkeyButton buttonPrefab;

		public List<UIHotkeyButton> hotkeys = new List<UIHotkeyButton>();

		public override void OnStarting()
		{
			AddHotkeys(MAX_HOTKEYS);
		}

		public override void OnDestroying()
		{
		}

		void Update()
		{
			ValidateHotkeys();
			UpdateInput();
		}

		/// <summary>
		/// Hack to get our hotkey virtual key. Offset by 1.
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
			Character character = Character.localCharacter;
			if (character == null) return;

			for (int i = 0; i < hotkeys.Count; ++i)
			{
				if (hotkeys[i] == null) continue;

				switch (hotkeys[i].hotkeyType)
				{
					case HotkeyType.None:
						break;
					case HotkeyType.Any:
						break;
					case HotkeyType.Inventory:
						if (int.TryParse(hotkeys[i].referenceID, out int inventoryIndex))
						{
							if (!character.InventoryController.IsValidItem(inventoryIndex))
							{
								hotkeys[i].Clear();
							}
						}
						break;
					case HotkeyType.Equipment:
						if (int.TryParse(hotkeys[i].referenceID, out int equipmentIndex))
						{
							if (!character.EquipmentController.IsValidItem(equipmentIndex))
							{
								hotkeys[i].Clear();
							}
						}
						break;
					case HotkeyType.Ability:
						break;
				}
			}
		}

		private void UpdateInput()
		{
			Character character = Character.localCharacter;
			if (character == null || hotkeys == null || hotkeys.Count < 1)
				return;

			for (int i = 0; i < hotkeys.Count; ++i)
			{
				string keyMap = GetHotkeyIndexKeyMap(i);
				if (string.IsNullOrWhiteSpace(keyMap)) return;

				if (hotkeys[i] != null && InputManager.GetKeyDown(keyMap))
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
				button.index = i;
				button.keyMap = GetHotkeyIndexKeyMap(i);
				button.referenceID = "";
				button.allowedHotkeyType = HotkeyType.Any;
				button.hotkeyType = HotkeyType.None;
				hotkeys.Add(button);
			}
		}
	}
}