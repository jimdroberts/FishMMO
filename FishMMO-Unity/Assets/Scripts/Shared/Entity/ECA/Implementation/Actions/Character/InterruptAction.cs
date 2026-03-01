using System;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that interrupts the target character's current ability or action.
	/// </summary>
	[Serializable]
	public class InterruptAction : BaseAction
	{
		/// <summary>
		/// Interrupts the target character's current ability or action.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">The event data containing the target information.</param>
		/// <remarks>
		/// This method attempts to retrieve <see cref="CharacterHitEventData"/> from the event data. If successful, it interrupts the target's ability controller.
		/// </remarks>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			// Try to get the event data for a character hit. If not present, log a warning and exit.
			if (eventData.TryGet(out CharacterHitEventData targetEventData))
			{
				// Try to get the ability controller from the target. If present, interrupt the current ability.
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