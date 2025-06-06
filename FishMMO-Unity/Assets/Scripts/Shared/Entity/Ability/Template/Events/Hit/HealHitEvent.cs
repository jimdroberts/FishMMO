using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Heal Hit Event", menuName = "FishMMO/Character/Ability/Hit Event/Heal", order = 1)]
	public sealed class HealHitEvent : HitEvent
	{
		public int HealAmount;

		protected override int OnInvoke(ICharacter attacker, ICharacter defender, TargetInfo hitTarget, AbilityObject abilityObject)
		{
			if (defender.TryGet(out ICharacterDamageController damageController))
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