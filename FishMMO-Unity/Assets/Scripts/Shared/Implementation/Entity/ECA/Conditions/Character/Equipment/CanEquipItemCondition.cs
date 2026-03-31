using System;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks if an item can be equipped by the character (initiator or event target).
	/// Requires an <see cref="ItemEventData"/> in the <see cref="EventData"/>.
	/// </summary>
	[Serializable]
	public class CanEquipItemCondition : BaseCondition
	{
		/// <summary>
		/// Evaluates whether the character (or event target) can equip the specified item, based on the provided event data.
		/// </summary>
		/// <param name="initiator">The character to check, or the fallback if no event target is present.</param>
		/// <param name="eventData">Event data containing item and slot information.</param>
		/// <returns>True if the item can be equipped; otherwise, false.</returns>
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			ICharacter characterToCheck = ResolveTarget(initiator, eventData);

			if (characterToCheck == null || eventData == null)
			{
				Log.Warning("CanEquipItemCondition", "Character or EventData is null.");
				return false;
			}

			if (!eventData.TryGet(out ItemEventData itemEventData))
			{
				Log.Warning("CanEquipItemCondition", "EventData does not contain ItemEventData.");
				return false;
			}

			Item itemToEquip = itemEventData.Item;
			IItemContainer sourceContainer = itemEventData.SourceContainer;
			ItemSlot targetSlot = itemEventData.TargetSlot;

			if (itemToEquip == null)
			{
				Log.Warning("CanEquipItemCondition", "Item to equip is null in ItemEventData.");
				return false;
			}

			if (!itemToEquip.IsEquippable)
			{
				return false;
			}

			EquippableItemTemplate equippableTemplate = itemToEquip.Template as EquippableItemTemplate;
			if (equippableTemplate == null)
			{
				Log.Warning("CanEquipItemCondition", $"Item {itemToEquip.Template.name} does not have an EquippableItemTemplate.");
				return false;
			}

			if (targetSlot != equippableTemplate.Slot)
			{
				return false;
			}

			if (!characterToCheck.TryGet(out IEquipmentController equipmentController))
			{
				Log.Warning("CanEquipItemCondition", "Character does not have an IEquipmentController.");
				return false;
			}

			if (!equipmentController.CanManipulate())
			{
				return false;
			}

			if (equipmentController.TryGetItem((byte)equippableTemplate.Slot, out Item existingItemInSlot))
			{
				if (existingItemInSlot.ID == itemToEquip.ID && existingItemInSlot.Template.ID == itemToEquip.Template.ID)
				{
					return false;
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

			return true;
		}

	}
}