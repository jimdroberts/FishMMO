using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Apply Damage Action", menuName = "FishMMO/Triggers/Actions/Character/Apply Damage")]
	public class ApplyDamageAction : BaseAction
	{
		[Tooltip("The base amount of damage to apply.")]
		public int DamageAmount;

		[Tooltip("The attribute template associated with this damage type (e.g., 'Physical', 'Fire').")]
		public DamageAttributeTemplate DamageAttributeTemplate;

		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (eventData.TryGet(out CharacterHitEventData targetEventData))
			{
				if (targetEventData.Target.TryGet(out ICharacterDamageController defenderDamageController))
				{
					defenderDamageController.Damage(initiator, DamageAmount, DamageAttributeTemplate);
					Log.Debug("DamageAction", $"Initiator '{initiator.Name}' dealt {DamageAmount} damage to target '{targetEventData.Target.Name}'.");
			   }
		   }
		   else
		   {
			   Log.Warning("DamageAction", "Expected CharacterHitEventData.");
		   }
	   }
   }
}