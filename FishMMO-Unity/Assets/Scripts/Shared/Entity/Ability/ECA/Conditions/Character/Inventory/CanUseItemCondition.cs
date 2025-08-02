using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks if a character can use a specific item (i.e., possesses it in their inventory).
	/// </summary>
	[CreateAssetMenu(fileName = "New CanUseItemCondition", menuName = "FishMMO/Triggers/Conditions/Inventory/Can Use Item", order = 1)]
	public class CanUseItemCondition : BaseCondition
	{
		/// <summary>
		/// The item template required for this condition to pass.
		/// </summary>
		public BaseItemTemplate RequiredItem;

		/// <summary>
		/// Evaluates whether the character (or event target) can use the specified item.
		/// </summary>
		/// <param name="initiator">The character to check, or the fallback if no event target is present.</param>
		/// <param name="eventData">Optional event data that may provide a different character to check.</param>
		/// <returns>True if the character has the required item in their inventory; otherwise, false.</returns>
		/// <remarks>
		/// This method currently only checks for item presence in the inventory. Additional checks (e.g., cooldowns, requirements) can be added.
		/// </remarks>
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			// Determine which character to check: use the event target if available, otherwise use the initiator.
			ICharacter characterToCheck = initiator;
			if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
			{
				characterToCheck = charTargetEventData.Target;
			}
			if (characterToCheck == null)
			{
				Log.Warning("CanUseItemCondition", "Character does not exist.");
				return false;
			}
			// Ensure a required item is assigned.
			if (RequiredItem == null)
			{
				Log.Warning("CanUseItemCondition", "RequiredItem is not assigned.");
				return false;
			}
			// Check if the character has an inventory controller.
			if (!characterToCheck.TryGet(out IInventoryController inventoryController))
			{
				Log.Warning("CanUseItemCondition", "Character does not have an IInventoryController.");
				return false;
			}
			// FIXME: Add a check for item usage conditions, such as cooldowns or requirements.
			// For now, we will just check if the item exists in the inventory.
			return inventoryController.ContainsItem(RequiredItem);
		}

		/// <summary>
		/// Returns a formatted description of the item usage requirement for UI display.
		/// </summary>
		/// <returns>A string describing the required item.</returns>
		public override string GetFormattedDescription()
		{
			string itemName = RequiredItem != null ? RequiredItem.Name : "[Unassigned Item]";
			return $"Requires the ability to use item: {itemName}.";
		}
	}
}
