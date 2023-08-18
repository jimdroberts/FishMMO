using UnityEngine;

public sealed class BuffHitEvent : HitEvent
{
	public int Stacks = 1;
	public BuffTemplate BuffTemplate;

	public override int Invoke(Character attacker, Character defender, TargetInfo hitTarget, GameObject abilityObject)
	{
		if (defender != null && defender.BuffController != null)
		{
			defender.BuffController.Apply(BuffTemplate);
		}

		// a buff or debuff does not count as a hit so we return 0
		return 0;
	}
}