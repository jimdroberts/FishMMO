using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Event data for an ability tick. The tick subject's transform is reachable via
	/// <see cref="AbilityObject"/>.<see cref="AbilityObject.Transform"/> so no separate
	/// transform field is carried here.
	/// </summary>
	public class AbilityTickEventData : EventData
	{
		/// <summary>
		/// The ability object being updated during the tick event.
		/// </summary>
		public AbilityObject AbilityObject;

		/// <summary>
		/// The time delta for the tick (update interval).
		/// </summary>
		public float DeltaTime;

		/// <summary>
		/// Initializes a new instance of the <see cref="AbilityTickEventData"/> class.
		/// </summary>
		/// <param name="initiator">The character initiating the tick event.</param>
		/// <param name="deltaTime">The time delta for the tick.</param>
		/// <param name="abilityObject">The ability object being updated.</param>
		public AbilityTickEventData(ICharacter initiator, float deltaTime, AbilityObject abilityObject)
			: base(initiator)
		{
			DeltaTime = deltaTime;
			AbilityObject = abilityObject;
		}
	}
}
