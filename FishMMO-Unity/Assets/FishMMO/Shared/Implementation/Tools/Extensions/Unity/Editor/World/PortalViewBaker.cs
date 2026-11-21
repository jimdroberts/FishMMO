using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using FishMMO.Shared.WorldMaps;
using Object = UnityEngine.Object;

namespace FishMMO.Shared
{
	/// <summary>
	/// Bakes what each scene teleporter shows through its opening (<see cref="PortalSurface"/>): one cubemap per
	/// <see cref="TeleporterDestination"/>, photographed from a standing eye at the arrival, and puts the portal's surface in
	/// every <see cref="SceneTeleporter"/>'s opening.
	/// </summary>
	/// <remarks>
	/// <para><b>Kept like the world maps.</b> The views are a per-machine cache in <see cref="Folder"/> (Assets/Generated,
	/// gitignored), named by destination id. The cut bakes the views of the scene it cuts; a client build bakes any missing or
	/// older than their scene (<see cref="BakeOutOfDate"/>), registers them in the <see cref="AddressableGroupName"/> group
	/// for that build (<see cref="PortalSurface.AddressOf"/>), and removes the group afterwards (<see cref="RemoveBuildGroup"/>).
	/// Nothing in a scene references a view: the surface loads it by address, so a server build carries none.</para>
	/// <para><b>Lit as the map is.</b> The capture runs under <see cref="WorldMapBaker.StageCapture"/>: noon on the equinox,
	/// weather cleared, terrain arrays bound. The surface dims the view with the local sky at runtime.</para>
	/// <para><b>Faces.</b> Each face is rendered by a 90° camera turned to the cube map convention (the side faces upright,
	/// +Y with -Z up, -Y with +Z up) and stored top row first, which is what <see cref="Cubemap.SetPixels(Color[], CubemapFace)"/>
	/// expects; a render reads back bottom row first, so every face is flipped on the way in.</para>
	/// </remarks>
	public static class PortalViewBaker
	{
		/// <summary>Where the baked views live (gitignored, per machine).</summary>
		public const string Folder = "Assets/Generated/PortalViews";

		/// <summary>The build-only Addressables group the views ship in.</summary>
		public const string AddressableGroupName = "ClientPortalViews";

		/// <summary>The portal surface's material (committed; the shader is FishMMO/Portal).</summary>
		public const string MaterialPath = "Assets/Prefabs/Shared/Portal/PortalSurface.mat";

		/// <summary>The surface's object under its teleporter.</summary>
		public const string SurfaceName = "Portal Surface";

		/// <summary>Edge of one cube face in pixels. The opening is a few metres wide and the swirl softens it.</summary>
		public const int FaceSize = 256;

		private const string ShaderName = "FishMMO/Portal";
		private const float EyeHeight = 1.7f;
		private const float FarClip = 2500f;

		// The opening when the teleporter has no box to size it by, and the bounds a box's size is held to (a teleporter volume
		// can be a whole zone edge; its portal is still a doorway).
		private static readonly Vector2 DefaultOpening = new Vector2(2.2f, 3.2f);
		private static readonly Vector2 MinimumOpening = new Vector2(1.2f, 2.0f);
		private static readonly Vector2 MaximumOpening = new Vector2(4.0f, 5.0f);
		private const float WidthShare = 0.9f;
		private const float HeightShare = 0.94f;

		/// <summary>The view file of a destination.</summary>
		public static string ViewPath(string destinationId) => $"{Folder}/{destinationId}.asset";

		/// <summary>Dashboard: puts portal surfaces in the open scene and bakes its destinations' views.</summary>
		[DashboardTool(DashboardToolAttribute.Maintenance, "Bake portal views (open scene)", Section = "World", Order = 22,
			Tooltip = "Puts a portal surface in every scene teleporter of the open world scene and bakes the view of every teleporter destination in it (Assets/Generated/PortalViews, not committed). Saves the scene.")]
		public static void ProcessOpenScene()
		{
			var notes = new List<string>();
			ProcessScene(SceneManager.GetActiveScene(), notes);
			Debug.Log("[Portal views]\n  " + string.Join("\n  ", notes));
		}

