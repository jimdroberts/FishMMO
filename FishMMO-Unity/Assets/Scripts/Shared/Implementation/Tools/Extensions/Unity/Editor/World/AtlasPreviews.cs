#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>An image of a scene and the world rectangle it covers.</summary>
	public readonly struct AtlasImage
	{
		public readonly Texture2D Texture;
		/// <summary>The world XZ rectangle (metres) the image covers.</summary>
		public readonly Rect WorldRect;
		/// <summary>Clockwise turn of the image's up from world +Z, in degrees.</summary>
		public readonly float NorthDegrees;
		/// <summary>True for the scene's baked world map, false for the designer's own preview.</summary>
		public readonly bool Baked;

		public AtlasImage(Texture2D texture, Rect worldRect, float northDegrees, bool baked)
		{
			Texture = texture;
			WorldRect = worldRect;
			NorthDegrees = northDegrees;
			Baked = baked;
		}

		public bool IsValid => Texture != null && WorldRect.width > 0f && WorldRect.height > 0f;

		/// <summary>
		/// Texture coordinates of a world XZ point: the inverse of the bake camera, which looks
		/// straight down turned by <see cref="NorthDegrees"/> and frames <see cref="WorldRect"/>.
		/// </summary>
		public Vector2 UV(Vector2 world)
		{
			float a = NorthDegrees * Mathf.Deg2Rad;
			var right = new Vector2(Mathf.Cos(a), -Mathf.Sin(a));
			var up = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
			Vector2 d = world - WorldRect.center;
			return new Vector2(Vector2.Dot(d, right) / WorldRect.width + 0.5f, Vector2.Dot(d, up) / WorldRect.height + 0.5f);
		}
	}

	/// <summary>
	/// Scene images for the globe: the scene's baked world map when one exists, otherwise the
	/// designer's own small top-down preview kept in <c>Library/</c>.
	/// </summary>
	/// <remarks>
	/// Real map bakes are build output (FishMMO Dashboard → World Map → Bake Maps, and every client
	/// build, which removes them again), so the designer also renders its own 256-pixel previews.
	/// Those are never committed and never shipped. An image's top is the scene's +Z; the globe
	/// turns it by the scene's heading.
	/// </remarks>
	public static class AtlasPreviews
	{
		public const string Folder = "Library/FishMMO/AtlasPreviews";
		private const int Edge = 256;
		private const float CaptureHeight = 2000f;
		private static readonly string[] CaptureLayerNames = { "Default", "Ground", "Water" };

		private static readonly Dictionary<string, (DateTime stamp, Texture2D texture)> loaded = new Dictionary<string, (DateTime, Texture2D)>(StringComparer.Ordinal);

		public static string PathOf(string sceneName) => $"{Folder}/{WorldEditorAssets.Sanitize(sceneName)}.png";

		/// <summary>Where a preview's frame is kept: the world rectangle the image was rendered over.</summary>
		public static string FramePathOf(string sceneName) => $"{Folder}/{WorldEditorAssets.Sanitize(sceneName)}.frame";

		/// <summary>
		/// The best image of a scene: its baked world map (the hand-assigned definition first,
		/// then the bake's own), else the designer's preview, else an invalid image — except that a
		/// preview rendered after the map was baked wins, since it shows the ground as it is now.
		/// </summary>
		/// <remarks>
		/// Maps are build output and are not re-baked when a scene is re-cut, so without the newer
		/// preview winning, a re-cut scene showed its old ground on the globe until the next bake.
		/// </remarks>
		public static AtlasImage Find(string sceneName, WorldSceneDetailsCache details)
		{
			WorldSceneDetails sceneDetails = null;
			details?.Scenes?.TryGetValue(sceneName, out sceneDetails);
			Rect boundaries = sceneDetails != null ? MapBoundsResolver.FromSceneBoundaries(sceneDetails) : Rect.zero;

			WorldMapDefinition definition = sceneDetails != null && sceneDetails.MapDefinition != null
				? sceneDetails.MapDefinition
				: AssetDatabase.LoadAssetAtPath<WorldMapDefinition>(WorldMapDefinition.BakedAssetPath(sceneName));
			if (definition != null)
			{
				Texture2D map = definition.MapImage != null ? definition.MapImage.editorAsset as Texture2D : null;
				if (map == null)
				{
					map = AssetDatabase.LoadAssetAtPath<Texture2D>(WorldMapDefinition.BakedImagePath(sceneName));
				}
				Rect rect = definition.HasBounds ? definition.MapRect : boundaries;
				var baked = new AtlasImage(map, rect, definition.NorthOffsetDegrees, true);
				if (baked.IsValid && !PreviewIsNewer(sceneName, map))
				{
					return baked;
				}
			}
			// The rectangle the preview was rendered over, which the cache may not know (a scene not
			// yet in it) or know wrongly (a scene re-cut to a new size); the cache's for older previews.
			Rect frame = ReadFrame(sceneName, out Rect saved) ? saved : boundaries;
			return new AtlasImage(Get(sceneName), frame, 0f, false);
		}

		private static bool ReadFrame(string sceneName, out Rect frame)
		{
			frame = Rect.zero;
			string path = FramePathOf(sceneName);
			if (!File.Exists(path))
			{
				return false;
			}
			string[] parts = File.ReadAllText(path).Split(' ');
			if (parts.Length != 4
				|| !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
				|| !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)
				|| !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float width)
				|| !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float depth)
				|| width <= 0f || depth <= 0f)
			{
				return false;
			}
			frame = new Rect(x, z, width, depth);
			return true;
		}

		private static void WriteFrame(string sceneName, Rect frame)
		{
			File.WriteAllText(FramePathOf(sceneName), string.Format(CultureInfo.InvariantCulture, "{0:R} {1:R} {2:R} {3:R}",
				frame.x, frame.y, frame.width, frame.height));
		}

		/// <summary>True when the scene's preview was rendered after <paramref name="map"/>'s file was written.</summary>
		private static bool PreviewIsNewer(string sceneName, Texture2D map)
		{
			string preview = PathOf(sceneName);
			string mapPath = map != null ? AssetDatabase.GetAssetPath(map) : null;
			if (!File.Exists(preview) || string.IsNullOrEmpty(mapPath) || !File.Exists(mapPath))
			{
				return false;
			}
			return File.GetLastWriteTimeUtc(preview) > File.GetLastWriteTimeUtc(mapPath);
		}

		/// <summary>The designer's preview of a scene, or null when none has been rendered.</summary>
		public static Texture2D Get(string sceneName)
		{
			string path = PathOf(sceneName);
			if (!File.Exists(path))
			{
				return null;
			}
			DateTime stamp = File.GetLastWriteTimeUtc(path);
			if (loaded.TryGetValue(sceneName, out var cached) && cached.stamp == stamp && cached.texture != null)
			{
				return cached.texture;
			}
			var texture = new Texture2D(2, 2, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
			if (!texture.LoadImage(File.ReadAllBytes(path)))
			{
				UnityEngine.Object.DestroyImmediate(texture);
				return null;
			}
			if (cached.texture != null)
			{
				UnityEngine.Object.DestroyImmediate(cached.texture);
			}
			loaded[sceneName] = (stamp, texture);
			return texture;
		}

		/// <summary>
		/// Renders previews for scenes, opening each additively and closing it again. Scenes that
		/// are already open are rendered as they are and left open. Returns how many were written.
		/// </summary>
		public static int Render(IReadOnlyList<string> scenePaths)
		{
			Directory.CreateDirectory(Folder);
			int written = 0;
			try
			{
				for (int i = 0; i < scenePaths.Count; i++)
				{
					string path = scenePaths[i];
					string sceneName = Path.GetFileNameWithoutExtension(path);
					if (EditorUtility.DisplayCancelableProgressBar("World Atlas previews", sceneName, i / (float)Math.Max(1, scenePaths.Count)))
					{
						break;
					}
					if (RenderOne(path, sceneName))
					{
						written++;
					}
				}
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}
			return written;
		}

		private static bool RenderOne(string scenePath, string sceneName)
		{
			Scene scene = SceneManager.GetSceneByPath(scenePath);
			bool openedHere = false;
			if (!scene.IsValid() || !scene.isLoaded)
			{
				scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
				openedHere = true;
			}

			/* Framed from the scene itself, now that it is open: its boundaries and terrain as they
			 * are. Asking the details cache refused every scene not yet in it — a freshly generated
			 * one — and would frame a re-cut that changed size by its old size. */
			Rect rect = MapBoundsResolver.FromOpenScene(scene);
			if (rect.width <= 0f || rect.height <= 0f)
			{
				Debug.LogWarning($"[World Atlas] {sceneName} has no boundaries or terrain; nothing to frame.");
				if (openedHere)
				{
					EditorSceneManager.CloseScene(scene, true);
				}
				return false;
			}

			float longest = Mathf.Max(rect.width, rect.height);
			int width = Mathf.Max(32, Mathf.RoundToInt(Edge * rect.width / longest));
			int height = Mathf.Max(32, Mathf.RoundToInt(Edge * rect.height / longest));
			var cameraObject = new GameObject("WorldAtlasPreviewCamera") { hideFlags = HideFlags.HideAndDontSave };
			var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
			RenderTexture previous = RenderTexture.active;
			bool previousFog = RenderSettings.fog;
			try
			{
				RenderSettings.fog = false;
				Camera camera = cameraObject.AddComponent<Camera>();
				camera.transform.position = new Vector3(rect.center.x, CaptureHeight, rect.center.y);
				camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
				camera.orthographic = true;
				camera.orthographicSize = rect.height * 0.5f;
				camera.aspect = rect.width / rect.height;
				camera.nearClipPlane = 0.3f;
				camera.farClipPlane = CaptureHeight * 3f;
				camera.clearFlags = CameraClearFlags.SolidColor;
				camera.backgroundColor = new Color(0.12f, 0.14f, 0.16f, 1f);
				camera.cullingMask = CaptureMask();
				camera.scene = scene;
				camera.enabled = false;
				camera.targetTexture = target;
				camera.Render();

				RenderTexture.active = target;
				var image = new Texture2D(width, height, TextureFormat.RGB24, false);
				image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
				image.Apply();
				File.WriteAllBytes(PathOf(sceneName), image.EncodeToPNG());
				WriteFrame(sceneName, rect);
				UnityEngine.Object.DestroyImmediate(image);
				return true;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[World Atlas] Preview of {sceneName} failed: {ex.Message}");
				return false;
			}
			finally
			{
				RenderSettings.fog = previousFog;
				RenderTexture.active = previous;
				target.Release();
				UnityEngine.Object.DestroyImmediate(target);
				UnityEngine.Object.DestroyImmediate(cameraObject);
				if (openedHere)
				{
					EditorSceneManager.CloseScene(scene, true);
				}
			}
		}

		private static int CaptureMask()
		{
			int mask = 0;
			foreach (string name in CaptureLayerNames)
			{
				int layer = LayerMask.NameToLayer(name);
				if (layer >= 0)
				{
					mask |= 1 << layer;
				}
			}
			return mask == 0 ? ~0 : mask;
		}
	}
}
#endif
