using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that applies an ability effect to a single targeted character.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>WIRING: OnSpawn, OnTick or OnDestroy.</b> This runs the ability's whole OnHit set against
	/// one character, so it belongs on an event that is not itself part of that set — "the blast
	/// goes off on the target the cast resolved" without a projectile ever having to reach it.
	/// The choice is no longer only a comment: <see cref="MayRunHitChain"/> refuses an event that
	/// carries a collision payload, which is exactly the shape an OnHit dispatch has, so the
	/// recursion this note used to warn about cannot be authored at all.
	/// </para>
	/// <para>
	/// <b>It takes its target from the fan-out.</b> The trigger's own <c>TargetSelector</c> (or this
	/// action's) is what names the character, as everywhere else — see the closing remark on
	/// <see cref="AbilityDestroyEventData"/>. An event with no target character is a no-op that says
	/// so, rather than inventing the caster as a victim.
	/// </para>
	/// </remarks>
	[Serializable]
	public class AbilityApplyTargetAction : BaseAction
	{
		/// <summary>
		/// True when this event may re-enter the ability's OnHit chain.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>The recursion guard, as a rule rather than a warning.</b> This action executes every
		/// OnHit trigger the ability owns. Wired to an OnHit event it re-entered the chain that had
		/// just invoked it and recursed until the stack gave out — the comment telling designers not
		/// to do that was the only thing standing between an authored asset and a hard crash.
		/// </para>
		/// <para>
		/// <b>The collision payload is what distinguishes the two.</b> An OnHit dispatch always
		/// carries an <see cref="AbilityCollisionEventData"/> — that is what an ability hit IS, on
		/// every producer: the object's swept hit, the server's echo, the area and hitscan queries,
		/// the self-target dispatch, and the payload this action builds below. The OnSpawn, OnTick
		/// and OnDestroy dispatches carry <see cref="AbilitySpawnEventData"/>,
		/// <see cref="AbilityTickEventData"/> and <see cref="AbilityDestroyEventData"/> and never a
		/// collision. So refusing a collision-carrying event both rejects the documented mis-wiring
		/// and bounds the chain at one level: the event this action dispatches is a collision, so a
		/// second instance of this action reached from inside it declines.
		/// </para>
		/// </remarks>
		/// <param name="eventCarriesCollision">True when the event already carries a collision payload.</param>
		/// <returns>True when the OnHit chain may be run for this event.</returns>
		internal static bool MayRunHitChain(bool eventCarriesCollision) => !eventCarriesCollision;

		/// <summary>
		/// Executes the action, applying the ability to the targeted character.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">Event data containing context for the action.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (eventData == null)
			{
				return;
			}

			/* Resolved through the shared helper, which reads all four ability event payloads.
			 *
			 * This used to test AbilityCollisionEventData and nothing else — and since that is the
			 * only ability payload deriving from CollisionEventData, the action was unusable in BOTH
			 * of its documented wirings: on OnSpawn or OnTick it logged a warning and did nothing,
			 * and the one event shape that did resolve was the one that recurses. Same defect, and
			 * the same fix, as AbilityApplyAreaAction. See AbilityObject.TryResolveFrom. */
			if (!AbilityObject.TryResolveFrom(eventData, out AbilityObject abilityObject))
			{
				Log.Warning("AbilityApplyTargetAction",
					"Expected an event carrying an AbilityObject — wire this to OnSpawn, OnTick or OnDestroy.");
				return;
			}

			if (!MayRunHitChain(eventData.Contains<AbilityCollisionEventData>()))
			{
				Log.Warning("AbilityApplyTargetAction",
					"Wired to an OnHit event. This action RUNS the OnHit set, so doing that re-enters " +
					"the chain that invoked it; wire it to OnSpawn, OnTick or OnDestroy instead.");
				return;
			}

			/* Read from the ability object rather than the event, so a DETACHED object — one whose
			 * caster disconnected and was replaced by a SnapshotCharacter phantom with no
			 * NetworkObject — answers server-only and stops predicting, exactly as the swept hit
			 * does. The gate is the server or the client owning the caster, matching
			 * AbilityApplyAreaAction and AbilityApplyHitscanAction; its downstream actions each
			 * self-gate again, so nothing authoritative leaks either way. */
			if (!abilityObject.ResolvesHitsLocally)
			{
				return;
			}

			/* Strict, never TryResolveTargetOrInitiator: an outward-effecting action with a
			 * misconfigured selector must be a no-op rather than a self-hit. */
			if (!TryResolveTarget(eventData, out ICharacter target))
			{
				Log.Debug("AbilityApplyTargetAction",
					"No target character on the event; give the trigger or this action a TargetSelector.");
				return;
			}

			var onHitEvents = abilityObject.OnHitEvents;
			if (onHitEvents == null)
			{
				Log.Warning("AbilityApplyTargetAction", "No OnHitEvents available.");
				return;
			}

			/* A real impact point, not the "whole overlap at once" constructor.
			 *
			 * The point has to be a place BOTH halves of the effect agree on: the local OnHit chain
			 * reads it through PlayFXAction, and the observers' copy reads it off the broadcast
			 * below. Publishing the object's position while playing at the target's would put the
			 * same impact in two different places depending on who was watching. The target is
			 * where the effect lands, and the normal faces back along the line to the object that
			 * delivered it — degenerate only when the two are coincident, which is a self-cast. */
			Vector3 objectPosition = abilityObject.Transform != null
				? abilityObject.Transform.position
				: Vector3.zero;
			Vector3 point = target.Transform != null ? target.Transform.position : objectPosition;
			Vector3 toObject = objectPosition - point;
			Vector3 normal = toObject.sqrMagnitude > 0f ? toObject.normalized : Vector3.up;

			/* Published before the events, for the reason AbilityObject.ApplyHit gives: an authored
			 * action may end this object while handling the effect, and the impact still has to
			 * reach the people watching. Server only, and to observers except the owner — see
			 * AbilityObject.PublishActionHit.
			 *
			 * This was the one apply-action that never published, so even once it resolved, its
			 * impacts existed on the resolving peer alone. */
			abilityObject.PublishActionHit(target, point, normal);

			/* Tick context is lifted from the parent event so the child collision inherits it.
			 * Without it a downstream ApplyBuffAction falls back to TimeManager.LocalTick and loses
			 * tick alignment in prediction paths — the same propagation AbilityApplyAreaAction does. */
			eventData.TryGet(out TickEventData tickToPropagate);

			AbilityCollisionEventData collisionEvent = new AbilityCollisionEventData(
				initiator, target, abilityObject, point, normal, abilityObject.RNG);
			if (tickToPropagate != null)
			{
				collisionEvent.Add(tickToPropagate);
			}

			foreach (var trigger in onHitEvents.Values)
			{
				trigger?.Execute(collisionEvent);
			}
		}
	}
}
