using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FishNet.Transporting;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIMerchant : UICharacterControl
	{
		public RectTransform Parent;
		public UITooltipButton Prefab;

		public Button AbilitiesButton;
		public Button AbilityEventsButton;
		public Button ItemsButton;

		private List<UITooltipButton> Abilities;
		private List<UITooltipButton> AbilityEvents;
		private List<UITooltipButton> Items;

		private Dictionary<int, UITooltipButton> SelectedItems = new Dictionary<int, UITooltipButton>();

		public override void OnStarting()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
		}

		public override void OnDestroying()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState -= ClientManager_OnClientConnectionState;

			ClearAllSlots();
		}

		public void ClientManager_OnClientConnectionState(ClientConnectionStateArgs args)
		{
			if (args.ConnectionState == LocalConnectionState.Started)
			{
				Client.NetworkManager.ClientManager.RegisterBroadcast<MerchantBroadcast>(OnClientMerchantBroadcastReceived);
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				Client.NetworkManager.ClientManager.UnregisterBroadcast<MerchantBroadcast>(OnClientMerchantBroadcastReceived);
			}
		}

		private void OnClientMerchantBroadcastReceived(MerchantBroadcast msg)
		{
			MerchantTemplate template = MerchantTemplate.Get<MerchantTemplate>(msg.ID);
			if (template != null)
			{
				// set up prefab lists
				SetButtonSlots(template.Abilities.Select(s => s as ITooltip).ToList(), ref Abilities, AbilityEntry_OnLeftClick, AbilityEntry_OnRightClick);
				AbilitiesButton.gameObject.SetActive((Abilities == null || Abilities.Count < 1) ? false : true);

				SetButtonSlots(template.AbilityEvents.Select(s => s as ITooltip).ToList(), ref AbilityEvents, AbilityEventEntry_OnLeftClick, AbilityEventEntry_OnRightClick);
				AbilityEventsButton.gameObject.SetActive((AbilityEvents == null || AbilityEvents.Count < 1) ? false : true);

				SetButtonSlots(template.Items.Select(s => s as ITooltip).ToList(), ref Items, ItemEntry_OnLeftClick, ItemEntry_OnRightClick);
				ItemsButton.gameObject.SetActive((Items == null || Items.Count < 1) ? false : true);

				// show the first valid tab if any otherwise hide
				if (AbilitiesButton.gameObject.activeSelf)
				{
					ShowEntries(Abilities);
					ShowEntries(AbilityEvents, false);
					ShowEntries(Items, false);
					Show();
				}
				else if (AbilityEventsButton.gameObject.activeSelf)
				{
					ShowEntries(Abilities, false);
					ShowEntries(AbilityEvents);
					ShowEntries(Items, false);
					Show();
				}
				else if (ItemsButton.gameObject.activeSelf)
				{
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

		private void ClearAllSlots()
		{
			ClearSlots(ref Abilities);
			ClearSlots(ref AbilityEvents);
			ClearSlots(ref Items);
		}

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

		private void SetButtonSlots(List<ITooltip> items, ref List<UITooltipButton> slots, Action<int> onLeftClick, Action<int> onRightClick)
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

		public void Tab_OnClick(Button button)
		{
			switch (button.name)
			{
				case "ItemButton":
					ShowEntries(Items);
					ShowEntries(Abilities, false);
					ShowEntries(AbilityEvents, false);
					break;
				case "AbilitiesButton":
					ShowEntries(Items, false);
					ShowEntries(Abilities);
					ShowEntries(AbilityEvents, false);
					break;
				case "AbilityEventsButton":
					ShowEntries(Items, false);
					ShowEntries(Abilities, false);
					ShowEntries(AbilityEvents);
					break;
				default: return;
			}
		}

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

		private void PurchaseEventEntry_OnCtrlClick(int index)
		{
			/*if (index > -1 && index < Abilities.Count &&
				Character != null)
			{
			}*/
			Debug.Log("Purchase Complete!");
		}

		private void AbilityEntry_OnLeftClick(int index)
		{
			if (index > -1 && index < Abilities.Count &&
				Character != null)
			{
			}
		}

		private void AbilityEntry_OnRightClick(int index)
		{
		}

		private void AbilityEventEntry_OnLeftClick(int index)
		{
			if (index > -1 && index < AbilityEvents.Count &&
				Character != null)
			{
			}
		}

		private void AbilityEventEntry_OnRightClick(int index)
		{
		}

		private void ItemEntry_OnLeftClick(int index)
		{
			if (index > -1 && index < Items.Count &&
				Character != null)
			{
			}
		}

		private void ItemEntry_OnRightClick(int index)
		{
		}

		public void OnPurchase()
		{
			// purchase it on the server
		}
	}
}