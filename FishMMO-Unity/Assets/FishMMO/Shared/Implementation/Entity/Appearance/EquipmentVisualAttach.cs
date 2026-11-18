using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Puts one equipment item's visual on a character body: armour bound to the body's skeleton, a weapon held on a
	/// bone. Shared by the live <see cref="EquipmentVisualController"/> and the character select preview, which
	/// dresses a body from item templates alone.
	/// </summary>
	public static class EquipmentVisualAttach
	{
		/// <summary>
		/// Shows <paramref name="prefab"/>'s skinned mesh on <paramref name="target"/>, bound to the body under
		/// <paramref name="skeletonRoot"/>. False (and <paramref name="target"/> left without a mesh) when it cannot be.
		/// </summary>
		public static bool AttachArmor(GameObject prefab, SkinnedMeshRenderer target, Transform skeletonRoot)
		{
			if (target == null || prefab == null)
			{
				return false;
			}
			SkinnedMeshRenderer src = prefab.GetComponentInChildren<SkinnedMeshRenderer>();
			if (src == null) { Debug.LogError($"[EVC] '{prefab.name}' has no SkinnedMeshRenderer."); return false; }
			Mesh m = src.sharedMesh;
			Material[] mats = src.sharedMaterials;
			if (m == null) { Debug.LogError($"[EVC] '{prefab.name}' has no mesh."); return false; }
			if (skeletonRoot != null && src.bones != null && src.bones.Length > 0)
			{
				target.sharedMesh = m;
				target.bones = src.bones;
				if (!SkeletonBinder.BindMeshKeepParent(target, skeletonRoot))
				{
					Debug.LogError($"[EVC] Failed to bind bones for '{prefab.name}'.");
					target.sharedMesh = null;
					return false;
				}
			}
			target.sharedMesh = m;
			target.materials = mats;
			target.enabled = true;
			return true;
		}

		/// <summary>
		/// Makes <paramref name="prefab"/>'s mesh a child of <paramref name="weapon"/>'s attach bone under
		/// <paramref name="skeletonRoot"/>, and returns that new object (null when it cannot). A skinned weapon is baked
		/// into <paramref name="bakedMesh"/>, which the caller owns and must destroy with the weapon.
		/// </summary>
		public static GameObject AttachWeapon(GameObject prefab, WeaponTemplate weapon, Transform skeletonRoot, out Mesh bakedMesh)
		{
			bakedMesh = null;
			if (prefab == null || weapon == null || skeletonRoot == null || string.IsNullOrEmpty(weapon.AttachBoneName))
			{
				return null;
			}
			MeshFilter mf = prefab.GetComponentInChildren<MeshFilter>();
			MeshRenderer mr = prefab.GetComponentInChildren<MeshRenderer>();
			if (mf == null || mr == null)
			{
				SkinnedMeshRenderer smr = prefab.GetComponentInChildren<SkinnedMeshRenderer>();
				if (smr != null)
				{
					Mesh bm = new Mesh();
					smr.BakeMesh(bm);
					bakedMesh = bm;
					return AttachWeaponMesh(bm, smr.sharedMaterials, weapon, skeletonRoot);
				}
				Debug.LogError($"[EVC] '{prefab.name}' has no MeshRenderer or SkinnedMeshRenderer.");
				return null;
			}
			return AttachWeaponMesh(mf.sharedMesh, mr.sharedMaterials, weapon, skeletonRoot);
		}

		private static GameObject AttachWeaponMesh(Mesh mesh, Material[] materials, WeaponTemplate weapon, Transform skeletonRoot)
		{
			if (mesh == null) return null;
			Transform bone = SkeletonBinder.GetBoneTransform(skeletonRoot, weapon.AttachBoneName);
			if (bone == null) { Debug.LogError($"[EVC] Bone '{weapon.AttachBoneName}' not found."); return null; }
			GameObject go = new GameObject($"Weapon_{weapon.Name}");
			go.transform.SetParent(bone, false);
			BaseCharacter.ApplyVisualLayer(go);
			go.transform.localPosition = Vector3.zero;
			go.transform.localRotation = Quaternion.identity;
			go.transform.localScale = Vector3.one;
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			go.AddComponent<MeshRenderer>().materials = materials;
			return go;
		}
	}
}
