using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Buff Hit Event", menuName = "Character/Ability/Hit Event/Buff", order = 1)]
	public sealed class BuffHitEvent : HitEvent
	{
		public int Stacks;
		public BuffTemplate BuffTemplate;

		public override int Invoke(Character attacker, Character defender, TargetInfo hitTarget, GameObject abilityObject)
		{
			if (defender != null && defender.BuffController != null)
			{
				defender.BuffController.Apply(BuffTemplate);
			}

			// a buff or debuff does not count as a hit so we return 0
			return 0;
		}

		public override string Tooltip()
		{
			return base.Tooltip().Replace("$BUFF$", BuffTemplate.Name)
								 .Replace("$STACKS$", Stacks.ToString());
		}
	}
}