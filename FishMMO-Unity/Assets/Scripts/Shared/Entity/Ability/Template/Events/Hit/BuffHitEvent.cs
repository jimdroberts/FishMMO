using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Buff Hit Event", menuName = "FishMMO/Character/Ability/Hit Event/Buff", order = 1)]
	public sealed class BuffHitEvent : HitEvent
	{
		public int Stacks;
		public BaseBuffTemplate BuffTemplate;

		protected override int OnInvoke(ICharacter attacker, ICharacter defender, TargetInfo hitTarget, AbilityObject abilityObject)
		{
			if (defender.TryGet(out IBuffController buffController))
			{
				buffController.Apply(BuffTemplate);
			}
			return 0;
		}

		public override string GetFormattedDescription()
		{
			return Description.Replace("$BUFF$", BuffTemplate.Name)
							  .Replace("$STACKS$", Stacks.ToString());
		}
	}
}