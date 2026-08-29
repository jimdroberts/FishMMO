using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that applies a knockback force to a target character, pushing them away from the source.
	/// Uses the KCC motor's velocity system for deterministic, collision-aware knockback
	/// instead of direct Transform.position manipulation which bypasses prediction,
	/// reconciliation, and collision detection. Runs on both client (prediction) and
	/// server (authoritative); the KCC motor velocity is deterministic so the server
	/// reconcile will correct any misprediction.
	/// </summary>
	[Serializable]
	public class KnockbackHitAction : BaseAction
	{
		/// <summary>
		/// The value provider that determines the initial knockback force applied to the target.
		/// </summary>
		[Tooltip("The value provider that determines the initial knockback force.")]
		[SerializeReference, SubclassSelector]
		public IFloatValueProvider ForceValue;

		/// <summary>
		/// Applies a knockback effect to the target character if they are not immortal.
		/// Knockback is applied as a single-tick velocity impulse through the KCC motor,
		/// ensuring deterministic prediction, server reconciliation, and collision awareness.
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
			if (!EcaAuthority.IsServer(initiator, eventData))
			{
				if (EcaAuthority.MayPredict(initiator, eventData))
				{
					PlayPredictedReaction(eventData);
				}
				return;
			}

			if (ForceValue == null)
			{
				Log.Warning("KnockbackHitAction", "ForceValue provider is null.");
				return;
			}

			if (!TryResolveTarget(eventData, out ICharacter target))
			{
				return;
			}
			if (target is BaseCharacter character &&
				character.TryGet(out ICharacterDamageController defenderDamageController) &&
				!defenderDamageController.Immortal)
			{
				if (eventData.TryGet(out AbilityCollisionEventData abilityEventData) && abilityEventData.AbilityObject != null)
				{
					Vector3 knockbackDirection = abilityEventData.AbilityObject.Transform.forward;
					float force = ForceValue.GetValue(initiator, eventData);

					// Apply knockback as a velocity impulse through the KCC motor.
					// This respects collision geometry, prediction/reconciliation,
					// and is deterministic across client and server.
					if (character is IPlayerCharacter playerCharacter &&
						playerCharacter.CharacterController?.Motor != null)
					{
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
				}
			}
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
		/// replay rather than lean once.
		/// </para>
		/// </remarks>
		private static void PlayPredictedReaction(EventData eventData)
		{
			if (IsReplayTick(eventData))
			{
				return;
			}

			if (!TryResolveTarget(eventData, out ICharacter target) || target == null)
			{
				return;
			}

			/* The ability object's forward is the impact direction the authoritative branch uses
			 * below, so the predicted lean points the same way the real displacement will. Without
			 * one there is no direction to lean along and nothing is played. */
			if (!eventData.TryGet(out AbilityCollisionEventData abilityEventData) ||
				abilityEventData.AbilityObject == null)
			{
				return;
			}

			CharacterHitReaction.PlayOn(target, abilityEventData.AbilityObject.Transform.forward);
		}

	}
}