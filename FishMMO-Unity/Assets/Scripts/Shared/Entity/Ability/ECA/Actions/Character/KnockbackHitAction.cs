using UnityEngine;
using System.Collections;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that applies a knockback force to a target character, pushing them away from the source.
	/// </summary>
	[CreateAssetMenu(fileName = "New Knockback Hit Action", menuName = "FishMMO/Triggers/Actions/Character/Knockback Hit")]
	public class KnockbackHitAction : BaseAction
	{
		/// <summary>
		/// The initial force applied to the target for the knockback effect.
		/// </summary>
		public float Force;

		/// <summary>
		/// Applies a knockback effect to the target character if they are not immortal.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">The event data containing the target and ability information.</param>
		/// <remarks>
		/// This method attempts to retrieve <see cref="CharacterHitEventData"/> and <see cref="AbilityCollisionEventData"/> from the event data. If successful, it applies a smooth knockback coroutine to the target.
		/// </remarks>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			// Check if the event data contains a valid target character and that the target is not immortal.
			if (eventData.TryGet(out CharacterHitEventData targetEventData) &&
				targetEventData.Target is BaseCharacter character &&
				character.TryGet(out ICharacterDamageController defenderDamageController) &&
				!defenderDamageController.Immortal)
			{
				// If the event data contains ability collision info, use the ability's forward direction for knockback.
				if (eventData.TryGet(out AbilityCollisionEventData abilityEventData) && abilityEventData.AbilityObject != null)
				{
					Vector3 knockbackDirection = abilityEventData.AbilityObject.Transform.forward;
					// Start a coroutine to smoothly apply knockback over time.
					character.StartCoroutine(SmoothKnockback(character.Transform, knockbackDirection, Force));
				}
			}
		}

		/// <summary>
		/// Coroutine that applies a smooth knockback effect to the target transform, gradually reducing the force.
		/// </summary>
		/// <param name="target">The transform to move.</param>
		/// <param name="direction">The direction in which to apply the knockback.</param>
		/// <param name="initialForce">The initial force to apply.</param>
		/// <returns>An enumerator for coroutine execution.</returns>
		private IEnumerator SmoothKnockback(Transform target, Vector3 direction, float initialForce)
		{
			// Continue applying knockback while there is still force left.
			while (initialForce > 0.0f)
			{
				target.position += direction * initialForce * Time.deltaTime;
				initialForce *= 0.8f; // Dampen the force each frame for a smooth effect.
				yield return null;
			}
		}

		/// <summary>
		/// Returns a formatted description of the knockback action for UI display.
		/// </summary>
		/// <returns>A string describing the force of the knockback.</returns>
		public override string GetFormattedDescription()
		{
			return $"Knocks the target back with <color=#FFD700>{Force}</color> force.";
		}
	}
}