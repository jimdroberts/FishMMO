using UnityEngine;

public class AbilityHitscanEvent : AbilityEvent
{
	public override void Invoke(Ability ability, Character self, TargetInfo other, GameObject abilityObject)
	{
		if (ability == null) return;
		if (self == null) return;
		if (other.target == null)
		{

		}
		else if (other.hitPosition == null)
		{

		}
	}
}