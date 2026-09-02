using System.Collections.Generic;
using FishMMO.Shared.Core;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace FishMMO.Shared
{
	/// <summary>
	/// Manages visual equipment rendering on a character.
	/// Pre-allocates renderers per slot, loads equipment prefabs via Addressables,
	/// extracts meshes and materials, binds skinned meshes to the character skeleton,
	/// and coordinates body region hiding with <see cref="IBodyVisibilityManager"/>.
	///
	/// All rendering operations are client-only, guarded with #if !UNITY_SERVER.
	/// Public API and interface implementations exist unconditionally for server compilation.
	/// </summary>
	public class EquipmentVisualController : CharacterBehaviour, IEquipmentVisualController, IModelReadyHandler
	{
		private class SlotRenderer
		{
			/// <summary>The root GameObject for this slot's equipment visuals.</summary>
			public GameObject GameObject;
			/// <summary>The SkinnedMeshRenderer for armor-type equipment items.</summary>
			public SkinnedMeshRenderer SkinnedRenderer;
			/// <summary>The MeshRenderer for weapon-type equipment items.</summary>
			public MeshRenderer MeshRenderer;
			/// <summary>The MeshFilter for weapon-type equipment items.</summary>
			public MeshFilter MeshFilter;
			/// <summary>The equipment slot this renderer is assigned to.</summary>
			public ItemSlot Slot;
			/// <summary>True if this slot has an active equipment visual.</summary>
			public bool IsActive;
			/// <summary>The Addressables async operation handle for the loaded equipment prefab.</summary>
			public AsyncOperationHandle<GameObject> PrefabHandle;
			/// <summary>The instantiated weapon GameObject, if this slot holds a weapon.</summary>
			public GameObject WeaponInstance;
			/// <summary>Incrementing generation counter to reject stale async load completions.</summary>
			public int EquipGeneration;
		}

		private SlotRenderer[] slotRenderers;
		private Transform equipmentRoot;
		private IEquipmentController equipmentController;
		private IBodyVisibilityManager bodyVisibilityManager;
		private ICharacterAppearanceManager appearanceManager;
		private Transform skeletonRoot;
		private int slotCount;
		private bool modelReady;

		// ── Overrides from CharacterBehaviour (must exist unconditionally) ──

		/// <summary>Initializes the equipment system: creates EquipmentRoot, pre-allocates renderers, subscribes to equip events.</summary>
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();
#if !UNITY_SERVER
			if (Character == null) return;

			Character.TryGet(out equipmentController);
			Character.TryGet(out bodyVisibilityManager);
			Character.TryGet(out ICharacterAppearanceManager ami);
			appearanceManager = ami;

			Transform meshRoot = Character.MeshRoot;
			// Fall back to KCC MeshRoot when the serialized MeshRoot is null
			// (e.g. race model not yet bound or prefab missing the reference).
			if (meshRoot == null && Character is IPlayerCharacter pc && pc.CharacterController != null)
				meshRoot = pc.CharacterController.MeshRoot;
			if (meshRoot == null)
			{
				Debug.LogWarning($"[EquipmentVisualController] MeshRoot is null for {Character.GameObject.name}. Appearance will be retried.");
				return;
			}

			GameObject rootGO = new GameObject("EquipmentRoot");
			rootGO.transform.SetParent(meshRoot, false);
			equipmentRoot = rootGO.transform;

			slotCount = System.Enum.GetNames(typeof(ItemSlot)).Length;
			slotRenderers = new SlotRenderer[slotCount];
			for (int i = 0; i < slotCount; i++)
				CreateSlotRenderer((ItemSlot)i);

			if (equipmentController != null)
			{
				equipmentController.OnItemEquipped += OnItemEquipped;
				equipmentController.OnItemUnequipped += OnItemUnequipped;
			}

			TryRefreshModelState();
#endif
		}

		/// <summary>Unsubscribes from equipment events and releases all slot renderers.</summary>
		public override void OnStopCharacter()
		{
#if !UNITY_SERVER
			TearDownVisuals();
#endif
			base.OnStopCharacter();
		}

		/// <summary>Destroys the equipment root GameObject and clears the skeleton bone cache.</summary>
		public override void OnDestroying()
		{
#if !UNITY_SERVER
			TearDownVisuals();
#endif
			base.OnDestroying();
		}

		/// <summary>
		/// Releases every equipment visual before the object returns to the pool.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Neither existing teardown covers a pooled despawn.</b> <see cref="OnDestroying"/> is
		/// the destroy path, which a pooled object never takes, and
		/// <see cref="OnStopCharacter"/> is dispatched by <c>PlayerCharacter.OnStopClient</c>
		/// only when <c>IsOwner</c> — so an object recycled for a different character kept the
		/// previous one's loaded prefabs. Each of those holds an Addressables handle and, for
		/// weapons, an instantiated GameObject parented under a mesh root that is about to be
		/// destroyed: a leak that grows by one handle per equipped slot per reuse and never
		/// returns.
		/// </para>
		/// <para>
		/// The generation counter is bumped as part of releasing. An equip whose Addressables
		/// load is still in flight completes into a callback that tests
		/// <c>EquipGeneration</c> to reject stale results, and without a bump here that test
		/// passes — so a load started by the previous occupant would attach its mesh to the next
		/// one, arriving out of nowhere some frames after the new character spawned.
		/// </para>
		/// </remarks>
		/// <param name="asServer">True if called on the server.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

#if !UNITY_SERVER
			TearDownVisuals();
#endif
		}

		// ── IModelReadyHandler (must exist unconditionally) ──

		/// <summary>Called when the character model finishes loading. Re-discovers skeleton and refreshes equipment.</summary>
		public void OnModelReady()
		{
#if !UNITY_SERVER
			if (!TryRefreshModelState()) return;
			RefreshAllEquipment();
#endif
		}

		// ── IEquipmentVisualController (must exist unconditionally) ──

		/// <summary>Re-equips all items from the current equipment state. Used after model load or spawn.</summary>
		public void RefreshAllEquipment()
		{
#if !UNITY_SERVER
			if (equipmentController == null || !modelReady) return;
			for (int i = 0; i < slotCount; i++)
			{
				if (equipmentController.TryGetItem(i, out Item item) && item != null)
					EquipItemVisual(item, (ItemSlot)i);
			}
#endif
		}

		// ── Private helpers (client-only, all rendering APIs) ──

