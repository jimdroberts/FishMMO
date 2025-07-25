namespace FishMMO.Shared
{
	using UnityEngine;

	public class AbilityTickEventData : TickEventData
	{
		public AbilityObject AbilityObject;

		public AbilityTickEventData(ICharacter initiator, Transform target, float deltaTime, AbilityObject abilityObject)
			: base(initiator, target, deltaTime)
		{
			AbilityObject = abilityObject;
		}
	}
}