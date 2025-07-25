using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Apply Heal Action", menuName = "FishMMO/Triggers/Actions/Character/Apply Heal")]
	public class ApplyHealAction : BaseAction
	{
		[Tooltip("The amount of health to restore.")]
		public int HealAmount;

		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (eventData.TryGet(out CharacterHitEventData targetEventData))
			{
				if (targetEventData.Target.TryGet(out ICharacterDamageController defenderDamageController))
				{
					defenderDamageController.Heal(initiator, HealAmount);
					Log.Debug("HealAction", $"Initiator '{initiator.Name}' healed target '{targetEventData.Target.Name}' for {HealAmount}.");
				}
			}
			else
			{
				Log.Warning("HealAction", "Expected CharacterHitEventData.");
			}
		}
	}
}