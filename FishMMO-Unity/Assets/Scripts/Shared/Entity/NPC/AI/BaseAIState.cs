using System.Collections.Generic;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for all AI states. Defines common parameters and logic for state transitions, leash checks, enemy detection, and line of sight.
	/// </summary>
	public abstract class BaseAIState : CachedScriptableObject<BaseAIState>, ICachedObject
	{
		[SerializeField]
		private float updateRate = 1.0f;

		/// <summary>
		/// The rate (in seconds) at which the AI will check if it should leash and return home. If 0 or below, leashing is disabled.
		/// </summary>
		[Tooltip("The rate at which the AI will check if it should leash and return home. If 0 or below it will never leash.")]
		public float LeashUpdateRate = 5.0f;

		/// <summary>
		/// Minimum distance at which the AI will forget the current target and return home.
		/// </summary>
		public float MinLeashRange = 50.0f;

		/// <summary>
		/// Maximum distance at which the AI will forget the current target and teleport home.
		/// </summary>
		public float MaxLeashRange = 100.0f;

		/// <summary>
		/// The radius within which the AI will detect enemies.
		/// </summary>
		public float DetectionRadius = 10;

		/// <summary>
		/// The layers considered as enemies for detection.
		/// </summary>
		public LayerMask EnemyLayers;

		/// <summary>
		/// The layers that block line of sight checks.
		/// </summary>
		public LayerMask LineOfSightBlockingLayers;

		/// <summary>
		/// Returns the update rate for this state (in seconds).
		/// </summary>
		public virtual float GetUpdateRate() { return updateRate; }

		/// <summary>
		/// Called when the state is entered. Implement state-specific logic here.
		/// </summary>
		/// <param name="controller">The AI controller managing this NPC.</param>
		public abstract void Enter(AIController controller);

		/// <summary>
		/// Called when the state is exited. Implement cleanup logic here.
		/// </summary>
		/// <param name="controller">The AI controller managing this NPC.</param>
		public abstract void Exit(AIController controller);

		/// <summary>
		/// Called every frame while in this state. Implement state update logic here.
		/// </summary>
		/// <param name="controller">The AI controller managing this NPC.</param>
		/// <param name="deltaTime">Time since last update.</param>
		public abstract void UpdateState(AIController controller, float deltaTime);

		/// <summary>
		/// Checks if the AI has line of sight to the target. Uses raycasting to determine if any objects block the view.
		/// </summary>
		/// <param name="controller">The AI controller managing this NPC.</param>
		/// <param name="target">The target character to check line of sight to.</param>
		/// <returns>True if line of sight exists, false otherwise.</returns>
		public virtual bool HasLineOfSight(AIController controller, ICharacter target)
		{
			if (target == null || controller.Character == null)
			{
				return false;
			}

			// Use the eye transform for ray origin if available, otherwise use character position.
			Vector3 rayOrigin = controller.EyeTransform.position;

			// Use the center of the target's collider if available for more accurate targeting.
			Vector3 targetPoint = target.Transform.position;
			if (target != null && target.Collider != null)
			{
				targetPoint = target.Collider.bounds.center;
			}

			Vector3 direction = (targetPoint - rayOrigin).normalized;
			float distance = Vector3.Distance(rayOrigin, targetPoint);

			RaycastHit hit;
			// Offset the ray origin slightly to avoid self-intersection with the AI's own collider.
			float colliderRadius = 0.1f;

			// Raycast to check if anything blocks line of sight before reaching the target.
			if (controller.PhysicsScene.Raycast(rayOrigin + direction * colliderRadius, direction, out hit, distance - colliderRadius, LineOfSightBlockingLayers))
			{
				// If the raycast hit something, check if it was the target itself.
				// If it hit something else before the target, line of sight is blocked.
				return hit.transform == target.Transform || hit.collider == target?.Collider;
			}
			return true;
		}

		/// <summary>
		/// Sweeps for nearby enemies using physics overlap checks and faction logic. Returns true if any enemies are detected with line of sight.
		/// </summary>
		/// <param name="controller">The AI controller managing this NPC.</param>
		/// <param name="detectedEnemies">List of detected enemy characters.</param>
		/// <returns>True if any enemies are detected, false otherwise.</returns>
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

			// Only sweep for enemies if alive.
			if (controller.Character.TryGet(out ICharacterDamageController damageController) &&
				!damageController.IsAlive)
			{
				detectedEnemies = null;
				return false;
			}

			// Use physics overlap sphere to find nearby colliders in enemy layers.
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
				// Ignore self collider.
				if (hitCollider != controller.Character.Collider)
				{
					ICharacter def = controller.SweepHits[i].gameObject.GetComponent<ICharacter>();

					// Check faction alliance and only add enemies.
					if (def != null &&
						def.TryGet(out IFactionController defenderFactionController) &&
						defenderFactionController.GetAllianceLevel(ourFactionController) == FactionAllianceLevel.Enemy)
					{
						bool lineOfSight = HasLineOfSight(controller, def);

						Log.Debug("BaseAIState", $"{controller.gameObject.name} Enemy Detected: {def.GameObject.name} | Line of Sight: {lineOfSight}");

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
