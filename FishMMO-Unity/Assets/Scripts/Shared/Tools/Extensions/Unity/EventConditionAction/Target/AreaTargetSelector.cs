using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Selects all <see cref="GameObject"/>s within a certain radius of the context object.
	/// Useful for area-of-effect abilities or detection.
	/// </summary>
	[CreateAssetMenu(fileName = "AreaTargetSelector", menuName = "FishMMO/TargetSelectors/Area", order = 1)]
	public class AreaTargetSelector : TargetSelector
	{
		/// <summary>
		/// Radius of the area effect.
		/// </summary>
		[Tooltip("Radius of the area effect.")]
		public float Radius = 5f;

		/// <summary>
		/// Maximum number of hits to process in the area.
		/// </summary>
		[Tooltip("Maximum number of hits to process in the area.")]
		public int MaxHits = 5;

		/// <summary>
		/// Layer mask to filter targets in the area.
		/// </summary>
		[Tooltip("Layer mask to filter targets in the area.")]
		public LayerMask TargetLayer = ~0; // All layers by default

		private Collider[] hits;

		/// <summary>
		/// Initializes a new instance of the <see cref="AreaTargetSelector"/> class.
		/// </summary>
		void Awake()
		{
			hits = new Collider[MaxHits];
		}

		/// <summary>
		/// Returns all <see cref="GameObject"/>s within <see cref="Radius"/> of the context object, filtered by <see cref="TargetLayer"/>.
		/// </summary>
		/// <param name="context">The center <see cref="GameObject"/> for the area search.</param>
		/// <returns>An enumerable of <see cref="GameObject"/>s within the area, or empty if context is null.</returns>
		public override IEnumerable<GameObject> SelectTargets(GameObject context)
		{
			if (context == null) yield break;
			var scene = context.scene;
			PhysicsScene physicsScene = scene.GetPhysicsScene();
			Vector3 center = context.transform.position;
			int hitCount = physicsScene.OverlapSphere(center, Radius, hits, TargetLayer, QueryTriggerInteraction.UseGlobal);
			var initiator = context != null ? context.GetComponent<ICharacter>() : null;
			for (int i = 0; i < hitCount; i++)
			{
				var hit = hits[i];
				if (hit != null && AreConditionsMet(hit.gameObject, initiator))
					yield return hit.gameObject;
			}
		}
	}
}