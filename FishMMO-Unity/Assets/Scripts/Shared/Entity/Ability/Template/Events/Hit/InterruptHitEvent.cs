using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Interrupt Hit Event", menuName = "Character/Ability/Hit Event/Interrupt", order = 1)]
	public sealed class InterruptHitEvent : HitEvent
	{
		public override int Invoke(Character attacker, Character defender, TargetInfo hitTarget, GameObject abilityObject)
		{
			if (attacker != null &&
				defender != null &&
				attacker.TryGet(out IFactionController attackerFactionController) &&
				defender.TryGet(out IFactionController defenderFactionController) &&
				defender.TryGet(out IAbilityController abilityController) &&
				attackerFactionController.GetAllianceLevel(defenderFactionController) == FactionAllianceLevel.Ally)
			{
				abilityController.Interrupt(attacker);
			}
			// interrupt doesn't count as a hit
			return 0;
		}
	}
}