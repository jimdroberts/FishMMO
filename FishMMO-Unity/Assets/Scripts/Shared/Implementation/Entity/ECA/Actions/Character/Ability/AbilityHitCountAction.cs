using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that tracks or modifies the hit count of an ability.
	/// </summary>
	[Serializable]
	public sealed class AbilityHitCountAction : BaseAction
	{
		/// <summary>
		/// The value provider that determines the amount to add to the AbilityObject's HitCount.
		/// Use a provider returning a positive value to increment (e.g., for piercing), and a negative value to decrement (e.g., for consuming a hit).
		/// </summary>
		[Tooltip("The value provider that determines the amount to add to the AbilityObject's HitCount.")]
		[SerializeReference, SubclassSelector]
		public IIntValueProvider AmountValue;

		/// <summary>
		/// Executes the hit count action, applying the hit count logic to the ability.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">The event data containing ability information.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (AmountValue == null)
			{
				Log.Warning("AbilityHitCountAction", "AmountValue provider is null.");
				return;
			}

			if (eventData.TryGet(out AbilityCollisionEventData hitEventData))
			{
				AbilityObject abilityObject = hitEventData.AbilityObject;

				if (abilityObject != null)
				{
					abilityObject.HitCount += AmountValue.GetValue(initiator, eventData);
				}
				else
				{
					Log.Warning("AbilityHitCountAction", $"AbilityCollisionEventData did not contain a valid AbilityObject for initiator {initiator?.Name}.");
				}
			}
			else
			{
				Log.Warning("AbilityHitCountAction", $"EventData does not contain AbilityCollisionEventData for initiator {initiator?.Name}.");
			}
		}
	}
}