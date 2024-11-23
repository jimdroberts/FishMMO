using UnityEngine;
using System.Collections;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Knockback Hit Event", menuName = "Character/Ability/Hit Event/Knockback", order = 1)]
	public sealed class KnockbackHitEvent : HitEvent
	{
		public float Force;

		public override int Invoke(ICharacter attacker, ICharacter defender, TargetInfo hitTarget, AbilityObject abilityObject)
		{
			if (attacker == null ||
				defender == null ||
				attacker.ID == defender.ID ||
				abilityObject == null)
			{
				return 0;
			}

			/*if (attacker.TryGet(out IFactionController attackerFactionController) &&
				defender.TryGet(out IFactionController defenderFactionController) &&
				attackerFactionController.GetAllianceLevel(defenderFactionController) == FactionAllianceLevel.Enemy)
			{
				Debug.Log($"Knockback! {Force}");
			}*/

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