using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Dispel Hit Event", menuName = "Character/Ability/Hit Event/Dispel", order = 1)]
	public sealed class DispelHitEvent : HitEvent
	{
		public byte AmountToRemove;
		public bool IncludeDebuffs;
		public bool IncludeBuffs;

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
				defender.TryGet(out IBuffController defenderBuffController))
			{
				FactionAllianceLevel allianceLevel = attackerFactionController.GetAllianceLevel(defenderFactionController);

				//Debug.Log($"{attacker.GameObject.name} hit {defender.GameObject.name} - Alliance: {allianceLevel}");
				
				if (allianceLevel == FactionAllianceLevel.Enemy)
				{
					for (int i = 0; i < AmountToRemove && defenderBuffController.Buffs.Count > 0; ++i)
					{
						defenderBuffController.RemoveRandom(abilityObject.RNG, IncludeBuffs, IncludeDebuffs);
					}
				}
			}
			return 1;
		}
	}
}