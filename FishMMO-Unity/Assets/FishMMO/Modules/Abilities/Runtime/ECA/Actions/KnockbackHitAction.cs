using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Which way a knockback pushes its victim.
	/// </summary>
	/// <remarks>Serialized by value on authored actions: append only, never reorder.</remarks>
	public enum KnockbackDirection : byte
	{
		/// <summary>
		/// Decided by the shape of the attack (<see cref="KnockbackHitAction.ResolveOrigin"/>): a blast pushes away from
		/// its centre, a projectile along its flight, a melee swing away from the swinger.
		/// </summary>
		Auto = 0,
		/// <summary>Along the ability object's heading: the way a projectile or a hitscan line was travelling.</summary>
		Heading = 1,
		/// <summary>Radially away from the centre of the effect: the blast, the impact point, or the caster of a self-centred area.</summary>
		AwayFromCentre = 2,
		/// <summary>Radially away from the caster.</summary>
		AwayFromCaster = 3,
	}

	/// <summary>
	/// Action that applies a knockback force to a target character, pushing them away from the source.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Direction follows the attack</b> (<see cref="Direction"/>, <see cref="ResolveOrigin"/>). An area of effect is a
	/// blast radius and pushes everyone it catches away from its centre; a projectile pushes along its own flight, so a
	/// victim facing the shot is driven backwards; a melee swing pushes away from the one swinging. It used to push along
	/// the ability object's forward in every case, which sent everyone in a blast the same way, and did nothing at all for
	/// an ability with no object (a self-centred area), because there was no forward to read.
	/// </para>
	/// <para>
	/// <b>Who moves.</b> A player is moved through its KCC motor (a velocity impulse the motor resolves against collision,
	/// prediction and reconciliation). An NPC — monsters and wildlife alike — is moved through its server brain
	/// (<see cref="INPCBrain.ApplyKnockback"/>), which slides the NavMesh agent, so it can be shoved along the ground
	/// but never through a wall or off the mesh. Both are horizontal for an NPC; a player keeps the full direction.
	/// </para>
	/// <para>
	/// The displacement is SERVER ONLY — see the note in <see cref="Execute"/> for why this is the
	/// one feedback action that did not move to <c>EcaAuthority.MayPredict</c>. The victim's own
	/// client receives it through the reconcile (or the NPC's transform stream), and the attacker's client plays a
	/// cosmetic flinch (<see cref="PlayPredictedReaction"/>) in the meantime.
	/// </para>
	/// </remarks>
	[Serializable]
	[UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "FishMMO.Shared", sourceAssembly: "FishMMO.Shared")]
	public class KnockbackHitAction : BaseAction
	{
		/// <summary>
		/// The value provider that determines the initial knockback force applied to the target.
		/// </summary>
		[Tooltip("The value provider that determines the initial knockback force (metres per second of push).")]
		[SerializeReference, SubclassSelector]
		public IFloatValueProvider ForceValue;

		/// <summary>
		/// How fast a knockback bleeds off, metres per second per second: the NPC brain's law
		/// (<c>AIController.KnockbackDeceleration</c>) and the attacker's predicted shove
		/// (<see cref="CharacterHitReaction.Shove"/>), so the shove drawn now is the slide the server makes.
		/// </summary>
		public const float DefaultDeceleration = 24f;

		/// <summary>
		/// Which way the victim is pushed. <see cref="KnockbackDirection.Auto"/> decides from the attack's shape.
		/// </summary>
		[Tooltip("Which way the victim is pushed. Auto: area = away from the blast centre, projectile = along its flight, melee = away from the caster.")]
		public KnockbackDirection Direction = KnockbackDirection.Auto;

		/// <summary>
		/// Where a knockback's direction is measured from, once <see cref="Direction"/> has been applied to the attack.
		/// </summary>
		internal enum KnockbackOrigin : byte
		{
			/// <summary>The ability object's forward.</summary>
			Heading = 0,
			/// <summary>Away from the ability object's position (a blast's centre, a placed volume).</summary>
			AbilityObject = 1,
			/// <summary>Away from where the parent hit landed (a projectile whose hit fans out over an area).</summary>
			Impact = 2,
			/// <summary>Away from the caster.</summary>
			Caster = 3,
		}

		/// <summary>
		/// The attack-shape rule, as a pure function of what the event carries.
		/// </summary>
		/// <param name="mode">The authored <see cref="Direction"/>.</param>
		/// <param name="hasAbilityObject">An ability object is on the event (false for a self-cast, which has none).</param>
		/// <param name="isSplash">The hit came from an area query around the object (<see cref="AbilityApplyAreaAction"/>).</param>
		/// <param name="fannedOut">
		/// The victim was chosen by the event's own selector rather than being the body the object struck — an area
		/// selector on a projectile's hit event.
		/// </param>
		/// <param name="spawnTarget">How the ability's object was placed.</param>
		/// <returns>What the direction is measured from.</returns>
		internal static KnockbackOrigin ResolveOrigin(KnockbackDirection mode, bool hasAbilityObject, bool isSplash, bool fannedOut, AbilitySpawnTarget spawnTarget)
		{
			switch (mode)
			{
				case KnockbackDirection.AwayFromCaster:
					return KnockbackOrigin.Caster;
				case KnockbackDirection.Heading:
					return hasAbilityObject ? KnockbackOrigin.Heading : KnockbackOrigin.Caster;
				case KnockbackDirection.AwayFromCentre:
					return CentreOf(hasAbilityObject, isSplash, fannedOut);
			}

			// Auto.
			if (!hasAbilityObject || isSplash || fannedOut)
			{
				// Every area: a self-centred one (no object), a blast around the object, an area off an impact.
				return CentreOf(hasAbilityObject, isSplash, fannedOut);
			}
			switch (spawnTarget)
			{
				case AbilitySpawnTarget.Forward:
					// A melee volume in front of the caster: the swing pushes away from the swinger, not from the
					// middle of the volume, which would pull a victim standing in its near half towards them.
					return KnockbackOrigin.Caster;
				case AbilitySpawnTarget.Self:
				case AbilitySpawnTarget.PointBlank:
				case AbilitySpawnTarget.Target:
					// A volume placed where it acts (a trap, a ground effect): its own centre.
					return KnockbackOrigin.AbilityObject;
				default:
					// Launched (Spawner, SpawnerWithCameraRotation, Camera): a projectile or a hitscan line.
					return KnockbackOrigin.Heading;
			}
		}

		private static KnockbackOrigin CentreOf(bool hasAbilityObject, bool isSplash, bool fannedOut)
		{
			if (!hasAbilityObject)
			{
				return KnockbackOrigin.Caster;
			}
			if (fannedOut && !isSplash)
			{
				return KnockbackOrigin.Impact;
			}
			return KnockbackOrigin.AbilityObject;
		}

		/// <summary>
		/// The push direction for a victim at <paramref name="victim"/>: the heading as is, or radially (across the ground)
		/// away from <paramref name="centre"/>, falling back to <paramref name="fallbackForward"/> when the victim stands on
		/// the centre itself.
		/// </summary>
		internal static Vector3 ResolveDirection(KnockbackOrigin origin, Vector3 heading, Vector3 centre, Vector3 victim, Vector3 fallbackForward)
		{
			if (origin == KnockbackOrigin.Heading)
			{
				return heading.sqrMagnitude > 1e-8f ? heading.normalized : Flat(fallbackForward);
			}
			Vector3 away = victim - centre;
			away.y = 0f;
			if (away.sqrMagnitude < 1e-4f)
			{
				return Flat(fallbackForward);
			}
			return away.normalized;
		}

		private static Vector3 Flat(Vector3 v)
		{
			v.y = 0f;
			return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward;
		}

		/// <summary>
		/// Applies a knockback effect to the target character if they are not immortal.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">The event data containing the target and ability information.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			/* The DISPLACEMENT stays server only, and deliberately so. It moves the target, and on
			 * the attacker's client that target is driven by NetworkTransform — a locally applied
			 * impulse is overwritten by the next transform update one to three ticks later, so the
			 * character snaps back. That reads worse than the delay it would be hiding, which is why
			 * this is the one feedback action that did NOT move to MayPredict.
			 *
			 * What the attacker gets instead is a cosmetic flinch, played now, on a child transform
			 * NetworkTransform does not touch — see CharacterHitReaction. The server's real
			 * displacement then arrives and the two compose rather than fight. */
			if (ForceValue == null)
			{
				Log.Warning("KnockbackHitAction", "ForceValue provider is null.");
				return;
			}

			/* Drawn BEFORE the peer gate, never after — see AbilityObject.RNG. */
			float force = ForceValue.GetValue(initiator, eventData);

			if (!EcaAuthority.IsServer(initiator, eventData))
			{
				if (EcaAuthority.MayPredict(initiator, eventData))
				{
					PlayPredictedReaction(initiator, eventData, force);
				}
				return;
			}

			if (!TryResolveTarget(eventData, out ICharacter target))
			{
				return;
			}

			/* Never the caster. A self-centred area includes its caster in the query, and the harm rule allows harming
			 * oneself, so without this a war stomp launched the stomper. */
			if (initiator != null && ReferenceEquals(target, initiator))
			{
				return;
			}

			/* An evading NPC cannot be displaced: the same question the damage and debuff paths
			 * ask (CharacterEvade), so no hostile route stays open during the walk home. Separate
			 * from the Immortal test below, whose meaning is unchanged. */
			// And a target the harm rule protects from this initiator (HarmRule): a friend is not knocked about.
			if (CharacterEvade.RefusesHostileEffects(target, initiator))
			{
				return;
			}

			if (!(target is BaseCharacter character) ||
				!character.TryGet(out ICharacterDamageController defenderDamageController) ||
				defenderDamageController.Immortal)
			{
				return;
			}

			/* A victim whose motor will not run this tick is not knocked back at all, rather than
			 * knocked back later.
			 *
			 * KCCPlayer.OnReplicate returns before Motor.UpdatePhase1/2 for an incapacitated or
			 * dead character, and BaseVelocity is motor state that survives in the reconcile —
			 * so an impulse written here was not lost, it was STORED, and the whole of it fired
			 * as one lurch on the tick the stun ended or the character was revived. The
			 * ForceUnground timer went with it. Refusing is the honest reading of "this
			 * character cannot be moved right now": crowd control that pins somebody in place
			 * should not also be charging up the hit that lands when it breaks.
			 *
			 * Tested on the replicated HEALTH value rather than CharacterFlags.IsDead, matching
			 * KCCPlayer's own gate — flags ride the spawn payload and are never re-synced. */
			if (CharacterIncapacitation.IsIncapacitated(character) || !defenderDamageController.IsAlive)
			{
				return;
			}

			if (!TryResolvePush(initiator, eventData, target, out Vector3 knockbackDirection))
			{
				return;
			}

			if (character is IPlayerCharacter playerCharacter &&
				playerCharacter.CharacterController?.Motor != null)
			{
				// Apply knockback as a velocity impulse through the KCC motor.
				// This respects collision geometry, prediction/reconciliation,
				// and is deterministic across client and server.
				var motor = playerCharacter.CharacterController.Motor;
				motor.ForceUnground();
				Vector3 currentVelocity = motor.Velocity;
				// Remove velocity opposing the knockback direction so the
				// impulse doesn't fight the character's own momentum.
				Vector3 lateralVelocity = Vector3.ProjectOnPlane(currentVelocity, motor.CharacterUp);
				float opposingComponent = Vector3.Dot(lateralVelocity, knockbackDirection);
				if (opposingComponent < 0f)
				{
					currentVelocity -= opposingComponent * knockbackDirection;
				}
				currentVelocity += knockbackDirection * force;
				motor.BaseVelocity = currentVelocity;
			}
			else if (character.TryGet(out INPCBrain brain))
			{
				/* Monsters, wildlife and pets: the brain slides the NavMesh agent, which keeps the push on the walkable
				 * ground. Only the horizontal part survives that, so it is handed over flat and at full strength. */
				Vector3 flat = knockbackDirection;
				flat.y = 0f;
				if (flat.sqrMagnitude > 1e-6f)
				{
					brain.ApplyKnockback(flat.normalized * force);
				}
			}
		}

		/// <summary>
		/// Resolves which way <paramref name="target"/> is pushed by the attack this event describes.
		/// </summary>
		/// <returns>False when the event carries no ability hit at all, so there is no attack to take a direction from.</returns>
		private bool TryResolvePush(ICharacter initiator, EventData eventData, ICharacter target, out Vector3 direction)
		{
			direction = Vector3.zero;
			if (target?.Transform == null || !eventData.TryGet(out AbilityCollisionEventData hit))
			{
				return false;
			}

			AbilityObject abilityObject = hit.AbilityObject;
			bool hasObject = abilityObject != null && abilityObject.Transform != null;
			/* A fork carries its parent's hit as a sub-payload, so the hit's own target is the body the object struck
			 * and this event's target is the one the selector chose. They differ only when a selector fanned out. */
			bool fannedOut = hasObject && !ReferenceEquals(eventData.TargetCharacter, hit.TargetCharacter);
			AbilitySpawnTarget spawnTarget = hasObject && abilityObject.Ability?.Template != null
				? abilityObject.Ability.Template.AbilitySpawnTarget
				: AbilitySpawnTarget.Self;

			KnockbackOrigin origin = ResolveOrigin(Direction, hasObject, hit.IsSplash, fannedOut, spawnTarget);

			Vector3 casterPosition = initiator?.Transform != null ? initiator.Transform.position : target.Transform.position;
			Vector3 casterForward = initiator?.Transform != null ? initiator.Transform.forward : Vector3.forward;
			Vector3 heading = hasObject ? abilityObject.Transform.forward : casterForward;

			Vector3 centre;
			switch (origin)
			{
				case KnockbackOrigin.AbilityObject:
					centre = abilityObject.Transform.position;
					break;
				case KnockbackOrigin.Impact:
					centre = hit.HasHitPoint ? hit.HitPoint : abilityObject.Transform.position;
					break;
				case KnockbackOrigin.Caster:
					centre = casterPosition;
					break;
				default:
					centre = casterPosition;
					break;
			}

			direction = ResolveDirection(origin, heading, centre, target.Transform.position, hasObject ? heading : casterForward);
			return true;
		}

		/// <summary>
		/// Plays the attacker-side flinch for a knockback the server has not confirmed yet.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Cosmetic only. It offsets the target's model, never its networked position, so nothing
		/// the simulation, hit detection or lag compensation reads is affected — all of those work
		/// from the root transform and the position history.
		/// </para>
		/// <para>
		/// Suppressed on a replayed tick: a reconcile replays every tick since the correction, and
		/// restarting the flinch on each of them would make the model jitter for the length of the
		/// replay rather than lean once. Leans the way the authoritative branch will push, so the two agree.
		/// </para>
		/// </remarks>
		private void PlayPredictedReaction(ICharacter initiator, EventData eventData, float force)
		{
			if (IsReplayTick(eventData))
			{
				return;
			}

			if (!TryResolveTarget(eventData, out ICharacter target) || target == null ||
				(initiator != null && ReferenceEquals(target, initiator)))
			{
				return;
			}

			// Not on a character the harm rule protects from this attacker: the server will not move it either.
			if (!HarmRule.MayHarm(initiator, target, out _))
			{
				return;
			}

			if (!TryResolvePush(initiator, eventData, target, out Vector3 direction))
			{
				return;
			}

			CharacterHitReaction.PlayOn(target, direction);
			// The displacement itself, drawn now on the model and handed over to the server's as it arrives.
			CharacterHitReaction.ShoveOn(target, direction, force, DefaultDeceleration);
		}
	}
}
