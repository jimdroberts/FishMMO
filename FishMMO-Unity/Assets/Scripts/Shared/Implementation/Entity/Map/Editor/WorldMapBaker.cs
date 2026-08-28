using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Core;

namespace FishMMO.Shared.Editor
{
	/// <summary>
	/// Photographs every world scene from directly overhead and writes the result, together with
	/// the scene's bounds, labels and landmarks, into a <see cref="WorldMapDefinition"/>.
	/// </summary>
	/// <remarks>
	/// <para><b>Why the world map is baked and the minimap is not.</b> The minimap is a live
	/// camera because it is small and centred on the player. The world map covers a whole zone, and
	/// at runtime a client has only the small part of that zone the server has streamed to it — so
	/// a live capture of it would be a map with holes where nothing had spawned yet. In the editor
	/// the whole scene is loaded, nothing is streamed, and the capture is deterministic: every
	/// player gets the same map of the same terrain, which is also why widening the view on a
	/// modified client reveals nothing that is not public.</para>
	///
	/// <para><b>It needs a graphics device.</b> Reading pixels back off a render texture is not
	/// possible under <c>-nographics</c>. Run under <c>xvfb-run</c> in a headless environment; when
	/// there is no device the bake still writes the definition's data — bounds, labels,
	/// landmarks, the migrated loading image — and skips only the photograph, so the world map
	/// works with a plain background rather than not at all.</para>
	/// </remarks>
	public static class WorldMapBaker
	{
		/// <summary>Folder the definitions and their baked images are written to.</summary>
		private const string OutputDirectory = "Assets/Prefabs/Shared/WorldMaps";

		/// <summary>Addressable group the baked images are placed in.</summary>
		private const string AddressableGroupName = "WorldMaps";

		/// <summary>Longest edge, in pixels, of a baked map image.</summary>
		/// <remarks>
		/// 2048 across a zone that may be two kilometres wide is about a metre per pixel, which is
		/// more than a world map ever shows: at the closest zoom the panel is 600 points across
		/// roughly 60 metres of world, so the texture is being magnified either way. Going higher
		/// costs memory in every client for detail the panel cannot present.
		/// </remarks>
		private const int MaximumImageEdge = 2048;

		/// <summary>How far above the terrain the capture camera sits, in metres.</summary>
		private const float CaptureHeight = 2000.0f;

		/// <summary>How far below the camera it can see, in metres.</summary>
		private const float CaptureDepth = 4000.0f;

		/// <summary>Layers photographed for the map.</summary>
		/// <remarks>
		/// The same set the minimap uses, and for the same reason: terrain, water and ordinary
		/// scenery are the map, and characters are drawn as markers so that the map can apply a
		/// visibility rule to them.
		/// </remarks>
		private static readonly string[] CaptureLayerNames = { "Default", "Ground", "Water" };

