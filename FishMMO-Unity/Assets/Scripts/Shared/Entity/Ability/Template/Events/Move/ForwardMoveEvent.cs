using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Forward Move Event", menuName = "Character/Ability/Move Event/Forward Move", order = 1)]
	public sealed class ForwardMoveEvent : MoveEvent
	{
		public override void Invoke(Ability ability, Transform abilityObject, float deltaTime)
		{
			abilityObject.position += abilityObject.forward * ability.Speed * deltaTime;
		}
	}
}