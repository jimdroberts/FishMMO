using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIInventory : UICharacterControl
	{
		public RectTransform content;
		public UIInventoryButton buttonPrefab;
		public List<UIInventoryButton> inventorySlots = null;

		public override void OnDestroying()
		{
			DestroySlots();
		}

		private void DestroySlots()
		{
			if (inventorySlots != null)
			{
				for (int i = 0; i < inventorySlots.Count; ++i)
				{
					Destroy(inventorySlots[i].gameObject);
				}
				inventorySlots.Clear();
			}
		}

		public override void OnPreSetCharacter()
		{
			if (Character != null)
			{
				Character.InventoryController.OnSlotUpdated -= OnInventorySlotUpdated;
			}
		}

		public override void SetCharacter(Character character)
		{
			base.SetCharacter(character);

			if (Character == null ||
				content == null ||
				buttonPrefab == null ||
				Character.InventoryController == null)
			{
				return;
			}

			// destroy the old slots
			Character.InventoryController.OnSlotUpdated -= OnInventorySlotUpdated;
			DestroySlots();

			// generate new slots
			inventorySlots = new List<UIInventoryButton>();
			for (int i = 0; i < Character.InventoryController.Items.Count; ++i)
			{
				UIInventoryButton button = Instantiate(buttonPrefab, content);
				button.Character = Character;
				button.ReferenceID = i;
				button.AllowedType = ReferenceButtonType.Any;
				button.Type = ReferenceButtonType.Inventory;
				if (Character.InventoryController.TryGetItem(i, out Item item))
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
				inventorySlots.Add(button);
			}
			// update our buttons when the inventory slots change
			Character.InventoryController.OnSlotUpdated += OnInventorySlotUpdated;
		}

		public void OnInventorySlotUpdated(ItemContainer container, Item item, int inventoryIndex)
		{
			if (inventorySlots == null)
			{
				return;
			}

			if (!container.IsSlotEmpty(inventoryIndex))
			{
				// update our button display
				UIInventoryButton button = inventorySlots[inventoryIndex];
				button.Type = ReferenceButtonType.Inventory;
				if (button.Icon != null)
				{
					button.Icon.sprite = item.Template.Icon;
				}
				//inventorySlots[i].cooldownText = character.CooldownController.IsOnCooldown();
				if (button.AmountText != null)
				{
					button.AmountText.text = item.IsStackable ? item.Stackable.Amount.ToString() : "";
				}
			}
			else
			{
				// the item no longer exists
				inventorySlots[inventoryIndex].Clear();
			}
		}
	}
}