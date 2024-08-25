using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	public abstract class BaseAIState : CachedScriptableObject<BaseAIState>, ICachedObject
	{
		private static Collider[] Hits = new Collider[10];

		public float UpdateRate;
		public bool RandomizeState;

		// Sweep
		public float DetectionRadius;
		public LayerMask EnemyLayers;

		// Agent
		public float Radius;
		public float Height;
		public float Speed;

		public abstract void Enter(AIController controller);
		public abstract void Exit(AIController controller);
		public abstract void UpdateState(AIController controller);

		/// <summary>
		/// Implement your line of sight logic here.
		/// </summary>
		public virtual bool HasLineOfSight(AIController controller, ICharacter target)
		{
			RaycastHit hit;
			Vector3 direction = (target.Transform.position - controller.Transform.position).normalized;
			return controller.PhysicsScene.Raycast(controller.Transform.position, direction, out hit) && hit.transform == target.Transform;
		}

		/// <summary>
		/// Implement your enemy detection logic here.
		/// </summary>
		public virtual bool SweepForEnemies(AIController controller, out List<ICharacter> detectedEnemies)
		{
			if (controller.Character == null ||
				controller.AttackingState == null ||
				!controller.Character.TryGet(out IFactionController ourFactionController))
			{
				detectedEnemies = null;
				return false;
			}

			int overlapCount = controller.PhysicsScene.OverlapSphere(
					controller.Transform.position,
					controller.CurrentState.DetectionRadius,
					Hits,
					controller.CurrentState.EnemyLayers,
					QueryTriggerInteraction.UseGlobal);

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
