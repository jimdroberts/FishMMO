using System.Collections.Generic;
using UnityEngine;
using FishNet.Object;
using FishNet.Component.Prediction;
using System;

namespace FishMMO.Shared
{
	[RequireComponent(typeof(NetworkTrigger))]
	public class Region : NetworkBehaviour
	{
		public Region Parent;
		[NonSerialized]
		public List<Region> Children = new List<Region>();

		public string Name { get { return gameObject.name; } }

		public Collider Collider;
		[Tooltip("Add a terrain if you would like the region to span the entire map. (Requires BoxCollider)")]
		public Terrain Terrain;

		public List<RegionAction> OnRegionEnter = new List<RegionAction>();
		public List<RegionAction> OnRegionStay = new List<RegionAction>();
		public List<RegionAction> OnRegionExit = new List<RegionAction>();

		private NetworkTrigger networkTrigger;

		void Awake()
		{
			gameObject.layer = Constants.Layers.IgnoreRaycast;

			if (Parent != null)
			{
				Parent.Children.Add(this);
			}

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

			networkTrigger = gameObject.GetComponent<NetworkTrigger>();
			if (networkTrigger != null)
			{
				networkTrigger.OnEnter += NetworkCollider_OnEnter;
				networkTrigger.OnStay += NetworkCollider_OnStay;
				networkTrigger.OnExit += NetworkCollider_OnExit;
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

		private void NetworkCollider_OnEnter(Collider other)
		{
			IPlayerCharacter character = other.GetComponent<IPlayerCharacter>();
			if (character == null)
			{
				return; 
			}
			if (character.IsTeleporting)
			{
				return;
			}
			// children take priority
			if (Children != null && other != null)
			{
				foreach (Region child in Children)
				{
					if (child == null)
					{
						continue;
					}
					// does a child of this region already contain our character?
					if (child.Collider.bounds.Intersects(other.bounds))
					{
						//Debug.Log($"OnEnter: {other.gameObject.name} intersects child {child.gameObject.name}");
						return;
					}
				}
			}
			if (Parent != null)
			{
				Parent.NetworkCollider_OnExit(other);
			}
			if (OnRegionEnter != null)
			{
				//Debug.Log($"OnEnter: {character.CharacterName} Entered {gameObject.name}");
				foreach (RegionAction action in OnRegionEnter)
				{
					action?.Invoke(character, this, base.PredictionManager.IsReconciling);
				}
			}
		}

		private void NetworkCollider_OnStay(Collider other)
		{
			IPlayerCharacter character = other.GetComponent<IPlayerCharacter>();
			if (character == null)
			{
				return;
			}
			if (character.IsTeleporting)
			{
				return;
			}
			if (OnRegionStay != null)
			{
				foreach (RegionAction action in OnRegionStay)
				{
					action?.Invoke(character, this, base.PredictionManager.IsReconciling);
				}
			}
		}

		private void NetworkCollider_OnExit(Collider other)
		{
			IPlayerCharacter character = other.GetComponent<IPlayerCharacter>();
			if (character == null)
			{
				return;
			}
			if (character.IsTeleporting)
			{
				return;
			}
			if (OnRegionExit != null)
			{
				//Debug.Log($"OnExit: {character.CharacterName} Exited {gameObject.name}");
				foreach (RegionAction action in OnRegionExit)
				{
					action?.Invoke(character, this, base.PredictionManager.IsReconciling);
				}
			}
			if (Parent != null)
			{
				Parent.NetworkCollider_OnEnter(other);
			}
		}
	}
}