		/// <summary>
		/// Bakes a map for every world scene.
		/// </summary>
		[MenuItem("FishMMO/World Map/Bake Maps")]
		public static void BakeAll()
		{
			string worldScenePath = Constants.Configuration.WorldScenePath.Replace(@"\", @"/");

			HashSet<string> scenes = DirectoryExtensions.GetAllFiles(Constants.Configuration.WorldScenePath, ".unity");
			if (EditorPrefs.GetBool("FishMMOEnableLocalDirectory"))
			{
				scenes.UnionWith(DirectoryExtensions.GetAllFiles(Constants.Configuration.LocalScenePath, ".unity"));
			}

			if (scenes.Count < 1)
			{
				Debug.LogWarning($"[WorldMapBaker] No scenes found under '{worldScenePath}'. Nothing to bake.");
				return;
			}

			Directory.CreateDirectory(OutputDirectory);

			Scene initialScene = EditorSceneManager.GetActiveScene();
			string initialScenePath = initialScene.path;

			int baked = 0;
			foreach (string scenePath in scenes)
			{
				Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
				if (!scene.IsValid())
				{
					continue;
				}

				try
				{
					if (BakeOpenScene(scene))
					{
						++baked;
					}
				}
				finally
				{
					/* Saved before closing, because the bake can WRITE to the scene: it assigns a
					 * newly created definition onto WorldSceneSettings and clears the migrated
					 * loading image. Closing without saving would discard both and the next bake
					 * would make the same definition again. */
					if (scene.isDirty)
					{
						EditorSceneManager.SaveScene(scene);
					}
					EditorSceneManager.CloseScene(scene, true);
				}
			}

			if (!string.IsNullOrEmpty(initialScenePath) && !initialScene.isLoaded)
			{
				EditorSceneManager.OpenScene(initialScenePath, OpenSceneMode.Additive);
			}

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();

			Debug.Log($"[WorldMapBaker] Baked {baked} of {scenes.Count} world scene maps into '{OutputDirectory}'.");
		}

		/// <summary>
		/// Bakes the map for a scene that is already open.
		/// </summary>
		/// <param name="scene">The open scene.</param>
		/// <returns>True when a definition was written.</returns>
		private static bool BakeOpenScene(Scene scene)
		{
			WorldSceneSettings settings = FindInScene<WorldSceneSettings>(scene);
			if (settings == null)
			{
				Debug.LogWarning($"[WorldMapBaker] Scene '{scene.name}' has no WorldSceneSettings, so there is nowhere to record its map definition. Add the component, or leave the scene out of the world scene folder.");
				return false;
			}

			WorldMapDefinition definition = settings.MapDefinition;
			if (definition == null)
			{
				definition = CreateDefinition(scene.name);
				settings.MapDefinition = definition;
				EditorUtility.SetDirty(settings);
				EditorSceneManager.MarkSceneDirty(scene);
			}

			definition.SceneName = scene.name;

			if (string.IsNullOrWhiteSpace(definition.DisplayName))
			{
				definition.DisplayName = scene.name;
			}

			/* The loading image migrates on the first bake and only then: the component's copy is
			 * cleared afterwards, so a later bake finds nothing to move and leaves whatever the
			 * definition holds alone. Somebody who changes the image on the definition does not
			 * have it overwritten by a stale value on a component they were told to stop using. */
			if (definition.SceneTransitionImage == null && settings.SceneTransitionImage != null)
			{
				definition.SceneTransitionImage = settings.SceneTransitionImage;
				settings.SceneTransitionImage = null;
				EditorUtility.SetDirty(settings);
				EditorSceneManager.MarkSceneDirty(scene);
				Debug.Log($"[WorldMapBaker] Moved '{scene.name}' loading image from WorldSceneSettings into its map definition.");
			}

			HarvestAuthoredContent(scene, definition);

			if (!definition.HasAuthoredBounds)
			{
				Rect derived = MapBoundsResolver.FromOpenScene();
				if (derived.width > 0.0f && derived.height > 0.0f)
				{
					definition.SetDerivedBounds(derived);
				}
			}

			if (!definition.HasBounds)
			{
				Debug.LogWarning($"[WorldMapBaker] Scene '{scene.name}' has no boundary or terrain, so its map has no extents and no image could be captured. Add a SceneBoundary, or set the bounds on '{definition.name}' by hand.");
				EditorUtility.SetDirty(definition);
				return true;
			}

			CaptureImage(definition);

			EditorUtility.SetDirty(definition);
			AssetDatabase.SaveAssetIfDirty(definition);
			return true;
		}

		/// <summary>
		/// Creates a definition asset for a scene.
		/// </summary>
		/// <param name="sceneName">The scene's name.</param>
		/// <returns>The new asset.</returns>
		private static WorldMapDefinition CreateDefinition(string sceneName)
		{
			WorldMapDefinition definition = ScriptableObject.CreateInstance<WorldMapDefinition>();
			definition.SceneName = sceneName;
			definition.DisplayName = sceneName;

			string path = AssetDatabase.GenerateUniqueAssetPath($"{OutputDirectory}/{SanitizeFileName(sceneName)}Map.asset");
			AssetDatabase.CreateAsset(definition, path);
			Debug.Log($"[WorldMapBaker] Created map definition '{path}' for scene '{sceneName}'.");
			return definition;
		}

		/// <summary>
		/// Copies the scene's region labels and landmarks into the definition.
		/// </summary>
		/// <param name="scene">The open scene.</param>
		/// <param name="definition">The definition to fill.</param>
		private static void HarvestAuthoredContent(Scene scene, WorldMapDefinition definition)
		{
			definition.RegionLabels.Clear();
			foreach (MapRegionLabel region in FindAllInScene<MapRegionLabel>(scene))
			{
				definition.RegionLabels.Add(region.ToDetails());
			}

			definition.PointsOfInterest.Clear();
			foreach (MapPointOfInterest landmark in FindAllInScene<MapPointOfInterest>(scene))
			{
				definition.PointsOfInterest.Add(landmark.ToDetails());
			}
		}

		/// <summary>
		/// Photographs the scene from overhead and writes the image beside the definition.
		/// </summary>
		/// <param name="definition">The definition being baked.</param>
		private static void CaptureImage(WorldMapDefinition definition)
		{
			if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
			{
				Debug.LogWarning($"[WorldMapBaker] No graphics device, so no map image was captured for '{definition.SceneName}'. Re-run without -nographics (under xvfb-run on a headless machine) to produce the image; everything else in the definition has been written.");
				return;
			}

			Rect rect = definition.MapRect;

			/* Pixel dimensions follow the scene's aspect rather than forcing a square. The view
			 * maps texture coordinates from the world rectangle, so any aspect draws correctly —
			 * but squashing a long coastal zone into a square texture would spend half its pixels
			 * on nothing and halve the resolution along the axis that needed it. */
			float longestSide = Mathf.Max(rect.width, rect.height);
			int width = Mathf.Max(64, Mathf.RoundToInt(MaximumImageEdge * (rect.width / longestSide)));
			int height = Mathf.Max(64, Mathf.RoundToInt(MaximumImageEdge * (rect.height / longestSide)));

			GameObject cameraObject = new GameObject("WorldMapBakeCamera");
			cameraObject.hideFlags = HideFlags.HideAndDontSave;

			RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
			RenderTexture previousActive = RenderTexture.active;

			/* Fog is disabled around the capture and restored afterwards. This is a global setting
			 * and mutating it is normally something to avoid — but here it is one editor-only
			 * frame, nothing else is rendering, and scene fog on an overhead shot from two
			 * kilometres up washes the entire map to a flat colour. */
			bool previousFog = RenderSettings.fog;

			try
			{
				RenderSettings.fog = false;

				Camera camera = cameraObject.AddComponent<Camera>();
				camera.transform.position = new Vector3(rect.center.x, CaptureHeight, rect.center.y);
				camera.transform.rotation = Quaternion.Euler(90.0f, definition.NorthOffsetDegrees, 0.0f);
				camera.orthographic = true;
				camera.orthographicSize = Mathf.Max(rect.width, rect.height) * 0.5f;
				camera.aspect = rect.width / rect.height;
				camera.nearClipPlane = 0.3f;
				camera.farClipPlane = CaptureHeight + CaptureDepth;
				camera.clearFlags = CameraClearFlags.SolidColor;
				camera.backgroundColor = definition.MapBackground;
				camera.cullingMask = BuildCaptureMask();
				camera.enabled = false;
				camera.targetTexture = target;

				camera.Render();

				RenderTexture.active = target;
				Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
				image.ReadPixels(new Rect(0.0f, 0.0f, width, height), 0, 0);
				image.Apply();

				byte[] png = image.EncodeToPNG();
				Object.DestroyImmediate(image);

				string imagePath = $"{OutputDirectory}/{SanitizeFileName(definition.SceneName)}Map.png";
				File.WriteAllBytes(imagePath, png);
				AssetDatabase.ImportAsset(imagePath, ImportAssetOptions.ForceUpdate);

				ConfigureImporter(imagePath);
				string guid = AssetDatabase.AssetPathToGUID(imagePath);
				MakeAddressable(imagePath, guid, definition.SceneName);

				definition.MapImage = new AssetReferenceTexture2D(guid);
			}
			finally
			{
				RenderSettings.fog = previousFog;
				RenderTexture.active = previousActive;

				target.Release();
				Object.DestroyImmediate(target);
				Object.DestroyImmediate(cameraObject);
			}
		}

		/// <summary>
		/// Sets the import settings a map image needs.
		/// </summary>
		/// <param name="imagePath">Asset path of the image.</param>
		private static void ConfigureImporter(string imagePath)
		{
			TextureImporter importer = AssetImporter.GetAtPath(imagePath) as TextureImporter;
			if (importer == null)
			{
				return;
			}

			importer.textureType = TextureImporterType.Default;
			importer.wrapMode = TextureWrapMode.Clamp;
			importer.filterMode = FilterMode.Bilinear;

			/* Mip maps on, unlike most UI art. The world map is drawn at every scale between a
			 * whole zone in 600 points and sixty metres in the same 600 points, so the texture
			 * spends most of its life minified — without mips that reads as a shimmering mess
			 * whenever the player pans. */
			importer.mipmapEnabled = true;
			importer.maxTextureSize = MaximumImageEdge;
			importer.textureCompression = TextureImporterCompression.Compressed;

			/* Not readable. Nothing samples this on the CPU, and a readable texture keeps a second
			 * copy of every pixel in system memory for the life of the client. */
			importer.isReadable = false;

			importer.SaveAndReimport();
		}

		/// <summary>
		/// Places a baked image in the addressable group the client loads it from.
		/// </summary>
		/// <param name="imagePath">Asset path of the image.</param>
		/// <param name="guid">The image's asset GUID.</param>
		/// <param name="sceneName">The scene the image belongs to, used as its address.</param>
		/// <remarks>
		/// Addressable rather than a hard reference on the definition, because
		/// <c>WorldSceneDetails</c> holds that definition and is read by the scene server. A hard
		/// reference would pull every zone's map art into a dedicated server build that never
		/// draws a frame.
		/// </remarks>
		private static void MakeAddressable(string imagePath, string guid, string sceneName)
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null)
			{
				Debug.LogWarning($"[WorldMapBaker] Addressables is not initialised in this project, so '{imagePath}' could not be made addressable and the client will not be able to load it. Open Window > Asset Management > Addressables > Groups once to create the settings.");
				return;
			}

