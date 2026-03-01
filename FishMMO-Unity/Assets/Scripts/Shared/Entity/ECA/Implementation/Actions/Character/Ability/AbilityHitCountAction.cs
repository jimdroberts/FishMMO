using System;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that tracks or modifies the hit count of an ability.
	/// </summary>
	[Serializable]
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
	}
}