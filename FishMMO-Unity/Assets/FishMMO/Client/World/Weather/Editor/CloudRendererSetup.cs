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
		/// <summary>
		/// The guids of the project's own renderer assets — under Assets/ only.
		/// </summary>
		/// <remarks>
		/// An unscoped FindAssets also returns URP's package-internal default renderer
		/// (Library/PackageCache/…/Runtime/Data/UniversalRendererData.asset). Features written into
		/// that file outlive the scripts they name: when the weather overlay feature was removed its
		/// sub-object stayed behind as a null list entry, and URP logged "UniversalRendererData is
		/// missing RendererFeatures" on every load of it — each domain reload, each scene recut, each
		/// click on the console entry. Never touch package assets.
		/// </remarks>
		internal static string[] ProjectRenderers()
		{
			return AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets" });
		}

		/// <summary>Adds the cloud feature to every renderer that has none. Returns how many were changed.</summary>
		public static int EnsureFeature(Material cloudMaterial)
		{
			int added = 0;
			foreach (string guid in ProjectRenderers())
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
				if (data == null)
				{
					continue;
				}
				FishCloudsFeature existing = null;
				bool unresolved = false;
				foreach (ScriptableRendererFeature feature in data.rendererFeatures)
				{
					if (feature == null)
					{
						unresolved = true;
					}
					else if (feature is FishCloudsFeature clouds)
					{
						existing = clouds;
						break;
					}
				}
				if (existing == null && unresolved)
				{
					/* A null in the list is a feature that did not resolve (a script mid-compile, or one
					 * deleted), which may be this one: adding a cloud feature now could save a second one
					 * into the asset. Judged again on the next call. */
					continue;
				}
				if (existing != null)
				{
					bool changed = AssignComputes(existing);
					if (existing.CloudMaterial != cloudMaterial && cloudMaterial != null)
					{
						existing.CloudMaterial = cloudMaterial;
						changed = true;
					}
					if (changed)
					{
						EditorUtility.SetDirty(existing);
					}
					continue;
				}

				var created = ScriptableObject.CreateInstance<FishCloudsFeature>();
				created.name = "Fish Clouds";
				created.CloudMaterial = cloudMaterial;
				AssignComputes(created);
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
		/// Gives a cloud feature its compute kernels where it has none. The feature finds them by name
		/// itself in the editor, but only the serialized reference ships: a build whose renderer never
		/// had them assigned falls back to the fragment resolve and the CPU weather map for good.
		/// </summary>
		private static bool AssignComputes(FishCloudsFeature feature)
		{
			bool changed = false;
			if (feature.ResolveCompute == null)
			{
				feature.ResolveCompute = FindCompute(FishCloudsFeature.ResolveComputeName);
				changed |= feature.ResolveCompute != null;
			}
			if (feature.WeatherMapCompute == null)
			{
				feature.WeatherMapCompute = FindCompute(FishCloudsFeature.WeatherMapComputeName);
				changed |= feature.WeatherMapCompute != null;
			}
			return changed;
		}

		private static ComputeShader FindCompute(string name)
		{
			foreach (string guid in AssetDatabase.FindAssets($"{name} t:ComputeShader"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (System.IO.Path.GetFileNameWithoutExtension(path) == name)
				{
					return AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
				}
			}
			return null;
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
			foreach (string guid in ProjectRenderers())
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
				if (data == null || Has<FishHeightFogFeature>(data) != false)
				{
					continue;   // present, or not resolved yet (a null in the list): never add a second one
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
			foreach (string guid in ProjectRenderers())
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
				if (data == null || Has<FishVolumetricFogFeature>(data) != false)
				{
					continue;   // present, or not resolved yet (a null in the list): never add a second one
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
		/// Adds the heat-shimmer pass to every renderer that has none, with its shader assigned so a build
		/// carries it. Returns how many were changed.
		/// </summary>
		/// <remarks>
		/// Added switched OFF on the Performant renderer (the feature's own active toggle): a full-screen copy
		/// and a displaced redraw of the frame is the kind of cost that tier exists to leave out. Anyone can tick
		/// it on there in the renderer's inspector; an existing feature is never touched.
		/// </remarks>
		public static int EnsureHeatShimmer()
		{
			int added = 0;
			Shader shader = Shader.Find(FishHeatShimmerFeature.ShaderName);
			foreach (string guid in ProjectRenderers())
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
				if (data == null || Has<FishHeatShimmerFeature>(data) != false)
				{
					continue;   // present, or not resolved yet (a null in the list): never add a second one
				}
				var created = ScriptableObject.CreateInstance<FishHeatShimmerFeature>();
				created.name = "Fish Heat Shimmer";
				created.ShimmerShader = shader;
				bool performant = System.IO.Path.GetFileNameWithoutExtension(path).IndexOf("Performant", System.StringComparison.OrdinalIgnoreCase) >= 0;
				created.SetActive(!performant);
				AssetDatabase.AddObjectToAsset(created, data);
				AssetDatabase.SaveAssets();
				Append(data, created);
				added++;
				Debug.Log($"[Heat] Added the heat-shimmer pass to {System.IO.Path.GetFileNameWithoutExtension(path)}{(performant ? " (switched off on this tier)" : "")}.");
			}
			if (added > 0)
			{
				AssetDatabase.SaveAssets();
			}
			return added;
		}

		/// <summary>
		/// True when the renderer has a feature of this type, false when it surely has none, null when it
		/// cannot tell: a null entry is a feature that did not resolve (mid-compile, deleted), which may be it.
		/// </summary>
		private static bool? Has<T>(ScriptableRendererData data) where T : ScriptableRendererFeature
		{
			bool unresolved = false;
			foreach (ScriptableRendererFeature feature in data.rendererFeatures)
			{
				if (feature is T)
				{
					return true;
				}
				unresolved |= feature == null;
			}
			return unresolved ? (bool?)null : false;
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
