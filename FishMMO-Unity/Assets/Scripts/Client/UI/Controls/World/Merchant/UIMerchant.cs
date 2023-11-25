using FishNet.Transporting;
using UnityEngine;
using TMPro;
using System.Collections.Generic;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIMerchant : UICharacterControl
	{
		public RectTransform ItemsParent;
		public UITooltipButton ItemPrefab;

		private List<UITooltipButton> Abilities;
		private List<UITooltipButton> AbilityEvents;
		private List<UITooltipButton> Items;

		public override void OnStarting()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
		}

		public override void OnDestroying()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState -= ClientManager_OnClientConnectionState;

			ClearSlots();
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
			Show();
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

		private void ClearSlots()
		{
			if (Items != null)
			{
				for (int i = 0; i < Items.Count; ++i)
				{
					if (Items[i] == null)
					{
						continue;
					}
					Items[i].OnRightClick = null;
					Items[i].OnLeftClick = null;
					if (Items[i].gameObject != null)
					{
						Destroy(Items[i].gameObject);
					}
				}
				Items.Clear();
			}
		}

		private void SetEventSlots(int count)
		{
			ClearSlots();

			Items = new List<UITooltipButton>();

			for (int i = 0; i < count; ++i)
			{
				UITooltipButton eventButton = Instantiate(ItemPrefab, ItemsParent);
				eventButton.Initialize(i, ItemEntry_OnLeftClick, ItemEntry_OnRightClick);
				Items.Add(eventButton);
			}
		}

		public void OnPurchase()
		{
			// craft it on the server
		}
	}
}