		/// <summary>Dashboard: every world scene, whether or not its views are current.</summary>
		[DashboardTool(DashboardToolAttribute.Maintenance, "Bake all portal views", Section = "World", Order = 23,
			Tooltip = "Opens every world scene, puts a portal surface in each scene teleporter and bakes every destination's view (Assets/Generated/PortalViews, not committed).")]
		public static void BakeAll() => BakeScenes(onlyOutOfDate: false);

		/// <summary>Batch entry: <see cref="BakeAll"/>, exiting 0 on success.</summary>
		public static void BakeAllBatch()
		{
			bool ok;
			try
			{
				ok = BakeScenes(onlyOutOfDate: false) >= 0;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[Portal views] FAILED: {ex}");
				ok = false;
			}
			EditorApplication.Exit(ok ? 0 : 1);
		}

		/// <summary>
		/// The build's bake: every world scene's surfaces checked, the views missing or older than their scene baked, and every
		/// view of a known destination registered in the build group.
		/// </summary>
		public static void BakeOutOfDate()
		{
			try
			{
				BakeScenes(onlyOutOfDate: true);
			}
			finally
			{
				RegisterForBuild();
			}
		}

		/// <summary>
		/// Puts portal surfaces in an open, active world scene's teleporters (saving it when that changed anything) and bakes
		/// the view of every destination in it. The number of views baked.
		/// </summary>
		public static int ProcessScene(Scene scene, List<string> notes)
		{
			if (!scene.IsValid() || !scene.isLoaded)
			{
				return 0;
			}
			if (EnsureSurfaces(scene, notes) > 0 && !string.IsNullOrEmpty(scene.path))
			{
				EditorSceneManager.SaveScene(scene);
			}
			return BakeScene(scene, notes, onlyOutOfDate: false);
		}

		/// <summary>True when every destination the teleporter cache knows in this scene has a view no older than the scene file.</summary>
		public static bool IsCurrent(string scenePath)
		{
			string sceneName = Path.GetFileNameWithoutExtension(scenePath);
			DateTime sceneTime = File.GetLastWriteTimeUtc(scenePath);
			TeleporterCache cache = TeleporterCache.GetOrCreateCache();
			if (cache == null)
			{
				return true;
			}
			foreach (KeyValuePair<string, TeleporterCacheEntry> pair in cache.Destinations)
			{
				if (pair.Value == null || pair.Value.SceneName != sceneName)
				{
					continue;
				}
				if (!IsViewCurrent(pair.Key, sceneTime))
				{
					return false;
				}
			}
			return true;
		}

		private static bool IsViewCurrent(string destinationId, DateTime sceneTime)
		{
			string path = ViewPath(destinationId);
			return File.Exists(path) && File.GetLastWriteTimeUtc(path) >= sceneTime;
		}

		/// <summary>
		/// Opens each world scene in turn, puts in its surfaces and bakes its views (only the stale ones when asked), then puts
		/// the editor's scenes back. The number of views baked, or -1 when the bake did not run.
		/// </summary>
		private static int BakeScenes(bool onlyOutOfDate)
		{
			List<string> scenes = FishMMO.Shared.WorldDesign.WorldScenePaths.FindAll();
			if (scenes.Count == 0)
			{
				Debug.LogWarning("[Portal views] No world scenes found; nothing to bake.");
				return 0;
			}
			if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
			{
				Debug.Log("[Portal views] Bake cancelled: the open scenes have unsaved changes.");
				return -1;
			}

			SceneSetup[] setup = Array.FindAll(EditorSceneManager.GetSceneManagerSetup(), s => !string.IsNullOrEmpty(s.path));
			var notes = new List<string>();
			int baked = 0;
			try
			{
				for (int i = 0; i < scenes.Count; i++)
				{
					string path = scenes[i];
					EditorUtility.DisplayProgressBar("Baking portal views", Path.GetFileNameWithoutExtension(path), i / (float)scenes.Count);
					Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
					if (!scene.IsValid())
					{
						continue;
					}
					SceneManager.SetActiveScene(scene);
					if (EnsureSurfaces(scene, notes) > 0)
					{
						EditorSceneManager.SaveScene(scene);
					}
					baked += BakeScene(scene, notes, onlyOutOfDate);
				}
			}
			finally
			{
				EditorUtility.ClearProgressBar();
				AssetDatabase.SaveAssets();
				if (setup.Length > 0)
				{
					EditorSceneManager.RestoreSceneManagerSetup(setup);
				}
				else
				{
					EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
				}
				Debug.Log($"[Portal views] {baked} view(s) baked over {scenes.Count} world scene(s).\n  " + string.Join("\n  ", notes));
			}
			return baked;
		}

