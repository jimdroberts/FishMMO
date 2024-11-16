using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	public abstract class BaseAIState : CachedScriptableObject<BaseAIState>, ICachedObject
	{
		private static Collider[] Hits = new Collider[10];

		[SerializeField]
		private float updateRate = 1.0f;
		[Tooltip("The rate at which the AI will check if it should leash and return home. If 0 or below it will never leash.")]
		public float LeashUpdateRate = 5.0f;
		/// <summary>
		/// Minimum distance at which the AI will forget the current target and return home
		/// </summary>
		public float MinLeashRange = 100.0f;
		/// <summary>
		/// Maximum distance at which the AI will forget the current target and teleport return home
		/// </summary>
		public float MaxLeashRange = 200.0f;

		// Sweep
		public float DetectionRadius = 10;
		public LayerMask EnemyLayers;

		public virtual float GetUpdateRate() { return updateRate; }
		public abstract void Enter(AIController controller);
		public abstract void Exit(AIController controller);
		public abstract void UpdateState(AIController controller, float deltaTime);

		/// <summary>
		/// Implement your line of sight logic here.
		/// </summary>
		public virtual bool HasLineOfSight(AIController controller, ICharacter target)
		{
			RaycastHit hit;
			Vector3 direction = (target.Transform.position - controller.Character.Transform.position).normalized;
			return controller.PhysicsScene.Raycast(controller.Character.Transform.position, direction, out hit) && hit.transform == target.Transform;
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

			int overlapCount = controller.PhysicsScene.OverlapSphere(
					controller.Character.Transform.position,
					controller.CurrentState.DetectionRadius,
					Hits,
					controller.CurrentState.EnemyLayers,
					QueryTriggerInteraction.Ignore);

			detectedEnemies = new List<ICharacter>();

			for (int i = 0; i < overlapCount && detectedEnemies.Count < 10; ++i)
			{
				if (Hits[i] != controller.Character.Collider)
				{
					ICharacter def = Hits[i].gameObject.GetComponent<ICharacter>();
					if (def != null &&
						def.TryGet(out IFactionController defenderFactionController) &&
						def.TryGet(out ICharacterDamageController damageController) &&
						ourFactionController.GetAllianceLevel(defenderFactionController) == FactionAllianceLevel.Enemy &&
						HasLineOfSight(controller, def))
					{
						detectedEnemies.Add(def);
					}
				}
			}
			return detectedEnemies.Count > 0;
		}
	}
}
