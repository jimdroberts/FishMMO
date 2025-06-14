﻿using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Forward Move Event", menuName = "FishMMO/Character/Ability/Move Event/Forward Move", order = 1)]
	public sealed class ForwardMoveEvent : MoveEvent
	{
		public override void Invoke(AbilityObject abilityObject, float deltaTime)
		{
			if (abilityObject == null)
			{
				return;
			}
			abilityObject.Transform.position += abilityObject.Transform.forward * abilityObject.Ability.Speed * deltaTime;
		}
	}
}