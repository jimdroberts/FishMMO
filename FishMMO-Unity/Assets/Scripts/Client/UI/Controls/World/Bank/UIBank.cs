using System.Collections.Generic;
using UnityEngine;
using FishNet.Transporting;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIBank : UICharacterControl
	{
		public RectTransform content;
		public UIBankButton buttonPrefab;
		public List<UIBankButton> bankSlots = null;

		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
		}

		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState -= ClientManager_OnClientConnectionState;
		}

		public override void OnDestroying()
		{
			DestroySlots();
		}

		private void DestroySlots()
		{
			if (bankSlots != null)
			{
				for (int i = 0; i < bankSlots.Count; ++i)
				{
					UIBankButton button = bankSlots[i];
					button.Character = null;
					Destroy(button.gameObject);
				}
				bankSlots.Clear();
			}
		}

		public void ClientManager_OnClientConnectionState(ClientConnectionStateArgs args)
		{
			if (args.ConnectionState == LocalConnectionState.Started)
			{
				Client.NetworkManager.ClientManager.RegisterBroadcast<BankerBroadcast>(OnClientBankerBroadcastReceived);
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				Client.NetworkManager.ClientManager.UnregisterBroadcast<BankerBroadcast>(OnClientBankerBroadcastReceived);
			}
		}

		private void OnClientBankerBroadcastReceived(BankerBroadcast msg, Channel channel)
		{
			if (Character == null ||
				!Character.TryGet(out IBankController bankController))
			{
				Hide();
				return;
			}
			if (UIManager.TryGet("UIBank", out UIBank bank))
			{
				bank.Show();
			}
		}

		public override void OnPreSetCharacter()
		{
			if (Character != null &&
				Character.TryGet(out IBankController bankController))
			{
				bankController.OnSlotUpdated -= OnBankSlotUpdated;
			}
		}

		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character == null ||
				content == null ||
				buttonPrefab == null ||
				!Character.TryGet(out IBankController bankController))
			{
				return;
			}

			// destroy the old slots
			bankController.OnSlotUpdated -= OnBankSlotUpdated;
			DestroySlots();

			// generate new slots
			bankSlots = new List<UIBankButton>();
			for (int i = 0; i < bankController.Items.Count; ++i)
			{
				UIBankButton button = Instantiate(buttonPrefab, content);
				button.Character = Character;
				button.ReferenceID = i;
				button.Type = ReferenceButtonType.Bank;
				if (bankController.TryGetItem(i, out Item item))
				{
					if (button.Icon != null)
					{
						button.Icon.sprite = item.Template.Icon;
					}
					if (button.AmountText != null)
					{
						button.AmountText.text = item.IsStackable ? item.Stackable.Amount.ToString() : "";
					}
				}
				button.gameObject.SetActive(true);
				bankSlots.Add(button);
			}
			// update our buttons when the bank slots change
			bankController.OnSlotUpdated += OnBankSlotUpdated;
		}

		public void OnBankSlotUpdated(IItemContainer container, Item item, int bankIndex)
		{
			if (bankSlots == null)
			{
				return;
			}

			if (!container.IsSlotEmpty(bankIndex))
			{
				// update our button display
				UIBankButton button = bankSlots[bankIndex];
				button.Type = ReferenceButtonType.Bank;
				if (button.Icon != null)
				{
					button.Icon.sprite = item.Template.Icon;
				}
				//bankSlots[i].cooldownText = character.CooldownController.IsOnCooldown();
				if (button.AmountText != null)
				{
					button.AmountText.text = item.IsStackable ? item.Stackable.Amount.ToString() : "";
				}
			}
			else
			{
				// the item no longer exists
				bankSlots[bankIndex].Clear();
			}
		}
	}
}