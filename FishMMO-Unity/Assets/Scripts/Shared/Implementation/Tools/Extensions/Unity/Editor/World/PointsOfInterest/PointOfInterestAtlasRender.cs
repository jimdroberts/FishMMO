#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Photographs the World Atlas page with a cut scene selected: its points of interest on the globe at three zooms and the
	/// scene inspector's flat preview beside it. Reads the real atlas (nothing is moved or saved). FISHMMO_POI_ATLAS_SCENE
	/// (default the POI render probe's scene) picks the scene; PNGs go to FISHMMO_POI_ATLAS_OUT. Runs in a non-batch editor
	/// driven by EditorApplication.update and exits through EditorApplication.Exit.
	/// </summary>
	public static class PointOfInterestAtlasRender
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const int Width = 1800;
		private const int Height = 1500;
		private const int SettleFrames = 90;

		private static readonly Queue<(string name, float zoom)> stages = new Queue<(string, float)>();
		private static string output;
		private static string sceneName;
		private static (string name, float zoom)? current;
		private static int frames;
		private static GameObject host;
		private static RenderTexture texture;
		private static PanelSettings settings;
		private static int written;

		public static void Run()
		{
			output = Environment.GetEnvironmentVariable("FISHMMO_POI_ATLAS_OUT") ?? Path.Combine(Path.GetTempPath(), "fishmmo-poi-atlas");
			sceneName = Environment.GetEnvironmentVariable("FISHMMO_POI_ATLAS_SCENE") ?? PointOfInterestRenderProbe.ProbeName;
			Directory.CreateDirectory(output);
			stages.Clear();
			stages.Enqueue(("atlas-globe", 3f));
			stages.Enqueue(("atlas-scene", 12f));
			stages.Enqueue(("atlas-scene-close", 30f));
			written = 0;
			current = null;
			EditorApplication.update -= Pump;
			EditorApplication.update += Pump;
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
					string path = Path.Combine(output, current.Value.name + ".png");
					Capture(path);
					written++;
					Debug.Log($"[POI atlas render] wrote {path}");
					Teardown();
					current = null;
					return;
				}
				if (stages.Count == 0)
				{
					EditorApplication.update -= Pump;
					Release();
					Debug.Log($"[POI atlas render] {written} image(s) to {output}");
					EditorApplication.Exit(written > 0 ? 0 : 1);
					return;
				}
				current = stages.Dequeue();
				frames = 0;
				Mount(current.Value.zoom);
			}
			catch (Exception ex)
			{
				Debug.LogError($"[POI atlas render] {ex}");
				EditorApplication.update -= Pump;
				EditorApplication.Exit(1);
			}
		}

		private static void Mount(float zoom)
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
				settings.colorClearValue = new Color(0.1f, 0.1f, 0.1f, 1f);
			}
			host = new GameObject("POI Atlas Render") { hideFlags = HideFlags.HideAndDontSave };
			UIDocument document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			VisualElement root = document.rootVisualElement;
			root.style.flexGrow = 1f;
			root.style.backgroundColor = new Color(0.16f, 0.16f, 0.17f, 1f);
			// The page selects this scene, faces it and draws its inspector (with the points-of-interest preview) as it builds.
			WorldAtlasPage.PendingSelection = sceneName;
			var page = new WorldAtlasPage();
			page.style.width = Width;
			page.style.height = Height;
			root.Add(page);
			// Held for the first second and a half: the page sets its own zoom as it settles, after this would have run once.
			page.Globe.schedule.Execute(() =>
			{
				page.Globe.Zoom = zoom;
				page.Globe.MarkDirtyRepaint();
			}).Every(100).ForDuration(1500);
		}

		private static void Capture(string path)
		{
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = texture;
			try
			{
				var image = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
				image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
				image.Apply();
				File.WriteAllBytes(path, image.EncodeToPNG());
				UnityEngine.Object.DestroyImmediate(image);
			}
			finally
			{
				RenderTexture.active = previous;
			}
		}

		private static void Teardown()
		{
			if (host != null)
			{
				UnityEngine.Object.DestroyImmediate(host);
			}
			host = null;
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
			settings = null;
			texture = null;
		}
	}
}
#endif
