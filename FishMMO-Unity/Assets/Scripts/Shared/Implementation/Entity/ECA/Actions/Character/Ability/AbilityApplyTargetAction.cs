using System;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that applies an ability effect to a single targeted character.
	/// </summary>
	[Serializable]
	public class AbilityApplyTargetAction : BaseAction
	{
		/// <summary>
		/// Executes the action, applying the ability to the targeted character.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">Event data containing context for the action.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			/* Server only, like every other action that resolves an ability onto a target.
			 *
			 * This was the one ability action with no gate of its own. Its downstream actions each
			 * self-gate, so nothing authoritative leaked — but "safe because of what it happens to
			 * call" is not a property this can keep while the OnHit set is designer-authored. Every
			 * sibling states its peer explicitly; this one stated nothing.
			 *
			 * WIRING: this re-executes the ability's whole OnHit set, so it belongs on OnSpawn or
			 * OnTick — an event that is NOT itself part of that set. Wired to OnHit it re-enters the
			 * chain that invoked it and recurses until the stack gives out. */
			if (!EcaAuthority.IsServer(initiator, eventData))
			{
				return;
			}

			if (eventData.TryGet(out AbilityCollisionEventData abilityEventData))
			{
				AbilityObject abilityObject = abilityEventData.AbilityObject;

				if (abilityObject != null)
				{
					var onHitEvents = abilityObject.OnHitEvents;
					if (onHitEvents == null)
					{
						Log.Warning("AbilityApplyTargetAction", "No OnHitEvents available.");
						return;
					}

					foreach (var action in onHitEvents.Values)
					{
						action?.Execute(abilityEventData);
					}
				}
				else
				{
					Log.Warning("AbilityApplyTargetAction", "AbilityObject is null.");
				}
			}
			else
			{
				Log.Warning("AbilityApplyTargetAction", "Expected AbilityCollisionEventData.");
			}
		}
	}
}