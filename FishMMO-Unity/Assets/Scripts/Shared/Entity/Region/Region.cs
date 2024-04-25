using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	public class Region : MonoBehaviour
	{
		public Region Parent;
		public List<Region> Children = new List<Region>();

		public string Name { get { return gameObject.name; } }

		public Collider Collider;
		[Tooltip("Add a terrain if you would like the region to span the entire map. (Requires BoxCollider)")]
		public Terrain Terrain;

		public List<RegionAction> OnRegionEnter = new List<RegionAction>();
		public List<RegionAction> OnRegionStay = new List<RegionAction>();
		public List<RegionAction> OnRegionExit = new List<RegionAction>();

		void Awake()
		{
			Collider = gameObject.GetComponent<Collider>();
			if (Collider == null)
			{
				Debug.Log(Name + " collider is null and will not function properly.");
				return;
			}
			// set the collider to trigger just incase we forgot to set it in the inspector
			Collider.isTrigger = true;

			// terrain bounds override the collider
			if (Terrain != null)
			{
				BoxCollider box = Collider as BoxCollider;
				if (box != null)
				{
					box.size = Terrain.terrainData.size;
				}
			}
		}

#if UNITY_EDITOR
		public Color GizmoColor = Color.cyan;

		void OnDrawGizmos()
		{
			Collider collider = gameObject.GetComponent<Collider>();
			if (collider != null)
			{
				collider.DrawGizmo(GizmoColor);
			}
		}
#endif

		private void OnTriggerEnter(Collider other)
		{
			Character character = other.GetComponent<Character>();
			if (character != null &&
				!character.PredictionManager.IsReconciling)
			{
				// children take priority
				if (Children != null)
				{
					foreach (Region child in Children)
					{
						if (child.Collider.bounds.Intersects(other.bounds))
						{
							return;
						}
					}
				}
				foreach (RegionAction action in OnRegionEnter)
				{
					action.Invoke(character, this);
				}
			}
		}

		private void OnTriggerStay(Collider other)
		{
			Character character = other.GetComponent<Character>();
			if (character != null && !character.PredictionManager.IsReconciling)
			{
				foreach (RegionAction action in OnRegionStay)
				{
					action.Invoke(character, this);
				}
			}
		}

		private void OnTriggerExit(Collider other)
		{
			Character character = other.GetComponent<Character>();
			if (character != null && !character.PredictionManager.IsReconciling)
			{
				if (Parent != null)
				{
					Parent.OnTriggerEnter(other);
				}
				foreach (RegionAction action in OnRegionExit)
				{
					action.Invoke(character, this);
				}
			}
		}
	}
}