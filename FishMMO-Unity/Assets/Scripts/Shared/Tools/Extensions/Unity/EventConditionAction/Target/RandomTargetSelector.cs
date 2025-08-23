using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Selects a random <see cref="GameObject"/> from all within a given radius and layer mask.
	/// Useful for random targeting effects or abilities.
	/// </summary>
	[CreateAssetMenu(fileName = "RandomTargetSelector", menuName = "FishMMO/TargetSelectors/Random", order = 15)]
	public class RandomTargetSelector : TargetSelector
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
		/// Initializes a new instance of the <see cref="RandomTargetSelector"/> class.
		/// </summary>
		void Awake() { hits = new Collider[MaxHits]; }

		/// <summary>
		/// Returns a random <see cref="GameObject"/> from all within <see cref="Radius"/> of the context.
		/// </summary>
		/// <param name="context">The <see cref="GameObject"/> to search from.</param>
		/// <returns>An enumerable containing one random <see cref="GameObject"/>, or empty if none found.</returns>
		public override IEnumerable<GameObject> SelectTargets(GameObject context)
		{
			if (context == null) yield break;
			var scene = context.scene;
			PhysicsScene physicsScene = scene.GetPhysicsScene();
			Vector3 origin = context.transform.position;
			int hitCount = physicsScene.OverlapSphere(origin, Radius, hits, TargetLayer, QueryTriggerInteraction.UseGlobal);
			var initiator = context != null ? context.GetComponent<ICharacter>() : null;
			List<GameObject> candidates = new List<GameObject>();
			for (int i = 0; i < hitCount; i++)
			{
				var hit = hits[i];
				if (hit != null && hit.gameObject != context && AreConditionsMet(hit.gameObject, initiator))
					candidates.Add(hit.gameObject);
			}
			if (candidates.Count > 0)
			{
				int idx = Random.Range(0, candidates.Count);
				yield return candidates[idx];
			}
		}
	}
}