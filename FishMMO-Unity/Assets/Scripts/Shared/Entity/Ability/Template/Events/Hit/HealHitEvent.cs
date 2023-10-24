using UnityEngine;

public sealed class HealHitEvent : HitEvent
{
	public int Amount;

	public override int Invoke(Character attacker, Character defender, TargetInfo hitTarget, GameObject abilityObject)
	{
		if (defender != null && defender.DamageController != null)
		{
			defender.DamageController.Heal(attacker, Amount);
		}
		return 1;
	}
}