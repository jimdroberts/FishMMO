using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Selects all <see cref="GameObject"/>s within a certain radius of the context object.
	/// Useful for area-of-effect abilities or detection.
	/// </summary>
	[Serializable]
	public class AreaTargetSelector : TargetSelector
	{
		/// <summary>
		/// Radius of the area effect.
		/// </summary>
		[Tooltip("Radius of the area effect.")]
		[Min(0f)]
		public float Radius = 5f;

		/// <summary>
		/// Maximum number of hits to process in the area.
		/// </summary>
		[Tooltip("Maximum number of hits to process in the area.")]
		[Min(1)]
		public int MaxHits = 5;

		/// <summary>
		/// Layer mask to filter targets in the area.
		/// </summary>
		[Tooltip("Layer mask to filter targets in the area.")]
		public LayerMask TargetLayer = ~0; // All layers by default

		private Collider[] hits;

		/// <summary>
		/// Returns all <see cref="GameObject"/>s within <see cref="Radius"/> of the context object, filtered by <see cref="TargetLayer"/>.
		/// </summary>
		/// <param name="context">The center <see cref="GameObject"/> for the area search.</param>
		/// <returns>An enumerable of <see cref="GameObject"/>s within the area, or empty if context is null.</returns>
		public override IEnumerable<GameObject> SelectTargets(EventData eventData)
		{
			GameObject context = GetContext(eventData);
			if (context == null) yield break;
			EnsureHitBuffer();
			var scene = context.scene;
			PhysicsScene physicsScene = scene.GetPhysicsScene();
			Vector3 center = context.transform.position;
			int hitCount = physicsScene.OverlapSphere(center, Radius, hits, TargetLayer, QueryTriggerInteraction.UseGlobal);
			for (int i = 0; i < hitCount; i++)
			{
				Collider hit = hits[i];
				if (hit != null && AreConditionsMet(hit.gameObject, eventData))
					yield return hit.gameObject;
			}
		}

		/// <summary>
		/// Ensures the reusable collider buffer matches <see cref="MaxHits"/>.
		/// </summary>
		private void EnsureHitBuffer()
		{
			int maxHits = Mathf.Max(1, MaxHits);
			if (hits == null || hits.Length != maxHits)
			{
				hits = new Collider[maxHits];
			}
		}
	}
}