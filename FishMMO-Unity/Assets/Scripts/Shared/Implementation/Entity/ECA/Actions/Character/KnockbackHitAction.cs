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
					if (character.TryGet(out IPlayerCharacter playerCharacter) &&
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
						motor.SetVelocity(currentVelocity);
					}
				}
			}
		}
	}
}