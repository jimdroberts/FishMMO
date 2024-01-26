using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Heal Hit Event", menuName = "Character/Ability/Hit Event/Heal", order = 1)]
	public sealed class HealHitEvent : HitEvent
	{
		public int Heal;

		public override int Invoke(Character attacker, Character defender, TargetInfo hitTarget, GameObject abilityObject)
		{
			if (defender != null &&
				defender.TryGet(out CharacterDamageController damageController))
			{
				damageController.Heal(attacker, Heal);
			}
			return 1;
		}

		public override string GetFormattedDescription()
		{
			return Description.Replace("$HEAL$", Heal.ToString());
		}
	}
}