#if !UNITY_SERVER
		private bool TryRefreshModelState()
		{
			skeletonRoot = bodyVisibilityManager?.SkeletonRoot;
			if (skeletonRoot == null && Character?.MeshRoot != null)
			{
				Animator animator = Character.MeshRoot.GetComponentInChildren<Animator>();
				if (animator != null) skeletonRoot = animator.transform;
			}
			modelReady = skeletonRoot != null;
			return modelReady;
		}

		private void CreateSlotRenderer(ItemSlot slot)
		{
			int index = (int)slot;
			GameObject go = new GameObject($"Equipment_{slot}");
			go.transform.SetParent(equipmentRoot, false);
			go.SetActive(false);
			SlotRenderer r = new SlotRenderer { GameObject = go, Slot = slot };
			SkinnedMeshRenderer smr = go.AddComponent<SkinnedMeshRenderer>();
			smr.enabled = false;
			r.SkinnedRenderer = smr;
			slotRenderers[index] = r;
		}

		private void OnItemEquipped(Item item, ItemSlot slot) => EquipItemVisual(item, slot);
		private void OnItemUnequipped(Item item, ItemSlot slot) => UnequipItemVisual(slot);

		private void EquipItemVisual(Item item, ItemSlot slot)
		{
			if (item?.Template == null || !modelReady) return;
			EquippableItemTemplate template = item.Template as EquippableItemTemplate;
			if (template == null) return;

			int index = (int)slot;
			if (index < 0 || index >= slotRenderers.Length) return;

			AssetReference assetRef = SelectEquipmentMesh(template, item)
				?? template.MeshReference;

			/* RuntimeKeyIsValid as well as null. A template that simply has no model assigned
			 * still serializes an AssetReference — an object with an empty m_AssetGUID — so it
			 * passes a null check and then throws InvalidKeyException out of LoadAssetAsync
			 * ("No MergeMode is set to merge the multiple keys requested. Keys=", with nothing
			 * after the equals because the key is empty). That surfaces as an unhandled
			 * exception in the player's console on equipping an ordinary item, rather than as
			 * the "no mesh configured" case this warning already exists to describe. */
			if (assetRef == null || !assetRef.RuntimeKeyIsValid())
			{
				Debug.LogWarning($"[EquipmentVisualController] No mesh for '{item.Name}' slot {slot}.");
				return;
			}

			SlotRenderer renderer = slotRenderers[index];
			if (renderer == null) return;

			bodyVisibilityManager?.ShowRegionsForSlot(slot);
			ReleaseSlotRenderer(renderer);
			int equipGen = ++renderer.EquipGeneration;

			BodyRegion[] hiddenRegions = template.HiddenRegions;
			WeaponTemplate weaponTemplate = template as WeaponTemplate;
			bool isWeapon = weaponTemplate != null;
			string boneName = isWeapon ? weaponTemplate.AttachBoneName : null;

			assetRef.LoadAssetAsync<GameObject>().Completed += (handle) =>
			{
				/* A superseded load still holds an Addressables reference, and nothing else will
				 * ever release it: ReleaseSlotRenderer only knows about the handle that WON. This
				 * path is no longer rare — a predicted equip is restored and re-applied by every
				 * reconcile that predates it, so several loads for one socket can be in flight and
				 * all but the last are superseded. Release here or the refcount only ever climbs. */
				if (renderer.EquipGeneration != equipGen)
				{
					Addressables.Release(handle);
					return;
				}
				if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
				{
					Debug.LogError($"[EquipmentVisualController] Failed to load asset for '{item.Name}'.");
					Addressables.Release(handle);
					return;
				}
				GameObject prefab = handle.Result;

				if (isWeapon)
					AttachWeaponVisual(prefab, weaponTemplate, renderer);
				else
					AttachArmorVisual(prefab, template, renderer);

				renderer.PrefabHandle = handle;

				if (hiddenRegions != null && hiddenRegions.Length > 0)
					bodyVisibilityManager?.HideRegions(hiddenRegions, slot);

				if (renderer.SkinnedRenderer != null && appearanceManager != null)
				{
					appearanceManager.SyncBlendShapesToEquipment(renderer.SkinnedRenderer);
					appearanceManager.RegisterEquipmentRenderer(renderer.SkinnedRenderer);
				}
				renderer.IsActive = true;
			};
		}

		private static AssetReference SelectEquipmentMesh(EquippableItemTemplate template, Item item)
		{
			if (template.EquipmentMeshes == null || template.EquipmentMeshes.Count == 0) return null;
			if (template.EquipmentMeshes.Count == 1) return template.EquipmentMeshes[0];

			if (template.ModelPools != null && template.ModelPools.Length > 0 && item.IsGenerated)
			{
				int pi = (int)(item.Generator.Seed % (uint)template.ModelPools.Length);
				return template.EquipmentMeshes[template.ModelPools[pi] % template.EquipmentMeshes.Count];
			}

			int idx = item.IsGenerated ? (int)(item.Generator.Seed % (uint)template.EquipmentMeshes.Count) : 0;
			return template.EquipmentMeshes[idx];
		}

		private void AttachArmorVisual(GameObject prefab, EquippableItemTemplate template, SlotRenderer renderer)
		{
			SkinnedMeshRenderer smr = renderer.SkinnedRenderer;
			if (smr == null) return;

			SkinnedMeshRenderer src = prefab.GetComponentInChildren<SkinnedMeshRenderer>();
			if (src == null) { Debug.LogError($"[EVC] '{prefab.name}' has no SkinnedMeshRenderer."); return; }

			Mesh m = src.sharedMesh;
			Material[] mats = src.sharedMaterials;
			if (m == null) { Debug.LogError($"[EVC] '{prefab.name}' has no mesh."); return; }

			if (skeletonRoot != null && src.bones != null && src.bones.Length > 0)
			{
				smr.sharedMesh = m;
				smr.bones = src.bones;
				if (!SkeletonBinder.BindMeshKeepParent(smr, skeletonRoot))
				{
					Debug.LogError($"[EVC] Failed to bind bones for '{prefab.name}'.");
					smr.sharedMesh = null;
					return;
				}
			}

			smr.sharedMesh = m;
			smr.materials = mats;
			smr.enabled = true;
			renderer.GameObject.SetActive(true);
		}

		private void AttachWeaponVisual(GameObject prefab, WeaponTemplate wt, SlotRenderer renderer)
		{
			if (skeletonRoot == null || string.IsNullOrEmpty(wt.AttachBoneName)) return;

			MeshFilter mf = prefab.GetComponentInChildren<MeshFilter>();
			MeshRenderer mr = prefab.GetComponentInChildren<MeshRenderer>();

			if (mf == null || mr == null)
			{
				SkinnedMeshRenderer smr = prefab.GetComponentInChildren<SkinnedMeshRenderer>();
				if (smr != null) { Mesh bm = new Mesh(); smr.BakeMesh(bm); AttachWeaponMesh(bm, smr.sharedMaterials, wt, renderer); return; }
				Debug.LogError($"[EVC] '{prefab.name}' has no MeshRenderer or SkinnedMeshRenderer.");
				return;
			}
			AttachWeaponMesh(mf.sharedMesh, mr.sharedMaterials, wt, renderer);
		}

		private void AttachWeaponMesh(Mesh mesh, Material[] materials, WeaponTemplate wt, SlotRenderer renderer)
		{
			if (mesh == null) return;
			Transform bone = SkeletonBinder.GetBoneTransform(skeletonRoot, wt.AttachBoneName);
			if (bone == null) { Debug.LogError($"[EVC] Bone '{wt.AttachBoneName}' not found."); return; }

			GameObject go = new GameObject($"Weapon_{wt.Name}");
			go.transform.SetParent(bone, false);
			go.transform.localPosition = Vector3.zero;
			go.transform.localRotation = Quaternion.identity;
			go.transform.localScale = Vector3.one;
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			go.AddComponent<MeshRenderer>().materials = materials;

			renderer.WeaponInstance = go;
			renderer.MeshRenderer = go.GetComponent<MeshRenderer>();
			renderer.MeshFilter = go.GetComponent<MeshFilter>();
			renderer.GameObject.SetActive(true);
		}

		private void UnequipItemVisual(ItemSlot slot)
		{
			int i = (int)slot;
			// slotRenderers is null before the first OnStartCharacter and again after
			// TearDownVisuals, and unlike EquipItemVisual there is no modelReady test above to
			// have caught it.
			if (slotRenderers == null || i < 0 || i >= slotRenderers.Length || slotRenderers[i] == null) return;
			SlotRenderer r = slotRenderers[i];
			bodyVisibilityManager?.ShowRegionsForSlot(slot);
			if (r.SkinnedRenderer != null && appearanceManager != null)
				appearanceManager.UnregisterEquipmentRenderer(r.SkinnedRenderer);
			ReleaseSlotRenderer(r);
		}

		/// <summary>
		/// Releases every slot renderer, drops the equipment root and the discovered skeleton,
		/// and detaches from the equipment controller. Shared by the pool, stop and destroy
		/// paths.
		/// </summary>
		private void TearDownVisuals()
		{
			if (equipmentController != null)
			{
				equipmentController.OnItemEquipped -= OnItemEquipped;
				equipmentController.OnItemUnequipped -= OnItemUnequipped;
			}
			if (slotRenderers != null)
			{
				for (int i = 0; i < slotRenderers.Length; i++)
				{
					SlotRenderer renderer = slotRenderers[i];
					if (renderer == null)
					{
						continue;
					}
					// Invalidate in-flight loads before releasing what they would attach to.
					++renderer.EquipGeneration;
					ReleaseSlotRenderer(renderer);
				}
				slotRenderers = null;
			}
			if (skeletonRoot != null)
			{
				SkeletonBinder.ClearBoneCache(skeletonRoot);
				skeletonRoot = null;
			}
			if (equipmentRoot != null)
			{
				Destroy(equipmentRoot.gameObject);
				equipmentRoot = null;
			}

			/* Rediscovered by OnStartCharacter on the next spawn. Held references to sibling
			 * behaviours are harmless in themselves, but modelReady must not stay true across a
			 * despawn: RefreshAllEquipment reads it as "the skeleton is live". */
			equipmentController = null;
			bodyVisibilityManager = null;
			appearanceManager = null;
			modelReady = false;
			slotCount = 0;
		}

		private void ReleaseSlotRenderer(SlotRenderer renderer)
		{
			if (renderer == null) return;
			if (renderer.SkinnedRenderer != null)
			{
				renderer.SkinnedRenderer.sharedMesh = null;
				// Renderer.materials requires an array — assigning null throws
				// ArgumentNullException out of Renderer.SetMaterialArray, which aborts the
				// rest of this cleanup (and whatever despawn/disconnect path invoked it)
				// partway through. An empty array is the supported way to clear materials.
				renderer.SkinnedRenderer.materials = System.Array.Empty<Material>();
				renderer.SkinnedRenderer.enabled = false;
			}
			if (renderer.WeaponInstance != null)
			{
				if (Application.isPlaying) Destroy(renderer.WeaponInstance);
				else DestroyImmediate(renderer.WeaponInstance);
				renderer.WeaponInstance = null;
				renderer.MeshRenderer = null;
				renderer.MeshFilter = null;
			}
			if (renderer.PrefabHandle.IsValid())
			{
				Addressables.Release(renderer.PrefabHandle);
				renderer.PrefabHandle = default;
			}
			if (renderer.GameObject != null) renderer.GameObject.SetActive(false);
			renderer.IsActive = false;
		}
#endif
	}
}
