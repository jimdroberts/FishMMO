using System.IO;
using UnityEditor;
using UnityEngine;
using FishNet.Object;

namespace FishMMO.Shared
{
	/// <summary>
	/// Builds "a fire elemental", the pet Summon Fire Elemental calls: "a lesser fire elemental" a quarter bigger and with
	/// half as much health again. Summon Fire Elemental used to call the lesser prefab itself (Jim, 11-16).
	/// </summary>
	/// <remarks>
	/// Made through <see cref="NPCPrefabFactory"/>, so the copy gets its own GUID, FishNet prefab entry, Addressables entry and
	/// AI brain like any designer-made NPC. Idempotent: an existing prefab or bonus asset is reused and only re-pointed.
	/// Headless: <c>-executeMethod FishMMO.Shared.PetVariantBuilder.BuildBatch</c>.
	/// </remarks>
	public static class PetVariantBuilder
	{
		private const string LOG = "PetVariantBuilder";
		private const string PetFolder = "Assets/Prefabs/Shared/Entity/NPCs/Pets";
		private const string LesserPath = PetFolder + "/a lesser fire elemental.prefab";
		private const string GreaterName = "a fire elemental";
		private const string BonusFolder = "Assets/Prefabs/Shared/Entity/NPCs/Attributes/Pets";
		private const string BonusPath = BonusFolder + "/a fire elemental.asset";
		private const string HealthPath = "Assets/Templates/Entity/CharacterAttributes/Resource/Health.asset";
		private const string SummonPath = "Assets/Templates/Entity/Abilities/Types/Summons/Summon Fire Elemental.asset";

		/// <summary>The greater elemental's size against the lesser.</summary>
		public const float Scale = 1.25f;

		/// <summary>The greater elemental's health, percent of the lesser's.</summary>
		public const int HealthPercent = 150;

		[DashboardTool(DashboardToolAttribute.Maintenance, "Build pet variants", Section = "World", Order = 21,
			Tooltip = "Builds \"a fire elemental\" from the lesser one (bigger, more health) and points Summon Fire Elemental at it.")]
		public static void Build()
		{
			GameObject lesser = AssetDatabase.LoadAssetAtPath<GameObject>(LesserPath);
			if (lesser == null)
			{
				Debug.LogError($"[{LOG}] '{LesserPath}' not found.");
				return;
			}

			NPCAttributeDatabase bonus = EnsureHealthBonus();
			if (bonus == null)
			{
				return;
			}

			string greaterPath = $"{PetFolder}/{GreaterName}.prefab";
			GameObject greater = AssetDatabase.LoadAssetAtPath<GameObject>(greaterPath);
			if (greater == null)
			{
				greater = CopyLikeTheFactory(lesser, greaterPath);
				if (greater == null)
				{
					return;
				}
			}

			ApplyScaleAndBonus(greaterPath, bonus);
			PointSummonAt(greaterPath);

			AssetDatabase.SaveAssets();
			Debug.Log($"[{LOG}] DONE '{greaterPath}' (x{Scale} size, {HealthPercent}% health), '{SummonPath}' summons it.");
		}

		/// <summary>Headless entry point.</summary>
		public static void BuildBatch()
		{
			Build();
		}

		/// <summary>
		/// <see cref="NPCPrefabFactory.Create"/>'s steps without its recipe check, which demands a race: the elementals have
		/// none, their body is authored on the prefab (MeshRoot), not loaded from a race.
		/// </summary>
		private static GameObject CopyLikeTheFactory(GameObject basePrefab, string targetPath)
		{
			string basePath = AssetDatabase.GetAssetPath(basePrefab);
			if (!AssetDatabase.CopyAsset(basePath, targetPath))
			{
				Debug.LogError($"[{LOG}] Could not copy '{basePath}' to '{targetPath}'.");
				return null;
			}
			GameObject root = PrefabUtility.LoadPrefabContents(targetPath);
			try
			{
				root.name = GreaterName;
				PrefabUtility.SaveAsPrefabAsset(root, targetPath);
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(root);
			}
			if (NetworkObjectBindingValidator.Scan(targetPath).Count > 0)
			{
				NetworkObjectBindingValidator.Repair(targetPath);
			}
			NPCPrefabFactory.RegisterAddressableLike(targetPath, basePath);
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			GameObject created = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
			AIBrainCatalogueEditorUtility.SetArchetype(created,
				AIBrainCatalogueEditorUtility.GetArchetype(basePrefab));
			Debug.Log($"[{LOG}] Created '{targetPath}' from '{basePath}'.");
			return created;
		}

		private static NPCAttributeDatabase EnsureHealthBonus()
		{
			CharacterAttributeTemplate health = AssetDatabase.LoadAssetAtPath<CharacterAttributeTemplate>(HealthPath);
			if (health == null)
			{
				Debug.LogError($"[{LOG}] '{HealthPath}' not found.");
				return null;
			}
			if (!AssetDatabase.IsValidFolder(BonusFolder))
			{
				AssetDatabase.CreateFolder(Path.GetDirectoryName(BonusFolder).Replace('\\', '/'), Path.GetFileName(BonusFolder));
			}
			NPCAttributeDatabase bonus = AssetDatabase.LoadAssetAtPath<NPCAttributeDatabase>(BonusPath);
			if (bonus == null)
			{
				bonus = ScriptableObject.CreateInstance<NPCAttributeDatabase>();
				AssetDatabase.CreateAsset(bonus, BonusPath);
			}
			bonus.Attributes.Clear();
			// Scalar, fixed: health is HealthPercent percent of what the shared attribute database gives the lesser.
			bonus.Attributes.Add(new NPCAttribute(true, false, HealthPercent, HealthPercent, health));
			EditorUtility.SetDirty(bonus);
			return bonus;
		}

		private static void ApplyScaleAndBonus(string prefabPath, NPCAttributeDatabase bonus)
		{
			GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
			try
			{
				/* The model, not the root: the root carries the colliders, the agent and the networked transform, which a
				 * scale would distort in ways the AI's reach and the prediction do not expect. */
				Transform meshRoot = root.transform.Find("MeshRoot");
				if (meshRoot != null)
				{
					meshRoot.localScale = Vector3.one * Scale;
				}
				else
				{
					Debug.LogWarning($"[{LOG}] '{prefabPath}' has no MeshRoot; its size is unchanged.");
				}
				NPC npc = root.GetComponent<NPC>();
				if (npc != null)
				{
					npc.AttributeBonuses = bonus;
				}
				PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(root);
			}
		}

		private static void PointSummonAt(string prefabPath)
		{
			PetAbilityTemplate summon = AssetDatabase.LoadAssetAtPath<PetAbilityTemplate>(SummonPath);
			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
			if (summon == null || prefab == null)
			{
				Debug.LogError($"[{LOG}] '{SummonPath}' or '{prefabPath}' not found; the summon was not re-pointed.");
				return;
			}
			summon.PetPrefab = prefab.GetComponent<NetworkObject>();
			EditorUtility.SetDirty(summon);
		}
	}
}
