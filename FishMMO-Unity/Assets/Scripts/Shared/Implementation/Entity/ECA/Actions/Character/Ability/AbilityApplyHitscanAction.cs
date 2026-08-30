using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that resolves an instantaneous ray from the ability object and runs the ability's
	/// OnHit events for everything it passes through — the hitscan half of the ability system.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>This is what makes Bullet and Beam abilities possible, and they are the same action wired
	/// to different events.</b> Nothing else about the two differs: both resolve a ray from the
	/// ability object's pose, in ray order, capped at <see cref="MaxHitsValue"/>.
	/// </para>
	/// <list type="table">
	/// <item><term>Bullet</term><description>
	/// An instant ability with this action on its <b>OnSpawn</b> event. The object exists for one
	/// tick, fires once, and its lifetime expires. Nothing travels, so there is no projectile to
	/// dodge and no lead to aim — the shot lands on the tick it was fired.
	/// </description></item>
	/// <item><term>Beam</term><description>
	/// A <b>channelled</b> ability (one carrying <c>AbilityController.ChanneledTemplate</c>) with
	/// this action on its <b>OnSpawn</b> event. <c>SpawnChanneledAbility</c> already spawns one
	/// object per tick for the life of the channel, each at the caster's CURRENT aim, so the beam
	/// tracks where the player is looking and re-resolves its hits every tick. Damage per tick is
	/// the ability's damage; hold duration is the channel's.
	/// </description></item>
	/// </list>
	/// <para>
	/// <b>Why a per-tick respawn rather than one long-lived object with this on OnTick.</b> An
	/// ability object's pose is fixed at spawn and advanced by a closed form
	/// (<see cref="AbilityMoveTransformAction"/>), so a single object re-raycasting each tick would
	/// keep firing along the heading it was born with — a beam welded to the direction you were
	/// facing when you pressed the button. The channel path re-resolves the aim per tick because it
	/// genuinely re-spawns, which is also why every peer can reproduce it: each spawn is broadcast
	/// with its own pose. Putting this on OnTick is still legal and is the right choice for a fixed
	/// emplacement — a turret, a trap, a laser fence — where not tracking is the point.
	/// </para>
	/// <para>
	/// <b>Server only</b>, like every other hit-resolving action. A physics query is not
	/// reproducible across peers, so it must run exactly once, where hits are authoritative; clients
	/// learn the outcome through the usual paths (damage via the resource broadcast, the visual via
	/// <c>AbilityActivatedBroadcast</c> reproducing the spawn). The owner still sees its own damage
	/// immediately, because <c>ApplyDamageAction</c> predicts on the caster's client and
	/// <c>PredictedCombatEvents</c> greys out anything the server does not confirm.
	/// </para>
	/// <para>
	/// <b>Lag compensated, and it is the shape that needs it most.</b> A ray is infinitely thin, so
	/// unlike a blast radius it has no tolerance to absorb the gap between where the shooter saw a
	/// target and where the server holds it — at 300&#160;ms that gap is metres and the shot is
	/// simply a miss. <see cref="LagCompensatedQuery.RaycastNearest"/> rewinds every character in
	/// the scene to the tick the caster's client was rendering, runs the ray there, and orders and
	/// caps the result inside the same scope.
	/// </para>
	/// </remarks>
	[Serializable]
	public class AbilityApplyHitscanAction : BaseAction
	{
		/// <summary>
		/// The value provider that determines how far the ray reaches, in metres.
		/// </summary>
		/// <remarks>
		/// Authored rather than taken from <c>Ability.Range</c>, which is <c>Speed * LifeTime</c> and
		/// therefore zero for anything that does not travel — every hitscan ability by definition.
		/// </remarks>
		[Tooltip("The value provider that determines how far the ray reaches, in metres.")]
		[SerializeReference, SubclassSelector]
		public IFloatValueProvider RangeValue;

		/// <summary>
		/// The value provider that determines how many distinct characters the ray may hit.
		/// </summary>
		/// <remarks>
		/// The pierce count. One is a shot that stops at the first target; zero or less is a beam
		/// that passes through everything on its line, matching
		/// <see cref="TargetOrdering.CappedCount"/> and every selector.
		/// </remarks>
		[Tooltip("How many distinct characters the ray may hit. 1 stops at the first; 0 or less pierces everything.")]
		[SerializeReference, SubclassSelector]
		public IIntValueProvider MaxHitsValue;

		/// <summary>
		/// Layers the ray tests against.
		/// </summary>
		[Tooltip("Layers the ray tests against.")]
		public LayerMask TargetLayerMask = ~0;

		/// <summary>
		/// Whether scenery stops the shot.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The one authored decision this action has that the area version does not, and it is a
		/// real gameplay choice rather than an implementation detail. <b>True</b> — the default —
		/// means the ray is blocked by the first thing it meets that is not a character, so a target
		/// behind a wall is safe and cover works. <b>False</b> means only characters are considered
		/// at all and the ray passes through the world, which is what a debug weapon or a
		/// deliberately wall-piercing ability wants.
		/// </para>
		/// <para>
		/// It is expressed as "does scenery count" rather than left to the layer mask because the
		/// mask defaults to every layer: an author who never touches it would otherwise get a shot
		/// that spends its entire pierce count on terrain and hits nobody.
		/// </para>
		/// </remarks>
		[Tooltip("When true the ray is blocked by scenery, so cover works. When false it passes through the world and only characters count.")]
		public bool BlockedByScenery = true;

		/// <summary>
		/// Reused result list, lent out for the duration of one <see cref="Execute"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Not static, because two different hitscan actions can be mid-fan-out at once. But an
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
		/// Resolves the ray and runs the ability's OnHit events for each character it found.
		/// </summary>
		/// <param name="initiator">The character that cast the ability.</param>
		/// <param name="eventData">Event data carrying the ability object.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (RangeValue == null || MaxHitsValue == null)
			{
				Log.Warning("AbilityApplyHitscanAction", "RangeValue or MaxHitsValue provider is null.");
				return;
			}

			if (eventData == null || !TryGetAbilityObject(eventData, out AbilityObject abilityObject))
			{
				Log.Warning("AbilityApplyHitscanAction",
					"Expected an event carrying an AbilityObject — wire this to OnSpawn, OnTick or OnHit.");
				return;
			}

			/* Server only. See the remarks on this type: a physics query is not reproducible across
			 * peers, so it runs where hits are authoritative and nowhere else. Gating on the peer
			 * rather than on the attached tick being a replicate tick, because the server's own spawn
			 * dispatches carry replicate ticks too — the trap AbilityApplyAreaAction was corrected
			 * for, and the reason an area effect wired to OnSpawn used to run on no peer at all. */
			if (!abilityObject.IsServer)
			{
				return;
			}

			Transform shooter = abilityObject.Transform;
			if (shooter == null)
			{
				return;
			}

			float range = RangeValue.GetValue(initiator, eventData);
			if (range <= 0f)
			{
				return;
			}
			int maxHits = MaxHitsValue.GetValue(initiator, eventData);

			var onHitEvents = abilityObject.OnHitEvents;
			if (onHitEvents == null)
			{
				Log.Warning("AbilityApplyHitscanAction", "No OnHitEvents available.");
				return;
			}

			/* Borrow the shared list, or allocate a private one when a nested execution already holds
			 * it. See the remarks on `hits`. */
			bool ownsSharedList = !inUse;
			List<LagCompensatedQuery.CompensatedHit> hitBuffer;
			if (ownsSharedList)
			{
				inUse = true;
				hitBuffer = hits ??= new List<LagCompensatedQuery.CompensatedHit>(8);
			}
			else
			{
				hitBuffer = new List<LagCompensatedQuery.CompensatedHit>(8);
			}

			/* try/finally around everything that touches the borrowed list: an OnHit trigger below
			 * runs arbitrary authored actions and may throw, which would otherwise leave `inUse`
			 * latched true for the life of the asset and make every later shot allocate. */
			try
			{
				/* The object's own pose IS the aim. AbilityObject.ResolveSpawnPose builds it from the
				 * replicated aim origin and direction, and carries it verbatim to observers, so every
				 * peer agrees on the ray without any of them re-deriving it from an interpolated
				 * caster. The object needs no lag compensation of its own for the same reason. */
				int hitCount = LagCompensatedQuery.RaycastNearest(
					eventData, abilityObject.GameObject, shooter.position, shooter.forward, range,
					TargetLayerMask, maxHits, charactersOnly: !BlockedByScenery, hitBuffer);

				// Inherited once so each child collision event carries the same tick; without it a
				// downstream ApplyBuffAction falls back to TimeManager.LocalTick and loses alignment.
				eventData.TryGet(out TickEventData tickToPropagate);

				for (int i = 0; i < hitCount; i++)
				{
					LagCompensatedQuery.CompensatedHit hit = hitBuffer[i];

					/* The object never shoots itself. Its collider sits exactly on the ray's origin, so
					 * with BlockedByScenery on it would otherwise be the first non-character hit and stop
					 * every shot at zero range. Children go with it, since a prefab is free to hang the
					 * visual's collider off one. This is the same exclusion AbilityObjectSweep.Accept
					 * applies for the same reason.
					 *
					 * The CASTER is deliberately not excluded here: whether a shot can hit its own owner
					 * is a gameplay question, and the OnHit triggers' own conditions are where it is
					 * answered — the same place friendly fire is. */
					if (hit.Collider != null && hit.Collider.transform.IsChildOf(shooter))
					{
						continue;
					}

					/* Scenery ENDS the shot rather than being skipped, when it is in the set at all.
					 * Anything past it is behind cover, and the query already handed these back in ray
					 * order — so the first non-character is exactly where the shot stops. When
					 * BlockedByScenery is false the query filtered scenery out entirely and this never
					 * fires. */
					if (hit.Character == null)
					{
						break;
					}

					AbilityCollisionEventData collisionEvent = new AbilityCollisionEventData(
						initiator, hit.Character, abilityObject, hit.Point, hit.Normal, abilityObject.RNG);
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

		/// <summary>
		/// Pulls the ability object out of whichever event shape this action was wired to.
		/// </summary>
		/// <remarks>
		/// A spawn event and a collision event carry it on different payloads, and this action is
		/// meaningful on both — OnSpawn for a bullet or a beam tick, OnHit for a shot that fires a
		/// second ray from wherever the first one landed.
		/// </remarks>
		private static bool TryGetAbilityObject(EventData eventData, out AbilityObject abilityObject)
		{
			if (eventData.TryGet(out AbilityCollisionEventData collision) && collision.AbilityObject != null)
			{
				abilityObject = collision.AbilityObject;
				return true;
			}
			if (eventData.TryGet(out AbilitySpawnEventData spawn) && spawn.InitialAbilityObject != null)
			{
				abilityObject = spawn.InitialAbilityObject;
				return true;
			}
			if (eventData.TryGet(out AbilityTickEventData tick) && tick.AbilityObject != null)
			{
				abilityObject = tick.AbilityObject;
				return true;
			}
			abilityObject = null;
			return false;
		}
	}
}
