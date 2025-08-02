using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that applies a specified amount of damage to a target character, using a given damage attribute type.
	/// </summary>
	[CreateAssetMenu(fileName = "New Apply Damage Action", menuName = "FishMMO/Triggers/Actions/Character/Apply Damage")]
	public class ApplyDamageAction : BaseAction
	{
		/// <summary>
		/// The base amount of damage to apply to the target.
		/// </summary>
		[Tooltip("The base amount of damage to apply.")]
		public int DamageAmount;

		/// <summary>
		/// The attribute template associated with this damage type (e.g., 'Physical', 'Fire').
		/// Used to determine the element or type of damage applied.
		/// </summary>
		[Tooltip("The attribute template associated with this damage type (e.g., 'Physical', 'Fire').")]
		public DamageAttributeTemplate DamageAttributeTemplate;

		/// <summary>
		/// Applies damage to the target character using the specified amount and attribute template.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">The event data containing the target information.</param>
		/// <remarks>
		/// This method attempts to retrieve <see cref="CharacterHitEventData"/> from the event data. If successful, it applies damage to the target's damage controller.
		/// </remarks>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			// Try to get the event data for a character hit. If not present, log a warning and exit.
			if (eventData.TryGet(out CharacterHitEventData targetEventData))
			{
				// Try to get the damage controller from the target. If present, apply the damage.
				if (targetEventData.Target.TryGet(out ICharacterDamageController defenderDamageController))
				{
					defenderDamageController.Damage(initiator, DamageAmount, DamageAttributeTemplate);
					// Log the damage event for debugging purposes.
					Log.Debug("DamageAction", $"Initiator '{initiator.Name}' dealt {DamageAmount} damage to target '{targetEventData.Target.Name}'.");
				}
			}
			else
			{
				Log.Warning("DamageAction", "Expected CharacterHitEventData.");
			}
		}

		/// <summary>
		/// Returns a formatted description of the apply damage action for UI display.
		/// </summary>
		/// <returns>A string describing the damage amount and element type, with color formatting.</returns>
		public override string GetFormattedDescription()
		{
			return Description.Replace("$DAMAGE$", "<size=125%><color=#" + DamageAttributeTemplate.DisplayColor.ToHex() + ">" + DamageAmount + "</color></size>")
							  .Replace("$ELEMENT$", "<size=125%><color=#" + DamageAttributeTemplate.DisplayColor.ToHex() + ">" + DamageAttributeTemplate.Name + "</color></size>");
		}
	}
}