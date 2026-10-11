#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishMMO.Shared;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.WorldDesign;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// Photographs the in-game world map's markers for a generated scene's points of interest: the real
	/// <see cref="UITKMapView"/> with the map's style sheets, fed by the real pipeline (the scene's
	/// <see cref="ScenePointOfInterest"/>s harvested as the details cache does, then <see cref="MapContent.AppendPointsOfInterest"/>).
	/// The map image is the POI probe's overview (FISHMMO_POI_MAP_IMAGE), laid over the scene's ground rectangle. Stages:
	/// the whole scene, a closer view, and the whole scene under fog with only its middle explored (every point waits for
	/// discovery). PNGs go to FISHMMO_POI_MAP_OUT. Non-batch editor, exits through EditorApplication.Exit.
	/// </summary>
	public static class PointOfInterestMapRender
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string MapFolder = "Assets/Scripts/Client/GUI/World/Map/";
		private const int Width = 1600;
		private const int Height = 1200;
		private const int SettleFrames = 60;

		private enum Stage { Whole, Close, Fogged }

		private static readonly Queue<Stage> stages = new Queue<Stage>();
		private static Stage? current;
		private static int frames;
		private static string output;
		private static GameObject host;
		private static RenderTexture texture;
		private static PanelSettings settings;
		private static Texture2D image;
		private static Rect area;
		private static WorldSceneDetails details;
		private static int written;

		public static void Run()
		{
			try
			{
				output = Environment.GetEnvironmentVariable("FISHMMO_POI_MAP_OUT") ?? Path.Combine(Path.GetTempPath(), "fishmmo-poi-map");
				Directory.CreateDirectory(output);
				WorldAtlasScene entry = WorldEditorAssets.FindAll<WorldAtlasScene>().FirstOrDefault(e => e != null && e.SceneName == PointOfInterestRenderProbe.ProbeName);
				if (entry == null || entry.Body == null)
				{
					throw new InvalidOperationException("no POI probe scene; run PointOfInterestRenderProbe.Run with FISHMMO_POI_KEEP=1 first");
				}
				Scene scene = EditorSceneManager.OpenScene($"{SceneGenerator.WorldFolder(entry.Body)}/{PointOfInterestRenderProbe.ProbeName}.unity", OpenSceneMode.Single);
				details = new WorldSceneDetails { PointsOfInterest = WorldSceneDetailsCacheReader.HarvestPointsOfInterest(scene) };
				area = GroundRect(scene);
				string imagePath = Environment.GetEnvironmentVariable("FISHMMO_POI_MAP_IMAGE");
				if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
				{
					image = new Texture2D(2, 2, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
					image.LoadImage(File.ReadAllBytes(imagePath));
				}
				Debug.Log($"[POI map render] {details.PointsOfInterest.Count} point(s) harvested; ground {area}");
			}
			catch (Exception ex)
			{
				Debug.LogError($"[POI map render] {ex}");
				EditorApplication.Exit(1);
				return;
			}
			stages.Clear();
			stages.Enqueue(Stage.Whole);
			stages.Enqueue(Stage.Close);
			stages.Enqueue(Stage.Fogged);
			written = 0;
			current = null;
			EditorApplication.update -= Pump;
			EditorApplication.update += Pump;
		}

		private static Rect GroundRect(Scene scene)
		{
			float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (Terrain terrain in root.GetComponentsInChildren<Terrain>(true))
				{
					Vector3 p = terrain.GetPosition();
					Vector3 s = terrain.terrainData.size;
					minX = Mathf.Min(minX, p.x);
					minZ = Mathf.Min(minZ, p.z);
					maxX = Mathf.Max(maxX, p.x + s.x);
					maxZ = Mathf.Max(maxZ, p.z + s.z);
				}
			}
			return Rect.MinMaxRect(minX, minZ, maxX, maxZ);
		}

		private static void Pump()
		{
			try
			{
				if (current != null)
				{
					if (++frames < SettleFrames)
					{
						host?.GetComponent<UIDocument>()?.rootVisualElement?.MarkDirtyRepaint();
						return;
					}
					string path = Path.Combine(output, $"map-{current.Value.ToString().ToLowerInvariant()}.png");
					Capture(path);
					written++;
					Debug.Log($"[POI map render] wrote {path}");
					if (host != null)
					{
						UnityEngine.Object.DestroyImmediate(host);
					}
					host = null;
					current = null;
					return;
				}
				if (stages.Count == 0)
				{
					EditorApplication.update -= Pump;
					Release();
					EditorApplication.Exit(written == 3 ? 0 : 1);
					return;
				}
				current = stages.Dequeue();
				frames = 0;
				Mount(current.Value);
			}
			catch (Exception ex)
			{
				Debug.LogError($"[POI map render] {ex}");
				EditorApplication.update -= Pump;
				EditorApplication.Exit(1);
			}
		}

		private static void Mount(Stage stage)
		{
			if (texture == null)
			{
				texture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
				texture.Create();
				settings = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath));
				settings.hideFlags = HideFlags.HideAndDontSave;
				settings.targetTexture = texture;
				settings.scaleMode = PanelScaleMode.ConstantPixelSize;
				settings.scale = 1f;
				settings.clearColor = true;
				settings.colorClearValue = new Color(0.05f, 0.05f, 0.06f, 1f);
			}
			host = new GameObject("POI Map Render") { hideFlags = HideFlags.HideAndDontSave };
			UIDocument document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			VisualElement root = document.rootVisualElement;
			root.style.flexGrow = 1f;
			foreach (string sheet in new[] { "Assets/Scripts/Client/GUI/FishMMO-Theme.uss", MapFolder + "UIMapShared.uss", MapFolder + "UIMapPOI.uss", MapFolder + "UIMap.uss" })
			{
				StyleSheet loaded = AssetDatabase.LoadAssetAtPath<StyleSheet>(sheet);
				if (loaded != null)
				{
					root.styleSheets.Add(loaded);
				}
			}

			float half = Mathf.Max(area.width, area.height) * 0.5f;
			Vector3 centre = new Vector3(area.center.x, 0f, area.center.y);
			float range = half;
			FogOfWarMap fog = null;
			if (stage == Stage.Close)
			{
				// Closer in, on the densest quarter: where the most points lie within a quarter of the scene.
				centre = Densest(half * 0.5f);
				range = half * 0.25f;
			}
			else if (stage == Stage.Fogged)
			{
				fog = new FogOfWarMap(area, 64f);
				fog.RevealAround(new Vector3(area.center.x, 0f, area.center.y), half * 0.45f);
			}

			var view = new UITKMapView();
			view.style.width = Width;
			view.style.height = Height;
			view.MapTexture = image;
			view.MapTextureRect = area;
			view.Fog = fog;
			view.View = new MapViewTransform(centre, range, 0f);
			root.Add(view);

			var markers = new List<MapMarkerSnapshot>();
			MapContent.AppendPointsOfInterest(markers, details, null, fog, true, MapContent.LabelTierForZoom(range, half));
			view.SetMarkers(markers);
			view.schedule.Execute(() =>
			{
				view.RefreshSurface();
				view.RelayoutMarkers();
			}).ExecuteLater(100);
			Debug.Log($"[POI map render] {stage}: {markers.Count} marker(s), range {range:0} m");
		}

		private static Vector3 Densest(float radius)
		{
			Vector3 best = new Vector3(area.center.x, 0f, area.center.y);
			int bestCount = -1;
			foreach (MapPointOfInterestDetails a in details.PointsOfInterest)
			{
				int count = details.PointsOfInterest.Count(b => (b.Position - a.Position).sqrMagnitude < radius * radius);
				if (count > bestCount)
				{
					bestCount = count;
					best = a.Position;
				}
			}
			return best;
		}

		private static void Capture(string path)
		{
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = texture;
			try
			{
				var shot = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
				shot.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
				shot.Apply();
				File.WriteAllBytes(path, shot.EncodeToPNG());
				UnityEngine.Object.DestroyImmediate(shot);
			}
			finally
			{
				RenderTexture.active = previous;
			}
		}

		private static void Release()
		{
			if (settings != null)
			{
				UnityEngine.Object.DestroyImmediate(settings);
			}
			if (texture != null)
			{
				texture.Release();
				UnityEngine.Object.DestroyImmediate(texture);
			}
			if (image != null)
			{
				UnityEngine.Object.DestroyImmediate(image);
			}
			settings = null;
			texture = null;
			image = null;
		}
	}
}
#endif
