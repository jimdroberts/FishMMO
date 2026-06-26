using System;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that attempts to equip an item on the initiating character.
	/// Requires an <see cref="ItemEventData"/> in the <see cref="EventData"/>.
	/// </summary>
	[Serializable]
	public class EquipItemAction : BaseAction
	{
		/// <summary>
		/// Equips the item specified in the <see cref="ItemEventData"/> onto the initiator's <see cref="IEquipmentController"/>.
		/// </summary>
		/// <param name="initiator">The character performing the equip.</param>
		/// <param name="eventData">Event data containing the <see cref="ItemEventData"/> with item and slot information.</param>
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

			if (!initiator.TryGet(out IEquipmentController equipmentController))
			{
				Log.Warning("EquipItemAction", "Initiator does not have an IEquipmentController.");
				return;
			}

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
}
