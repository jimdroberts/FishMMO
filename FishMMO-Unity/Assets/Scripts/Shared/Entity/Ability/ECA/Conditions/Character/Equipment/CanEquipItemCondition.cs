using System.Collections.Generic;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks if an item can be equipped by the character (initiator or event target).
	/// Requires an <see cref="ItemEventData"/> in the <see cref="EventData"/>.
	/// </summary>
	[CreateAssetMenu(fileName = "CanEquipItemCondition", menuName = "FishMMO/Triggers/Conditions/Equipment/Can Equip Item Condition")]
	public class CanEquipItemCondition : BaseCondition
	{
		/// <summary>
		/// Evaluates whether the character (or event target) can equip the specified item, based on the provided event data.
		/// </summary>
		/// <param name="initiator">The character to check, or the fallback if no event target is present.</param>
		/// <param name="eventData">Event data containing item and slot information.</param>
		/// <returns>True if the item can be equipped; otherwise, false.</returns>
		/// <remarks>
		/// This method performs a series of checks:
		/// <list type="number">
		/// <item>Determines the character to check (event target or initiator).</item>
		/// <item>Ensures both character and event data are present.</item>
		/// <item>Extracts <see cref="ItemEventData"/> from the event data.</item>
		/// <item>Checks if the item is equippable and has a valid template.</item>
		/// <item>Verifies the target slot matches the item's required slot.</item>
		/// <item>Ensures the character has an <see cref="EquipmentController"/> and can manipulate equipment.</item>
		/// <item>Checks for slot conflicts and whether the source container can accept swapped items.</item>
		/// <item>Allows for additional requirements (e.g., level, class) to be added as needed.</item>
		/// </list>
		/// </remarks>
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			// Determine which character to check: use the event target if available, otherwise use the initiator.
			ICharacter characterToCheck = initiator;
			if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
			{
				characterToCheck = charTargetEventData.Target;
			}
			// Ensure both character and event data are present.
			if (characterToCheck == null || eventData == null)
			{
				Log.Warning("CanEquipItemCondition", "Character or EventData is null.");
				return false;
			}
			// Extract ItemEventData from the event data.
			if (!eventData.TryGet(out ItemEventData itemEventData))
			{
				Log.Warning("CanEquipItemCondition", "EventData does not contain ItemEventData.");
				return false;
			}
			Item itemToEquip = itemEventData.Item;
			IItemContainer sourceContainer = itemEventData.SourceContainer;
			ItemSlot targetSlot = itemEventData.TargetSlot;
			// Check if the item is present and equippable.
			if (itemToEquip == null)
			{
				Log.Warning("CanEquipItemCondition", "Item to equip is null in ItemEventData.");
				return false;
			}
			if (!itemToEquip.IsEquippable)
			{
				return false;
			}
			// Ensure the item has an equippable template.
			EquippableItemTemplate equippableTemplate = itemToEquip.Template as EquippableItemTemplate;
			if (equippableTemplate == null)
			{
				Log.Warning($"CanEquipItemCondition", $"Item {itemToEquip.Template.name} does not have an EquippableItemTemplate.");
				return false;
			}
			// Verify the target slot matches the item's required slot.
			if (targetSlot != equippableTemplate.Slot)
			{
				return false;
			}
			// Ensure the character has an EquipmentController and can manipulate equipment.
			if (!characterToCheck.TryGet(out EquipmentController equipmentController))
			{
				Log.Warning("CanEquipItemCondition", "Character does not have an EquipmentController.");
				return false;
			}
			if (!equipmentController.CanManipulate())
			{
				return false;
			}
			// Check for slot conflicts and whether the source container can accept swapped items.
			if (equipmentController.TryGetItem((byte)equippableTemplate.Slot, out Item existingItemInSlot))
			{
				if (existingItemInSlot.ID == itemToEquip.ID && existingItemInSlot.Template.ID == itemToEquip.Template.ID)
				{
					return false; // Already equipped
				}
				if (sourceContainer != null && !sourceContainer.CanAddItem(existingItemInSlot))
				{
					return false;
				}
			}
			else
			{
				if (sourceContainer != null && !sourceContainer.ContainsItem(itemToEquip.Template))
				{
					return false;
				}
			}
			// Additional checks can be added here, e.g., level requirements, class requirements etc.
			return true;
		}

		public override string GetFormattedDescription()
		{
			return "Requires the character to be able to equip the specified item in the specified slot.";
		}
	}

	/// <summary>
	/// Action that attempts to equip an item.
	/// Requires an ItemEventData in the EventData.
	/// </summary>
	[CreateAssetMenu(fileName = "EquipItemAction", menuName = "FishMMO/Triggers/Conditions/Equipment/Equip Item Action")]
	public class EquipItemAction : BaseAction
	{
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (initiator == null || eventData == null)
			{
				Log.Warning("EquipItemAction", "Initiator or EventData is null. Cannot execute action.");
				return;
			}

			if (!eventData.TryGet(out ItemEventData itemEventData))
			{
				Log.Warning("EquipItemAction", "EventData does not contain ItemEventData. Cannot execute action.");
				return;
			}

			Item itemToEquip = itemEventData.Item;
			int inventoryIndex = itemEventData.InventoryIndex;
			IItemContainer sourceContainer = itemEventData.SourceContainer;
			ItemSlot targetSlot = itemEventData.TargetSlot;

			if (itemToEquip == null)
			{
				Log.Warning("EquipItemAction", "Item to equip is null in ItemEventData.");
				return;
			}

			if (!initiator.TryGet(out EquipmentController equipmentController))
			{
				Log.Warning("EquipItemAction", "Initiator does not have an EquipmentController.");
				return;
			}

			// It's good practice to re-evaluate conditions here or assume the calling system already did.
			// For robustness, you might want to call the CanEquipItemCondition here, or ensure the event system
			// only triggers this action if the conditions are met.
			// If you want to be super robust:
			// var canEquipCondition = CachedScriptableObject<CanEquipItemCondition>.Get("CanEquipItemCondition");
			// if (canEquipCondition != null && !canEquipCondition.Evaluate(initiator, eventData))
			// {
			//     Log.Warning("EquipItemAction: Conditions for equipping item were not met. Aborting action.");
			//     return;
			// }

			// Ensure the target slot is correctly determined from the item if not explicitly provided
			if (itemToEquip.IsEquippable && itemToEquip.Template is EquippableItemTemplate equippableTemplate)
			{
				targetSlot = equippableTemplate.Slot;
			}

			bool success = equipmentController.Equip(itemToEquip, inventoryIndex, sourceContainer, targetSlot);

			if (success)
			{
				Log.Debug("EquipItemAction", $"Successfully equipped {itemToEquip.Template.name} to {targetSlot}.");
			}
			else
			{
				Log.Warning("EquipItemAction", $"Failed to equip {itemToEquip.Template.name} to {targetSlot}.");
			}
		}
	}

	/// <summary>
	/// Action that attempts to unequip an item.
	/// Requires an ItemEventData in the EventData, or a specific slot.
	/// </summary>
	[CreateAssetMenu(fileName = "UnequipItemAction", menuName = "FishMMO/Triggers/Conditions/Equipment/Unequip Item Action")]
	public class UnequipItemAction : BaseAction
	{
		public ItemSlot SourceSlotToUnequip = ItemSlot.Head;

		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (initiator == null)
			{
				Log.Warning("UnequipItemAction", "Initiator is null. Cannot execute action.");
				return;
			}

			if (!initiator.TryGet(out EquipmentController equipmentController))
			{
				Log.Warning("UnequipItemAction", "Initiator does not have an EquipmentController.");
				return;
			}

			ItemSlot unequipSlot = SourceSlotToUnequip;
			IItemContainer targetContainer = null;

			// Override with EventData
			if (eventData != null && eventData.TryGet(out ItemEventData itemEventData))
			{
				if (itemEventData.TargetSlot != unequipSlot)
				{
					unequipSlot = itemEventData.TargetSlot;
				}
				targetContainer = itemEventData.SourceContainer; // SourceContainer in ItemEventData represents where the item is going
			}

			if (targetContainer == null)
			{
				Log.Warning("UnequipItemAction", "No target container specified to unequip item to. Aborting.");
				return;
			}

			bool success = equipmentController.Unequip(targetContainer, (byte)unequipSlot, out List<Item> modifiedItems);

			if (success)
			{
				Log.Debug("UnequipItemAction", $"Successfully unequipped item from {unequipSlot}.");
				// You might want to do something with modifiedItems here, e.g., update UI
			}
			else
			{
				Log.Warning("UnequipItemAction", $"Failed to unequip item from {unequipSlot}.");
			}
		}
	}
}