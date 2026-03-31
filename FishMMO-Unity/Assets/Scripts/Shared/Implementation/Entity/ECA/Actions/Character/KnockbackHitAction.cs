using System;
using UnityEngine;
using System.Collections;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that applies a knockback force to a target character, pushing them away from the source.
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

			ICharacter target = ResolveTarget(initiator, eventData);
			if (target is BaseCharacter character &&
				character.TryGet(out ICharacterDamageController defenderDamageController) &&
				!defenderDamageController.Immortal)
			{
				if (eventData.TryGet(out AbilityCollisionEventData abilityEventData) && abilityEventData.AbilityObject != null)
				{
					Vector3 knockbackDirection = abilityEventData.AbilityObject.Transform.forward;
					float force = ForceValue.GetValue(initiator, eventData);
					character.StartCoroutine(SmoothKnockback(character.Transform, knockbackDirection, force));
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
	}
}