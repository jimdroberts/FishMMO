using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Fork Hit Event", menuName = "Character/Ability/Hit Event/Fork", order = 1)]
	public sealed class ForkHitEvent : HitEvent
	{
		public float Arc = 180.0f;
		public float Distance = 60.0f;

		public override int Invoke(ICharacter attacker, ICharacter defender, TargetInfo hitTarget, GameObject abilityObject)
		{
			if (attacker == defender ||
				attacker == null ||
				defender == null)
			{
				return 0;
			}
			// Fork - redirects towards a random direction on hit
			abilityObject.transform.rotation = abilityObject.transform.forward.GetRandomConicalDirection(abilityObject.transform.transform.position, Arc, Distance);

			// Fork counts as a hit
			return 1;
		}

		public override string GetFormattedDescription()
		{
			return Description.Replace("$ARC$", Arc.ToString())
							  .Replace("$DISTANCE$", Distance.ToString());
		}
	}
}