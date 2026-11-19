using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace FishMMO.Client
{
	/// <summary>
	/// Puts the core's module passes host on every URP renderer and fills the weather's render features in the weather render
	/// profile (clouds, height fog, froxel fog, heat shimmer), so they are drawn on every quality tier without anyone wiring
	/// a renderer asset by hand.
	/// </summary>
	/// <remarks>
	/// A renderer feature lives inside its renderer asset as a sub-asset. The host is the only one the weather needs (Phase 4
	/// of the core and modules plan, 2026-11-18); its tier comes from the renderer's name.
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

		/// <summary>
		/// Gives the profile's cloud feature its material and compute kernels where it has none, and puts the core's module
		/// passes host on every renderer that has none (Phase 4 of the core and modules plan, 2026-11-18: the weather's passes
		/// stand in the weather render profile now, drawn by the renderers' Fish Module Passes). Returns how many renderers
		/// were changed.
		/// </summary>
		public static int EnsureFeature(Material cloudMaterial)
		{
			FishCloudsFeature clouds = Settings(p => p.CloudsFeature);
			if (clouds != null)
			{
				bool changed = AssignComputes(clouds);
				if (clouds.CloudMaterial != cloudMaterial && cloudMaterial != null)
				{
					clouds.CloudMaterial = cloudMaterial;
					changed = true;
				}
				if (changed)
				{
					SaveProfile();
				}
			}
			return EnsureHost();
		}

		/// <summary>
		/// Gives a cloud feature its compute kernels where it has none. The feature finds them by name
		/// itself in the editor, but only the serialized reference ships: a build whose profile never
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

		/// <summary>The height-fog pass: its shader on the profile, and the host on every renderer.</summary>
		/// <remarks>
		/// On every tier, not only the top ones. The pass is a single full-screen shader with a
		/// closed-form integral and no loop, so it costs a fraction of what the cloud march already
		/// costs on the cheapest tier — and fog lying in the low ground is most of what makes
		/// weather read as weather.
		/// </remarks>
		public static int EnsureHeightFog()
		{
			FishHeightFogFeature fog = Settings(p => p.HeightFog);
			if (fog != null && fog.FogShader == null)
			{
				fog.FogShader = Shader.Find(FishHeightFogFeature.ShaderName);
				SaveProfile();
			}
			return EnsureHost();
		}

		/// <summary>The froxel volumetric-fog pass: the host on every renderer.</summary>
		/// <remarks>
		/// Drawn on every tier even though it needs compute: the feature checks
		/// <see cref="FishVolumetricFogFeature.Supported"/> itself and enqueues nothing where there
		/// is none. The Performant renderer leaves the falls' mist out.
		/// </remarks>
		public static int EnsureVolumetricFog()
		{
			return EnsureHost();
		}

		/// <summary>The heat-shimmer pass: its shader on the profile, and the host on every renderer.</summary>
		/// <remarks>Not drawn on the Performant renderer (a full-screen copy and a displaced redraw of the frame is the kind of
		/// cost that tier exists to leave out).</remarks>
		public static int EnsureHeatShimmer()
		{
			FishHeatShimmerFeature shimmer = Settings(p => p.HeatShimmer);
			if (shimmer != null && shimmer.ShimmerShader == null)
			{
				shimmer.ShimmerShader = Shader.Find(FishHeatShimmerFeature.ShaderName);
				SaveProfile();
			}
			return EnsureHost();
		}

		/// <summary>The tier a renderer asset draws for, from its name.</summary>
		public static RendererTier TierOf(string rendererPath)
		{
			string name = System.IO.Path.GetFileNameWithoutExtension(rendererPath);
			return name.IndexOf("Performant", System.StringComparison.OrdinalIgnoreCase) >= 0 ? RendererTier.Performant
				: name.IndexOf("Balanced", System.StringComparison.OrdinalIgnoreCase) >= 0 ? RendererTier.Balanced
				: RendererTier.HighFidelity;
		}

		/// <summary>Adds the core's module passes host to every renderer that has none. Returns how many were changed.</summary>
		public static int EnsureHost()
		{
			int added = 0;
			foreach (string guid in ProjectRenderers())
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
				if (data == null || Has<FishModulePassesFeature>(data) != false)
				{
					continue;   // present, or not resolved yet (a null in the list): never add a second one
				}
				var created = ScriptableObject.CreateInstance<FishModulePassesFeature>();
				created.name = "Fish Module Passes";
				created.Tier = TierOf(path);
				AssetDatabase.AddObjectToAsset(created, data);
				AssetDatabase.SaveAssets();
				Append(data, created);
				added++;
				Debug.Log($"[Weather render] Added the module passes host to {System.IO.Path.GetFileNameWithoutExtension(path)}.");
			}
			if (added > 0)
			{
				AssetDatabase.SaveAssets();
			}
			return added;
		}

		private static T Settings<T>(System.Func<WeatherRenderProfile, T> of) where T : ModuleRenderFeature
		{
			var profile = AssetDatabase.LoadAssetAtPath<WeatherRenderProfile>(WeatherRenderAssets.ProfilePath);
			return profile != null ? of(profile) : null;
		}

		private static void SaveProfile()
		{
			var profile = AssetDatabase.LoadAssetAtPath<WeatherRenderProfile>(WeatherRenderAssets.ProfilePath);
			if (profile != null)
			{
				EditorUtility.SetDirty(profile);
				ModuleRenderFeatures.Changed();
			}
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
