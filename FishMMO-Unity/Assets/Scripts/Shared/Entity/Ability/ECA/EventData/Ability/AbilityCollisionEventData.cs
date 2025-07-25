using UnityEngine;

namespace FishMMO.Shared
{
	public class AbilityCollisionEventData : CollisionEventData
	{
		public ICharacter HitCharacter { get; }
		public AbilityObject AbilityObject { get; }

		public AbilityCollisionEventData(ICharacter initiator, ICharacter hitCharacter, AbilityObject abilityObject = null, Collision collision = null)
			: base(initiator, collision)
		{
			HitCharacter = hitCharacter;
			AbilityObject = abilityObject;
		}
	}
}