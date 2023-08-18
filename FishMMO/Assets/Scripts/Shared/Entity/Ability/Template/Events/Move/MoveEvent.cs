using UnityEngine;

public abstract class MoveEvent : AbilityEvent
{
	public abstract void Invoke(GameObject abilityObject);
}