using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New HasInventorySpaceCondition", menuName = "FishMMO/Triggers/Conditions/Inventory/Has Inventory Space", order = 1)]
	public class HasInventorySpaceCondition : BaseCondition
	{
		public int RequiredSlots = 1;

		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			ICharacter characterToCheck = initiator;
			if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
			{
				characterToCheck = charTargetEventData.Target;
			}
			if (characterToCheck == null)
			{
				Log.Warning("HasInventorySpaceCondition", "Character does not exist.");
				return false;
			}
			if (!characterToCheck.TryGet(out IInventoryController inventoryController))
			{
				Log.Warning("HasInventorySpaceCondition", "Character does not have an IInventoryController.");
				return false;
			}
			return inventoryController.FreeSlots() >= RequiredSlots;
		}

		public override string GetFormattedDescription()
		{
			return $"Requires at least {RequiredSlots} free inventory slot{(RequiredSlots == 1 ? "" : "s")}.";
		}
	}
}