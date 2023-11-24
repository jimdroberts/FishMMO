using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Damage Hit Event", menuName = "Character/Ability/Hit Event/Damage", order = 1)]
	public sealed class DamageHitEvent : HitEvent
	{
		public int Damage;
		public DamageAttributeTemplate DamageAttributeTemplate;

		public override int Invoke(Character attacker, Character defender, TargetInfo hitTarget, GameObject abilityObject)
		{
			if (defender != null && defender.DamageController != null)
			{
				defender.DamageController.Damage(attacker, Damage, DamageAttributeTemplate);
			}
			return 1;
		}

		public override string Tooltip()
		{
			return base.Tooltip().Replace("$DAMAGE$", "<color=#" + DamageAttributeTemplate.DisplayColor.ToHex() + ">" + Damage + "</color>")
								 .Replace("$ELEMENT$", "<color=#" + DamageAttributeTemplate.DisplayColor.ToHex() + ">" + DamageAttributeTemplate.Name + "</color>");
		}
	}
}