		/// <summary>
		/// Gives every scene teleporter in the scene a portal surface in its opening, aimed at its destination. The number of
		/// surfaces added or changed (the scene is marked dirty, not saved).
		/// </summary>
		public static int EnsureSurfaces(Scene scene, List<string> notes)
		{
			Material material = EnsureMaterial();
			Mesh quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
			TeleporterCache cache = TeleporterCache.GetOrCreateCache();
			int changed = 0;
			foreach (SceneTeleporter teleporter in FindAll<SceneTeleporter>(scene))
			{
				Transform surface = teleporter.transform.Find(SurfaceName);
				bool created = surface == null;
				if (created)
				{
					var surfaceObject = new GameObject(SurfaceName);
					surfaceObject.layer = teleporter.gameObject.layer;
					surface = surfaceObject.transform;
					surface.SetParent(teleporter.transform, false);
					surfaceObject.AddComponent<MeshFilter>();
					surfaceObject.AddComponent<MeshRenderer>();
					surfaceObject.AddComponent<PortalSurface>();
				}

				// The opening: the trigger box's face, held to a doorway's size, standing on the box's floor.
				Vector2 opening = DefaultOpening;
				float floor = 0f;
				float depth = 0f;
				BoxCollider box = teleporter.GetComponent<BoxCollider>();
				if (box != null)
				{
					opening = new Vector2(
						Mathf.Clamp(box.size.x * WidthShare, MinimumOpening.x, MaximumOpening.x),
						Mathf.Clamp(box.size.y * HeightShare, MinimumOpening.y, MaximumOpening.y));
					floor = box.center.y - box.size.y * 0.5f;
					depth = box.center.z;
				}
				Vector3 localPosition = new Vector3(box != null ? box.center.x : 0f, floor + opening.y * 0.5f, depth);
				Vector3 localScale = new Vector3(opening.x, opening.y, 1f);

				// TryGetComponent, not GetComponent ??: in the editor a missing component comes back as a fake null object.
				if (!surface.TryGetComponent(out MeshFilter filter))
				{
					filter = surface.gameObject.AddComponent<MeshFilter>();
				}
				if (!surface.TryGetComponent(out MeshRenderer renderer))
				{
					renderer = surface.gameObject.AddComponent<MeshRenderer>();
				}
				if (!surface.TryGetComponent(out PortalSurface portal))
				{
					portal = surface.gameObject.AddComponent<PortalSurface>();
				}

				// The arrival's facing: ahead through the portal is ahead on arrival. A teleporter with no destination yet looks
				// straight on through.
				float yaw = teleporter.transform.eulerAngles.y + 180f;
				if (cache != null && !string.IsNullOrEmpty(teleporter.DestinationID) &&
					cache.Destinations.TryGetValue(teleporter.DestinationID, out TeleporterCacheEntry entry) && entry != null)
				{
					yaw = entry.Rotation.eulerAngles.y;
				}

				bool differs = created
					|| surface.localPosition != localPosition
					|| surface.localRotation != Quaternion.identity
					|| surface.localScale != localScale
					|| filter.sharedMesh != quad
					|| renderer.sharedMaterial != material
					|| renderer.shadowCastingMode != ShadowCastingMode.Off
					|| renderer.receiveShadows
					|| renderer.lightProbeUsage != LightProbeUsage.Off
					|| renderer.reflectionProbeUsage != ReflectionProbeUsage.Off
					|| !string.IsNullOrEmpty(portal.DestinationID)
					|| !Mathf.Approximately(portal.DestinationYaw, yaw);
				if (!differs)
				{
					continue;
				}

				surface.localPosition = localPosition;
				surface.localRotation = Quaternion.identity;
				surface.localScale = localScale;
				filter.sharedMesh = quad;
				renderer.sharedMaterial = material;
				renderer.shadowCastingMode = ShadowCastingMode.Off;
				renderer.receiveShadows = false;
				renderer.lightProbeUsage = LightProbeUsage.Off;
				renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
				// Empty: the surface takes its teleporter's destination, so re-aiming the teleporter re-aims the view.
				portal.DestinationID = string.Empty;
				portal.DestinationYaw = yaw;
				EditorUtility.SetDirty(surface.gameObject);
				EditorUtility.SetDirty(filter);
				EditorUtility.SetDirty(renderer);
				EditorUtility.SetDirty(portal);
				changed++;
				notes?.Add($"{scene.name}: portal surface {(created ? "added to" : "updated on")} '{teleporter.name}' ({opening.x:0.0} × {opening.y:0.0} m, arrival facing {yaw:0}°).");
			}
			if (changed > 0)
			{
				EditorSceneManager.MarkSceneDirty(scene);
			}
			return changed;
		}

