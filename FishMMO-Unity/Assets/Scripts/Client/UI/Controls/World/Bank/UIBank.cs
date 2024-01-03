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
					Destroy(bankSlots[i].gameObject);
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

		private void OnClientBankerBroadcastReceived(BankerBroadcast msg)
		{
			if (UIManager.TryGet("UIBank", out UIBank bank))
			{
				bank.Show();
			}
		}

		public override void OnPreSetCharacter()
		{
			if (Character != null)
			{
				Character.BankController.OnSlotUpdated -= OnBankSlotUpdated;
			}
		}

		public override void SetCharacter(Character character)
		{
			base.SetCharacter(character);

			if (Character == null ||
				content == null ||
				buttonPrefab == null ||
				Character.BankController == null)
			{
				return;
			}

			// destroy the old slots
			Character.BankController.OnSlotUpdated -= OnBankSlotUpdated;
			DestroySlots();

			// generate new slots
			bankSlots = new List<UIBankButton>();
			for (int i = 0; i < Character.BankController.Items.Count; ++i)
			{
				UIBankButton button = Instantiate(buttonPrefab, content);
				button.Character = Character;
				button.ReferenceID = i;
				button.AllowedType = ReferenceButtonType.Bank;
				button.Type = ReferenceButtonType.Bank;
				if (Character.BankController.TryGetItem(i, out Item item))
				{
					if (button.Icon != null)
					{
						button.Icon.sprite = item.Template.Icon;
					}
				}
				bankSlots.Add(button);
			}
			// update our buttons when the bank slots change
			Character.BankController.OnSlotUpdated += OnBankSlotUpdated;
		}

		public void OnBankSlotUpdated(ItemContainer container, Item item, int bankIndex)
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