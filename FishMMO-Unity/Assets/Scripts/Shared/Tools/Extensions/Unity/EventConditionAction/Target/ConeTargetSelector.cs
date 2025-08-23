using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Selects all <see cref="GameObject"/>s within a cone in front of the context object.
	/// Useful for cone-shaped area-of-effect abilities.
	/// </summary>
	[CreateAssetMenu(fileName = "ConeTargetSelector", menuName = "FishMMO/TargetSelectors/Cone", order = 10)]
	public class ConeTargetSelector : TargetSelector
	{
		/// <summary>
		/// Radius of the cone.
		/// </summary>
		[Tooltip("Radius of the cone.")]
		public float Radius = 5f;

		/// <summary>
		/// Angle of the cone in degrees.
		/// </summary>
		[Tooltip("Angle of the cone in degrees.")]
		public float Angle = 45f;

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
		/// Initializes a new instance of the <see cref="ConeTargetSelector"/> class.
		/// </summary>
		void Awake() { hits = new Collider[MaxHits]; }

		/// <summary>
		/// Returns all <see cref="GameObject"/>s within a cone in front of the context object.
		/// </summary>
		/// <param name="context">The center <see cref="GameObject"/> for the cone search.</param>
		/// <returns>An enumerable of <see cref="GameObject"/>s within the cone, or empty if context is null.</returns>
		public override IEnumerable<GameObject> SelectTargets(GameObject context)
		{
			if (context == null) yield break;
			var scene = context.scene;
			PhysicsScene physicsScene = scene.GetPhysicsScene();
			Vector3 origin = context.transform.position;
			Vector3 forward = context.transform.forward;
			int hitCount = physicsScene.OverlapSphere(origin, Radius, hits, TargetLayer, QueryTriggerInteraction.UseGlobal);
			var initiator = context != null ? context.GetComponent<ICharacter>() : null;
			for (int i = 0; i < hitCount; i++)
			{
				var hit = hits[i];
				if (hit != null)
				{
					Vector3 toTarget = (hit.transform.position - origin).normalized;
					float dot = Vector3.Dot(forward, toTarget);
					float angleToTarget = Mathf.Acos(dot) * Mathf.Rad2Deg;
					if (angleToTarget <= Angle * 0.5f && AreConditionsMet(hit.gameObject, initiator))
						yield return hit.gameObject;
				}
			}
		}
	}
}