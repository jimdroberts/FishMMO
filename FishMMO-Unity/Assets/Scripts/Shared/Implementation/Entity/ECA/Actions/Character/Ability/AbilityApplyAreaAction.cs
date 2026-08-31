using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that applies an ability effect to all targets within a specified area.
	/// </summary>
	[Serializable]
	public class AbilityApplyAreaAction : BaseAction
	{
		/// <summary>
		/// The value provider that determines the radius of the area effect.
		/// </summary>
		[Tooltip("The value provider that determines the radius of the area effect.")]
		[SerializeReference, SubclassSelector]
		public IFloatValueProvider RadiusValue;

		/// <summary>
		/// The value provider that determines the maximum number of hits to process in the area.
		/// </summary>
		[Tooltip("The value provider that determines the maximum number of hits.")]
		[SerializeReference, SubclassSelector]
		public IIntValueProvider MaxHitsValue;

		/// <summary>
		/// Layer mask to filter targets in the area.
		/// </summary>
		[Tooltip("Layer mask to filter targets in the area.")]
		public LayerMask TargetLayerMask = ~0;

		/// <summary>
		/// Reused result list, lent out for the duration of one <see cref="Execute"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Not static, because two different area actions can be mid-fan-out at once. But an
		/// instance field is not enough on its own either: an OnHit trigger fired from the loop below
		/// is free to reach the SAME serialized action instance again — an ability whose hit event
		/// re-enters itself — and the re-entrant call would clear the list the outer loop is still
		/// walking. <see cref="inUse"/> is what closes that: the outer call owns the field, and any
		/// nested one allocates its own.
		/// </para>
		/// <para>
		/// The nested case is rare enough to be worth an allocation and common enough to be worth
		/// surviving; the ordinary path still allocates once for the life of the asset.
		/// </para>
		/// </remarks>
		[NonSerialized]
		private List<LagCompensatedQuery.CompensatedHit> hits;

		/// <summary>True while <see cref="hits"/> is lent to an <see cref="Execute"/> still running.</summary>
		[NonSerialized]
		private bool inUse;

		/// <summary>
		/// Executes the area effect, applying the ability to all valid targets within the radius.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">Event data containing context for the action.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (RadiusValue == null || MaxHitsValue == null)
			{
				Log.Warning("AbilityApplyAreaAction", "RadiusValue or MaxHitsValue provider is null.");
				return;
			}

			/* Resolved through the shared helper, which reads all three ability event payloads.
			 *
			 * This used to test AbilityCollisionEventData and nothing else, so an area effect wired
			 * to OnSpawn or OnTick returned here having done nothing, on every peer. The peer gate
			 * below was corrected for that failure once already — the tick-domain test it replaced
			 * suppressed the server too — but the correction stopped at the gate and left the
			 * payload test that was the other half of it. See AbilityObject.TryResolveFrom. */
			if (AbilityObject.TryResolveFrom(eventData, out AbilityObject abilityObject))
			{
				/* Server only. Physics queries are not deterministic across peers, so this
				 * must run exactly once, where hits are authoritative. Gated on the PEER rather
				 * than on the attached tick being a replicate-domain tick: the server's own
				 * spawn and self-target dispatches carry replicate ticks too, so the tick test
				 * this replaced suppressed the server as well and the effect happened nowhere.
				 * Clients receive the results through the usual authoritative paths. */
				/* Drawn BEFORE the peer gate, never after — see AbilityObject.RNG. A provider may
				 * consume this object's generator, which every action in the event chain shares. */
				int maxHits = MaxHitsValue.GetValue(initiator, eventData);
				float radius = RadiusValue.GetValue(initiator, eventData);

				/* The server, or the client that owns the caster — the same predicate the swept
				 * projectile hit uses, and for the same reason. This was server-only, which meant a
				 * point-blank blast moved nothing on the caster's screen until the server's report
				 * came back. The query below is lag-compensated: the server rewinds to the caster's
				 * view, the client queries its live world (which is that view), so both resolve the
				 * same bodies and cap the same ordered list. Everything authoritative downstream —
				 * kill, combat report, loot rights, threat — self-gates a level lower. See
				 * TargetSelector.ResolvesTargetsLocally. */
				if (!abilityObject.ResolvesHitsLocally)
				{
					return;
				}

				/* Borrow the shared list, or allocate a private one when a nested execution already
				 * holds it. See the remarks on `hits`. */
				bool ownsSharedList = !inUse;
				List<LagCompensatedQuery.CompensatedHit> hitBuffer;
				if (ownsSharedList)
				{
					inUse = true;
					hitBuffer = hits ??= new List<LagCompensatedQuery.CompensatedHit>(16);
				}
				else
				{
					hitBuffer = new List<LagCompensatedQuery.CompensatedHit>(16);
				}

				Vector3 center = abilityObject.Transform.position;
				/* Resolved against where the caster's client saw these characters, not where
				 * they are now. The ability object's own position needs no compensation: its
				 * motion is deterministic, so every peer already agrees on it.
				 *
				 * The query, the distance ranking, the per-character dedupe and the cap all
				 * happen inside one rewind scope in there. This method used to do the last three
				 * itself, out here, and got all three wrong: it capped a buffer that
				 * LagCompensatedQuery had sorted by ObjectId — so the cap kept the characters
				 * the server spawned earliest rather than the ones nearest the blast — it sized
				 * its buffer once and never grew it, so a crowd past the buffer was truncated by
				 * the broadphase before any of that ran, and it resolved characters with a bare
				 * GetComponent on the collider, which drops a character whose hitbox is a child
				 * and counts a character with two hitboxes twice. */
				/* try/finally around everything that touches the borrowed list. An OnHit trigger
				 * below runs arbitrary authored actions and may throw, and the early return for a
				 * missing OnHitEvents set is inside this block too — either would otherwise leave
				 * `inUse` latched true for the life of the asset, so every later cast on every
				 * character allocated a fresh list forever. */
				try
				{
					int hitCount = LagCompensatedQuery.OverlapSphereNearest(
						eventData, abilityObject.GameObject, center, radius, TargetLayerMask,
						maxHits, charactersOnly: true, hitBuffer);

					var onHitEvents = abilityObject.OnHitEvents;
					if (onHitEvents == null)
					{
						Log.Warning("AbilityApplyAreaAction", "No OnHitEvents available.");
						return;
					}

					// Extract tick context once from the parent event so each child collision
					// event inherits it. Without this, downstream ApplyBuffAction falls back
					// to TimeManager.LocalTick and loses tick alignment in prediction paths.
					eventData.TryGet(out TickEventData tickToPropagate);

					for (int i = 0; i < hitCount; i++)
					{
						/* Already deduplicated per character, ordered nearest-first and filtered to
						 * characters by the query, so this loop neither re-resolves components nor has
						 * to guard against the same body arriving twice. */
						ICharacter targetCharacter = hitBuffer[i].Character;
						if (targetCharacter == null)
						{
							continue;
						}

						/* The impact point and normal travel now that the query measures them inside the
						 * rewind scope. An OnHit effect on an area ability used to be placed with no
						 * point at all and defaulted to the target's origin, so a blast marked every
						 * victim at its feet rather than on the side facing the explosion. */
						AbilityCollisionEventData collisionEvent = new AbilityCollisionEventData(
							initiator, targetCharacter, abilityObject, hitBuffer[i].Point, hitBuffer[i].Normal, abilityObject.RNG);
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
				finally
				{
					// Nothing holds a reference into the query's reusable buffers past this point.
					hitBuffer.Clear();
					if (ownsSharedList)
					{
						inUse = false;
					}
				}
			}
			else
			{
				Log.Warning("AbilityApplyAreaAction",
					"Expected an event carrying an AbilityObject — wire this to OnSpawn, OnTick or OnHit.");
			}
		}
	}
}