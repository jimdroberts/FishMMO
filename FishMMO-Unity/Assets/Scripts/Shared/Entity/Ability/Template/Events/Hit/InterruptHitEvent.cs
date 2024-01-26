using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Interrupt Hit Event", menuName = "Character/Ability/Hit Event/Interrupt", order = 1)]
	public sealed class InterruptHitEvent : HitEvent
	{
		public override int Invoke(Character attacker, Character defender, TargetInfo hitTarget, GameObject abilityObject)
		{
			if (defender != null && defender.TryGet(out AbilityController abilityController))
			{
				abilityController.Interrupt(attacker);
			}
			// interrupt doesn't count as a hit
			return 0;
		}
	}
}