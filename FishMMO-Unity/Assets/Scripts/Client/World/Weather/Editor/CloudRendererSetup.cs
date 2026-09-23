using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace FishMMO.Client
{
	/// <summary>
	/// Puts the weather's render passes on every URP renderer, so they are drawn on every quality
	/// tier without anyone wiring a renderer asset by hand.
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

		/// <summary>
		/// Adds the height-fog pass to every renderer that has none. Returns how many were changed.
		/// </summary>
		/// <remarks>
		/// On every tier, not only the top ones. The pass is a single full-screen shader with a
		/// closed-form integral and no loop, so it costs a fraction of what the cloud march already
		/// costs on the cheapest tier — and fog lying in the low ground is most of what makes
		/// weather read as weather.
		/// </remarks>
		public static int EnsureHeightFog()
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
				bool present = false;
				foreach (ScriptableRendererFeature feature in data.rendererFeatures)
				{
					if (feature is FishHeightFogFeature)
					{
						present = true;
						break;
					}
				}
				if (present)
				{
					continue;
				}

				var created = ScriptableObject.CreateInstance<FishHeightFogFeature>();
				created.name = "Fish Height Fog";
				AssetDatabase.AddObjectToAsset(created, data);
				AssetDatabase.SaveAssets();
				Append(data, created);
				added++;
				Debug.Log($"[Fog] Added the height-fog pass to {System.IO.Path.GetFileNameWithoutExtension(path)}.");
			}
			if (added > 0)
			{
				AssetDatabase.SaveAssets();
			}
			return added;
		}

		/// <summary>
		/// Adds the froxel volumetric-fog pass to every renderer that has none.
		/// </summary>
		/// <remarks>
		/// Added on every tier even though it needs compute: the feature checks
		/// <see cref="FishVolumetricFogFeature.Supported"/> itself and enqueues nothing where there
		/// is none, so a WebGL2 build carries an inert feature rather than a different renderer.
		/// One asset that behaves correctly everywhere beats two that have to be kept in step.
		/// </remarks>
		public static int EnsureVolumetricFog()
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
				bool present = false;
				foreach (ScriptableRendererFeature feature in data.rendererFeatures)
				{
					if (feature is FishVolumetricFogFeature)
					{
						present = true;
						break;
					}
				}
				if (present)
				{
					continue;
				}
				var created = ScriptableObject.CreateInstance<FishVolumetricFogFeature>();
				created.name = "Fish Volumetric Fog";
				AssetDatabase.AddObjectToAsset(created, data);
				AssetDatabase.SaveAssets();
				Append(data, created);
				added++;
				Debug.Log($"[Fog] Added the volumetric fog pass to {System.IO.Path.GetFileNameWithoutExtension(path)}.");
			}
			if (added > 0)
			{
				AssetDatabase.SaveAssets();
			}
			return added;
		}

		/// <summary>Adds the weather screen overlay to every renderer that has none.</summary>
		public static int EnsureOverlay()
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
				bool present = false;
				foreach (ScriptableRendererFeature feature in data.rendererFeatures)
				{
					if (feature is FishWeatherOverlayFeature)
					{
						present = true;
						break;
					}
				}
				if (present)
				{
					continue;
				}
				var created = ScriptableObject.CreateInstance<FishWeatherOverlayFeature>();
				created.name = "Fish Weather Overlay";
				AssetDatabase.AddObjectToAsset(created, data);
				AssetDatabase.SaveAssets();
				Append(data, created);
				added++;
				Debug.Log($"[Overlay] Added the weather overlay to {System.IO.Path.GetFileNameWithoutExtension(path)}.");
			}
			if (added > 0)
			{
				AssetDatabase.SaveAssets();
			}
			return added;
		}

		/// <summary>
		/// Appends a feature to a renderer's list, keeping the feature map in step.
		/// </summary>
		/// <remarks>
		/// The map is the list's identity: eight bytes of local file id per feature, in order. A
		/// feature appended without it loads as a null entry and the renderer silently drops it,
		/// which looks exactly like the pass not working.
		/// </remarks>
		private static void Append(ScriptableRendererData data, ScriptableRendererFeature feature)
		{
			var serialized = new SerializedObject(data);
			SerializedProperty features = serialized.FindProperty("m_RendererFeatures");
			SerializedProperty map = serialized.FindProperty("m_RendererFeatureMap");
			features.arraySize++;
			features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
			long id = LocalId(feature);
			map.arraySize = features.arraySize * 8;
			for (int i = 0; i < 8; i++)
			{
				map.GetArrayElementAtIndex((features.arraySize - 1) * 8 + i).intValue = (int)((id >> (i * 8)) & 0xFF);
			}
			serialized.ApplyModifiedProperties();
			EditorUtility.SetDirty(data);
		}

		private static long LocalId(Object asset)
		{
			return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string _, out long localId) ? localId : 0L;
		}
	}
}
