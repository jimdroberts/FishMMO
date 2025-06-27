using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Apply Damage Action", menuName = "FishMMO/Actions/Apply Damage")]
	public class ApplyDamageAction : BaseAction
	{
		[Tooltip("Who should receive the damage.")]
		public ActionTarget Target = ActionTarget.Target;

		[Tooltip("The base amount of damage to apply.")]
		public int DamageAmount;

		[Tooltip("The attribute template associated with this damage type (e.g., 'Physical', 'Fire').")]
		public DamageAttributeTemplate DamageAttributeTemplate;

		public override void Execute(ICharacter initiator, EventData eventData)
		{
			// Handle damaging the Initiator if configured
			if (Target == ActionTarget.Initiator || Target == ActionTarget.Both)
			{
				if (initiator.TryGet(out ICharacterDamageController initiatorDamageController))
				{
					initiatorDamageController.Damage(initiator, DamageAmount, DamageAttributeTemplate);
					Log.Debug($"DamageAction: Initiator '{initiator.Name}' took {DamageAmount} damage.");
				}
			}

			// Handle damaging the Target if configured
			if (Target == ActionTarget.Target || Target == ActionTarget.Both)
			{
				if (eventData.TryGet(out CharacterTargetEventData targetEventData))
				{
					if (targetEventData.Target.TryGet(out ICharacterDamageController defenderDamageController))
					{
						defenderDamageController.Damage(initiator, DamageAmount, DamageAttributeTemplate);
						Log.Debug($"DamageAction: Initiator '{initiator.Name}' dealt {DamageAmount} damage to target '{targetEventData.Target.Name}'.");
					}
				}
				else
				{
					Log.Warning("DamageAction: Expected CharacterTargetEventData for target damage, but none found in EventData.");
				}
			}
		}
	}
}