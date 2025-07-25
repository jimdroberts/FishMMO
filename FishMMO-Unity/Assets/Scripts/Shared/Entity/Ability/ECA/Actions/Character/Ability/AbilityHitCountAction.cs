using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Ability Hit Count Action", menuName = "FishMMO/Triggers/Actions/Ability/Hit Count", order = 10)]
	public sealed class AbilityHitCountAction : BaseAction
	{
		[Tooltip("The amount to add to the AbilityObject's HitCount. Use a positive value to increment (e.g., for piercing), and a negative value to decrement (e.g., for consuming a hit).")]
		public int Amount = 1; // Default to 1 for piercing if that's a common use case, or -1 for consuming a hit.

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