using UnityEngine;

public abstract class MoveEvent : AbilityEvent
{
	public abstract void Invoke(Ability ability, Transform abilityObject, float deltaTime);
}