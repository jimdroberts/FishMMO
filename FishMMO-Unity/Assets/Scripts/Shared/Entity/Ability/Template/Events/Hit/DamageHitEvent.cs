using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Damage Hit Event", menuName = "Character/Ability/Hit Event/Damage", order = 1)]
	public sealed class DamageHitEvent : HitEvent
	{
		public int Damage;
		public DamageAttributeTemplate DamageAttributeTemplate;

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
				defender.TryGet(out ICharacterDamageController damageController))
			{
				FactionAllianceLevel allianceLevel = attackerFactionController.GetAllianceLevel(defenderFactionController);
				Debug.Log($"{attacker.GameObject.name} hit {defender.GameObject.name} - Alliance: {allianceLevel}");
				if (allianceLevel == FactionAllianceLevel.Enemy)
				{
					damageController.Damage(attacker, Damage, DamageAttributeTemplate);
				}
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