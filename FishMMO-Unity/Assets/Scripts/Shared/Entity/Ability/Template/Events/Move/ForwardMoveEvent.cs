using UnityEngine;

namespace FishMMO.Shared
{
	public sealed class ForwardMoveEvent : MoveEvent
	{
		public override void Invoke(Ability ability, Transform abilityObject, float deltaTime)
		{
			abilityObject.position += abilityObject.forward * ability.Speed * deltaTime;
		}
	}
}