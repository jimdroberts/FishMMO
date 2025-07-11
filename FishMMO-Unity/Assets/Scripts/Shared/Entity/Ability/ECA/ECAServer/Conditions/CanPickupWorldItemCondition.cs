using FishMMO.Shared;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Server
{
	[CreateAssetMenu(fileName = "New Can PickUp WorldItem Condition", menuName = "FishMMO/Conditions/WorldItem/Can Pick Up", order = 0)]
	public class CanPickUpWorldItemCondition : BaseCondition
	{
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			if (!eventData.TryGet(out InteractableEventData interactableEventData))
			{
				Log.Warning("CanPickUpWorldItemCondition", "Missing InteractableEventData.");
				return false;
			}

			// Cast the generic IInteractable to a specific WorldItem
			WorldItem worldItem = interactableEventData.Interactable as WorldItem;
			if (worldItem == null || worldItem.Template == null)
			{
				return false; // Not a world item or invalid item
			}

			if (worldItem.Amount < 1)
			{
				return false; // Item already gone
			}

			// Try to get the InventoryController from the event data's nested dictionary
			if (!interactableEventData.TryGet(out InventoryControllerEventData inventoryControllerEventData) ||
				inventoryControllerEventData.InventoryController == null)
			{
				Log.Warning("CanPickUpWorldItemCondition", $"Initiator {initiator?.Name} does not have an inventory controller in EventData.");
				return false;
			}

			// Additional checks like inventory space could go here
			return true;
		}
	}
}