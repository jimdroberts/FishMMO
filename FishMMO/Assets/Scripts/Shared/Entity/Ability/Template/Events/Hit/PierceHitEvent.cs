using UnityEngine;

public sealed class PierceHitEvent : HitEvent
{
	public override int Invoke(Character attacker, Character defender, TargetInfo hitTarget, GameObject abilityObject)
	{
		return 1;
	}
}