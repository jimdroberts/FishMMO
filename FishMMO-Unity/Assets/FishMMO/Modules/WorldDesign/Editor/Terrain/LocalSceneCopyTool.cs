#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Atlas;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Makes a private copy of the open generated scene under <c>Assets/LOCAL/SceneCopies</c>, with its own
	/// terrain data, and repaints it with this machine's LOCAL art.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why a copy.</b> A committed scene may never reference LOCAL art (<see cref="LocalArtScope"/>),
	/// so on its committed self it shows LOCAL ground textures (the arrays) but the generated trees,
	/// grass and rock pieces. A scene under the gitignored <c>Assets/LOCAL</c> may reference anything,
	/// so the copy is where real scatter art is seen. It is a viewing copy: sculpt and author in the
	/// committed scene, then copy again.
	/// </para>
	/// <para>
	/// <b>Its own terrain data, or it would leak.</b> A plain copy of the .unity file still points at
	/// the committed TerrainData, and the paint writes its references into the TerrainData, not the
	/// scene. So every TerrainData the scene uses is copied into the copy's <c>Terrain</c> folder and
	/// every reference to it in the copy (terrains, colliders, anything else) is re-pointed before the
	/// repaint; the gate refuses to paint LOCAL art into committed terrain data anyway.
	/// </para>
	/// <para>
	/// <b>Its own name.</b> The copy is <c>&lt;name&gt;</c> + <see cref="LocalArtScope.LocalSceneSuffix"/>,
	/// because the terrain arrays are baked and found by scene name and the copy's palette may differ
	/// from the committed one's. The repaint finds the atlas entry through the suffix.
	/// </para>
	/// </remarks>
	public static class LocalSceneCopyTool
	{
		[DashboardTool(DashboardToolAttribute.WorldSceneDetails, "Copy open scene to LOCAL for real art", Section = "Generated scenes", Order = 11,
			Tooltip = "Copies the open generated scene and its terrain data to Assets/LOCAL/SceneCopies (gitignored) and repaints the copy with this machine's LOCAL art overrides (trees, grass, terrain layers, cliff materials, ice prefabs). The committed scene is not changed.")]
		public static void CopyOpenScene()
		{
			string problem = Copy(EditorSceneManager.GetActiveScene(), out string copyPath, out SceneGenerationResult result);
			if (problem != null)
			{
				Debug.LogWarning($"[Copy scene to LOCAL] {problem}");
				return;
			}
			Debug.Log($"[Copy scene to LOCAL] '{copyPath}' is open, painted with LOCAL art: {result.BiomeSummary}.");
			foreach (string note in result.Notes)
			{
				Debug.LogWarning($"[Copy scene to LOCAL] {note}");
			}
		}

		/// <summary>Where a scene's LOCAL copy lives.</summary>
		public static string CopyPathFor(string sceneName)
		{
			string safe = WorldEditorAssets.Sanitize(sceneName);
			return $"{LocalArtScope.LocalScenesFolder}/{safe}/{safe}{LocalArtScope.LocalSceneSuffix}.unity";
		}

		/// <summary>Copies, opens and repaints. Returns why it did nothing, or null.</summary>
		public static string Copy(Scene source, out string copyPath, out SceneGenerationResult result)
		{
			copyPath = null;
			result = null;
			if (!source.IsValid() || string.IsNullOrEmpty(source.path))
			{
				return "Open a saved generated scene first.";
			}
			if (LocalArtScope.IsLocalScenePath(source.path))
			{
				return $"'{source.path}' is already under Assets/LOCAL; use \"Repaint biomes in open scene\" on it.";
			}
			WorldAtlasScene entry = WorldAtlasScene.Find(source.name);
			if (entry == null || entry.Body == null || !entry.Placed)
			{
				return $"'{source.name}' has no placed atlas entry, so its copy could not be repainted from its biomes.";
			}
			if (source.isDirty && !EditorSceneManager.SaveModifiedScenesIfUserWantsTo(new[] { source }))
			{
				return "Cancelled.";
			}
			if (source.isDirty)
			{
				return "The scene has unsaved changes; save it (or discard them) first, so the copy is what is on disk.";
			}

			copyPath = CopyPathFor(source.name);
			string folder = Path.GetDirectoryName(copyPath).Replace('\\', '/');
			string terrainFolder = folder + "/Terrain";
			if (AssetDatabase.IsValidFolder(folder))
			{
				if (!EditorUtility.DisplayDialog("Copy scene to LOCAL", $"'{folder}' already exists. Replace it (its scene and terrain data) with a fresh copy?", "Replace", "Cancel"))
				{
					return "Cancelled; the existing copy is untouched.";
				}
				AssetDatabase.DeleteAsset(folder);
			}
			WorldEditorAssets.EnsureFolder(terrainFolder);

			string sourcePath = source.path;
			if (!AssetDatabase.CopyAsset(sourcePath, copyPath))
			{
				return $"Could not copy '{sourcePath}' to '{copyPath}'.";
			}
			Scene copy = EditorSceneManager.OpenScene(copyPath, OpenSceneMode.Single);

			// Copy every TerrainData once, then re-point every reference to it in the copy.
			var copies = new Dictionary<TerrainData, TerrainData>();
			foreach (GameObject root in copy.GetRootGameObjects())
			{
				foreach (Terrain terrain in root.GetComponentsInChildren<Terrain>(true))
				{
					TerrainData data = terrain.terrainData;
					if (data == null || copies.ContainsKey(data))
					{
						continue;
					}
					string from = AssetDatabase.GetAssetPath(data);
					if (string.IsNullOrEmpty(from))
					{
						return $"'{terrain.name}' has terrain data that is not an asset; the copy at '{copyPath}' was left unpainted.";
					}
					string to = AssetDatabase.GenerateUniqueAssetPath($"{terrainFolder}/{Path.GetFileName(from)}");
					if (!AssetDatabase.CopyAsset(from, to))
					{
						return $"Could not copy terrain data '{from}' to '{to}'; the copy at '{copyPath}' was left unpainted.";
					}
					copies[data] = AssetDatabase.LoadAssetAtPath<TerrainData>(to);
				}
			}
			int repointed = Repoint(copy, copies);
			EditorSceneManager.MarkSceneDirty(copy);
			EditorSceneManager.SaveScene(copy);

			string problem = BiomeRepaintTool.Repaint(copy, out result);
			if (problem != null)
			{
				return $"Copied to '{copyPath}' ({copies.Count} terrain data, {repointed} reference(s) re-pointed) but not repainted: {problem}";
			}
			EditorSceneManager.SaveScene(copy);
			return null;
		}

		/// <summary>Points every object reference in the scene that names a key of <paramref name="map"/> at its value.</summary>
		private static int Repoint(Scene scene, Dictionary<TerrainData, TerrainData> map)
		{
			int count = 0;
			if (map.Count == 0)
			{
				return 0;
			}
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (Component component in root.GetComponentsInChildren<Component>(true))
				{
					if (component == null)
					{
						continue;
					}
					var serialized = new SerializedObject(component);
					SerializedProperty property = serialized.GetIterator();
					bool changed = false;
					while (property.Next(true))
					{
						if (property.propertyType == SerializedPropertyType.ObjectReference
							&& property.objectReferenceValue is TerrainData data && map.TryGetValue(data, out TerrainData local) && local != null)
						{
							property.objectReferenceValue = local;
							changed = true;
							count++;
						}
					}
					if (changed)
					{
						serialized.ApplyModifiedPropertiesWithoutUndo();
					}
				}
			}
			return count;
		}
	}
}
#endif
