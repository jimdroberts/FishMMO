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

				Append(data, created);
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

		/// <summary>
		/// Appends a feature to a renderer's list, keeping the feature map in step.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The map is the list's identity: ONE local file id per feature, in order — URP's
		/// <c>List&lt;long&gt;</c>, which the asset writes out as eight hex bytes per entry. A feature
		/// appended without it loads as a null entry and the renderer silently drops it, which
		/// looks exactly like the pass not working.
		/// </para>
		/// <para>
		/// This used to treat the map as a byte array, eight entries per feature holding one byte
		/// each, which left every renderer's map eight times too long and matching nothing. URP
		/// quietly rebuilt it in memory on load (a map whose count disagrees with the list is
		/// treated as missing), so nothing broke — but the one thing the map exists for, relinking
		/// a feature whose script failed to load, could never have worked from it.
		/// </para>
		/// </remarks>
		private static void Append(ScriptableRendererData data, ScriptableRendererFeature feature)
		{
			var serialized = new SerializedObject(data);
			SerializedProperty features = serialized.FindProperty("m_RendererFeatures");
			SerializedProperty map = serialized.FindProperty("m_RendererFeatureMap");
			features.arraySize++;
			features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
			map.arraySize = features.arraySize;
			map.GetArrayElementAtIndex(features.arraySize - 1).longValue = LocalId(feature);
			serialized.ApplyModifiedProperties();
			EditorUtility.SetDirty(data);
		}

		private static long LocalId(Object asset)
		{
			return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string _, out long localId) ? localId : 0L;
		}
	}
}
