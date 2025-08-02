namespace FishMMO.Shared
{
	using UnityEngine;

	/// <summary>
	/// Event data for a tick (update) event related to an ability, including the ability object being updated.
	/// </summary>
	public class AbilityTickEventData : TickEventData
	{
		/// <summary>
		/// The ability object being updated during the tick event.
		/// </summary>
		public AbilityObject AbilityObject;

		/// <summary>
		/// Initializes a new instance of the <see cref="AbilityTickEventData"/> class.
		/// </summary>
		/// <param name="initiator">The character initiating the tick event.</param>
		/// <param name="target">The transform being targeted by the tick event.</param>
		/// <param name="deltaTime">The time delta for the tick.</param>
		/// <param name="abilityObject">The ability object being updated.</param>
		public AbilityTickEventData(ICharacter initiator, Transform target, float deltaTime, AbilityObject abilityObject)
			: base(initiator, target, deltaTime)
		{
			AbilityObject = abilityObject;
		}
	}
}