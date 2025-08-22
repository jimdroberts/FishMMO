using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Selects all <see cref="GameObject"/>s along a line (ray) from the context in a given direction and distance.
	/// Useful for beam, projectile, or piercing effects.
	/// </summary>
	[CreateAssetMenu(fileName = "LineTargetSelector", menuName = "FishMMO/TargetSelectors/Line", order = 13)]
	public class LineTargetSelector : TargetSelector
	{
		/// <summary>
		/// Length of the line.
		/// </summary>
		[Tooltip("Length of the line.")]
		public float Length = 10f;

		/// <summary>
		/// Layer mask to filter targets.
		/// </summary>
		[Tooltip("Layer mask to filter targets.")]
		public LayerMask TargetLayer = ~0;

		/// <summary>
		/// Maximum number of hits to process.
		/// </summary>
		[Tooltip("Maximum number of hits to process.")]
		public int MaxHits = 16;

		/// <summary>
		/// Preallocated array for storing raycast hits during line queries.
		/// </summary>
		private RaycastHit[] hits;

		/// <summary>
		/// Initializes a new instance of the <see cref="LineTargetSelector"/> class.
		/// </summary>
		public LineTargetSelector() { hits = new RaycastHit[MaxHits]; }

		/// <summary>
		/// Returns all <see cref="GameObject"/>s hit by a raycast from the context in its forward direction.
		/// </summary>
		/// <param name="context">The <see cref="GameObject"/> to cast the ray from.</param>
		/// <returns>An enumerable of <see cref="GameObject"/>s hit by the ray, or empty if none found.</returns>
		public override IEnumerable<GameObject> SelectTargets(GameObject context)
		{
			if (context == null) yield break;
			var scene = context.scene;
			PhysicsScene physicsScene = scene.GetPhysicsScene();
			Vector3 origin = context.transform.position;
			Vector3 direction = context.transform.forward;
			int hitCount = physicsScene.Raycast(origin, direction, hits, Length, TargetLayer, QueryTriggerInteraction.UseGlobal);
			var initiator = context != null ? context.GetComponent<ICharacter>() : null;
			for (int i = 0; i < hitCount; i++)
			{
				var hit = hits[i];
				if (hit.collider != null && AreConditionsMet(hit.collider.gameObject, initiator))
					yield return hit.collider.gameObject;
			}
		}
	}
}