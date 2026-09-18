using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace FishMMO.Client
{
	/// <summary>
	/// Puts the volumetric cloud pass on every URP renderer, so the clouds are drawn on every
	/// quality tier without anyone wiring a renderer asset by hand.
	/// </summary>
	/// <remarks>
	/// A renderer feature lives inside its renderer asset as a sub-asset. This adds one where it is
	/// missing, points it at the cloud material, and leaves any that already exist alone — the tier
	/// differences are settings on the weather render profile, not separate features.
	/// </remarks>
	public static class CloudRendererSetup
	{
		/// <summary>Adds the cloud feature to every renderer that has none. Returns how many were changed.</summary>
		public static int EnsureFeature(Material cloudMaterial)
		{
			int added = 0;
			foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
				if (data == null)
				{
					continue;
				}
				FishCloudsFeature existing = null;
				foreach (ScriptableRendererFeature feature in data.rendererFeatures)
				{
					if (feature is FishCloudsFeature clouds)
					{
						existing = clouds;
						break;
					}
				}
				if (existing != null)
				{
					if (existing.CloudMaterial != cloudMaterial && cloudMaterial != null)
					{
						existing.CloudMaterial = cloudMaterial;
						EditorUtility.SetDirty(existing);
					}
					continue;
				}

				var created = ScriptableObject.CreateInstance<FishCloudsFeature>();
				created.name = "Fish Clouds";
				created.CloudMaterial = cloudMaterial;
				AssetDatabase.AddObjectToAsset(created, data);
				AssetDatabase.SaveAssets();

				var serialized = new SerializedObject(data);
				SerializedProperty features = serialized.FindProperty("m_RendererFeatures");
				SerializedProperty map = serialized.FindProperty("m_RendererFeatureMap");
				features.arraySize++;
				features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = created;
				// The map is the feature list's identity: one 8-byte local id per feature, in order.
				long id = LocalId(created);
				map.arraySize = features.arraySize * 8;
				for (int i = 0; i < 8; i++)
				{
					map.GetArrayElementAtIndex((features.arraySize - 1) * 8 + i).intValue = (int)((id >> (i * 8)) & 0xFF);
				}
				serialized.ApplyModifiedProperties();
				EditorUtility.SetDirty(data);
				added++;
				Debug.Log($"[Clouds] Added the volumetric cloud pass to {System.IO.Path.GetFileNameWithoutExtension(path)}.");
			}
			if (added > 0)
			{
				AssetDatabase.SaveAssets();
			}
			return added;
		}

		private static long LocalId(Object asset)
		{
			return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string _, out long localId) ? localId : 0L;
		}
	}
}
