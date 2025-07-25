using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Interrupt Action", menuName = "FishMMO/Triggers/Actions/Character/Interrupt")]
	public class InterruptAction : BaseAction
	{
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (eventData.TryGet(out CharacterHitEventData targetEventData))
			{
				if (targetEventData.Target.TryGet(out IAbilityController abilityController))
				{
					abilityController.Interrupt(initiator);
				}
			}
			else
			{
				Log.Warning("InterruptAction", "Expected CharacterTargetEventData.");
			}
		}
		public override string GetFormattedDescription()
		{
			return "Interrupts the target's current ability or action.";
		}
	}
}