using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Selects the furthest <see cref="GameObject"/> from the context within a given radius and layer mask.
	/// Useful for targeting the most distant enemy, ally, or object.
	/// </summary>
	[CreateAssetMenu(fileName = "FurthestTargetSelector", menuName = "FishMMO/TargetSelectors/Furthest", order = 12)]
	public class FurthestTargetSelector : TargetSelector
	{
		/// <summary>
		/// Radius to search for targets.
		/// </summary>
		[Tooltip("Radius to search for targets.")]
		public float Radius = 10f;

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
		/// Preallocated array for storing collider hits during OverlapSphere queries.
		/// </summary>
		private Collider[] hits;

		/// <summary>
		/// Initializes a new instance of the <see cref="FurthestTargetSelector"/> class.
		/// </summary>
		void Awake() { hits = new Collider[MaxHits]; }

		/// <summary>
		/// Returns the furthest <see cref="GameObject"/> from the context within <see cref="Radius"/>.
		/// </summary>
		/// <param name="context">The <see cref="GameObject"/> to search from.</param>
		/// <returns>An enumerable containing the furthest <see cref="GameObject"/>, or empty if none found.</returns>
		public override IEnumerable<GameObject> SelectTargets(GameObject context)
		{
			if (context == null) yield break;
			var scene = context.scene;
			PhysicsScene physicsScene = scene.GetPhysicsScene();
			Vector3 origin = context.transform.position;
			int hitCount = physicsScene.OverlapSphere(origin, Radius, hits, TargetLayer, QueryTriggerInteraction.UseGlobal);
			GameObject furthest = null;
			float maxDist = float.MinValue;
			var initiator = context != null ? context.GetComponent<ICharacter>() : null;
			for (int i = 0; i < hitCount; i++)
			{
				var hit = hits[i];
				if (hit != null && hit.gameObject != context && AreConditionsMet(hit.gameObject, initiator))
				{
					float dist = Vector3.Distance(origin, hit.transform.position);
					if (dist > maxDist)
					{
						maxDist = dist;
						furthest = hit.gameObject;
					}
				}
			}
			if (furthest != null)
				yield return furthest;
		}
	}
}