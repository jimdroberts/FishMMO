using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Selects a chain of <see cref="GameObject"/>s starting from the context object, such as for chain lightning or similar effects.
	/// Each link in the chain is the closest unselected <see cref="GameObject"/> within <see cref="ChainRadius"/> of the previous target.
	/// </summary>
	[CreateAssetMenu(fileName = "ChainTargetSelector", menuName = "FishMMO/TargetSelectors/Chain", order = 2)]
	public class ChainTargetSelector : TargetSelector
	{
		/// <summary>
		/// The maximum number of targets to select in the chain (including the initial context).
		/// </summary>
		[Tooltip("The maximum number of targets to select in the chain (including the initial context).")]
		public int ChainLength = 3;

		/// <summary>
		/// The radius to search for the next target in the chain, in Unity units.
		/// </summary>
		[Tooltip("The radius to search for the next target in the chain, in Unity units.")]
		public float ChainRadius = 5f;

		/// <summary>
		/// The layer mask used to filter which <see cref="GameObject"/>s can be selected as chain targets.
		/// </summary>
		[Tooltip("The layer mask used to filter which GameObjects can be selected as chain targets.")]
		public LayerMask TargetLayer;

		/// <summary>
		/// The maximum number of colliders to consider per OverlapSphere query.
		/// </summary>
		[Tooltip("The maximum number of colliders to consider per OverlapSphere query.")]
		public int MaxHits = 16;

		/// <summary>
		/// Preallocated array for storing collider hits during OverlapSphere queries.
		/// </summary>
		private Collider[] hits;

		/// <summary>
		/// Initializes a new instance of the <see cref="ChainTargetSelector"/> class.
		/// </summary>
		void Awake()
		{
			hits = new Collider[MaxHits];
		}

		/// <summary>
		/// Selects a chain of <see cref="GameObject"/>s starting from the given context object.
		/// Each subsequent target is the closest unselected <see cref="GameObject"/> within <see cref="ChainRadius"/> of the previous one.
		/// The chain will contain at most <see cref="ChainLength"/> targets.
		/// </summary>
		/// <param name="context">The starting <see cref="GameObject"/> for the chain selection. Must not be null.</param>
		/// <returns>An enumerable of <see cref="GameObject"/>s representing the chain of selected targets, starting with <paramref name="context"/>.</returns>
		public override IEnumerable<GameObject> SelectTargets(GameObject context)
		{
			if (context == null) yield break;
			var scene = context.scene;
			PhysicsScene physicsScene = scene.GetPhysicsScene();
			var selected = new HashSet<GameObject>();
			var current = context;
			var initiator = context != null ? context.GetComponent<ICharacter>() : null;
			for (int i = 0; i < ChainLength && current != null; i++)
			{
				if (!AreConditionsMet(current, initiator))
				{
					current = null;
					break;
				}
				selected.Add(current);
				yield return current;
				Vector3 center = current.transform.position;
				int hitCount = physicsScene.OverlapSphere(center, ChainRadius, hits, TargetLayer, QueryTriggerInteraction.UseGlobal);
				GameObject next = null;
				float minDist = float.MaxValue;
				for (int j = 0; j < hitCount; j++)
				{
					var hit = hits[j];
					if (hit != null && !selected.Contains(hit.gameObject) && AreConditionsMet(hit.gameObject, initiator))
					{
						float dist = Vector3.Distance(center, hit.transform.position);
						if (dist < minDist)
						{
							minDist = dist;
							next = hit.gameObject;
						}
					}
				}
				current = next;
			}
		}
	}
}