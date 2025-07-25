using UnityEngine;
using System.Collections;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Knockback Hit Action", menuName = "FishMMO/Triggers/Actions/Character/Knockback Hit")]
	public class KnockbackHitAction : BaseAction
	{
		public float Force;

		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (eventData.TryGet(out CharacterHitEventData targetEventData) &&
				targetEventData.Target is BaseCharacter character &&
				character.TryGet(out ICharacterDamageController defenderDamageController) &&
				!defenderDamageController.Immortal)
			{
				if (eventData.TryGet(out AbilityCollisionEventData abilityEventData) && abilityEventData.AbilityObject != null)
				{
					Vector3 knockbackDirection = abilityEventData.AbilityObject.Transform.forward;
					character.StartCoroutine(SmoothKnockback(character.Transform, knockbackDirection, Force));
				}
			}
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

		public override string GetFormattedDescription()
		{
			return $"Knocks the target back with <color=#FFD700>{Force}</color> force.";
		}
	}
}