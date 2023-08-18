using UnityEngine;

public sealed class InterruptHitEvent : HitEvent
{
	public override int Invoke(Character attacker, Character defender, TargetInfo hitTarget, GameObject abilityObject)
	{
		if (defender != null && defender.AbilityController != null)
		{
			defender.AbilityController.Interrupt(attacker);
		}
		// interrupt doesn't count as a hit
		return 0;
	}
}