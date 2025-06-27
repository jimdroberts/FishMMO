using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	public abstract class BaseAIState : CachedScriptableObject<BaseAIState>, ICachedObject
	{
		[SerializeField]
		private float updateRate = 1.0f;
		[Tooltip("The rate at which the AI will check if it should leash and return home. If 0 or below it will never leash.")]
		public float LeashUpdateRate = 5.0f;
		/// <summary>
		/// Minimum distance at which the AI will forget the current target and return home
		/// </summary>
		public float MinLeashRange = 50.0f;
		/// <summary>
		/// Maximum distance at which the AI will forget the current target and teleport return home
		/// </summary>
		public float MaxLeashRange = 100.0f;

		// Sweep
		public float DetectionRadius = 10;
		public LayerMask EnemyLayers;
		public LayerMask LineOfSightBlockingLayers;

		public virtual float GetUpdateRate() { return updateRate; }
		public abstract void Enter(AIController controller);
		public abstract void Exit(AIController controller);
		public abstract void UpdateState(AIController controller, float deltaTime);

		/// <summary>
		/// Implement your line of sight logic here.
		/// </summary>
		public virtual bool HasLineOfSight(AIController controller, ICharacter target)
		{
			if (target == null || controller.Character == null)
			{
				return false;
			}

			// Define the ray origin. Use controller.EyeTransform if available, otherwise Character.Transform.position
			Vector3 rayOrigin = controller.EyeTransform.position;

			Vector3 targetPoint = target.Transform.position;
			if (target != null && target.Collider != null)
			{
				targetPoint = target.Collider.bounds.center;
			}

			Vector3 direction = (targetPoint - rayOrigin).normalized;
			float distance = Vector3.Distance(rayOrigin, targetPoint);

			RaycastHit hit;
			// IMPORTANT: Ensure the raycast originates slightly outside the AI's own collider to prevent self-intersection.
			float colliderRadius = 0.1f;

			// If the ray hits *anything* in the LineOfSightBlockingLayers before it hits the target, LOS is blocked.
			if (controller.PhysicsScene.Raycast(rayOrigin + direction * colliderRadius, direction, out hit, distance - colliderRadius, LineOfSightBlockingLayers))
			{
				// If the raycast hit something, check if it was the target itself.
				// If it hit something else *before* the target, then LOS is blocked.
				return hit.transform == target.Transform || hit.collider == target?.Collider;
			}
			return true;
		}

		/// <summary>
		/// Implement your enemy detection logic here.
		/// </summary>
		public virtual bool SweepForEnemies(AIController controller, out List<ICharacter> detectedEnemies)
		{
			if (controller.Character == null ||
				controller.AttackingState == null ||
				!controller.Character.TryGet(out IFactionController ourFactionController) ||
				controller.Observers.Count < 1)
			{
				detectedEnemies = null;
				return false;
			}

			if (controller.Character.TryGet(out ICharacterDamageController damageController) &&
				!damageController.IsAlive)
			{
				detectedEnemies = null;
				return false;
			}

			int overlapCount = controller.PhysicsScene.OverlapSphere(
					controller.Character.Transform.position,
					DetectionRadius,
					controller.SweepHits,
					EnemyLayers,
					QueryTriggerInteraction.Ignore);

			detectedEnemies = new List<ICharacter>();

			for (int i = 0; i < overlapCount && i < controller.SweepHits.Length; ++i)
			{
				Collider hitCollider = controller.SweepHits[i];
				if (hitCollider != controller.Character.Collider)
				{
					ICharacter def = controller.SweepHits[i].gameObject.GetComponent<ICharacter>();

					if (def != null &&
						def.TryGet(out IFactionController defenderFactionController) &&
						defenderFactionController.GetAllianceLevel(ourFactionController) == FactionAllianceLevel.Enemy)
					{
						bool lineOfSight = HasLineOfSight(controller, def);

						Log.Debug($"{controller.gameObject.name} Enemy Detected: {def.GameObject.name} | Line of Sight: {lineOfSight}");

						if (lineOfSight)
						{
							detectedEnemies.Add(def);
						}
					}
				}
			}
			return detectedEnemies.Count > 0;
		}
	}
}
