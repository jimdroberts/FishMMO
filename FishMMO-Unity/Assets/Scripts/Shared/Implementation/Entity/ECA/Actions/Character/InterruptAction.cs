using System;
using FishMMO.Shared.Core;

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
			ICharacter target = ResolveTarget(initiator, eventData);
			if (target == null)
			{
				return;
			}

			if (target.TryGet(out IAbilityController abilityController))
			{
				abilityController.Interrupt(initiator);
			}
		}
	}
}