using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIInventory : UICharacterControl
	{
		/// <summary>
		/// The parent RectTransform that holds all inventory slot buttons.
		/// </summary>
		public RectTransform content;

		/// <summary>
		/// Prefab used to instantiate inventory slot buttons.
		/// </summary>
		public UIInventoryButton buttonPrefab;

		/// <summary>
		/// List of all inventory slot buttons currently displayed.
		/// </summary>
		public List<UIInventoryButton> inventorySlots = null;

		/// <summary>
		/// Called when the UIInventory is being destroyed. Cleans up slot buttons.
		/// </summary>
		public override void OnDestroying()
		{
			DestroySlots();
		}

		/// <summary>
		/// Destroys all inventory slot buttons and clears the slot list.
		/// </summary>
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

		/// <summary>
		/// Called before setting the character reference. Unsubscribes from inventory slot updates.
		/// </summary>
		public override void OnPreSetCharacter()
		{
			if (Character != null &&
				Character.TryGet(out IInventoryController inventoryController))
			{
				inventoryController.OnSlotUpdated -= OnInventorySlotUpdated;
			}
		}

		/// <summary>
		/// Called after setting the character reference. Initializes inventory slot buttons and subscribes to slot updates.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			// Validate required references and character
			if (Character == null ||
				content == null ||
				buttonPrefab == null ||
				!Character.TryGet(out IInventoryController inventoryController))
			{
				return;
			}

			// Unsubscribe from previous slot updates and destroy old slot buttons
			inventoryController.OnSlotUpdated -= OnInventorySlotUpdated;
			DestroySlots();

			// Generate new slot buttons for each inventory item
			inventorySlots = new List<UIInventoryButton>();
			for (int i = 0; i < inventoryController.Items.Count; ++i)
			{
				UIInventoryButton button = Instantiate(buttonPrefab, content);
				button.Character = Character;
				button.ReferenceID = i;
				button.Type = ReferenceButtonType.Inventory;
				// If an item exists in this slot, set its icon and amount
				if (inventoryController.TryGetItem(i, out Item item))
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
				inventorySlots.Add(button);
			}
			// Subscribe to inventory slot updates to refresh button display
			inventoryController.OnSlotUpdated += OnInventorySlotUpdated;
		}

		/// <summary>
		/// Callback for when an inventory slot is updated. Refreshes the corresponding button display.
		/// </summary>
		/// <param name="container">The item container holding the inventory.</param>
		/// <param name="item">The item in the updated slot.</param>
		/// <param name="inventoryIndex">The index of the updated inventory slot.</param>
		public void OnInventorySlotUpdated(IItemContainer container, Item item, int inventoryIndex)
		{
			if (inventorySlots == null)
			{
				return;
			}

			if (!container.IsSlotEmpty(inventoryIndex))
			{
				// Update the button display for the slot
				UIInventoryButton button = inventorySlots[inventoryIndex];
				button.Type = ReferenceButtonType.Inventory;
				if (button.Icon != null)
				{
					button.Icon.sprite = item.Template.Icon;
				}
				// If the item is stackable, show the amount
				if (button.AmountText != null)
				{
					button.AmountText.text = item.IsStackable ? item.Stackable.Amount.ToString() : "";
				}
			}
			else
			{
				// The item no longer exists, clear the button
				inventorySlots[inventoryIndex].Clear();
			}
		}
	}
}