using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Interrupt Action", menuName = "FishMMO/Actions/Interrupt")]
	public class InterruptAction : BaseAction
	{
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (eventData.TryGet(out CharacterTargetEventData targetEventData))
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
	}
}