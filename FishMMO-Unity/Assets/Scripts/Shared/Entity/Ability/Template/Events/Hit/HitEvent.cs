using UnityEngine;

namespace FishMMO.Shared
{
	public abstract class HitEvent : AbilityEvent
	{
		/// <summary>
		/// Returns the number of hits the event has issued,
		/// </summary>
		public abstract int Invoke(ICharacter attacker, ICharacter defender, TargetInfo hitTarget, AbilityObject abilityObject);
	}
}