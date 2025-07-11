using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Dispel Hit Event", menuName = "FishMMO/Character/Ability/Hit Event/Dispel", order = 1)]
	public sealed class DispelHitEvent : HitEvent
	{
		public byte AmountToRemove;
		public bool IncludeDebuffs;
		public bool IncludeBuffs;

		protected override int OnInvoke(ICharacter attacker, ICharacter defender, TargetInfo hitTarget, AbilityObject abilityObject)
		{
			if (defender.TryGet(out IBuffController defenderBuffController))
			{
				for (int i = 0; i < AmountToRemove && defenderBuffController.Buffs.Count > 0; ++i)
				{
					defenderBuffController.RemoveRandom(abilityObject.RNG, IncludeBuffs, IncludeDebuffs);
				}
			}
			return 1;
		}
	}
}