using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that restores health to a target character using a configurable value provider.
	/// </summary>
	[Serializable]
	public class ApplyHealAction : BaseAction
	{
		/// <summary>
		/// The value provider that determines the amount of health to restore.
		/// </summary>
		[Tooltip("The value provider that determines the amount of health to restore.")]
		[SerializeReference, SubclassSelector]
		public IIntValueProvider HealValue;

		/// <summary>
		/// Restores health to the target character using the computed value.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">The event data containing the target information.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (HealValue == null)
			{
				Log.Warning("HealAction", "HealValue provider is null.");
				return;
			}

			// Try to get the event data for a character hit. If not present, log a warning and exit.
			if (eventData.TryGet(out CharacterHitEventData targetEventData))
			{
				// Try to get the damage controller from the target. If present, apply the heal.
				if (targetEventData.Target.TryGet(out ICharacterDamageController defenderDamageController))
				{
					int amount = HealValue.GetValue(initiator, eventData);
					defenderDamageController.Heal(initiator, amount);
					Log.Debug("HealAction", $"Initiator '{initiator.Name}' healed target '{targetEventData.Target.Name}' for {amount}.");
				}
			}
			else
			{
				Log.Warning("HealAction", "Expected CharacterHitEventData.");
			}
		}
	}
}