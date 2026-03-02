using System;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that applies damage to a target character using a configurable value provider and a given damage attribute type.
	/// </summary>
	[Serializable]
	public class ApplyDamageAction : BaseAction
	{
		/// <summary>
		/// The value provider that determines the amount of damage to apply.
		/// </summary>
		[Tooltip("The value provider that determines the amount of damage to apply.")]
		[SerializeReference, SubclassSelector]
		public IIntValueProvider DamageValue;

		/// <summary>
		/// The attribute template associated with this damage type (e.g., 'Physical', 'Fire').
		/// Used to determine the element or type of damage applied.
		/// </summary>
		[Tooltip("The attribute template associated with this damage type (e.g., 'Physical', 'Fire').")]
		public DamageAttributeTemplate DamageAttributeTemplate;

		/// <summary>
		/// Applies damage to the target character using the computed value and attribute template.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">The event data containing the target information.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (DamageValue == null)
			{
				Log.Warning("DamageAction", "DamageValue provider is null.");
				return;
			}

			// Try to get the event data for a character hit. If not present, log a warning and exit.
			if (eventData.TryGet(out CharacterHitEventData targetEventData))
			{
				// Try to get the damage controller from the target. If present, apply the damage.
				if (targetEventData.Target.TryGet(out ICharacterDamageController defenderDamageController))
				{
					int amount = DamageValue.GetValue(initiator, eventData);
					defenderDamageController.Damage(initiator, amount, DamageAttributeTemplate);
					Log.Debug("DamageAction", $"Initiator '{initiator.Name}' dealt {amount} damage to target '{targetEventData.Target.Name}'.");
				}
			}
			else
			{
				Log.Warning("DamageAction", "Expected CharacterHitEventData.");
			}
		}
	}
}