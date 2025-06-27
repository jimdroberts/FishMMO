using UnityEngine;

namespace FishMMO.Shared
{
	public class HitEventData : EventData
	{
		public ICharacter Defender { get; }
		public TargetInfo HitTarget { get; }
		public AbilityObject AbilityObject { get; }
		public Collision Other { get; }

		public HitEventData(ICharacter initiator, ICharacter defender, TargetInfo hitTarget, AbilityObject abilityObject, Collision other = null)
			: base(initiator)
		{
			Defender = defender;
			HitTarget = hitTarget;
			AbilityObject = abilityObject;
			Other = other;
		}
	}
}