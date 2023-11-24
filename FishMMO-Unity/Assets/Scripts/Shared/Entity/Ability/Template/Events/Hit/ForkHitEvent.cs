using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Fork Hit Event", menuName = "Character/Ability/Hit Event/Fork", order = 1)]
	public sealed class ForkHitEvent : HitEvent
	{
		public float Arc = 180.0f;
		public float Distance = 60.0f;

		public override int Invoke(Character attacker, Character defender, TargetInfo hitTarget, GameObject abilityObject)
		{
			// fork - redirects towards a random direction on hit
			abilityObject.transform.rotation = abilityObject.transform.forward.GetRandomConicalDirection(abilityObject.transform.transform.position, Arc, Distance);

			// fork doesn't count as a hit
			return 0;
		}

		public override string Tooltip()
		{
			return base.Tooltip().Replace("$ARC$", Arc.ToString())
								 .Replace("$DISTANCE$", Distance.ToString());
		}
	}
}