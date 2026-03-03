using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that increases the hit count of an ability object, allowing it to persist through additional hits (pierce).
	/// Each execution adds the computed pierce count to the ability object's remaining hit count,
	/// effectively letting the projectile pass through targets instead of being destroyed on impact.
	/// </summary>
	[Serializable]
	public sealed class AbilityPierceHitAction : BaseAction
	{
		/// <summary>
		/// The value provider that determines the number of additional hits to grant the ability object per execution.
		/// </summary>
		[Tooltip("The value provider that determines the number of additional hits to grant the ability object.")]
		[SerializeReference, SubclassSelector]
		public IIntValueProvider PierceCountValue;

		/// <summary>
		/// Executes the pierce action, increasing the ability object's remaining hit count.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">The event data containing ability collision information.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (PierceCountValue == null)
			{
				Log.Warning("AbilityPierceHitAction", "PierceCountValue provider is null.");
				return;
			}

			if (eventData.TryGet(out AbilityCollisionEventData hitEventData))
			{
				AbilityObject abilityObject = hitEventData.AbilityObject;

				if (abilityObject != null)
				{
					abilityObject.HitCount += PierceCountValue.GetValue(initiator, eventData);
				}
				else
				{
					Log.Warning("AbilityPierceHitAction", $"AbilityCollisionEventData did not contain a valid AbilityObject for initiator {initiator?.Name}.");
				}
			}
			else
			{
				Log.Warning("AbilityPierceHitAction", $"EventData does not contain AbilityCollisionEventData for initiator {initiator?.Name}.");
			}
		}
	}
}