using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks if the character (initiator or event target) has a specific item equipped in a given slot.
	/// Requires an <see cref="ItemEventData"/> in the <see cref="EventData"/>, or checks by <see cref="EquippableItemTemplate"/> and slot.
	/// </summary>
	[CreateAssetMenu(fileName = "HasEquippedItemCondition", menuName = "FishMMO/Triggers/Conditions/Equipment/Has Equipped Item Condition")]
	public class HasEquippedItemCondition : BaseCondition
	{
		/// <summary>
		/// The item template to check for. Can be set in the inspector for a hardcoded condition.
		/// </summary>
		public EquippableItemTemplate ItemTemplate;

		/// <summary>
		/// Evaluates whether the character (or event target) has a specific item equipped in a given slot.
		/// </summary>
		/// <param name="initiator">The character to check, or the fallback if no event target is present.</param>
		/// <param name="eventData">Optional event data that may provide a different item or slot to check.</param>
		/// <returns>True if the item is equipped in the specified slot; otherwise, false.</returns>
		/// <remarks>
		/// This method determines the character and slot/item to check, using event data if available. It then checks if the equipment controller has the item equipped in the slot.
		/// </remarks>
		public override bool Evaluate(ICharacter initiator, EventData eventData = null)
		{
			// Determine which character to check: use the event target if available, otherwise use the initiator.
			ICharacter characterToCheck = initiator;
			if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
			{
				characterToCheck = charTargetEventData.Target;
			}
			if (characterToCheck == null)
			{
				Log.Warning("HasEquippedItemCondition", "Character is null.");
				return false;
			}
			// Ensure the character has an EquipmentController.
			if (!characterToCheck.TryGet(out EquipmentController equipmentController))
			{
				Log.Warning("HasEquippedItemCondition", "Character does not have an EquipmentController.");
				return false;
			}
			// Ensure an item template is assigned.
			if (ItemTemplate == null)
			{
				Log.Warning("HasEquippedItemCondition", "ItemTemplate is null.");
				return false;
			}
			int templateIDToCheck = ItemTemplate.ID;
			ItemSlot slotToCheck = ItemTemplate.Slot;
			// Override with event data if available
			if (eventData != null && eventData.TryGet(out ItemEventData itemEventData))
			{
				if (itemEventData.Item != null)
				{
					templateIDToCheck = itemEventData.Item.Template.ID;
					if (itemEventData.Item.IsEquippable)
					{
						slotToCheck = (itemEventData.Item.Template as EquippableItemTemplate).Slot;
					}
				}
				// Always use the slot from event data if provided
				slotToCheck = itemEventData.TargetSlot;
			}
			// Try to get the equipped item in the specified slot.
			if (equipmentController.TryGetItem((int)slotToCheck, out Item equippedItem))
			{
				if (templateIDToCheck == 0) // Just check if any item is equipped in the slot
				{
					return equippedItem != null;
				}
				else // Check for a specific item template ID
				{
					return equippedItem != null && equippedItem.Template.ID == templateIDToCheck;
				}
			}
			return false;
		}

		/// <summary>
		/// Returns a formatted description of the equipped item requirement for UI display.
		/// </summary>
		/// <returns>A string describing the required item and slot.</returns>
		public override string GetFormattedDescription()
		{
			string itemName = ItemTemplate != null ? ItemTemplate.Name : "[Unassigned Item]";
			string slotName = ItemTemplate != null ? ItemTemplate.Slot.ToString() : "[Unassigned Slot]";
			return $"Requires the character to have {itemName} equipped in slot {slotName}.";
		}
	}
}