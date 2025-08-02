using System.Collections.Generic;
using UnityEngine;
using FishNet.Transporting;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIBank : UICharacterControl
	{
		/// <summary>
		/// The parent RectTransform for bank slot UI elements.
		/// </summary>
		public RectTransform content;
		/// <summary>
		/// The prefab used to instantiate bank slot buttons.
		/// </summary>
		public UIBankButton buttonPrefab;
		/// <summary>
		/// List of all bank slot buttons currently displayed.
		/// </summary>
		public List<UIBankButton> bankSlots = null;

		/// <summary>
		/// Called when the client is set. Registers the broadcast handler for banker updates.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<BankerBroadcast>(OnClientBankerBroadcastReceived);
		}

		/// <summary>
		/// Called when the client is unset. Unregisters the broadcast handler for banker updates.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<BankerBroadcast>(OnClientBankerBroadcastReceived);
		}

		/// <summary>
		/// Called when the UI is being destroyed. Cleans up bank slot buttons.
		/// </summary>
		public override void OnDestroying()
		{
			DestroySlots();
		}

		/// <summary>
		/// Destroys all bank slot buttons in the UI and clears the list.
		/// </summary>
		private void DestroySlots()
		{
			// If there are bank slots, destroy each button and clear the list.
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

		/// <summary>
		/// Handles the broadcast message for banker updates. Shows or hides the UI based on character and bank controller presence.
		/// </summary>
		/// <param name="msg">The broadcast message containing banker info.</param>
		/// <param name="channel">The network channel.</param>
		private void OnClientBankerBroadcastReceived(BankerBroadcast msg, Channel channel)
		{
			// If no character or bank controller is present, hide the UI. Otherwise, show it.
			if (Character == null ||
				!Character.TryGet(out IBankController bankController))
			{
				Hide();
				return;
			}
			Show();
		}

		/// <summary>
		/// Called before the character is set. Unsubscribes from bank slot update events.
		/// </summary>
		public override void OnPreSetCharacter()
		{
			// Unsubscribe from bank slot update events if character and bank controller exist.
			if (Character != null &&
				Character.TryGet(out IBankController bankController))
			{
				bankController.OnSlotUpdated -= OnBankSlotUpdated;
			}
		}

		/// <summary>
		/// Called after the character is set. Initializes bank slot buttons and subscribes to slot update events.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			// Validate required components and bank controller before initializing slots.
			if (Character == null ||
				content == null ||
				buttonPrefab == null ||
				!Character.TryGet(out IBankController bankController))
			{
				return;
			}

			// Destroy the old slots and unsubscribe from previous events.
			bankController.OnSlotUpdated -= OnBankSlotUpdated;
			DestroySlots();

			// Generate new bank slot buttons for each item in the bank.
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
			// Subscribe to bank slot update events to keep buttons in sync.
			bankController.OnSlotUpdated += OnBankSlotUpdated;
		}

		/// <summary>
		/// Event handler for when a bank slot is updated. Updates the corresponding button display.
		/// </summary>
		/// <param name="container">The item container (bank).</param>
		/// <param name="item">The item in the slot.</param>
		/// <param name="bankIndex">The index of the bank slot.</param>
		public void OnBankSlotUpdated(IItemContainer container, Item item, int bankIndex)
		{
			// If there are no bank slots, nothing to update.
			if (bankSlots == null)
			{
				return;
			}

			if (!container.IsSlotEmpty(bankIndex))
			{
				// Update the button display for the item in the bank slot.
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
				// The item no longer exists in the slot; clear the button.
				bankSlots[bankIndex].Clear();
			}
		}
	}
}