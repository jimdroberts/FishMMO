using UnityEngine;

public sealed class DamageHitEvent : HitEvent
{
	public int Amount;
	public DamageAttributeTemplate DamageAttributeTemplate;

	public override int Invoke(Character attacker, Character defender, TargetInfo hitTarget, GameObject abilityObject)
	{
		if (defender != null && defender.DamageController != null)
		{
			defender.DamageController.Damage(attacker, Amount, DamageAttributeTemplate);
		}
		return 1;
	}
}