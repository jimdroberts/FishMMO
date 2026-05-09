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
		/// Resolves the target via <see cref="BaseAction.ResolveTarget"/> and interrupts that target's ability controller.
		/// </remarks>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			ICharacter target = (eventData?.TargetCharacter ?? initiator);
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