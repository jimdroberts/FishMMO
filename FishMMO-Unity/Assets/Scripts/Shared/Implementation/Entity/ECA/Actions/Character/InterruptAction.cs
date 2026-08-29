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
		/// Resolves the target via <see cref="BaseAction.TryResolveTarget"/> and interrupts that target's ability controller.
		/// </remarks>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			/* Server only. State forwarding is off, so an observer never simulates another
			 * character and has nothing to predict here; the outcome reaches every peer through the
			 * authoritative paths (reconcile, observer broadcast). Running it locally as well would
			 * apply the effect twice on the peer that also happens to be the server, and produce a
			 * value on a client that the server never agreed to. */
			if (!EcaAuthority.IsServer(initiator, eventData))
			{
				return;
			}

			if (!TryResolveTarget(eventData, out ICharacter target))
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