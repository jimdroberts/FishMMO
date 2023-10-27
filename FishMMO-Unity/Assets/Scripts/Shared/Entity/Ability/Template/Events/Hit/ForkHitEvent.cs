using UnityEngine;

namespace FishMMO.Shared
{
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
	}
}