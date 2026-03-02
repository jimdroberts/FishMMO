using System;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that forks an ability hit in a specified arc and distance.
	/// This action is typically used to create a spread or scatter effect for abilities, such as projectiles that split or fork after hitting a target.
	/// </summary>
	[Serializable]
	public class AbilityForkHitAction : BaseAction
	{
		/// <summary>
		/// The value provider that determines the arc in degrees within which the ability can fork.
		/// For example, 180 means the forked directions will be spread within a half-circle in front of the original direction.
		/// </summary>
		[Tooltip("The value provider that determines the arc in degrees for the fork spread.")]
		[SerializeReference, SubclassSelector]
		public IFloatValueProvider ArcValue;

		/// <summary>
		/// The value provider that determines the maximum distance the forked ability can reach.
		/// </summary>
		[Tooltip("The value provider that determines the maximum fork distance.")]
		[SerializeReference, SubclassSelector]
		public IFloatValueProvider DistanceValue;

		/// <summary>
		/// Executes the fork hit action, modifying the ability object's direction within the computed arc and distance.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">The event data containing ability collision information.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (ArcValue == null || DistanceValue == null)
			{
				Log.Warning("AbilityForkHitAction", "ArcValue or DistanceValue provider is null.");
				return;
			}

			if (eventData.TryGet(out AbilityCollisionEventData abilityEventData))
			{
				var abilityObject = abilityEventData.AbilityObject;
				if (abilityObject != null)
				{
					float arc = ArcValue.GetValue(initiator, eventData);
					float distance = DistanceValue.GetValue(initiator, eventData);

					abilityObject.Transform.rotation = abilityObject.Transform.forward.GetRandomConicalDirection(
						abilityObject.Transform.position, arc, distance, abilityObject.RNG);
				}
			}
		}
	}
}