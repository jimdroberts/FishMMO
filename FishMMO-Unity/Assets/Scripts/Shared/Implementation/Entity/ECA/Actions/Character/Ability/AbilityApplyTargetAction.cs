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
			/* The server, or the client that owns the caster — the predicate every sibling that
			 * resolves an ability onto bodies now uses (AbilityApplyAreaAction,
			 * AbilityApplyHitscanAction, and AbilityObject's own swept hit).
			 *
			 * This action had NO gate at all once, then a server-only one. Neither was right for
			 * long: its downstream actions each self-gate, so nothing authoritative leaks either way,
			 * but server-only also deleted the caster's feedback — a targeted ability wired through
			 * here applied nothing on the caster's screen until the reconcile, while every other
			 * ability shape now responds on the tick it was cast. "Safe because of what it happens to
			 * call" is still not a property to rely on, which is why the gate stays explicit rather
			 * than being removed; it has simply widened to match its siblings.
			 *
			 * WIRING: this re-executes the ability's whole OnHit set, so it belongs on OnSpawn or
			 * OnTick — an event that is NOT itself part of that set. Wired to OnHit it re-enters the
			 * chain that invoked it and recurses until the stack gives out. */
			if (eventData.TryGet(out AbilityCollisionEventData abilityEventData))
			{
				AbilityObject abilityObject = abilityEventData.AbilityObject;

				if (abilityObject != null)
				{
					/* Read from the ability object rather than the event, so a DETACHED object —
					 * one whose caster disconnected and was replaced by a SnapshotCharacter phantom
					 * with no NetworkObject — answers server-only and stops predicting, exactly as
					 * the swept hit does. */
					if (!abilityObject.ResolvesHitsLocally)
					{
						return;
					}

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