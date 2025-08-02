using System.Collections.Generic;
using UnityEngine;
using FishNet.Object;
using FishNet.Component.Prediction;
using System;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a networked region in the game world. Handles region hierarchy, collider setup, and triggers region actions on player entry, stay, and exit.
	/// </summary>
	[RequireComponent(typeof(NetworkTrigger))]
	public class Region : NetworkBehaviour
	{
		/// <summary>
		/// The parent region in the hierarchy. Used for nested region logic.
		/// </summary>
		public Region Parent;

		/// <summary>
		/// The child regions nested under this region. Managed at runtime.
		/// </summary>
		[NonSerialized]
		public List<Region> Children = new List<Region>();

		/// <summary>
		/// The name of the region, taken from the GameObject's name.
		/// </summary>
		public string Name { get { return gameObject.name; } }

		/// <summary>
		/// The collider that defines the region's bounds.
		/// </summary>
		public Collider Collider;

		/// <summary>
		/// Optional terrain reference. If set, overrides collider bounds to match terrain size (requires BoxCollider).
		/// </summary>
		[Tooltip("Add a terrain if you would like the region to span the entire map. (Requires BoxCollider)")]
		public Terrain Terrain;

		/// <summary>
		/// Actions to invoke when a player enters the region.
		/// </summary>
		public List<RegionAction> OnRegionEnter = new List<RegionAction>();

		/// <summary>
		/// Actions to invoke while a player stays in the region.
		/// </summary>
		public List<RegionAction> OnRegionStay = new List<RegionAction>();

		/// <summary>
		/// Actions to invoke when a player exits the region.
		/// </summary>
		public List<RegionAction> OnRegionExit = new List<RegionAction>();

		/// <summary>
		/// The NetworkTrigger component used to detect player entry, stay, and exit events.
		/// </summary>
		private NetworkTrigger networkTrigger;

		/// <summary>
		/// Initializes the region, sets up collider, terrain bounds, and event handlers for network triggers.
		/// </summary>
		void Awake()
		{
			// Set the region's layer to ignore raycasts.
			gameObject.layer = Constants.Layers.IgnoreRaycast;

			// Register this region as a child of its parent, if applicable.
			if (Parent != null)
			{
				Parent.Children.Add(this);
			}

			// Get and configure the collider for this region.
			Collider = gameObject.GetComponent<Collider>();
			if (Collider == null)
			{
				Log.Debug("Region", Name + " collider is null and will not function properly.");
				return;
			}
			// Ensure the collider is set as a trigger.
			Collider.isTrigger = true;

			// If terrain is assigned, override collider bounds to match terrain size (BoxCollider only).
			if (Terrain != null)
			{
				BoxCollider box = Collider as BoxCollider;
				if (box != null)
				{
					box.size = Terrain.terrainData.size;
				}
			}

			// Set up network trigger event handlers for region entry, stay, and exit.
			networkTrigger = gameObject.GetComponent<NetworkTrigger>();
			if (networkTrigger != null)
			{
				networkTrigger.OnEnter += NetworkCollider_OnEnter;
				networkTrigger.OnStay += NetworkCollider_OnStay;
				networkTrigger.OnExit += NetworkCollider_OnExit;
			}
		}

#if UNITY_EDITOR
		/// <summary>
		/// The color used to draw the region's gizmo in the editor.
		/// </summary>
		public Color GizmoColor = Color.cyan;

		/// <summary>
		/// Draws the region's collider gizmo in the editor for visualization.
		/// </summary>
		void OnDrawGizmos()
		{
			Collider collider = gameObject.GetComponent<Collider>();
			if (collider != null)
			{
				collider.DrawGizmo(GizmoColor);
			}
		}
#endif

		/// <summary>
		/// Handles logic when a player enters the region's collider. Children regions take priority over parents.
		/// </summary>
		/// <param name="other">The collider of the entering object.</param>
		private void NetworkCollider_OnEnter(Collider other)
		{
			if (other == null)
			{
				return;
			}
			IPlayerCharacter character = other.GetComponent<IPlayerCharacter>();
			if (character == null)
			{
				return;
			}
			if (character.IsTeleporting)
			{
				return;
			}
			// Children regions take priority: if any child contains the character, do not process parent entry.
			if (Children != null && other != null)
			{
				foreach (Region child in Children)
				{
					if (child == null)
					{
						continue;
					}
					// If a child region contains the character, skip parent entry logic.
					if (child.Collider.bounds.Intersects(other.bounds))
					{
						//Log.Debug($"OnEnter: {other.gameObject.name} intersects child {child.gameObject.name}");
						return;
					}
				}
			}
			// Notify parent region of exit, if applicable.
			if (Parent != null)
			{
				Parent.NetworkCollider_OnExit(other);
			}
			// Invoke all region entry actions.
			if (OnRegionEnter != null)
			{
				//Log.Debug($"OnEnter: {character.CharacterName} Entered {gameObject.name}");
				foreach (RegionAction action in OnRegionEnter)
				{
					action?.Invoke(character, this, base.PredictionManager.IsReconciling);
				}
			}
		}

		/// <summary>
		/// Handles logic while a player stays within the region's collider.
		/// </summary>
		/// <param name="other">The collider of the staying object.</param>
		private void NetworkCollider_OnStay(Collider other)
		{
			if (other == null)
			{
				return;
			}
			IPlayerCharacter character = other.GetComponent<IPlayerCharacter>();
			if (character == null)
			{
				return;
			}
			if (character.IsTeleporting)
			{
				return;
			}
			// Invoke all region stay actions.
			if (OnRegionStay != null)
			{
				foreach (RegionAction action in OnRegionStay)
				{
					action?.Invoke(character, this, base.PredictionManager.IsReconciling);
				}
			}
		}

		/// <summary>
		/// Handles logic when a player exits the region's collider.
		/// </summary>
		/// <param name="other">The collider of the exiting object.</param>
		private void NetworkCollider_OnExit(Collider other)
		{
			if (other == null)
			{
				return;
			}
			IPlayerCharacter character = other.GetComponent<IPlayerCharacter>();
			if (character == null)
			{
				return;
			}
			if (character.IsTeleporting)
			{
				return;
			}
			// Invoke all region exit actions.
			if (OnRegionExit != null)
			{
				//Log.Debug($"OnExit: {character.CharacterName} Exited {gameObject.name}");
				foreach (RegionAction action in OnRegionExit)
				{
					action?.Invoke(character, this, base.PredictionManager.IsReconciling);
				}
			}
			// Notify parent region of entry, if applicable.
			if (Parent != null)
			{
				Parent.NetworkCollider_OnEnter(other);
			}
		}
	}
}