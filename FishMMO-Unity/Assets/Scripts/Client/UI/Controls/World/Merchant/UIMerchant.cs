using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using FishNet.Transporting;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIMerchant : UICharacterControl
	{
		/// <summary>
		/// The parent RectTransform for merchant entry buttons.
		/// </summary>
		public RectTransform Parent;
		/// <summary>
		/// The prefab used to instantiate merchant entry buttons.
		/// </summary>
		public UITooltipButton Prefab;

		/// <summary>
		/// Button to show abilities tab.
		/// </summary>
		public Button AbilitiesButton;
		/// <summary>
		/// Button to show ability events tab.
		/// </summary>
		public Button AbilityEventsButton;
		/// <summary>
		/// Button to show items tab.
		/// </summary>
		public Button ItemsButton;

		/// <summary>
		/// List of ability entry buttons.
		/// </summary>
		private List<UITooltipButton> Abilities;
		/// <summary>
		/// List of ability event entry buttons.
		/// </summary>
		private List<UITooltipButton> AbilityEvents;
		/// <summary>
		/// List of item entry buttons.
		/// </summary>
		private List<UITooltipButton> Items;

		/// <summary>
		/// The last merchant's interactable ID.
		/// </summary>
		private long lastMerchantID = 0;
		/// <summary>
		/// The current merchant template ID.
		/// </summary>
		private int currentTemplateID = 0;
		/// <summary>
		/// The currently selected merchant tab.
		/// </summary>
		private MerchantTabType currentTab = MerchantTabType.Item;

		/// <summary>
		/// Called when the client is set. Registers merchant broadcast handler.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<MerchantBroadcast>(OnClientMerchantBroadcastReceived);
		}

		/// <summary>
		/// Called when the client is unset. Unregisters merchant broadcast handler.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<MerchantBroadcast>(OnClientMerchantBroadcastReceived);
		}

		/// <summary>
		/// Called when the merchant UI is being destroyed. Clears all entry slots.
		/// </summary>
		public override void OnDestroying()
		{
			ClearAllSlots();
		}

		/// <summary>
		/// Handles merchant broadcast messages, sets up merchant entry buttons and tabs.
		/// </summary>
		/// <param name="msg">Merchant broadcast message.</param>
		/// <param name="channel">Network channel.</param>
		private void OnClientMerchantBroadcastReceived(MerchantBroadcast msg, Channel channel)
		{
			lastMerchantID = msg.InteractableID;
			currentTemplateID = msg.TemplateID;
			MerchantTemplate template = MerchantTemplate.Get<MerchantTemplate>(currentTemplateID);
			if (template != null)
			{
				// Set up prefab lists for each tab
				SetButtonSlots(template.Abilities.Select(s => s as ITooltip).ToList(), ref Abilities, AbilityEntry_OnLeftClick, AbilityEntry_OnRightClick);
				AbilitiesButton.gameObject.SetActive((Abilities == null || Abilities.Count < 1) ? false : true);

				SetButtonSlots(template.AbilityEvents.Select(s => s as ITooltip).ToList(), ref AbilityEvents, AbilityEventEntry_OnLeftClick, AbilityEventEntry_OnRightClick);
				AbilityEventsButton.gameObject.SetActive((AbilityEvents == null || AbilityEvents.Count < 1) ? false : true);

				SetButtonSlots(template.Items.Select(s => s as ITooltip).ToList(), ref Items, ItemEntry_OnLeftClick, ItemEntry_OnRightClick);
				ItemsButton.gameObject.SetActive((Items == null || Items.Count < 1) ? false : true);

				// Show the first valid tab if any, otherwise hide
				if (AbilitiesButton.gameObject.activeSelf)
				{
					currentTab = MerchantTabType.Ability;
					ShowEntries(Abilities);
					ShowEntries(AbilityEvents, false);
					ShowEntries(Items, false);
					Show();
				}
				else if (AbilityEventsButton.gameObject.activeSelf)
				{
					currentTab = MerchantTabType.AbilityEvent;
					ShowEntries(Abilities, false);
					ShowEntries(AbilityEvents);
					ShowEntries(Items, false);
					Show();
				}
				else if (ItemsButton.gameObject.activeSelf)
				{
					currentTab = MerchantTabType.Item;
					ShowEntries(Abilities, false);
					ShowEntries(AbilityEvents, false);
					ShowEntries(Items);
					Show();
				}
				else
				{
					Hide();
				}
			}
		}

		/// <summary>
		/// Clears all merchant entry slots and resets merchant state.
		/// </summary>
		private void ClearAllSlots()
		{
			lastMerchantID = 0;
			ClearSlots(ref Abilities);
			ClearSlots(ref AbilityEvents);
			ClearSlots(ref Items);
		}

		/// <summary>
		/// Clears the specified list of merchant entry buttons and destroys their game objects.
		/// </summary>
		/// <param name="slots">Reference to the list of entry buttons.</param>
		private void ClearSlots(ref List<UITooltipButton> slots)
		{
			if (slots != null)
			{
				for (int i = 0; i < slots.Count; ++i)
				{
					if (slots[i] == null)
					{
						continue;
					}
					if (slots[i].gameObject != null)
					{
						Destroy(slots[i].gameObject);
					}
				}
				slots.Clear();
			}
		}

		/// <summary>
		/// Sets up merchant entry buttons for a given list of items, assigning click handlers.
		/// </summary>
		/// <param name="items">List of items to display.</param>
		/// <param name="slots">Reference to the list of entry buttons.</param>
		/// <param name="onLeftClick">Handler for left click.</param>
		/// <param name="onRightClick">Handler for right click.</param>
		private void SetButtonSlots(List<ITooltip> items, ref List<UITooltipButton> slots, Action<int, object[]> onLeftClick, Action<int, object[]> onRightClick)
		{
			ClearSlots(ref slots);

			if (items == null ||
				items.Count < 1)
			{
				return;
			}

			slots = new List<UITooltipButton>();

			for (int i = 0; i < items.Count; ++i)
			{
				ITooltip cachedObject = items[i];
				if (cachedObject == null)
				{
					continue;
				}

				UITooltipButton eventButton = Instantiate(Prefab, Parent);
				eventButton.Initialize(i, onLeftClick, onRightClick, cachedObject, "\r\n\r\nCtrl+Left Mouse Button to purchase.", PurchaseEventEntry_OnCtrlClick);
				slots.Add(eventButton);
			}
		}

		/// <summary>
		/// Handles tab button clicks, switching the visible merchant tab.
		/// </summary>
		/// <param name="type">The tab type as an integer.</param>
		public void Tab_OnClick(int type)
		{
			MerchantTabType tabType = (MerchantTabType)type;
			switch (tabType)
			{
				case MerchantTabType.Item:
					currentTab = MerchantTabType.Item;
					ShowEntries(Items);
					ShowEntries(Abilities, false);
					ShowEntries(AbilityEvents, false);
					break;
				case MerchantTabType.Ability:
					currentTab = MerchantTabType.Ability;
					ShowEntries(Items, false);
					ShowEntries(Abilities);
					ShowEntries(AbilityEvents, false);
					break;
				case MerchantTabType.AbilityEvent:
					currentTab = MerchantTabType.AbilityEvent;
					ShowEntries(Items, false);
					ShowEntries(Abilities, false);
					ShowEntries(AbilityEvents);
					break;
				default: return;
			}
		}

		/// <summary>
		/// Shows or hides a list of merchant entry buttons.
		/// </summary>
		/// <param name="buttons">List of entry buttons.</param>
		/// <param name="show">Whether to show or hide the buttons.</param>
		private void ShowEntries(List<UITooltipButton> buttons, bool show = true)
		{
			if (buttons == null ||
				buttons.Count < 1)
			{
				return;
			}
			foreach (UITooltipButton button in buttons)
			{
				button.gameObject.SetActive(show);
			}
		}

		/// <summary>
		/// Handles Ctrl+click purchase events for merchant entries.
		/// </summary>
		/// <param name="index">Index of the entry.</param>
		/// <param name="optionalParams">Optional parameters.</param>
		private void PurchaseEventEntry_OnCtrlClick(int index, object[] optionalParams)
		{
			switch (currentTab)
			{
				case MerchantTabType.Item:
					if (Items == null
						|| Items.Count < 1)
					{
						return;
					}
					break;
				case MerchantTabType.Ability:
					if (Abilities == null ||
						Abilities.Count < 1)
					{
						return;
					}
					break;
				case MerchantTabType.AbilityEvent:
					if (AbilityEvents == null ||
						AbilityEvents.Count < 1)
					{
						return;
					}
					break;
				case MerchantTabType.None:
				default: return;
			}

			MerchantPurchaseBroadcast message = new MerchantPurchaseBroadcast()
			{
				InteractableID = lastMerchantID,
				ID = currentTemplateID,
				Index = index,
				Type = currentTab,
			};
			Client.Broadcast(message, Channel.Reliable);
		}

		/// <summary>
		/// Handles left click events for ability entries.
		/// </summary>
		private void AbilityEntry_OnLeftClick(int index, object[] optionalParams)
		{
			if (index > -1 && index < Abilities.Count &&
				Character != null)
			{
			}
		}

		/// <summary>
		/// Handles right click events for ability entries.
		/// </summary>
		private void AbilityEntry_OnRightClick(int index, object[] optionalParams)
		{
		}

		/// <summary>
		/// Handles left click events for ability event entries.
		/// </summary>
		private void AbilityEventEntry_OnLeftClick(int index, object[] optionalParams)
		{
			if (index > -1 && index < AbilityEvents.Count &&
				Character != null)
			{
			}
		}

		/// <summary>
		/// Handles right click events for ability event entries.
		/// </summary>
		private void AbilityEventEntry_OnRightClick(int index, object[] optionalParams)
		{
		}

		/// <summary>
		/// Handles left click events for item entries.
		/// </summary>
		private void ItemEntry_OnLeftClick(int index, object[] optionalParams)
		{
			if (index > -1 && index < Items.Count &&
				Character != null)
			{
			}
		}

		/// <summary>
		/// Handles right click events for item entries.
		/// </summary>
		private void ItemEntry_OnRightClick(int index, object[] optionalParams)
		{
		}
	}
}