		/// <summary>Bakes the view of every destination in an open, active scene (only those older than the scene when asked).</summary>
		public static int BakeScene(Scene scene, List<string> notes, bool onlyOutOfDate)
		{
			List<TeleporterDestination> destinations = FindAll<TeleporterDestination>(scene);
			if (destinations.Count == 0)
			{
				return 0;
			}
			DateTime sceneTime = !string.IsNullOrEmpty(scene.path) && File.Exists(scene.path) ? File.GetLastWriteTimeUtc(scene.path) : DateTime.MinValue;
			EnsureFolder(Folder);
			int baked = 0;
			foreach (TeleporterDestination destination in destinations)
			{
				string id = destination.DestinationID;
				if (string.IsNullOrEmpty(id))
				{
					notes?.Add($"{scene.name}: destination '{destination.name}' has no id; no view baked.");
					continue;
				}
				if (onlyOutOfDate && IsViewCurrent(id, sceneTime))
				{
					continue;
				}
				Transform at = destination.transform;
				Cubemap view = Render(scene, at.position + Vector3.up * EyeHeight);
				string path = ViewPath(id);
				if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
				{
					AssetDatabase.DeleteAsset(path);
				}
				AssetDatabase.CreateAsset(view, path);
				baked++;
				notes?.Add($"{scene.name}: view of '{destination.name}' baked ({id}).");
			}
			AssetDatabase.SaveAssets();
			return baked;
		}

