using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;
using FishNet.Transporting;

namespace FishMMO.Client
{
	public class UIHotkeyBar : UICharacterControl
	{
		/// <summary>
		/// The parent RectTransform that holds all hotkey groups.
		/// </summary>
		public RectTransform parent;

		/// <summary>
		/// Prefab used to instantiate hotkey group buttons.
		/// </summary>
		public UIHotkeyGroup buttonPrefab;

		/// <summary>
		/// List of all hotkey groups currently displayed.
		/// </summary>
		public List<UIHotkeyGroup> hotkeys = new List<UIHotkeyGroup>();

		/// <summary>
		/// Called when the hotkey bar is starting. Adds hotkey slots.
		/// </summary>
		public override void OnStarting()
		{
			AddHotkeys(Constants.Configuration.MaximumPlayerHotkeys);
		}

		/// <summary>
		/// Called when the client is set. Registers broadcast handlers for hotkey updates.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<HotkeySetBroadcast>(OnClientHotkeySetBroadcastReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<HotkeySetMultipleBroadcast>(OnClientHotkeySetMultipleBroadcastReceived);
		}

		/// <summary>
		/// Called when the client is unset. Unregisters broadcast handlers for hotkey updates.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<HotkeySetBroadcast>(OnClientHotkeySetBroadcastReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<HotkeySetMultipleBroadcast>(OnClientHotkeySetMultipleBroadcastReceived);
		}

		/// <summary>
		/// Handles a broadcast message to set a single hotkey slot.
		/// </summary>
		/// <param name="msg">The broadcast message containing hotkey data.</param>
		/// <param name="channel">The network channel.</param>
		private void OnClientHotkeySetBroadcastReceived(HotkeySetBroadcast msg, Channel channel)
		{
			if (msg.HotkeyData.Slot < 0 || msg.HotkeyData.Slot > hotkeys.Count)
			{
				return;
			}
			UIHotkeyGroup group = hotkeys[msg.HotkeyData.Slot];
			if (group.Button != null)
			{
				if (msg.HotkeyData.Type == 0)
				{
					group.Button.Clear();
				}
				else
				{
					group.Button.Type = (ReferenceButtonType)msg.HotkeyData.Type;
					group.Button.HotkeySlot = msg.HotkeyData.Slot;
					group.Button.ReferenceID = msg.HotkeyData.ReferenceID;
				}
			}
		}

		/// <summary>
		/// Handles a broadcast message to set multiple hotkey slots.
		/// </summary>
		/// <param name="msg">The broadcast message containing multiple hotkey data.</param>
		/// <param name="channel">The network channel.</param>
		private void OnClientHotkeySetMultipleBroadcastReceived(HotkeySetMultipleBroadcast msg, Channel channel)
		{
			foreach (HotkeySetBroadcast subMsg in msg.Hotkeys)
			{
				OnClientHotkeySetBroadcastReceived(subMsg, channel);
			}
		}

		/// <summary>
		/// Called after setting the character reference. Assigns character to all hotkey buttons.
		/// </summary>
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

		/// <summary>
		/// Unity Update callback. Validates hotkeys and processes input.
		/// </summary>
		void Update()
		{
			ValidateHotkeys();
			UpdateInput();
		}

		/// <summary>
		/// Get our hotkey virtual key code. Offset by 1.
		/// </summary>
		/// <summary>
		/// Gets the key mapping string for a hotkey index.
		/// </summary>
		/// <param name="hotkeyIndex">The hotkey index.</param>
		/// <returns>The key mapping string.</returns>
		public static string GetHotkeyIndexKeyMap(int hotkeyIndex)
		{
			switch (hotkeyIndex)
			{
				case 0:
					return "Left Mouse";
				case 1:
					return "Right Mouse";
				case 2:
					return "Hotkey 1";
				case 3:
					return "Hotkey 2";
				case 4:
					return "Hotkey 3";
				case 5:
					return "Hotkey 4";
				case 6:
					return "Hotkey 5";
				case 7:
					return "Hotkey 6";
				case 8:
					return "Hotkey 7";
				case 9:
					return "Hotkey 8";
				case 10:
					return "Hotkey 9";
				case 11:
					return "Hotkey 0";
				default:
					return "";
			}
		}

		/// <summary>
		/// Validates all the hotkeys. If an item in your inventory/equipment moves while it's on a hotkey slot it will remove the hotkey.
		/// </summary>
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

				// Validate the hotkey based on its type
				switch (button.Type)
				{
					case ReferenceButtonType.None:
						break;
					case ReferenceButtonType.Inventory:
						// If inventory slot is empty, clear the hotkey
						if (!Character.TryGet(out IInventoryController inventoryController) ||
							inventoryController.IsSlotEmpty((int)button.ReferenceID))
						{
							button.Clear();
						}
						else
						{
							// Update icon if item changed
							if (group.Button != null &&
								group.Button.Icon != null)
							{
								Item item = inventoryController.Items[(int)button.ReferenceID];
								if (item != null &&
									item.Template != null &&
									group.Button.Icon.sprite != item.Template.Icon)
								{
									group.Button.Icon.sprite = item.Template.Icon;
								}
							}
						}
						break;
					case ReferenceButtonType.Equipment:
						// If equipment slot is empty, clear the hotkey
						if (!Character.TryGet(out IEquipmentController equipmentController) ||
							equipmentController.IsSlotEmpty((int)button.ReferenceID))
						{
							button.Clear();
						}
						else
						{
							// Update icon if item changed
							if (group.Button != null &&
								group.Button.Icon != null)
							{
								Item item = equipmentController.Items[(int)button.ReferenceID];
								if (item != null &&
									item.Template != null &&
									group.Button.Icon.sprite != item.Template.Icon)
								{
									group.Button.Icon.sprite = item.Template.Icon;
								}
							}
						}
						break;
					case ReferenceButtonType.Ability:
						// If ability is not known, clear the hotkey
						if (!Character.TryGet(out IAbilityController abilityController) ||
							!abilityController.KnownAbilities.TryGetValue(button.ReferenceID, out Ability ability))
						{
							button.Clear();
						}
						else
						{
							// Update icon if ability changed
							if (group.Button != null &&
								group.Button.Icon != null)
							{
								if (ability != null &&
									ability.Template != null &&
									group.Button.Icon.sprite != ability.Template.Icon)
								{
									group.Button.Icon.sprite = ability.Template.Icon;
								}
							}
						}
						break;
					default:
						break;
				}
			}
		}

		/// <summary>
		/// Checks for input and activates the corresponding hotkey button if pressed.
		/// </summary>
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

				// If the key is pressed, activate the hotkey
				if (InputManager.GetKey(keyMap))
				{
					group.Button.Activate();
					return;
				}
			}
		}

		/// <summary>
		/// Adds hotkey groups to the bar up to the specified amount.
		/// </summary>
		/// <param name="amount">The number of hotkey groups to add.</param>
		public void AddHotkeys(int amount)
		{
			if (parent == null || buttonPrefab == null) return;

			for (int i = 0; i < amount && i < Constants.Configuration.MaximumPlayerHotkeys; ++i)
			{
				UIHotkeyGroup group = Instantiate(buttonPrefab, parent);
				if (group.Button != null)
				{
					group.Button.HotkeySlot = i;
					group.Button.Character = Character;
					group.Button.KeyMap = GetHotkeyIndexKeyMap(i);
					group.Button.ReferenceID = UIReferenceButton.NULL_REFERENCE_ID;
					group.Button.Type = ReferenceButtonType.None;
					// Set the label text for the hotkey
					if (group.Label != null)
					{
						group.Label.text = group.Button.KeyMap.Replace("Hotkey ", "").Replace("Left Mouse", "LMB").Replace("Right Mouse", "RMB");
					}
				}
				group.gameObject.SetActive(true);
				hotkeys.Add(group);
			}
		}
	}
}