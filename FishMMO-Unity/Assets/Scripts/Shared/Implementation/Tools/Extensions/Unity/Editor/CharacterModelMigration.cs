using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// Replaces the Standard Assets "Ethan" with the TestHuman everywhere a character wears a body: builds the TestHuman's
	/// material, clip set (<see cref="CharacterAnimationSet"/>) and prefab (<see cref="CharacterModel"/>), points the races
	/// at it, rebuilds the NPCs that had Ethan baked in, clears Ethan out of the scenes and the Addressables, and deletes
	/// him once nothing refers to him. Safe to run again: each step looks before it changes anything.
	/// </summary>
	/// <remarks>
	/// Run headless: <c>-executeMethod FishMMO.Shared.CharacterModelMigration.Run</c>. It writes what it did to the log, line
	/// by line, prefixed "[Character migration]", and an error for anything it could not do.
	/// </remarks>
	public static class CharacterModelMigration
	{
		public const string Folder = "Assets/Prefabs/Client/Models/TestHuman";
		public const string ModelPath = TestHumanImport.ModelPath;
		public const string BaseMapPath = Folder + "/TestHuman_BaseColor.jpg";
		public const string MaterialPath = Folder + "/TestHuman.mat";
		public const string SetPath = Folder + "/TestHuman Animations.asset";
		public const string PrefabPath = Folder + "/TestHuman.prefab";
		public const string HumanoidFolder = "Assets/Prefabs/Client/Models/Animation/Humanoid";
		public const string EthanFolder = "Assets/Prefabs/Client/Models/EthanCharacter";
		public const string EthanPrefabPath = EthanFolder + "/Prefabs/Ethan.prefab";
		public const string PlaceholderMaterialPath = "Assets/Prefabs/Client/Materials/Teleporter/fm_teleporter_PLACEHOLDER.mat";

		/// <summary>The races that wore Ethan.</summary>
		public static readonly string[] Races =
		{
			"Assets/Templates/Entity/Races/Humanoid/Human.asset",
			"Assets/Templates/Entity/Races/Humanoid/Elf.asset",
			"Assets/Templates/Entity/Races/Humanoid/Orc.asset",
		};

		/// <summary>The NPCs with Ethan baked in as their model.</summary>
		public static readonly string[] Npcs =
		{
			"Assets/Prefabs/Shared/Entity/NPCs/Monsters/Orcs/an orc.prefab",
			"Assets/Prefabs/Shared/Entity/NPCs/Monsters/Orcs/an orc warrior.prefab",
			"Assets/Prefabs/Shared/Entity/NPCs/Monsters/Orcs/an orc mage.prefab",
			"Assets/Prefabs/Shared/Entity/NPCs/Pets/a lesser fire elemental.prefab",
			"Assets/Prefabs/Shared/Entity/NPCs/Interactables/Human/Banker/HumanBanker.prefab",
			"Assets/Prefabs/Shared/Entity/NPCs/Interactables/Human/Merchants/GeneralGoods/HumanGeneralMerchant.prefab",
			"Assets/Prefabs/Shared/Entity/NPCs/Interactables/Human/AbilityCrafter/HumanAbilityCrafter.prefab",
		};

		public const string LoginScene = "Assets/Scenes/Client/ClientLoginGUI.unity";
		public const string StartScene = "Assets/Scenes/WorldScene/StartScene A.unity";

		private static int errors;

		public static void Run()
		{
			errors = 0;
			AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
			Material material = BuildMaterial();
			CharacterAnimationSet set = BuildAnimationSet();
			GameObject prefab = BuildPrefab(material, set);
			if (prefab == null)
			{
				Error("no TestHuman prefab; stopping before anything refers to it.");
				Finish();
				return;
			}
			string prefabGuid = AssetDatabase.AssetPathToGUID(PrefabPath);
			PointRaces(prefabGuid);
			Addressables(prefabGuid);
			foreach (string npc in Npcs)
			{
				RebuildNpc(npc, prefab);
			}
			CleanScenes();
			AssetDatabase.SaveAssets();
			DeleteEthan();
			Finish();
		}

		private static void Finish()
		{
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			Log(errors == 0 ? "done." : $"done with {errors} error(s).");
			if (Application.isBatchMode)
			{
				EditorApplication.Exit(errors == 0 ? 0 : 1);
			}
		}

		private static void Log(string message) => Debug.Log("[Character migration] " + message);

		private static void Error(string message)
		{
			errors++;
			Debug.LogError("[Character migration] " + message);
		}

		// ── The TestHuman's own assets ──

		private static Material BuildMaterial()
		{
			Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
			Shader shader = Shader.Find("FishMMO/Weather Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
			if (material == null)
			{
				material = new Material(shader) { name = "TestHuman" };
				AssetDatabase.CreateAsset(material, MaterialPath);
				Log($"made {MaterialPath} ({shader.name}).");
			}
			else if (material.shader != shader)
			{
				material.shader = shader;
			}
			// Weather-lit, so it wets in rain as the ground does.
			material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(BaseMapPath));
			material.SetFloat("_Smoothness", 0.3f);
			EditorUtility.SetDirty(material);
			return material;
		}

		private static AnimationClip Clip(string path, string name)
		{
			foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
			{
				if (asset is AnimationClip clip && clip.name == name)
				{
					return clip;
				}
			}
			Error($"no clip '{name}' in {path}.");
			return null;
		}

		private static CharacterAnimationSet BuildAnimationSet()
		{
			CharacterAnimationSet set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(SetPath);
			if (set == null)
			{
				set = ScriptableObject.CreateInstance<CharacterAnimationSet>();
				AssetDatabase.CreateAsset(set, SetPath);
				Log($"made {SetPath}.");
			}
			string h = HumanoidFolder + "/";
			set.Idle = Clip(h + "HumanoidIdle.fbx", "HumanoidIdle");
			set.Walk = Clip(h + "HumanoidWalk.fbx", "HumanoidWalk");
			set.Run = Clip(h + "HumanoidRun.fbx", "HumanoidRun");
			set.CrouchIdle = Clip(h + "HumanoidCrouch.fbx", "HumanoidCrouchIdle");
			set.CrouchWalk = Clip(h + "HumanoidCrouch.fbx", "HumanoidCrouchWalk");
			set.JumpUp = Clip(h + "HumanoidIdleJumpUp.fbx", "HumanoidJumpUp");
			set.MidAir = Clip(h + "HumanoidIdleJumpUp.fbx", "HumanoidMidAir");
			set.Fall = Clip(h + "HumanoidIdleJumpUp.fbx", "HumanoidFall");
			set.Tread = Clip(ModelPath, "SwimTread");
			set.SwimSurface = Clip(ModelPath, "SwimSurface");
			set.SwimUnder = Clip(ModelPath, "SwimUnder");
			set.Climb = Clip(ModelPath, "Climb");
			set.PunchRight = Clip(ModelPath, "PunchRight");
			set.PunchLeft = Clip(ModelPath, "PunchLeft");
			set.Cast = Clip(ModelPath, "Cast");
			set.Block = Clip(ModelPath, "Block");
			set.Roll = Clip(ModelPath, "Roll");
			set.Death = Clip(ModelPath, "Death");
			set.SitGround = Clip(ModelPath, "SitGround");
			set.SitSeat = Clip(ModelPath, "SitSeat");
			// The clips were keyed on the figure exported 1.8 m tall; the seat height scales with the model.
			set.SeatHeight = 0.46f;
			set.KeyedHeight = TestHumanImport.ExportedHeight;
			EditorUtility.SetDirty(set);
			return set;
		}

		private static GameObject BuildPrefab(Material material, CharacterAnimationSet set)
		{
			var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
			if (model == null)
			{
				Error($"no model at {ModelPath}.");
				return null;
			}
			var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
			try
			{
				instance.name = "TestHuman";
				Animator animator = instance.GetComponent<Animator>();
				if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
				{
					Error($"{ModelPath} imported without a Humanoid avatar.");
					return null;
				}
				animator.runtimeAnimatorController = null;
				animator.applyRootMotion = false;
				foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
				{
					renderer.sharedMaterial = material;
				}
				CharacterModel marker = instance.GetComponent<CharacterModel>();
				if (marker == null)
				{
					marker = instance.AddComponent<CharacterModel>();
				}
				marker.Animations = set;
				GameObject saved = PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath, out bool success);
				if (!success)
				{
					Error($"could not save {PrefabPath}.");
					return null;
				}
				CharacterBody.RestExtent(instance.transform, out float bottom, out float top);
				Log($"made {PrefabPath}: {top - bottom:F2} m tall, avatar {animator.avatar.name}.");
				return saved;
			}
			finally
			{
				Object.DestroyImmediate(instance);
			}
		}

		// ── Who wears it ──

		private static void PointRaces(string prefabGuid)
		{
			foreach (string path in Races)
			{
				var race = AssetDatabase.LoadAssetAtPath<RaceTemplate>(path);
				if (race == null)
				{
					Error($"no race at {path}.");
					continue;
				}
				if (race.PlaceholderModel != null && race.PlaceholderModel.AssetGUID == prefabGuid)
				{
					continue;
				}
				race.PlaceholderModel = new AssetReference(prefabGuid);
				EditorUtility.SetDirty(race);
				Log($"{race.name} wears the TestHuman.");
			}
		}

		private static void Addressables(string prefabGuid)
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null)
			{
				Error("no Addressables settings.");
				return;
			}
			string ethanGuid = AssetDatabase.AssetPathToGUID(EthanPrefabPath);
			AddressableAssetEntry ethan = string.IsNullOrEmpty(ethanGuid) ? null : settings.FindAssetEntry(ethanGuid);
			AddressableAssetGroup group = ethan != null ? ethan.parentGroup : settings.FindGroup("Client_Dynamic");
			if (group == null)
			{
				Error("no group for the TestHuman (Ethan's, or Client_Dynamic).");
				return;
			}
			AddressableAssetEntry entry = settings.CreateOrMoveEntry(prefabGuid, group, false, false);
			entry.address = "TestHuman.prefab";
			IEnumerable<string> labels = ethan != null ? ethan.labels.ToList() : new List<string> { group.Name };
			foreach (string label in labels)
			{
				entry.SetLabel(label, true, true, false);
			}
			Log($"TestHuman.prefab is addressable in {group.Name}.");

			// Every entry for anything in Ethan's folder goes (the humanoid clips moved out first, keeping their entries).
			foreach (string guid in AssetDatabase.FindAssets("", new[] { EthanFolder }))
			{
				if (settings.FindAssetEntry(guid) != null)
				{
					settings.RemoveAssetEntry(guid, false);
					Log($"removed the Addressables entry for {AssetDatabase.GUIDToAssetPath(guid)}.");
				}
			}
			settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, null, true, true);
		}

		/// <summary>
		/// An NPC that had Ethan's prefab nested as its model (and as its MeshRoot, carrying the labels and points under it):
		/// a plain MeshRoot in Ethan's place keeping those children, the TestHuman under it at Ethan's height, and the
		/// animation controller every character now needs.
		/// </summary>
		private static void RebuildNpc(string path, GameObject prefab)
		{
			GameObject root = PrefabUtility.LoadPrefabContents(path);
			try
			{
				BaseCharacter character = root.GetComponent<BaseCharacter>();
				if (character == null)
				{
					Error($"{path} has no character.");
					return;
				}
				var serialized = new SerializedObject(character);
				SerializedProperty meshProperty = serialized.FindProperty("meshRoot");
				var ethan = meshProperty.objectReferenceValue as Transform;
				bool changed = false;
				if (ethan != null && IsEthan(ethan.gameObject))
				{
					CharacterBody.RestExtent(ethan, out float bottom, out float top);
					float ethanHeight = top - bottom;

					var mesh = new GameObject("MeshRoot");
					mesh.layer = ethan.gameObject.layer;
					mesh.transform.SetParent(ethan.parent, false);
					mesh.transform.localPosition = ethan.localPosition;
					mesh.transform.localRotation = ethan.localRotation;
					mesh.transform.SetSiblingIndex(ethan.GetSiblingIndex());

					// The labels and points were added under Ethan's instance: they keep their place in the world.
					foreach (Transform child in ethan.Cast<Transform>().ToList())
					{
						if (!PrefabUtility.IsPartOfPrefabInstance(child.gameObject) || PrefabUtility.IsAddedGameObjectOverride(child.gameObject))
						{
							child.SetParent(mesh.transform, true);
						}
					}
					Retarget(root, ethan, mesh.transform, path);

					var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, mesh.transform);
					model.transform.localPosition = Vector3.zero;
					model.transform.localRotation = Quaternion.identity;
					model.transform.localScale = Vector3.one;
					foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
					{
						part.gameObject.layer = mesh.layer;
					}
					CharacterBody.RestExtent(model.transform, out float modelBottom, out float modelTop);
					float scale = ethanHeight > 0.1f && modelTop - modelBottom > 0.1f ? ethanHeight / (modelTop - modelBottom) : 1f;
					model.transform.localScale = Vector3.one * scale;

					Object.DestroyImmediate(ethan.gameObject);
					meshProperty.objectReferenceValue = mesh.transform;
					serialized.ApplyModifiedPropertiesWithoutUndo();
					Log($"{Path.GetFileNameWithoutExtension(path)}: Ethan ({ethanHeight:F2} m) replaced by the TestHuman at x{scale:F2}.");
					changed = true;
				}
				else if (character.MeshRoot == null || character.MeshRoot.GetComponentInChildren<CharacterModel>(true) == null)
				{
					Error($"{path}: its MeshRoot is neither Ethan nor a TestHuman; left alone.");
				}
				if (root.GetComponent<CharacterAnimationController>() == null)
				{
					root.AddComponent<CharacterAnimationController>();
					Log($"{Path.GetFileNameWithoutExtension(path)}: animation controller added.");
					changed = true;
				}
				if (changed)
				{
					PrefabUtility.SaveAsPrefabAsset(root, path);
				}
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(root);
			}
		}

		private static bool IsEthan(GameObject gameObject)
		{
			GameObject source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(gameObject);
			return source != null && AssetDatabase.GetAssetPath(source) == EthanPrefabPath;
		}

		/// <summary>
		/// Points every reference into Ethan's instance somewhere that will still exist: Ethan's root (as a Transform or a
		/// GameObject) becomes the new MeshRoot; anything deeper (his bones, his Animator) is reported, and cleared.
		/// </summary>
		private static void Retarget(GameObject root, Transform ethan, Transform mesh, string path)
		{
			var inEthan = new HashSet<Object>();
			foreach (Transform part in ethan.GetComponentsInChildren<Transform>(true))
			{
				inEthan.Add(part);
				inEthan.Add(part.gameObject);
				foreach (Component component in part.GetComponents<Component>())
				{
					inEthan.Add(component);
				}
			}
			foreach (Component component in root.GetComponentsInChildren<Component>(true))
			{
				if (component == null || inEthan.Contains(component))
				{
					continue;
				}
				var serialized = new SerializedObject(component);
				SerializedProperty property = serialized.GetIterator();
				bool dirty = false;
				while (property.Next(true))
				{
					if (property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue == null
						|| !inEthan.Contains(property.objectReferenceValue))
					{
						continue;
					}
					Object to = property.objectReferenceValue == ethan ? mesh
						: property.objectReferenceValue == ethan.gameObject ? mesh.gameObject
						: null;
					if (to == null)
					{
						Log($"{Path.GetFileNameWithoutExtension(path)}: {component.GetType().Name}.{property.propertyPath} pointed into Ethan ({property.objectReferenceValue.name}); cleared.");
					}
					property.objectReferenceValue = to;
					dirty = true;
				}
				if (dirty)
				{
					serialized.ApplyModifiedPropertiesWithoutUndo();
				}
			}
		}

		// ── The scenes ──

		private static void CleanScenes()
		{
			// The login scene kept a copy of Ethan made at runtime ("Ethan(Clone)" under an empty "1") that nothing refers to.
			Scene login = EditorSceneManager.OpenScene(LoginScene, OpenSceneMode.Single);
			bool loginChanged = false;
			foreach (GameObject top in login.GetRootGameObjects())
			{
				Transform clone = top.transform.Find("Ethan(Clone)");
				if (clone != null)
				{
					bool onlyEthan = top.transform.childCount == 1 && top.GetComponents<Component>().Length == 1;
					Object.DestroyImmediate(onlyEthan ? top : clone.gameObject);
					Log($"{LoginScene}: removed the stray Ethan(Clone){(onlyEthan ? $" and its empty holder '{top.name}'" : "")}.");
					loginChanged = true;
				}
			}
			if (loginChanged)
			{
				EditorSceneManager.SaveScene(login);
			}

			// The arena board's placeholder cube wore Ethan's skin.
			string ethanMaterial = EthanFolder + "/Materials/Ethan.mat";
			Material ethanSkin = AssetDatabase.LoadAssetAtPath<Material>(ethanMaterial);
			Material placeholder = AssetDatabase.LoadAssetAtPath<Material>(PlaceholderMaterialPath);
			Scene start = EditorSceneManager.OpenScene(StartScene, OpenSceneMode.Single);
			bool startChanged = false;
			foreach (GameObject top in start.GetRootGameObjects())
			{
				foreach (Renderer renderer in top.GetComponentsInChildren<Renderer>(true))
				{
					Material[] materials = renderer.sharedMaterials;
					bool hit = false;
					for (int i = 0; i < materials.Length; i++)
					{
						if (materials[i] != null && materials[i] == ethanSkin)
						{
							materials[i] = placeholder;
							hit = true;
						}
					}
					if (hit)
					{
						renderer.sharedMaterials = materials;
						Log($"{StartScene}: {renderer.name} wears {Path.GetFileName(PlaceholderMaterialPath)} instead of Ethan's skin.");
						startChanged = true;
					}
				}
			}
			if (startChanged)
			{
				EditorSceneManager.SaveScene(start);
			}
		}

		// ── Ethan himself ──

		/// <summary>Deletes Ethan's folder, only when no asset outside it names any of its GUIDs.</summary>
		private static void DeleteEthan()
		{
			if (!AssetDatabase.IsValidFolder(EthanFolder))
			{
				return;
			}
			var guids = new HashSet<string>(AssetDatabase.FindAssets("", new[] { EthanFolder }));
			guids.Add(AssetDatabase.AssetPathToGUID(EthanFolder));
			string[] kinds = { ".prefab", ".unity", ".asset", ".mat", ".controller", ".overrideController", ".playable", ".anim" };
			var users = new List<string>();
			foreach (string file in Directory.EnumerateFiles("Assets", "*.*", SearchOption.AllDirectories))
			{
				string path = file.Replace('\\', '/');
				if (path.StartsWith(EthanFolder + "/") || !kinds.Contains(Path.GetExtension(path)))
				{
					continue;
				}
				string text = File.ReadAllText(path, Encoding.UTF8);
				foreach (string guid in guids)
				{
					if (!string.IsNullOrEmpty(guid) && text.Contains(guid))
					{
						users.Add($"{path} ({AssetDatabase.GUIDToAssetPath(guid)})");
						break;
					}
				}
			}
			if (users.Count > 0)
			{
				Error($"Ethan is still used, so his folder stays: {string.Join("; ", users)}");
				return;
			}
			AssetDatabase.DeleteAsset(EthanFolder);
			Log($"deleted {EthanFolder}.");
		}
	}
}
