using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Heal Hit Event", menuName = "Character/Ability/Hit Event/Heal", order = 1)]
	public sealed class HealHitEvent : HitEvent
	{
		public int HealAmount;

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
				defender.TryGet(out ICharacterDamageController damageController) &&
				attackerFactionController.GetAllianceLevel(defenderFactionController) == FactionAllianceLevel.Ally)
			{
				damageController.Heal(attacker, HealAmount);
			}
			return 1;
		}

		public override string GetFormattedDescription()
		{
			return Description.Replace("$HEALAMOUNT$", "<color=#" + TinyColor.skyBlue.ToHex() + ">" + HealAmount + "</color>");
		}
	}
}