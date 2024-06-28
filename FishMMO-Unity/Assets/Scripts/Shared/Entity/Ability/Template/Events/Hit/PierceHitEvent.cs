using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Pierce Hit Event", menuName = "Character/Ability/Hit Event/Pierce", order = 1)]
	public sealed class PierceHitEvent : HitEvent
	{
		public override int Invoke(ICharacter attacker, ICharacter defender, TargetInfo hitTarget, GameObject abilityObject)
		{
			if (attacker == defender ||
				attacker == null ||
				defender == null)
			{
				return 0;
			}
			// Pierce counts as a hit
			return 1;
		}
	}
}