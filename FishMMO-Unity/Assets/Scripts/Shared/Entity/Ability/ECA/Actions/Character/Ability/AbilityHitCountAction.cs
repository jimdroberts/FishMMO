using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Ability Hit Count Action", menuName = "FishMMO/Triggers/Actions/Ability/Hit Count", order = 10)]
	/// <summary>
	/// Action that tracks or modifies the hit count of an ability.
	/// </summary>
	public sealed class AbilityHitCountAction : BaseAction
	{
		[Tooltip("The amount to add to the AbilityObject's HitCount. Use a positive value to increment (e.g., for piercing), and a negative value to decrement (e.g., for consuming a hit).")]
		/// <summary>
		/// The amount to add to the AbilityObject's HitCount. Use a positive value to increment (e.g., for piercing), and a negative value to decrement (e.g., for consuming a hit).
		/// </summary>
		public int Amount = 1; // Default to 1 for piercing if that's a common use case, or -1 for consuming a hit.

		/// <summary>
		/// Executes the hit count action, applying the hit count logic to the ability.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">The event data containing ability information.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (eventData.TryGet(out AbilityCollisionEventData hitEventData))
			{
				AbilityObject abilityObject = hitEventData.AbilityObject;

				if (abilityObject != null)
				{
					// Increment or Decrement the hit count based on the 'Amount'
					abilityObject.HitCount += Amount;
				}
				else
				{
					Log.Warning("HitCountAction", $"AbilityHitEventData did not contain a valid AbilityObject for initiator {initiator?.Name}.");
				}
			}
			else
			{
				Log.Warning("HitCountAction", $"EventData is not of type AbilityHitEventData for initiator {initiator?.Name}.");
			}
		}

		/// <summary>
		/// Returns a formatted description of the hit count action for UI display.
		/// </summary>
		/// <returns>A string describing the hit count.</returns>
		public override string GetFormattedDescription()
		{
			string verb = Amount > 0 ? "Increases" : "Decreases";
			return $"{verb} hit count by <color=#FFD700>{Amount}</color> on the ability object.";
		}
	}
}