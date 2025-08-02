using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks if a character has at least a specified number of free inventory slots.
	/// </summary>
	[CreateAssetMenu(fileName = "New HasInventorySpaceCondition", menuName = "FishMMO/Triggers/Conditions/Inventory/Has Inventory Space", order = 1)]
	public class HasInventorySpaceCondition : BaseCondition
	{
		/// <summary>
		/// The minimum number of free inventory slots required for the condition to pass.
		/// </summary>
		public int RequiredSlots = 1;

		/// <summary>
		/// Evaluates whether the character (or event target) has at least the required number of free inventory slots.
		/// </summary>
		/// <param name="initiator">The character to check, or the fallback if no event target is present.</param>
		/// <param name="eventData">Optional event data that may provide a different character to check.</param>
		/// <returns>True if the character has enough free inventory slots; otherwise, false.</returns>
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
				Log.Warning("HasInventorySpaceCondition", "Character does not exist.");
				return false;
			}
			// Check if the character has an inventory controller.
			if (!characterToCheck.TryGet(out IInventoryController inventoryController))
			{
				Log.Warning("HasInventorySpaceCondition", "Character does not have an IInventoryController.");
				return false;
			}
			// Check if the inventory has the required free slots.
			return inventoryController.FreeSlots() >= RequiredSlots;
		}

		/// <summary>
		/// Returns a formatted description of the inventory space requirement for UI display.
		/// </summary>
		/// <returns>A string describing the required free inventory slots.</returns>
		public override string GetFormattedDescription()
		{
			return $"Requires at least {RequiredSlots} free inventory slot{(RequiredSlots == 1 ? "" : "s")}.";
		}
	}
}