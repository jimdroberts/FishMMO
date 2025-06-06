using UnityEngine;
using System.Collections;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Knockback Hit Event", menuName = "FishMMO/Character/Ability/Hit Event/Knockback", order = 1)]
	public sealed class KnockbackHitEvent : HitEvent
	{
		public float Force;

		protected override int OnInvoke(ICharacter attacker, ICharacter defender, TargetInfo hitTarget, AbilityObject abilityObject)
		{
			if (abilityObject == null)
			{
				return 0;
			}

			BaseCharacter character = defender as BaseCharacter;
			if (character != null &&
				character.isActiveAndEnabled &&
				defender.TryGet(out ICharacterDamageController defenderDamageController) &&
				!defenderDamageController.Immortal)
			{
				// Calculate the knockback direction
				Vector3 knockbackDirection = abilityObject.transform.forward;

				character.StartCoroutine(SmoothKnockback(defender.Transform, knockbackDirection, Force));
			}

			// Knockback doesn't count as a hit
			return 0;
		}

		private IEnumerator SmoothKnockback(Transform target, Vector3 direction, float initialForce)
		{
			while (initialForce > 0.0f)
			{
				target.position += direction * initialForce * Time.deltaTime;
				initialForce *= 0.8f;
				yield return null;
			}
		}
	}
}