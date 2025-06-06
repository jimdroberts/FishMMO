using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Pierce Hit Event", menuName = "FishMMO/Character/Ability/Hit Event/Pierce", order = 1)]
	public sealed class PierceHitEvent : HitEvent
	{
		public int PierceCount = -1;

		protected override int OnInvoke(ICharacter attacker, ICharacter defender, TargetInfo hitTarget, AbilityObject abilityObject)
		{
			// Pierce reduces hits
			return PierceCount;
		}
	}
}