		/// <summary>Photographs the six faces around an eye, under the map capture's staging, into a compressed cubemap.</summary>
		private static Cubemap Render(Scene scene, Vector3 eye)
		{
			var cameraObject = new GameObject("PortalViewBakeCamera") { hideFlags = HideFlags.HideAndDontSave };
			var target = new RenderTexture(FaceSize, FaceSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
			var face = new Texture2D(FaceSize, FaceSize, TextureFormat.RGBA32, false, false);
			RenderTexture previousActive = RenderTexture.active;
			// sRGB, said explicitly: the faces are read back sRGB-encoded, and a cubemap made from a TextureFormat alone is linear,
			// which drew the view gamma-bright (encoded twice).
			var cube = new Cubemap(FaceSize, GraphicsFormat.R8G8B8A8_SRGB, TextureCreationFlags.MipChain);
			try
			{
				Camera camera = cameraObject.AddComponent<Camera>();
				camera.enabled = false;
				camera.fieldOfView = 90f;
				camera.aspect = 1f;
				camera.nearClipPlane = 0.1f;
				camera.farClipPlane = FarClip;
				camera.allowHDR = false;
				camera.allowMSAA = false;
				int ui = LayerMask.NameToLayer("UI");
				camera.cullingMask = ui >= 0 ? ~(1 << ui) : ~0;
				camera.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
				camera.backgroundColor = new Color(0.55f, 0.68f, 0.86f, 1f);
				camera.targetTexture = target;
				camera.transform.position = eye;

				using (WorldMapBaker.StageCapture(scene, camera))
				{
					for (int f = 0; f < 6; f++)
					{
						var which = (CubemapFace)f;
						camera.transform.SetPositionAndRotation(eye, FaceRotation(which));
						camera.Render();
						RenderTexture.active = target;
						face.ReadPixels(new Rect(0, 0, FaceSize, FaceSize), 0, 0);
						face.Apply(false);
						cube.SetPixels(FlipRows(face.GetPixels()), which);
					}
				}
				cube.Apply(true, false);
				EditorUtility.CompressCubemapTexture(cube, TextureFormat.DXT1, TextureCompressionQuality.Normal);
				cube.wrapMode = TextureWrapMode.Clamp;
				cube.filterMode = FilterMode.Trilinear;
				return cube;
			}
			catch
			{
				Object.DestroyImmediate(cube);
				throw;
			}
			finally
			{
				RenderTexture.active = previousActive;
				Object.DestroyImmediate(cameraObject);
				Object.DestroyImmediate(target);
				Object.DestroyImmediate(face);
			}
		}

		/// <summary>The camera turn that renders a cube face upright in the cube map convention.</summary>
		public static Quaternion FaceRotation(CubemapFace face)
		{
			switch (face)
			{
				case CubemapFace.PositiveX: return Quaternion.LookRotation(Vector3.right, Vector3.up);
				case CubemapFace.NegativeX: return Quaternion.LookRotation(Vector3.left, Vector3.up);
				case CubemapFace.PositiveY: return Quaternion.LookRotation(Vector3.up, Vector3.back);
				case CubemapFace.NegativeY: return Quaternion.LookRotation(Vector3.down, Vector3.forward);
				case CubemapFace.PositiveZ: return Quaternion.LookRotation(Vector3.forward, Vector3.up);
				default: return Quaternion.LookRotation(Vector3.back, Vector3.up);
			}
		}

		private static Color[] FlipRows(Color[] pixels)
		{
			var flipped = new Color[pixels.Length];
			for (int y = 0; y < FaceSize; y++)
			{
				Array.Copy(pixels, y * FaceSize, flipped, (FaceSize - 1 - y) * FaceSize, FaceSize);
			}
			return flipped;
		}

		/// <summary>Registers every view of a destination the teleporter cache knows in the build group.</summary>
		public static void RegisterForBuild()
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null || !AssetDatabase.IsValidFolder(Folder))
			{
				return;
			}
			TeleporterCache cache = TeleporterCache.GetOrCreateCache();
			AddressableAssetGroup group = null;
			int registered = 0;
			foreach (string guid in AssetDatabase.FindAssets("t:Cubemap", new[] { Folder }))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				string id = Path.GetFileNameWithoutExtension(path);
				if (cache == null || !cache.Destinations.ContainsKey(id))
				{
					continue;
				}
				group ??= settings.FindGroup(AddressableGroupName)
					?? settings.CreateGroup(AddressableGroupName, false, false, false, null, settings.DefaultGroup.Schemas.ConvertAll(schema => schema.GetType()).ToArray());
				AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group);
				if (entry != null)
				{
					entry.address = PortalSurface.AddressOf(id);
					registered++;
				}
			}
			if (registered > 0)
			{
				AssetDatabase.SaveAssets();
			}
			Debug.Log($"[Portal views] {registered} view(s) in the '{AddressableGroupName}' group for this build.");
		}

		/// <summary>Removes the build group (the baked views stay for the next build).</summary>
		public static void RemoveBuildGroup()
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			AddressableAssetGroup group = settings != null ? settings.FindGroup(AddressableGroupName) : null;
			if (group != null)
			{
				settings.RemoveGroup(group);
				AssetDatabase.SaveAssets();
			}
		}

		/// <summary>The portal material, created beside nothing else the first time.</summary>
		public static Material EnsureMaterial()
		{
			Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
			if (material != null)
			{
				return material;
			}
			Shader shader = Shader.Find(ShaderName);
			if (shader == null)
			{
				throw new InvalidOperationException($"The '{ShaderName}' shader is missing; portal surfaces need it.");
			}
			EnsureFolder(Path.GetDirectoryName(MaterialPath).Replace('\\', '/'));
			material = new Material(shader) { name = "PortalSurface" };
			AssetDatabase.CreateAsset(material, MaterialPath);
			AssetDatabase.SaveAssets();
			return material;
		}

		private static void EnsureFolder(string folder)
		{
			string[] parts = folder.Split('/');
			string current = parts[0];
			for (int i = 1; i < parts.Length; ++i)
			{
				string next = $"{current}/{parts[i]}";
				if (!AssetDatabase.IsValidFolder(next))
				{
					AssetDatabase.CreateFolder(current, parts[i]);
				}
				current = next;
			}
		}

		private static List<T> FindAll<T>(Scene scene) where T : Component
		{
			var found = new List<T>();
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				found.AddRange(root.GetComponentsInChildren<T>(true));
			}
			return found;
		}
	}
}
