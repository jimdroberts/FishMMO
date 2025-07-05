using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Apply Heal Action", menuName = "FishMMO/Actions/Apply Heal")]
	public class ApplyHealAction : BaseAction
	{
		[Tooltip("Who should receive the healing.")]
		public ActionTarget Target = ActionTarget.Target;

		[Tooltip("The amount of health to restore.")]
		public int HealAmount;

		public override void Execute(ICharacter initiator, EventData eventData)
		{
			// Handle healing the Initiator if configured
			if (Target == ActionTarget.Initiator || Target == ActionTarget.Both)
			{
				if (initiator.TryGet(out ICharacterDamageController initiatorDamageController))
				{
					initiatorDamageController.Heal(initiator, HealAmount);
					Log.Debug("HealAction", $"Initiator '{initiator.Name}' healed for {HealAmount}.");
				}
			}

			// Handle healing the Target if configured
			if (Target == ActionTarget.Target || Target == ActionTarget.Both)
			{
				if (eventData.TryGet(out CharacterTargetEventData targetEventData))
				{
					if (targetEventData.Target.TryGet(out ICharacterDamageController defenderDamageController))
					{
						defenderDamageController.Heal(initiator, HealAmount);
						Log.Debug("HealAction", $"Initiator '{initiator.Name}' healed target '{targetEventData.Target.Name}' for {HealAmount}.");
					}
				}
				else
				{
					Log.Warning("HealAction", "Expected CharacterTargetEventData for target healing, but none found in EventData.");
				}
			}
		}
	}
}