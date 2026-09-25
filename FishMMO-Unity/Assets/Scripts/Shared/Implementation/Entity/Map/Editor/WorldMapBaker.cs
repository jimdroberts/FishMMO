using System;
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
using Object = UnityEngine.Object;

namespace FishMMO.Shared.WorldMaps
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
	/// <para><b>Everything it writes is build output, not source.</b> A client build bakes a
	/// definition and an image per world scene into <see cref="WorldMapDefinition.BakedDirectory"/>,
	/// places the images in a client addressable group, rebuilds the world scene details cache so
	/// that it references the fresh definitions, builds, and then removes the bake again and rebuilds
	/// the cache once more (<see cref="CleanBakedMaps"/>). Nothing is hand-edited, nothing is committed,
	/// no scene is written: the loading image and every label, landmark and boundary are read from
	/// the scene, and the definition's presentation fields take their defaults. A scene that must
	/// share one map with another (an instanced twin) may still point <c>WorldSceneSettings.MapDefinition</c>
	/// at a hand-made definition, which the bake then fills in place instead of creating one.</para>
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
		private const string OutputDirectory = WorldMapDefinition.BakedDirectory;

		/// <summary>Addressable group the baked images are placed in.</summary>
		/// <remarks>
		/// The name carries "Client" because the build tool excludes groups by that substring from
		/// server bundles; the group is created by a client bake and removed after the build, but if
		/// one ever lingers it still stays out of a server build.
		/// </remarks>
		public const string AddressableGroupName = "ClientWorldMaps";

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

		/// <summary>True while a bake is running. Tools that touch the bake must wait for it.</summary>
		public static bool IsBusy => job != null;

		/// <summary>Raised when a bake starts or finishes.</summary>
		public static event Action BusyChanged;

		private const string ProgressTitle = "Baking world maps";

		/// <summary>One bake in progress: the scenes left, and the editor state to put back.</summary>
		private sealed class BakeJob
		{
			public readonly List<string> Scenes = new List<string>();
			public int Index;
			public int Baked;
			public SceneSetup[] Setup;
			public Scene Holder;
			public Scene ActiveUntitled;
			public Action<string> Done;
		}

		private static BakeJob job;

		/// <summary>
		/// Bakes a map for every world scene, start to finish, before returning. Used by the build.
		/// </summary>
		public static void BakeAll()
		{
			BakeJob bake = Begin();
			if (bake == null)
			{
				return;
			}
			bool cancelled = false;
			try
			{
				while (bake.Index < bake.Scenes.Count)
				{
					EditorUtility.DisplayProgressBar(ProgressTitle, ProgressText(bake), Progress(bake));
					Step(bake);
				}
			}
			catch
			{
				cancelled = true;
				throw;
			}
			finally
			{
				Finish(bake, cancelled ? "failed" : null);
			}
		}

		/// <summary>
		/// Starts a bake that works through the scenes one editor update at a time, waiting for each
		/// scene's capture and import to finish before opening the next, with a cancellable
		/// progress bar. <paramref name="done"/> receives a one-line result. Returns false when
		/// nothing was started (a bake is already running, or the user kept unsaved changes).
		/// </summary>
		public static bool StartBake(Action<string> done = null)
		{
			if (job != null)
			{
				done?.Invoke("A bake is already running.");
				return false;
			}
			BakeJob bake = Begin();
			if (bake == null)
			{
				done?.Invoke("Bake not started.");
				return false;
			}
			bake.Done = done;
			EditorApplication.update += Tick;
			EditorApplication.playModeStateChanged += OnPlayMode;
			return true;
		}

		private static void Tick()
		{
			BakeJob bake = job;
			if (bake == null)
			{
				EditorApplication.update -= Tick;
				return;
			}
			// The previous scene's import (and anything it triggered) must be done first.
			if (EditorApplication.isUpdating || EditorApplication.isCompiling)
			{
				return;
			}
			if (bake.Index >= bake.Scenes.Count)
			{
				Finish(bake, null);
				return;
			}
			if (EditorUtility.DisplayCancelableProgressBar(ProgressTitle, ProgressText(bake), Progress(bake)))
			{
				Finish(bake, "cancelled");
				return;
			}
			try
			{
				Step(bake);
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
				Finish(bake, $"failed on {Path.GetFileNameWithoutExtension(bake.Scenes[Math.Max(0, bake.Index - 1)])}: {ex.Message}");
			}
		}

		private static void OnPlayMode(PlayModeStateChange change)
		{
			if (change == PlayModeStateChange.ExitingEditMode && job != null)
			{
				Finish(job, "cancelled by entering play mode");
			}
		}

		private static float Progress(BakeJob bake) => bake.Scenes.Count == 0 ? 1f : bake.Index / (float)bake.Scenes.Count;

		private static string ProgressText(BakeJob bake) =>
			$"{Path.GetFileNameWithoutExtension(bake.Scenes[Math.Min(bake.Index, bake.Scenes.Count - 1)])} ({Math.Min(bake.Index + 1, bake.Scenes.Count)} of {bake.Scenes.Count})";

		/// <summary>
		/// Finds the scenes and clears the editor down to one empty scene. Each scene is then baked
		/// with nothing else loaded: the capture camera photographs every loaded scene and the
		/// bounds search finds every loaded boundary, so a scene left open in the editor used to
		/// appear in every other scene's map. Returns null when there is nothing to do.
		/// </summary>
		private static BakeJob Begin()
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
				return null;
			}

			if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
			{
				Debug.Log("[WorldMapBaker] Bake cancelled: the open scenes have unsaved changes.");
				return null;
			}

			Directory.CreateDirectory(OutputDirectory);

			var bake = new BakeJob();
			bake.Scenes.AddRange(scenes);
			bake.Scenes.Sort(StringComparer.Ordinal);
			// Saved scenes are closed and reopened afterwards; an untitled scene (a new scene, or the
			// test runner's) cannot be reopened, so it stays loaded.
			bake.Setup = Array.FindAll(EditorSceneManager.GetSceneManagerSetup(), s => !string.IsNullOrEmpty(s.path));
			Scene active = SceneManager.GetActiveScene();
			bake.ActiveUntitled = string.IsNullOrEmpty(active.path) ? active : default;
			bake.Holder = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
			SceneManager.SetActiveScene(bake.Holder);
			for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
			{
				Scene open = SceneManager.GetSceneAt(i);
				if (open != bake.Holder && !string.IsNullOrEmpty(open.path))
				{
					EditorSceneManager.CloseScene(open, true);
				}
			}

			job = bake;
			// No script reload may pull the job out from under itself halfway.
			EditorApplication.LockReloadAssemblies();
			BusyChanged?.Invoke();
			return bake;
		}

		/// <summary>Bakes the next scene: open it alone, make it active, capture, close.</summary>
		private static void Step(BakeJob bake)
		{
			string scenePath = bake.Scenes[bake.Index++];
			Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
			if (!scene.IsValid())
			{
				return;
			}
			try
			{
				// Its own lighting, skybox and fog settings, not the holder's.
				SceneManager.SetActiveScene(scene);
				if (BakeOpenScene(scene))
				{
					++bake.Baked;
				}
			}
			finally
			{
				// The bake only reads the scene; nothing it produces lives there.
				SceneManager.SetActiveScene(bake.Holder);
				EditorSceneManager.CloseScene(scene, true);
			}
		}

		/// <summary>Reopens the scenes the editor had, makes the same one active, and drops the holder.</summary>
		private static void RestoreScenes(BakeJob bake)
		{
			Scene active = bake.ActiveUntitled;
			foreach (SceneSetup entry in bake.Setup)
			{
				Scene reopened = EditorSceneManager.OpenScene(entry.path, entry.isLoaded ? OpenSceneMode.Additive : OpenSceneMode.AdditiveWithoutLoading);
				if (entry.isActive && entry.isLoaded)
				{
					active = reopened;
				}
			}
			if (!active.IsValid() || !active.isLoaded)
			{
				for (int i = 0; i < SceneManager.sceneCount; i++)
				{
					Scene candidate = SceneManager.GetSceneAt(i);
					if (candidate != bake.Holder && candidate.isLoaded)
					{
						active = candidate;
						break;
					}
				}
			}
			if (active.IsValid() && active.isLoaded)
			{
				SceneManager.SetActiveScene(active);
				if (bake.Holder.IsValid())
				{
					EditorSceneManager.CloseScene(bake.Holder, true);
				}
			}
		}

		/// <summary>Puts the editor's scenes back and reports. <paramref name="problem"/> is null on success.</summary>
		private static void Finish(BakeJob bake, string problem)
		{
			EditorApplication.update -= Tick;
			EditorApplication.playModeStateChanged -= OnPlayMode;
			EditorUtility.ClearProgressBar();
			try
			{
				if (!EditorApplication.isPlayingOrWillChangePlaymode)
				{
					RestoreScenes(bake);
				}
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
			}
			finally
			{
				if (job == bake)
				{
					job = null;
				}
				EditorApplication.UnlockReloadAssemblies();
				string result = problem == null
					? $"Baked {bake.Baked} of {bake.Scenes.Count} world scene maps into '{OutputDirectory}'."
					: $"Bake {problem} after {bake.Baked} of {bake.Scenes.Count} scene maps.";
				if (problem == null)
				{
					Debug.Log($"[WorldMapBaker] {result}");
				}
				else
				{
					Debug.LogWarning($"[WorldMapBaker] {result}");
				}
				BusyChanged?.Invoke();
				bake.Done?.Invoke(result);
			}
		}

		/// <summary>
		/// Removes everything the bake produced: the definitions, the images and their addressable
		/// group. The build tool calls this after a client build and then rebuilds the world scene
		/// details cache so it no longer references the removed definitions; it is also a FishMMO
		/// Dashboard button (World → World Map) for tidying up after a manual bake.
		/// </summary>
		public static void CleanBakedMaps()
		{
			if (job != null)
			{
				Debug.LogWarning("[WorldMapBaker] A bake is running; remove the baked maps after it finishes.");
				return;
			}
			bool removedFolder = AssetDatabase.IsValidFolder(OutputDirectory) && AssetDatabase.DeleteAsset(OutputDirectory);
			if (!removedFolder && Directory.Exists(OutputDirectory))
			{
				// Not known to the asset database (a bake without a refresh): remove it directly.
				Directory.Delete(OutputDirectory, true);
				File.Delete(OutputDirectory + ".meta");
				removedFolder = true;
			}

			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			AddressableAssetGroup group = settings != null ? settings.FindGroup(AddressableGroupName) : null;
			if (group != null)
			{
				// Deletes the group asset and its schema assets as well.
				settings.RemoveGroup(group);
			}

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();

			Debug.Log($"[WorldMapBaker] Removed {(removedFolder ? $"'{OutputDirectory}'" : "no bake folder")}{(group != null ? $" and the '{AddressableGroupName}' addressable group" : "")}.");
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

			/* A hand-assigned definition on the component is filled in place, so that two scenes can
			 * share one map. Otherwise the scene gets the transient definition the build produces. */
			WorldMapDefinition definition = settings.MapDefinition != null
				? settings.MapDefinition
				: LoadOrCreateBakedDefinition(scene.name);

			definition.SceneName = scene.name;

			if (string.IsNullOrWhiteSpace(definition.DisplayName))
			{
				definition.DisplayName = scene.name;
			}

			/* The loading image is authored on the component and copied here on every bake, so the
			 * client finds a scene's whole presentation in one place without anything being moved. */
			if (settings.SceneTransitionImage != null)
			{
				definition.SceneTransitionImage = settings.SceneTransitionImage;
			}

			HarvestAuthoredContent(scene, definition);

			if (!definition.HasAuthoredBounds)
			{
				Rect derived = MapBoundsResolver.FromOpenScene(scene);
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
		/// Loads the definition a previous bake wrote for a scene, or creates it.
		/// </summary>
		/// <param name="sceneName">The scene's name.</param>
		/// <returns>The asset, at <see cref="WorldMapDefinition.BakedAssetPath"/>.</returns>
		private static WorldMapDefinition LoadOrCreateBakedDefinition(string sceneName)
		{
			string path = WorldMapDefinition.BakedAssetPath(sceneName);
			WorldMapDefinition definition = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>(path);
			if (definition != null)
			{
				return definition;
			}

			definition = ScriptableObject.CreateInstance<WorldMapDefinition>();
			definition.SceneName = sceneName;
			definition.DisplayName = sceneName;
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
				// Half the HEIGHT: with the aspect below, the image then covers exactly the map
				// rectangle, which is what the world map and the atlas globe assume.
				camera.orthographicSize = rect.height * 0.5f;
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

				string imagePath = WorldMapDefinition.BakedImagePath(definition.SceneName);
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
		/// Scoped to one scene rather than using <c>FindAnyObjectByType</c>, because the bake
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

	}
}
