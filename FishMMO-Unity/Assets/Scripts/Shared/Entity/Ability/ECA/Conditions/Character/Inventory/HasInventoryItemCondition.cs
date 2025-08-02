using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks if a character has a required amount of each specified item in their inventory.
	/// </summary>
	[CreateAssetMenu(fileName = "New HasInventoryItemCondition", menuName = "FishMMO/Triggers/Conditions/Inventory/Has Inventory Item", order = 1)]
	public class HasInventoryItemCondition : BaseCondition
	{
		/// <summary>
		/// The list of item templates that must be present in the inventory.
		/// All items listed must be present in the required amount.
		/// </summary>
		[Tooltip("All items listed must be present in the required amount.")]
		public BaseItemTemplate[] RequiredItems;

		/// <summary>
		/// The minimum amount required for each item.
		/// </summary>
		public int RequiredAmount = 1;

		/// <summary>
		/// Evaluates whether the character (or event target) has the required amount of each specified item in their inventory.
		/// </summary>
		/// <param name="initiator">The character to check, or the fallback if no event target is present.</param>
		/// <param name="eventData">Optional event data that may provide a different character to check.</param>
		/// <returns>True if the character has the required amount of each item; otherwise, false.</returns>
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
				Log.Warning("HasInventoryItemCondition", "Character does not exist.");
				return false;
			}
			// Ensure there are required items specified.
			if (RequiredItems == null || RequiredItems.Length == 0)
			{
				Log.Warning("HasInventoryItemCondition", "No RequiredItems assigned.");
				return false;
			}
			// Check if the character has an inventory controller.
			if (!characterToCheck.TryGet(out IInventoryController inventoryController))
			{
				Log.Warning("HasInventoryItemCondition", "Character does not have an IInventoryController.");
				return false;
			}
			// Check each required item for the required amount.
			foreach (var item in RequiredItems)
			{
				if (item == null)
				{
					Log.Warning("HasInventoryItemCondition", "A RequiredItem entry is null.");
					return false;
				}
				if (inventoryController.GetItemCount(item) < RequiredAmount)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Returns a formatted description of the inventory item requirement for UI display.
		/// </summary>
		/// <returns>A string describing the required items and amounts.</returns>
		public override string GetFormattedDescription()
		{
			if (RequiredItems == null || RequiredItems.Length == 0)
				return $"Requires at least {RequiredAmount} of each required item (none specified).";
			var itemNames = string.Join(", ", System.Array.ConvertAll(RequiredItems, i => i != null ? i.Name : "[Unassigned Item]"));
			return $"Requires at least {RequiredAmount} of each: {itemNames}.";
		}
	}
}