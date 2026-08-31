using System.Collections.Generic;
using FishMMO.Logging;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Manages visibility of pre-split body region SkinnedMeshRenderers.
	/// Tracks per-slot hidden regions so unequipping one item doesn't reveal
	/// a region still hidden by another equipped item.
	/// </summary>
	public class BodyVisibilityManager : CharacterBehaviour, IBodyVisibilityManager, IModelReadyHandler
	{
		/// <summary>
		/// Maps each BodyRegion to its SkinnedMeshRenderer on the instantiated model.
		/// </summary>
		private readonly Dictionary<BodyRegion, SkinnedMeshRenderer> regionRenderers =
			new Dictionary<BodyRegion, SkinnedMeshRenderer>();

		/// <summary>
		/// Tracks which BodyRegions are hidden by which equipment slot.
		/// Key: ItemSlot, Value: set of BodyRegions hidden by that slot.
		/// </summary>
		private readonly Dictionary<ItemSlot, HashSet<BodyRegion>> slotHiddenRegions =
			new Dictionary<ItemSlot, HashSet<BodyRegion>>();

		/// <summary>
		/// Reference count per region — how many slots are currently hiding it.
		/// A region is visible only when its count is zero.
		/// </summary>
		private readonly Dictionary<BodyRegion, int> regionHideCounts =
			new Dictionary<BodyRegion, int>();

		/// <summary>
		/// The skeleton root Transform, cached for discovery.
		/// </summary>
		private Transform skeletonRoot;

		/// <inheritdoc />
		public Transform SkeletonRoot => skeletonRoot;

		/// <summary>
		/// Initial discovery attempt. May fail if model hasn't loaded yet.
		/// Full discovery happens in <see cref="OnModelReady"/>.
		/// </summary>
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();

#if !UNITY_SERVER
			if (Character == null)
			{
				return;
			}

			TryDiscoverRegionRenderers();
#endif
		}

		/// <inheritdoc />
		public void OnModelReady()
		{
#if !UNITY_SERVER
			// Clear previous state and re-discover from the now-loaded model
			ForgetDiscoveredModel();

			TryDiscoverRegionRenderers();
#endif
		}

		/// <summary>
		/// Forgets the model this manager discovered before the object returns to the pool.
		/// </summary>
		/// <remarks>
		/// Everything here points into a character model that is about to be destroyed: renderers
		/// keyed by body region, the per-slot hide counts that decide which of them are visible,
		/// and the skeleton root <c>CharacterAppearanceManager</c> and
		/// <c>EquipmentVisualController</c> both read from this component. Only
		/// <see cref="OnModelReady"/> cleared them, and that fires when the next model finishes
		/// loading — asynchronously, and not at all if the next occupant of the pool slot fails
		/// to load a model. Until then the two readers above would bind their meshes to a dead
		/// skeleton, and the inherited hide counts would leave body regions switched off on a
		/// character wearing nothing that hides them.
		/// </remarks>
		/// <param name="asServer">True if called on the server.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

#if !UNITY_SERVER
			ForgetDiscoveredModel();
#endif
		}

#if !UNITY_SERVER
		/// <summary>
		/// Drops every reference discovered from the current model. Shared by
		/// <see cref="OnModelReady"/> (re-discovery) and <see cref="ResetState(bool)"/> (pool).
		/// </summary>
		private void ForgetDiscoveredModel()
		{
			regionRenderers.Clear();
			regionHideCounts.Clear();
			slotHiddenRegions.Clear();
			skeletonRoot = null;
		}
#endif

