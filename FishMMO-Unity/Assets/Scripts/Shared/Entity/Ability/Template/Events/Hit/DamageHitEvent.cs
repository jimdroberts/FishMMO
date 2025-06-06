using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Damage Hit Event", menuName = "FishMMO/Character/Ability/Hit Event/Damage", order = 1)]
	public sealed class DamageHitEvent : HitEvent
	{
		public int Damage;
		public DamageAttributeTemplate DamageAttributeTemplate;

		protected override int OnInvoke(ICharacter attacker, ICharacter defender, TargetInfo hitTarget, AbilityObject abilityObject)
		{
			if (defender.TryGet(out ICharacterDamageController defenderDamageController) &&
				!defenderDamageController.Immortal)
			{
				defenderDamageController.Damage(attacker, Damage, DamageAttributeTemplate);
			}
			return 1;
		}

		public override string GetFormattedDescription()
		{
			return Description.Replace("$DAMAGE$", "<size=125%><color=#" + DamageAttributeTemplate.DisplayColor.ToHex() + ">" + Damage + "</color></size>")
							  .Replace("$ELEMENT$", "<size=125%><color=#" + DamageAttributeTemplate.DisplayColor.ToHex() + ">" + DamageAttributeTemplate.Name + "</color></size>");
		}
	}
}