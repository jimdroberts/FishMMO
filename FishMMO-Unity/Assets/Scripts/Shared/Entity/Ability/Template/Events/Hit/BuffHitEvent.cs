using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Buff Hit Event", menuName = "Character/Ability/Hit Event/Buff", order = 1)]
	public sealed class BuffHitEvent : HitEvent
	{
		public int Stacks;
		public BuffTemplate BuffTemplate;

		public override int Invoke(ICharacter attacker, ICharacter defender, TargetInfo hitTarget, AbilityObject abilityObject)
		{
			if (attacker == defender ||
				attacker == null ||
				defender == null)
			{
				return 0;
			}
			if (attacker.TryGet(out IFactionController attackerFactionController) &&
				defender.TryGet(out IFactionController defenderFactionController) &&
				defender.TryGet(out IBuffController buffController) &&
				attackerFactionController.GetAllianceLevel(defenderFactionController) == FactionAllianceLevel.Ally)
			{
				buffController.Apply(BuffTemplate);
			}

			// a buff or debuff does not count as a hit so we return 0
			return 0;
		}

		public override string GetFormattedDescription()
		{
			return Description.Replace("$BUFF$", BuffTemplate.Name)
							  .Replace("$STACKS$", Stacks.ToString());
		}
	}
}