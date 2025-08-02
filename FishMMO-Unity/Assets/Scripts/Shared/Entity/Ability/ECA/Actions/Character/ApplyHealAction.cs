using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that restores a specified amount of health to a target character.
	/// </summary>
	[CreateAssetMenu(fileName = "New Apply Heal Action", menuName = "FishMMO/Triggers/Actions/Character/Apply Heal")]
	public class ApplyHealAction : BaseAction
	{
		/// <summary>
		/// The amount of health to restore to the target.
		/// </summary>
		[Tooltip("The amount of health to restore.")]
		public int HealAmount;

		/// <summary>
		/// Restores health to the target character using the specified amount.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">The event data containing the target information.</param>
		/// <remarks>
		/// This method attempts to retrieve <see cref="CharacterHitEventData"/> from the event data. If successful, it heals the target's damage controller.
		/// </remarks>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			// Try to get the event data for a character hit. If not present, log a warning and exit.
			if (eventData.TryGet(out CharacterHitEventData targetEventData))
			{
				// Try to get the damage controller from the target. If present, apply the heal.
				if (targetEventData.Target.TryGet(out ICharacterDamageController defenderDamageController))
				{
					defenderDamageController.Heal(initiator, HealAmount);
					// Log the heal event for debugging purposes.
					Log.Debug("HealAction", $"Initiator '{initiator.Name}' healed target '{targetEventData.Target.Name}' for {HealAmount}.");
				}
			}
			else
			{
				Log.Warning("HealAction", "Expected CharacterHitEventData.");
			}
		}
		/// <summary>
		/// Returns a formatted description of the apply heal action for UI display.
		/// </summary>
		/// <returns>A string describing the amount of health restored.</returns>
		public override string GetFormattedDescription()
		{
			return $"Heals the target for <color=#00FF00>{HealAmount}</color> health.";
		}
	}
}