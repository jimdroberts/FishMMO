using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Interrupt Hit Event", menuName = "Character/Ability/Hit Event/Interrupt", order = 1)]
	public sealed class InterruptHitEvent : HitEvent
	{
		public override int Invoke(ICharacter attacker, ICharacter defender, TargetInfo hitTarget, AbilityObject abilityObject)
		{
			if (attacker == null ||
				defender == null ||
				attacker.ID == defender.ID)
			{
				return 0;
			}
			if (attacker.TryGet(out IFactionController attackerFactionController) &&
				defender.TryGet(out IFactionController defenderFactionController) &&
				defender.TryGet(out IAbilityController abilityController) &&
				attackerFactionController.GetAllianceLevel(defenderFactionController) == FactionAllianceLevel.Enemy)
			{
				abilityController.Interrupt(attacker);
			}
			// interrupt doesn't count as a hit
			return 0;
		}
	}
}