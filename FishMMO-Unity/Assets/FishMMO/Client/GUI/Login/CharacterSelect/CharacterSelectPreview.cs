using System.Collections.Generic;
using FishMMO.Shared;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace FishMMO.Client
{
	/// <summary>
	/// Stands the character selected on the character select screen at this spot in the login backdrop: its race's
	/// chosen model, dressed in what it is wearing (<see cref="CharacterDetails.EquippedItems"/>). Swaps when the
	/// selection does, and clears when the list goes.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Display only: the body is the race's model prefab, never the networked character, so nothing on it simulates or
	/// talks to a server. Equipment goes on through <see cref="EquipmentVisualAttach"/>, the same steps the live
	/// <see cref="EquipmentVisualController"/> uses, with each item's first model (the preview has templates, not the
	/// rolled items that pick among a template's models).
	/// </para>
	/// <para>
	/// Every load is stamped with the selection it was made for, so a slow load for a character the player has already
	/// moved past is released instead of appearing on the new one.
	/// </para>
	/// </remarks>
	public class CharacterSelectPreview : MonoBehaviour
	{
		[Tooltip("Where the character stands and which way it faces. This object's own transform when unset.")]
		public Transform Spot;

		private GameObject model;
		private Transform skeletonRoot;
		private int generation;
		private readonly List<AsyncOperationHandle<GameObject>> equipmentHandles = new List<AsyncOperationHandle<GameObject>>();
		private readonly List<Object> owned = new List<Object>();

		private Transform Anchor => Spot != null ? Spot : transform;

		private void OnEnable()
		{
			UITKCharacterSelect.OnSelectedCharacterChanged += Show;
		}

		private void OnDisable()
		{
			UITKCharacterSelect.OnSelectedCharacterChanged -= Show;
			Clear();
		}

		/// <summary>Stands <paramref name="details"/>'s character at the spot; null clears it.</summary>
		public void Show(CharacterDetails details)
		{
			int stamp = ++generation;
			Clear();
			if (details == null)
			{
				return;
			}
			RaceTemplate race = RaceTemplate.Get<RaceTemplate>(details.RaceTemplateID);
			AssetReference body = race != null ? race.GetModelReference(details.ModelIndex) : null;
			if (body == null || !body.RuntimeKeyIsValid())
			{
				Debug.LogWarning($"[Character preview] No model for '{details.CharacterName}' (race {details.RaceTemplateID}, model {details.ModelIndex}).");
				return;
			}
			AddressableLoadProcessor.LoadPrefabAsync(body, prefab =>
			{
				if (stamp != generation || this == null || prefab == null)
				{
					return;
				}
				Transform anchor = Anchor;
				model = Instantiate(prefab, anchor.position, anchor.rotation, anchor);
				model.name = $"Preview: {details.CharacterName}";
				Animator animator = model.GetComponentInChildren<Animator>();
				skeletonRoot = animator != null ? animator.transform : model.transform;
				Dress(details.EquippedItems, stamp);
			});
		}

		private void Dress(EquippedItemEntry[] equipped, int stamp)
		{
			if (equipped == null)
			{
				return;   // not sent (an older server): the body as it comes
			}
			foreach (EquippedItemEntry entry in equipped)
			{
				EquippableItemTemplate template = BaseItemTemplate.Get<BaseItemTemplate>(entry.TemplateID) as EquippableItemTemplate;
				if (template == null)
				{
					continue;
				}
				AssetReference mesh = template.EquipmentMeshes != null && template.EquipmentMeshes.Count > 0
					? template.EquipmentMeshes[0]
					: template.MeshReference;
				if (mesh == null || !mesh.RuntimeKeyIsValid())
				{
					continue;
				}
				ItemSlot slot = entry.Slot;
				Addressables.LoadAssetAsync<GameObject>(mesh.RuntimeKey).Completed += handle =>
				{
					if (stamp != generation || model == null)
					{
						Addressables.Release(handle);
						return;
					}
					equipmentHandles.Add(handle);
					if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
					{
						return;
					}
					if (template is WeaponTemplate weapon)
					{
						GameObject held = EquipmentVisualAttach.AttachWeapon(handle.Result, weapon, skeletonRoot, out Mesh baked);
						if (baked != null)
						{
							owned.Add(baked);
						}
						if (held != null)
						{
							owned.Add(held);
						}
						return;
					}
					var worn = new GameObject($"Equipment_{slot}");
					worn.transform.SetParent(model.transform, false);
					SkinnedMeshRenderer renderer = worn.AddComponent<SkinnedMeshRenderer>();
					if (!EquipmentVisualAttach.AttachArmor(handle.Result, renderer, skeletonRoot))
					{
						Destroy(worn);
					}
				};
			}
		}

		private void Clear()
		{
			if (model != null)
			{
				Destroy(model);
			}
			model = null;
			skeletonRoot = null;
			foreach (Object o in owned)
			{
				if (o != null)
				{
					Destroy(o);
				}
			}
			owned.Clear();
			foreach (AsyncOperationHandle<GameObject> handle in equipmentHandles)
			{
				if (handle.IsValid())
				{
					Addressables.Release(handle);
				}
			}
			equipmentHandles.Clear();
		}
	}
}
