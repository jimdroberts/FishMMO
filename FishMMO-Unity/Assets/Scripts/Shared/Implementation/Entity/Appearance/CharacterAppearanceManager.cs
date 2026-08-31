using System.Collections.Generic;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Manages character visual appearance: bone scaling for body proportions,
	/// blend shape synchronization, and appearance data serialization.
	///
	/// Bone scaling is separated into a global Height pass and individual body-part
	/// adjustments. Height scales all height-affecting bones uniformly. Individual
	/// sliders (TorsoLength, LegLength, ArmLength) apply additional proportional
	/// tweaks on top. This means a Dwarf with Height=0.75 and LegLength=0.70 has
	/// legs at 0.75 * 0.70 = 0.525x scale — intentionally shorter than the already
	/// reduced height.
	///
	/// All operations are client-only.
	/// </summary>
	public class CharacterAppearanceManager : CharacterBehaviour, ICharacterAppearanceManager, IModelReadyHandler
	{
		/// <summary>
		/// Current blend shape values keyed by name (e.g., "Weight" → 50.0f).
		/// All values are stored clamped to [0, 100].
		/// </summary>
		private readonly Dictionary<string, float> blendShapeValues = new Dictionary<string, float>();

		/// <summary>
		/// Body region SkinnedMeshRenderers discovered from the model.
		/// </summary>
		private readonly List<SkinnedMeshRenderer> bodyRenderers = new List<SkinnedMeshRenderer>();

		/// <summary>
		/// Equipment SkinnedMeshRenderers registered by EquipmentVisualController.
		/// </summary>
		private readonly List<SkinnedMeshRenderer> equipmentRenderers = new List<SkinnedMeshRenderer>();

		/// <summary>
		/// Current appearance data snapshot.
		/// </summary>
		private CharacterAppearanceData currentAppearance;

		/// <summary>
		/// Cached skeleton root for bone scaling.
		/// </summary>
		private Transform skeletonRoot;

	/// <summary>
	/// Initial discovery attempt. May not find everything if model hasn't loaded.
	/// Full initialization deferred to <see cref="OnModelReady"/>.
	/// </summary>
	public override void OnStartCharacter()
	{
		base.OnStartCharacter();
#if !UNITY_SERVER
		if (Character == null)
		{
			return;
		}

		TryRefreshState();
#endif
	}

	/// <summary>
	/// Drops the appearance discovered from the current model before the object is pooled.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="OnModelReady"/> clears the body renderers and the skeleton on its way to
	/// re-discovering them, but it only runs once the NEXT model has finished loading, and the
	/// three things it does not touch have no other writer at all: the blend shape values, the
	/// equipment renderers registered by <c>EquipmentVisualController</c>, and
	/// <see cref="currentAppearance"/> itself.
	/// </para>
	/// <para>
	/// Left in place, the blend shapes are re-applied wholesale to the next occupant's model —
	/// <c>ReapplyBlendShapes</c> walks that dictionary and writes every entry — so a recycled
	/// object carried the previous character's face and body sliders onto a different character.
	/// The equipment renderer list is worse than stale: its entries belong to renderers that the
	/// visual controller destroys on teardown, so every later blend shape write iterated a list
	/// of dead Unity objects.
	/// </para>
	/// </remarks>
	/// <param name="asServer">True if called on the server.</param>
	public override void ResetState(bool asServer)
	{
		base.ResetState(asServer);

#if !UNITY_SERVER
		bodyRenderers.Clear();
		equipmentRenderers.Clear();
		blendShapeValues.Clear();
		currentAppearance = default;
		skeletonRoot = null;
#endif
	}

	/// <inheritdoc />
	public void OnModelReady()
	{
#if !UNITY_SERVER
		// Clear and re-discover from the now-loaded model
		bodyRenderers.Clear();
		skeletonRoot = null;

		TryRefreshState();

		// Re-apply race proportions now that skeleton is available
		if (PlayerCharacter != null)
		{
			RaceTemplate race = RaceTemplate.Get<RaceTemplate>(PlayerCharacter.RaceID);
			if (race != null)
			{
				ApplyRaceProportions(race.DefaultProportions);
			}
		}
#endif
	}

#if !UNITY_SERVER
	/// <summary>
	/// Attempts to discover skeleton root and body renderers.
	/// Uses independent discovery to avoid OnModelReady execution-order dependencies
	/// on BodyVisibilityManager.
	/// Safe to call multiple times.
	/// </summary>
	private void TryRefreshState()
	{
		// Discover skeleton root — prefer BodyVisibilityManager cache, fall back to Animator search
		if (Character.TryGet(out IBodyVisibilityManager bodyVis))
		{
			skeletonRoot = bodyVis.SkeletonRoot;
		}
		if (skeletonRoot == null && Character?.MeshRoot != null)
		{
			Animator animator = Character.MeshRoot.GetComponentInChildren<Animator>();
			if (animator != null)
			{
				skeletonRoot = animator.transform;
			}
		}

		// Discover body region renderers (only once)
		Transform meshRoot = Character?.MeshRoot;
		if (meshRoot != null && bodyRenderers.Count == 0)
		{
			BodyRegion[] allRegions = (BodyRegion[])System.Enum.GetValues(typeof(BodyRegion));
			for (int i = 0; i < allRegions.Length; i++)
			{
				string name = SkeletonBones.GetRegionRendererName(allRegions[i]);
				Transform child = FindChildRecursive(meshRoot, name);
				if (child != null)
				{
					SkinnedMeshRenderer renderer = child.GetComponent<SkinnedMeshRenderer>();
					if (renderer != null)
					{
						bodyRenderers.Add(renderer);
					}
				}
			}
		}
	}

	private Transform FindChildRecursive(Transform parent, string name)
	{
		if (parent.name == name) return parent;
		for (int i = 0; i < parent.childCount; i++)
		{
			Transform found = FindChildRecursive(parent.GetChild(i), name);
			if (found != null) return found;
		}
		return null;
	}
#endif

		/// <inheritdoc />
		public void ApplyRaceProportions(RaceProportions proportions)
		{
#if !UNITY_SERVER
			if (skeletonRoot == null)
			{
				return;
			}

			// Reset all bone scales to identity before applying proportions.
			// This prevents double-scaling when proportions are re-applied (e.g., OnModelReady + customization change).
			ResetBoneScales();

			// Height scales all height-affecting bones uniformly as a baseline.
			ApplyBoneScaleY(SkeletonBones.SpineChainBones, proportions.Height);
			ApplyBoneScaleY(SkeletonBones.UpperLegBones, proportions.Height);
			ApplyBoneScaleY(SkeletonBones.LowerLegBones, proportions.Height);

			// Individual body-part adjustments stack on top of Height.
			// Dwarf example: Height=0.75, LegLength=0.70 → legs = 0.525x (intentionally short).
			ApplyBoneScaleY(SkeletonBones.SpineChainBones, proportions.TorsoLength);
			ApplyBoneScaleY(SkeletonBones.UpperLegBones, proportions.LegLength);
			ApplyBoneScaleY(SkeletonBones.LowerLegBones, proportions.LegLength);

			// Arms are not affected by Height — only ArmLength
			ApplyBoneScaleY(SkeletonBones.UpperArmBones, proportions.ArmLength);
			ApplyBoneScaleY(SkeletonBones.LowerArmBones, proportions.ArmLength);

			// Head and neck are not affected by Height — only HeadScale
			ApplyBoneScaleUniform(SkeletonBones.HeadChainBones, proportions.HeadScale);

			// Shoulder width
			ApplyBoneScaleX(SkeletonBones.ShoulderBones, proportions.ShoulderWidth);

			// Store in current appearance
			currentAppearance.Height = proportions.Height;
			currentAppearance.ArmLength = proportions.ArmLength;
			currentAppearance.LegLength = proportions.LegLength;
			currentAppearance.TorsoLength = proportions.TorsoLength;
			currentAppearance.ShoulderWidth = proportions.ShoulderWidth;
			currentAppearance.HeadScale = proportions.HeadScale;
#endif
		}

		/// <inheritdoc />
		public void SetBlendShape(string name, float value)
		{
#if !UNITY_SERVER
			float clampedValue = Mathf.Clamp(value, 0f, 100f);
			blendShapeValues[name] = clampedValue;

			// Apply to body renderers
			ApplyBlendShapeToRenderers(bodyRenderers, name, clampedValue);

			// Apply to equipment renderers
			ApplyBlendShapeToRenderers(equipmentRenderers, name, clampedValue);
#endif
		}

		/// <inheritdoc />
		public void SyncBlendShapesToEquipment(SkinnedMeshRenderer equipmentRenderer)
		{
#if !UNITY_SERVER
			if (equipmentRenderer == null || equipmentRenderer.sharedMesh == null)
			{
				return;
			}

			foreach (KeyValuePair<string, float> kvp in blendShapeValues)
			{
				// Ensure value is clamped (belt-and-suspenders with SetBlendShape clamping above)
				float clampedValue = Mathf.Clamp(kvp.Value, 0f, 100f);

				int index = equipmentRenderer.sharedMesh.GetBlendShapeIndex(kvp.Key);
				if (index >= 0)
				{
					equipmentRenderer.SetBlendShapeWeight(index, clampedValue);
				}
			}
#endif
		}

		/// <inheritdoc />
		public CharacterAppearanceData GetAppearanceData()
		{
#if !UNITY_SERVER
			// Update equipped item IDs from the equipment controller
			if (Character != null && Character.TryGet(out IEquipmentController equipment))
			{
				int sc = System.Enum.GetNames(typeof(ItemSlot)).Length;
				if (currentAppearance.EquippedItemIds == null ||
					currentAppearance.EquippedItemIds.Length != sc)
				{
					currentAppearance.EquippedItemIds = new long[sc];
				}

				for (int i = 0; i < sc; i++)
				{
					if (equipment.TryGetItem(i, out Item item) && item != null)
					{
						currentAppearance.EquippedItemIds[i] = item.ID;
					}
					else
					{
						currentAppearance.EquippedItemIds[i] = -1;
					}
				}
			}
#endif
			return currentAppearance;
		}

		/// <inheritdoc />
		public void ApplyAppearanceData(CharacterAppearanceData data)
		{
			currentAppearance = data;

#if !UNITY_SERVER
			if (Character == null)
			{
				return;
			}

			// Apply proportions
			ApplyRaceProportions(new RaceProportions
			{
				Height = data.Height,
				ArmLength = data.ArmLength,
				LegLength = data.LegLength,
				TorsoLength = data.TorsoLength,
				ShoulderWidth = data.ShoulderWidth,
				HeadScale = data.HeadScale,
			});

			// Apply blend shapes
			SetBlendShape("Weight", data.Weight);
			SetBlendShape("MuscleMass", data.MuscleMass);
#endif
		}

		/// <summary>
		/// Registers an equipment renderer for blend shape syncing.
		/// Called by EquipmentVisualController when an item is equipped.
		/// </summary>
		public void RegisterEquipmentRenderer(SkinnedMeshRenderer renderer)
		{
#if !UNITY_SERVER
			if (renderer != null && !equipmentRenderers.Contains(renderer))
			{
				equipmentRenderers.Add(renderer);
			}
#endif
		}

		/// <summary>
		/// Unregisters an equipment renderer.
		/// Called by EquipmentVisualController when an item is unequipped.
		/// </summary>
		public void UnregisterEquipmentRenderer(SkinnedMeshRenderer renderer)
		{
#if !UNITY_SERVER
			equipmentRenderers.Remove(renderer);
#endif
		}

#if !UNITY_SERVER
		/// <summary>
		/// Resets all bones that we scale back to identity (1,1,1).
		/// Prevents double-scaling when proportions are applied multiple times.
		/// Builds a name-to-Transform dictionary once to avoid repeated skeleton scans.
		/// </summary>
		private void ResetBoneScales()
		{
			if (skeletonRoot == null) return;

			// Build a name-to-Transform lookup once from a single skeleton scan
			Transform[] allBones = skeletonRoot.GetComponentsInChildren<Transform>();
			Dictionary<string, List<Transform>> boneMap = new Dictionary<string, List<Transform>>(allBones.Length);
			for (int i = 0; i < allBones.Length; i++)
			{
				string name = allBones[i].name;
				if (!boneMap.TryGetValue(name, out List<Transform> list))
				{
					list = new List<Transform>();
					boneMap[name] = list;
				}
				list.Add(allBones[i]);
			}

			// All bone name arrays from SkeletonBones that we might have scaled
			string[][] allBoneGroups = new[]
			{
				SkeletonBones.SpineChainBones,
				SkeletonBones.UpperLegBones,
				SkeletonBones.LowerLegBones,
				SkeletonBones.UpperArmBones,
				SkeletonBones.LowerArmBones,
				SkeletonBones.HeadChainBones,
				SkeletonBones.ShoulderBones,
			};

			foreach (string[] group in allBoneGroups)
			{
				for (int i = 0; i < group.Length; i++)
				{
					if (boneMap.TryGetValue(group[i], out List<Transform> list))
					{
						for (int j = 0; j < list.Count; j++)
						{
							list[j].localScale = Vector3.one;
						}
					}
				}
			}
		}

		/// <summary>
		/// Sets the local scale of all bones with the given name. Uses the same single-scan pattern.
		/// </summary>
		private static void SetBoneScale(Transform skeletonRoot, string boneName, Vector3 scale)
		{
			Transform[] allBones = skeletonRoot.GetComponentsInChildren<Transform>();
			for (int i = 0; i < allBones.Length; i++)
			{
				if (allBones[i].name == boneName)
				{
					allBones[i].localScale = scale;
				}
			}
		}

		/// <summary>
		/// Applies a blend shape value by name to a list of renderers.
		/// Only affects renderers whose mesh actually has the named blend shape.
		/// Value must already be clamped to [0, 100].
		/// </summary>
		private static void ApplyBlendShapeToRenderers(List<SkinnedMeshRenderer> renderers, string name, float value)
		{
			for (int i = 0; i < renderers.Count; i++)
			{
				SkinnedMeshRenderer r = renderers[i];
				if (r == null || r.sharedMesh == null) continue;

				int index = r.sharedMesh.GetBlendShapeIndex(name);
				if (index >= 0)
				{
					r.SetBlendShapeWeight(index, value);
				}
			}
		}

		/// <summary>
		/// Applies uniform scale to all bones whose names are in the given array.
		/// </summary>
		private void ApplyBoneScaleUniform(string[] boneNames, float uniformScale)
		{
			ApplyBoneScale(boneNames, new Vector3(uniformScale, uniformScale, uniformScale));
		}

		/// <summary>
		/// Multiplies the Y scale of the given bones (stretches/squashes length).
		/// </summary>
		private void ApplyBoneScaleY(string[] boneNames, float yScale)
		{
			ApplyBoneScale(boneNames, new Vector3(1f, yScale, 1f));
		}

		/// <summary>
		/// Multiplies the X scale of the given bones (stretches/squashes width).
		/// </summary>
		private void ApplyBoneScaleX(string[] boneNames, float xScale)
		{
			ApplyBoneScale(boneNames, new Vector3(xScale, 1f, 1f));
		}

		/// <summary>
		/// Multiplies the current localScale of bones matching any of the given names.
		/// </summary>
		private void ApplyBoneScale(string[] boneNames, Vector3 scale)
		{
			if (skeletonRoot == null || boneNames == null) return;

			Transform[] allBones = skeletonRoot.GetComponentsInChildren<Transform>();
			for (int i = 0; i < allBones.Length; i++)
			{
				for (int j = 0; j < boneNames.Length; j++)
				{
					if (allBones[i].name == boneNames[j])
					{
						allBones[i].localScale = Vector3.Scale(allBones[i].localScale, scale);
						break;
					}
				}
			}
		}
#endif
	}
}
