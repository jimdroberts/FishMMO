using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Interrupt Hit Event", menuName = "Character/Ability/Hit Event/Interrupt", order = 1)]
	public sealed class InterruptHitEvent : HitEvent
	{
		protected override int OnInvoke(ICharacter attacker, ICharacter defender, TargetInfo hitTarget, AbilityObject abilityObject)
		{
			if (defender.TryGet(out IAbilityController abilityController))
			{
				abilityController.Interrupt(attacker);
			}
			// interrupt doesn't count as a hit
			return 0;
		}
	}
}