#if !UNITY_SERVER
		/// <summary>
		/// Finds all body region renderers by name under the character's MeshRoot.
		/// Logs an error for any missing region — the model is incorrectly authored.
		/// </summary>
		private void TryDiscoverRegionRenderers()
		{
			Transform meshRoot = Character.MeshRoot;
			if (meshRoot == null)
			{
				Log.Warning("BodyVisibilityManager", "MeshRoot is null. Cannot discover body regions.");
				return;
			}

			// Find the skeleton root (first child with an Animator, or the model root itself)
			skeletonRoot = FindSkeletonRoot(meshRoot);

			// Initialize hide counts
			for (int i = 0; i < System.Enum.GetNames(typeof(BodyRegion)).Length; i++)
			{
				regionHideCounts[(BodyRegion)i] = 0;
			}

			// Discover each region renderer by its expected name
			BodyRegion[] allRegions = (BodyRegion[])System.Enum.GetValues(typeof(BodyRegion));
			for (int i = 0; i < allRegions.Length; i++)
			{
				BodyRegion region = allRegions[i];
				string rendererName = SkeletonBones.GetRegionRendererName(region);

				Transform regionTransform = meshRoot.Find(rendererName);
				if (regionTransform == null)
				{
					// Search recursively in case it's nested
					regionTransform = FindChildRecursive(meshRoot, rendererName);
				}

				if (regionTransform != null)
				{
					SkinnedMeshRenderer renderer = regionTransform.GetComponent<SkinnedMeshRenderer>();
					if (renderer != null)
					{
						regionRenderers[region] = renderer;
					}
					else
					{
						Log.Error("BodyVisibilityManager",
							$"Body region '{rendererName}' found but has no SkinnedMeshRenderer. Model is incorrectly authored.");
					}
				}
				else
				{
					Log.Error("BodyVisibilityManager",
						$"Body region '{rendererName}' not found in model. Model is incorrectly authored — " +
						$"the body mesh must be split into separate GameObjects named BodyHead, BodyTorso, BodyArms, BodyHands, BodyLegs, BodyFeet.");
				}
			}
		}

		/// <summary>Finds the skeleton root — the first Transform with bone children under the mesh root.</summary>
		private Transform FindSkeletonRoot(Transform meshRoot)
		{
			// Look for the Animator component — its transform is typically the skeleton root
			Animator animator = meshRoot.GetComponentInChildren<Animator>();
			if (animator != null)
			{
				return animator.transform;
			}
			// Fallback: assume first child is the model root with the skeleton
			if (meshRoot.childCount > 0)
			{
				return meshRoot.GetChild(0);
			}
			return meshRoot;
		}

		/// <summary>
		/// Recursively searches for a child Transform by name.
		/// </summary>
		private Transform FindChildRecursive(Transform parent, string name)
		{
			if (parent.name == name)
			{
				return parent;
			}
			for (int i = 0; i < parent.childCount; i++)
			{
				Transform found = FindChildRecursive(parent.GetChild(i), name);
				if (found != null)
				{
					return found;
				}
			}
			return null;
		}
#endif

		/// <inheritdoc />
		public void HideRegions(BodyRegion[] regions, ItemSlot slot)
		{
#if !UNITY_SERVER
			if (regions == null || regions.Length == 0)
			{
				return;
			}

			// Track which regions this slot is hiding
			if (!slotHiddenRegions.TryGetValue(slot, out HashSet<BodyRegion> hiddenSet))
			{
				hiddenSet = new HashSet<BodyRegion>();
				slotHiddenRegions[slot] = hiddenSet;
			}

			for (int i = 0; i < regions.Length; i++)
			{
				BodyRegion region = regions[i];

				// Increment hide count
				if (!regionHideCounts.ContainsKey(region))
				{
					regionHideCounts[region] = 0;
				}
				regionHideCounts[region]++;

				// Track which slot hides this
				hiddenSet.Add(region);

				// Hide the renderer if it exists and was visible
				if (regionRenderers.TryGetValue(region, out SkinnedMeshRenderer renderer) &&
					renderer != null)
				{
					renderer.enabled = false;
				}
			}
#endif
		}

		/// <inheritdoc />
		public void ShowRegionsForSlot(ItemSlot slot)
		{
#if !UNITY_SERVER
			if (!slotHiddenRegions.TryGetValue(slot, out HashSet<BodyRegion> hiddenSet))
			{
				return;
			}

			foreach (BodyRegion region in hiddenSet)
			{
				// Decrement hide count
				if (regionHideCounts.TryGetValue(region, out int count) && count > 0)
				{
					count--;
					regionHideCounts[region] = count;

					// Only show if no other slot is hiding this region
					if (count == 0 &&
						regionRenderers.TryGetValue(region, out SkinnedMeshRenderer renderer) &&
						renderer != null)
					{
						renderer.enabled = true;
					}
				}
			}

			slotHiddenRegions.Remove(slot);
#endif
		}

		/// <inheritdoc />
		public void ShowAllRegions()
		{
#if !UNITY_SERVER
			foreach (KeyValuePair<BodyRegion, SkinnedMeshRenderer> kvp in regionRenderers)
			{
				if (kvp.Value != null)
				{
					kvp.Value.enabled = true;
				}
			}

			regionHideCounts.Clear();
			slotHiddenRegions.Clear();
#endif
		}
	}
}
