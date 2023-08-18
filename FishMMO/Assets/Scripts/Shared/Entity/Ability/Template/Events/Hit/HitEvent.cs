using UnityEngine;

public abstract class HitEvent : AbilityEvent
{
	public HitTargetType targetType;

	/// <summary>
	/// Returns the number of hits the event has issued,
	/// </summary>
	public abstract int Invoke(Character attacker, Character defender, TargetInfo hitTarget, GameObject abilityObject);
}