			AddressableAssetGroup group = settings.FindGroup(AddressableGroupName);
			if (group == null)
			{
				group = settings.CreateGroup(AddressableGroupName, false, false, false, null, settings.DefaultGroup.Schemas.ConvertAll(schema => schema.GetType()).ToArray());
			}

			AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group);
			if (entry != null)
			{
				entry.address = $"WorldMaps/{sceneName}";
			}
		}

		/// <summary>
		/// Works out which layers the capture camera photographs.
		/// </summary>
		/// <returns>The culling mask.</returns>
		private static int BuildCaptureMask()
		{
			int mask = 0;
			for (int i = 0; i < CaptureLayerNames.Length; ++i)
			{
				int layer = LayerMask.NameToLayer(CaptureLayerNames[i]);
				if (layer >= 0)
				{
					mask |= 1 << layer;
				}
			}
			return mask;
		}

		/// <summary>
		/// The first component of a type in a scene.
		/// </summary>
		/// <typeparam name="T">The component type.</typeparam>
		/// <param name="scene">The scene to search.</param>
		/// <returns>The component, or null.</returns>
		/// <remarks>
		/// Scoped to one scene rather than using <c>FindFirstObjectByType</c>, because the bake
		/// opens scenes additively: a global search would find the previous scene's settings, or
		/// the editor's own, and write one scene's map into another scene's definition.
		/// </remarks>
		private static T FindInScene<T>(Scene scene) where T : Component
		{
			List<T> found = FindAllInScene<T>(scene);
			return found.Count > 0 ? found[0] : null;
		}

		/// <summary>
		/// Every component of a type in a scene.
		/// </summary>
		/// <typeparam name="T">The component type.</typeparam>
		/// <param name="scene">The scene to search.</param>
		/// <returns>The components found, in no particular order.</returns>
		private static List<T> FindAllInScene<T>(Scene scene) where T : Component
		{
			List<T> results = new List<T>();

			GameObject[] roots = scene.GetRootGameObjects();
			for (int i = 0; i < roots.Length; ++i)
			{
				results.AddRange(roots[i].GetComponentsInChildren<T>(true));
			}

			return results;
		}

		/// <summary>
		/// Makes a scene name usable as a file name.
		/// </summary>
		/// <param name="value">The scene name.</param>
		/// <returns>The sanitised name.</returns>
		private static string SanitizeFileName(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return "Unknown";
			}

			char[] characters = value.ToCharArray();
			char[] invalid = Path.GetInvalidFileNameChars();

			for (int i = 0; i < characters.Length; ++i)
			{
				if (System.Array.IndexOf(invalid, characters[i]) >= 0 || characters[i] == ' ')
				{
					characters[i] = '_';
				}
			}

			return new string(characters);
		}
	}
}
