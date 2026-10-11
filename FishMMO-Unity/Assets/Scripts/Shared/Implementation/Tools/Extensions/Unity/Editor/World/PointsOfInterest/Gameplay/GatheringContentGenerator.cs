#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using FishMMO.Shared.Core;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Creates the gathering content in <see cref="GatheringResourceCatalogue"/>: a placeholder
	/// <see cref="CraftingMaterialTemplate"/> per drop, a <see cref="GatheringNodeTemplate"/> per node, the shared
	/// "Gathering Node Gather" trigger, a tinted material, and a networked node prefab, all registered as addressables.
	/// </summary>
	/// <remarks>
	/// <para><b>Idempotent.</b> Every asset is loaded and updated in place when it exists, so a re-run after a catalogue
	/// change rewrites values without new guids, and a designer's later edits to an asset this tool does not set (an icon,
	/// an achievement) survive.</para>
	/// <para><b>Prefabs are cloned, not built.</b> A node prefab is a copy of the shipped <c>Dungeon Chest</c> — the
	/// project's known-good networked interactable (NetworkObject, observer conditions, authored naming) — with its
	/// container swapped for a <see cref="GatheringNode"/> and its model swapped for the resource's mesh. The same reasoning
	/// as <see cref="NPCPrefabFactory"/>: the boilerplate is never authored by hand and never drifts.</para>
	/// <para>Run headless with
	/// <c>-executeMethod FishMMO.Shared.WorldDesign.GatheringContentGenerator.Generate</c>, or from the dashboard.</para>
	/// </remarks>
	public static class GatheringContentGenerator
	{
		private const string LOG = "[GatheringContent]";

		public const string ItemFolder = "Assets/Templates/Entity/Items/Resources";
		public const string NodeTemplateFolder = "Assets/Templates/Entity/Interactables/Gathering";
		public const string TriggerPath = "Assets/Templates/Entity/ECA/Interactions/Gathering Node Gather.asset";
		public const string PrefabFolder = "Assets/Prefabs/Shared/Entity/Interactables/Gathering";
		public const string MaterialFolder = "Assets/Prefabs/Shared/Entity/Interactables/Gathering/Materials";
		public const string BasePrefabPath = "Assets/Prefabs/Shared/Entity/Interactables/Containers/Dungeon Chest.prefab";

		/// <summary>The prefab a resource's node is saved as.</summary>
		public static string PrefabPath(GatheringResource resource) => $"{PrefabFolder}/{resource.Node} Node.prefab";

		/// <summary>The node prefab of a resource, or null before <see cref="Generate"/> has run.</summary>
		public static GameObject LoadPrefab(GatheringResource resource)
		{
			return resource == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(resource));
		}

		[DashboardTool(DashboardToolAttribute.Maintenance, "Generate Gathering Content", Section = "Points of Interest", Order = 10,
			Tooltip = "Creates or updates the resource items, gathering node templates, the gather trigger and the node prefabs, and registers them as addressables.")]
		public static void Generate()
		{
			GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefabPath);
			if (basePrefab == null)
			{
				Debug.LogError($"{LOG} Base prefab '{BasePrefabPath}' not found; nothing generated.");
				return;
			}

			EnsureFolder(ItemFolder);
			EnsureFolder(NodeTemplateFolder);
			EnsureFolder(PrefabFolder);
			EnsureFolder(MaterialFolder);

			Trigger gather = EnsureGatherTrigger();
			int made = 0;
			var problems = new List<string>();
			foreach (GatheringResource resource in GatheringResourceCatalogue.All)
			{
				CraftingMaterialTemplate item = EnsureItem(resource);
				GatheringNodeTemplate template = EnsureNodeTemplate(resource, item);
				if (EnsurePrefab(resource, template, gather, problems))
				{
					made++;
				}
			}

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			foreach (string problem in problems)
			{
				Debug.LogWarning($"{LOG} {problem}");
			}
			Debug.Log($"{LOG} {made}/{GatheringResourceCatalogue.All.Count} gathering nodes generated.");
		}

		private static Trigger EnsureGatherTrigger()
		{
			Trigger trigger = AssetDatabase.LoadAssetAtPath<Trigger>(TriggerPath);
			if (trigger == null)
			{
				EnsureFolder(Path.GetDirectoryName(TriggerPath).Replace('\\', '/'));
				trigger = ScriptableObject.CreateInstance<Trigger>();
				AssetDatabase.CreateAsset(trigger, TriggerPath);
			}

			bool hasAction = false;
			foreach (BaseAction action in trigger.OnConditionsMetActions)
			{
				hasAction |= action is GatheringNodeAction;
			}
			if (!hasAction)
			{
				trigger.OnConditionsMetActions.Add(new GatheringNodeAction());
			}
			EditorUtility.SetDirty(trigger);
			WorldEditorAssets.RegisterAddressable(trigger);
			return trigger;
		}

		private static CraftingMaterialTemplate EnsureItem(GatheringResource resource)
		{
			CraftingMaterialTemplate item = LoadOrCreate<CraftingMaterialTemplate>($"{ItemFolder}/{resource.Item}.asset");
			item.Family = resource.Family.ToString();
			item.Tier = Mathf.Clamp(resource.Tier, 1, 4);
			item.Description = resource.Description;
			item.MaxStackSize = 50;
			item.Price = resource.Price;
			item.IsIdentifiable = false;
			item.Generate = false;
			EditorUtility.SetDirty(item);
			WorldEditorAssets.RegisterAddressable(item);
			return item;
		}

		private static GatheringNodeTemplate EnsureNodeTemplate(GatheringResource resource, CraftingMaterialTemplate item)
		{
			GatheringNodeTemplate template = LoadOrCreate<GatheringNodeTemplate>($"{NodeTemplateFolder}/{resource.Node}.asset");
			template.Description = resource.Description;
			template.MaxUses = Mathf.Max(1, resource.MaxUses);
			template.GatherTimeSeconds = Mathf.Max(0.0f, resource.GatherSeconds);
			template.Drops = new List<GatheringDrop>
			{
				new GatheringDrop { Item = item, MinAmount = Mathf.Max(1, resource.MinAmount), MaxAmount = Mathf.Max(resource.MinAmount, resource.MaxAmount), Weight = 1.0f },
			};
			EditorUtility.SetDirty(template);
			// The client resolves the template by ID for the gather bar, so it is shared like every other template.
			WorldEditorAssets.RegisterAddressable(template);
			return template;
		}

		/// <summary>Creates (by cloning the chest) or updates a node prefab.</summary>
		private static bool EnsurePrefab(GatheringResource resource, GatheringNodeTemplate template, Trigger gather, List<string> problems)
		{
			GameObject visual = AssetDatabase.LoadAssetAtPath<GameObject>($"{GatheringResourceCatalogue.VisualFolder}/{resource.Visual}.prefab");
			if (!TryGetVisualMesh(visual, out Mesh mesh, out Material[] materials, out Matrix4x4 meshToRoot))
			{
				problems.Add($"'{resource.Node}': visual '{resource.Visual}' is missing or has no mesh; prefab skipped.");
				return false;
			}

			string path = PrefabPath(resource);
			if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null && !AssetDatabase.CopyAsset(BasePrefabPath, path))
			{
				problems.Add($"'{resource.Node}': could not copy '{BasePrefabPath}' to '{path}'.");
				return false;
			}

			Material[] tinted = TintedMaterials(resource, materials);

			GameObject root = PrefabUtility.LoadPrefabContents(path);
			try
			{
				root.name = $"{resource.Node} Node";

				// The chest's container goes; the gathering node takes its place.
				foreach (Interactable interactable in root.GetComponents<Interactable>())
				{
					if (interactable is not GatheringNode)
					{
						Object.DestroyImmediate(interactable, true);
					}
				}
				GatheringNode node = root.GetComponent<GatheringNode>();
				if (node == null)
				{
					node = root.AddComponent<GatheringNode>();
				}
				node.Template = template;

				Transform model = root.transform.Find("Model");
				if (model == null)
				{
					model = new GameObject("Model").transform;
					model.SetParent(root.transform, false);
				}
				// Explicit checks, not ??: a missing component can come back as Unity's fake null.
				MeshFilter filter = model.GetComponent<MeshFilter>();
				if (filter == null)
				{
					filter = model.gameObject.AddComponent<MeshFilter>();
				}
				MeshRenderer renderer = model.GetComponent<MeshRenderer>();
				if (renderer == null)
				{
					renderer = model.gameObject.AddComponent<MeshRenderer>();
				}
				filter.sharedMesh = mesh;
				renderer.sharedMaterials = tinted;

				// Normalise the mesh to the resource's size, standing on the node's origin.
				Bounds local = TransformBounds(mesh.bounds, meshToRoot);
				float largest = Mathf.Max(local.size.x, Mathf.Max(local.size.y, local.size.z));
				float scale = resource.Size > 0.0f && largest > 0.0001f ? resource.Size / largest : 1.0f;
				Matrix4x4 placed = Matrix4x4.Scale(Vector3.one * scale) * meshToRoot;
				model.localPosition = placed.GetColumn(3);
				model.localRotation = placed.rotation;
				model.localScale = placed.lossyScale;
				Bounds bounds = TransformBounds(mesh.bounds, placed);
				model.localPosition -= new Vector3(0.0f, bounds.min.y, 0.0f);
				bounds.center -= new Vector3(0.0f, bounds.min.y, 0.0f);

				BoxCollider box = root.GetComponent<BoxCollider>();
				if (box == null)
				{
					box = root.AddComponent<BoxCollider>();
				}
				float footprint = Mathf.Clamp(resource.Footprint, 0.05f, 1.0f);
				Vector3 size = new Vector3(Mathf.Max(0.6f, bounds.size.x * footprint), Mathf.Max(0.6f, bounds.size.y), Mathf.Max(0.6f, bounds.size.z * footprint));
				box.size = size;
				box.center = new Vector3(bounds.center.x, size.y * 0.5f, bounds.center.z);
				box.isTrigger = false;

				// The ECA list is the whole behaviour; the range reaches past a wide trunk.
				var serialized = new SerializedObject(node);
				SerializedProperty triggers = serialized.FindProperty("onInteractTriggers");
				triggers.ClearArray();
				triggers.InsertArrayElementAtIndex(0);
				triggers.GetArrayElementAtIndex(0).objectReferenceValue = gather;
				serialized.FindProperty("InteractionRange").floatValue = Mathf.Max(3.5f, Mathf.Max(size.x, size.z) * 0.5f + 2.5f);
				serialized.ApplyModifiedPropertiesWithoutUndo();

				PrefabUtility.SaveAsPrefabAsset(root, path);
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(root);
			}

			if (NetworkObjectBindingValidator.Scan(path).Count > 0)
			{
				NetworkObjectBindingValidator.Repair(path);
			}
			// Same group and labels as the chest: servers and clients both resolve the spawnable through addressables.
			NPCPrefabFactory.RegisterAddressableLike(path, BasePrefabPath);
			return true;
		}

		/// <summary>The LOD0 mesh of a generated prefab, its materials, and its placement under the prefab root.</summary>
		private static bool TryGetVisualMesh(GameObject visual, out Mesh mesh, out Material[] materials, out Matrix4x4 meshToRoot)
		{
			mesh = null;
			materials = null;
			meshToRoot = Matrix4x4.identity;
			if (visual == null)
			{
				return false;
			}

			Renderer chosen = null;
			LODGroup group = visual.GetComponentInChildren<LODGroup>(true);
			if (group != null)
			{
				LOD[] lods = group.GetLODs();
				if (lods.Length > 0)
				{
					foreach (Renderer renderer in lods[0].renderers)
					{
						if (renderer is MeshRenderer)
						{
							chosen = renderer;
							break;
						}
					}
				}
			}
			if (chosen == null)
			{
				chosen = visual.GetComponentInChildren<MeshRenderer>(true);
			}
			MeshFilter filter = chosen != null ? chosen.GetComponent<MeshFilter>() : null;
			if (filter == null || filter.sharedMesh == null)
			{
				return false;
			}
			mesh = filter.sharedMesh;
			materials = chosen.sharedMaterials;
			meshToRoot = visual.transform.worldToLocalMatrix * chosen.transform.localToWorldMatrix;
			return true;
		}

		/// <summary>A tinted copy of each of the visual's materials, saved beside the prefabs.</summary>
		private static Material[] TintedMaterials(GatheringResource resource, Material[] source)
		{
			var tinted = new Material[source.Length];
			for (int i = 0; i < source.Length; ++i)
			{
				Material original = source[i];
				if (original == null)
				{
					continue;
				}
				string path = $"{MaterialFolder}/{resource.Node} {i}.mat";
				Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
				if (material == null)
				{
					material = new Material(original);
					AssetDatabase.CreateAsset(material, path);
				}
				else
				{
					material.shader = original.shader;
					material.CopyPropertiesFromMaterial(original);
				}
				foreach (string property in new[] { "_BaseColor", "_Color" })
				{
					if (material.HasProperty(property))
					{
						material.SetColor(property, original.GetColor(property) * resource.Tint);
					}
				}
				EditorUtility.SetDirty(material);
				tinted[i] = material;
			}
			return tinted;
		}

		private static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
		{
			Vector3 min = bounds.min, max = bounds.max;
			Bounds result = new Bounds(matrix.MultiplyPoint3x4(min), Vector3.zero);
			for (int i = 1; i < 8; ++i)
			{
				result.Encapsulate(matrix.MultiplyPoint3x4(new Vector3((i & 1) != 0 ? max.x : min.x, (i & 2) != 0 ? max.y : min.y, (i & 4) != 0 ? max.z : min.z)));
			}
			return result;
		}

		private static T LoadOrCreate<T>(string path) where T : ScriptableObject
		{
			T existing = AssetDatabase.LoadAssetAtPath<T>(path);
			if (existing != null)
			{
				return existing;
			}
			T created = ScriptableObject.CreateInstance<T>();
			AssetDatabase.CreateAsset(created, path);
			return created;
		}

		private static void EnsureFolder(string folder)
		{
			if (AssetDatabase.IsValidFolder(folder))
			{
				return;
			}
			string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
			EnsureFolder(parent);
			AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
		}
	}
}
#endif
