using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// Throwaway harness: drives the character preview for many frames in several render modes and
	/// reports how process memory moves in each, so a leak can be attributed by measurement.
	/// </summary>
	/// <remarks>
	/// Modes, run back to back in one editor launch:
	/// <list type="bullet">
	/// <item><c>none</c>: rig + document mounted, nothing rendered. The noise floor.</item>
	/// <item><c>full</c>: the game's own path, <c>CharacterSheetView.RefreshPreview</c> every frame.</item>
	/// <item><c>standard</c>: the renderer alone, <c>StandardRequest</c> every frame, no UI repaint.</item>
	/// <item><c>single</c>: the renderer's texture, but a <c>SingleCameraRequest</c> every frame.</item>
	/// <item><c>camrender</c>: the renderer's texture, but <c>Camera.Render()</c> every frame.</item>
	/// </list>
	/// </remarks>
	public static class PreviewLeakProbe
	{
		private const string PANEL_SETTINGS_PATH = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UXML_PATH = "Assets/Scripts/Client/GUI/World/CharacterSheet/UICharacterSheet.uxml";
		private const int WIDTH = 1200;
		private const int HEIGHT = 900;
		private const int WARMUP_FRAMES = 120;
		private const int SAMPLE_EVERY = 150;

		private static string[] MODES = { "none", "game", "repaint", "standard", "single" };

		private static int framesPerMode = 900;
		private static int modeIndex;
		private static int frame;
		private static int renders;
		private static int compilingFrames;
		private static double modeStart;
		private static long lastManaged;
		private static long managedAllocated;
		private static GameObject host;
		private static UIDocument document;
		private static PanelSettings settings;
		private static RenderTexture texture;
		private static UITKEquipment panel;
		private static CharacterSheetView sheet;
		private static EquipmentPreviewRenderer renderer;
		private static PlayerCharacter character;
		private static Camera mainCamera;
		private static RenderTexture mainTexture;
		private static object mainRequest;
		private static Sample baseline;
		private static readonly StringBuilder report = new StringBuilder();

		private struct Sample
		{
			public long Rss;
			public long Allocated;
			public long Reserved;
			public long Graphics;
			public long Managed;
			public int Objects;
			public int RenderTextures;

			public static Sample Take(bool countObjects)
			{
				Sample s = new Sample
				{
					Rss = ReadRss(),
					Allocated = Profiler.GetTotalAllocatedMemoryLong(),
					Reserved = Profiler.GetTotalReservedMemoryLong(),
					Graphics = Profiler.GetAllocatedMemoryForGraphicsDriver(),
					Managed = GC.GetTotalMemory(false),
				};
				if (countObjects)
				{
					s.Objects = Resources.FindObjectsOfTypeAll<UnityEngine.Object>().Length;
					s.RenderTextures = Resources.FindObjectsOfTypeAll<RenderTexture>().Length;
				}
				return s;
			}

			public string Delta(Sample from, int frames)
			{
				return $"rss {Mb(Rss - from.Rss)} ({KbPerFrame(Rss - from.Rss, frames)}) " +
					$"alloc {Mb(Allocated - from.Allocated)} ({KbPerFrame(Allocated - from.Allocated, frames)}) " +
					$"reserved {Mb(Reserved - from.Reserved)} gfx {Mb(Graphics - from.Graphics)} " +
					$"managed {Mb(Managed - from.Managed)} objects {Objects - from.Objects:+#;-#;0} rts {RenderTextures - from.RenderTextures:+#;-#;0}";
			}

			private static string Mb(long bytes) => $"{bytes / (1024.0 * 1024.0):+0.00;-0.00;0.00} MB";
			private static string KbPerFrame(long bytes, int frames) => $"{bytes / 1024.0 / Math.Max(1, frames):+0.0;-0.0;0.0} KB/frame";
		}

		private static long ReadRss()
		{
			try
			{
				foreach (string line in File.ReadLines("/proc/self/status"))
				{
					if (line.StartsWith("VmRSS:"))
					{
						string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
						return long.Parse(parts[1]) * 1024;
					}
				}
			}
			catch { }
			return 0;
		}

		[DashboardTool(DashboardToolAttribute.UITests, "Probe Preview Leak", Section = "Probes", Order = 6)]
		public static void Run()
		{
			try
			{
				string framesEnv = Environment.GetEnvironmentVariable("FISHMMO_LEAK_FRAMES");
				if (!string.IsNullOrEmpty(framesEnv) && int.TryParse(framesEnv, out int parsed))
				{
					framesPerMode = parsed;
				}

				string modesEnv = Environment.GetEnvironmentVariable("FISHMMO_LEAK_MODES");
				if (!string.IsNullOrEmpty(modesEnv))
				{
					MODES = modesEnv.Split(',');
				}

				Seed.All();
				modeIndex = 0;
				BeginMode();
				EditorApplication.update -= Pump;
				EditorApplication.update += Pump;
			}
			catch (Exception ex)
			{
				EditorApplication.update -= Pump;
				Debug.LogError($"[PreviewLeakProbe] setup failed: {ex}");
				EditorAutomation.Finish(1);
			}
		}

		private static void BeginMode()
		{
			frame = 0;
			renders = 0;
			compilingFrames = 0;
			modeStart = EditorApplication.timeSinceStartup;
			managedAllocated = 0;
			lastManaged = GC.GetTotalMemory(false);

			texture = new RenderTexture(WIDTH, HEIGHT, 24, RenderTextureFormat.ARGB32);
			texture.Create();

			settings = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(PANEL_SETTINGS_PATH));
			settings.hideFlags = HideFlags.HideAndDontSave;
			settings.targetTexture = texture;
			settings.clearColor = true;
			settings.colorClearValue = new Color(0.055f, 0.059f, 0.071f, 1.0f);

			host = new GameObject("Leak_" + MODES[modeIndex]) { hideFlags = HideFlags.HideAndDontSave };
			document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UXML_PATH);

			character = Rig.Build(host);
			Rig.AttachPreview(character);

			panel = host.AddComponent<UITKEquipment>();
			panel.Document = document;
			panel.OnStarting();
			panel.SetCharacter(character);

			FieldInfo sheetField = typeof(UITKEquipment).GetField("sheet", BindingFlags.NonPublic | BindingFlags.Instance);
			sheet = sheetField?.GetValue(panel) as CharacterSheetView;
			if (sheet == null)
			{
				throw new InvalidOperationException("UITKEquipment no longer holds a CharacterSheetView.");
			}

			FieldInfo rendererField = typeof(CharacterSheetView).GetField("previewRenderer", BindingFlags.NonPublic | BindingFlags.Instance);
			renderer = rendererField?.GetValue(sheet) as EquipmentPreviewRenderer;
			if (renderer == null)
			{
				throw new InvalidOperationException("CharacterSheetView has no previewRenderer.");
			}

			document.rootVisualElement?.MarkDirtyRepaint();
			Debug.Log($"[PreviewLeakProbe] === mode {MODES[modeIndex]} for {framesPerMode} frames ===");
		}

		private static void Step(string mode)
		{
			switch (mode)
			{
				case "none":
					return;

				case "game":
					// Exactly what UITKEquipment.OnTick does. Repaints only during warmup so the layout measures the viewport.
					sheet.RefreshPreview();
					if (frame <= WARMUP_FRAMES) document?.rootVisualElement?.MarkDirtyRepaint();
					return;

				case "main":
					RenderMain();
					return;

				case "main+game":
					RenderMain();
					sheet.RefreshPreview();
					if (frame <= WARMUP_FRAMES) document?.rootVisualElement?.MarkDirtyRepaint();
					return;

				case "repaint":
					document?.rootVisualElement?.MarkDirtyRepaint();
					return;

				case "standard":
					renderer.Configure(character.EquipmentViewCamera);
					renderer.Render(348, 224);
					return;

				case "std344":
				case "std348":
				case "std344framed":
				case "std348framed":
				case "std344framed+bg":
				case "std344+bg":
				case "std344framed+bg+rp":
				case "std344framed+rp":
				{
					if (mode.EndsWith("+rp") && frame <= WARMUP_FRAMES) document?.rootVisualElement?.MarkDirtyRepaint();
					int w = mode.StartsWith("std344") ? 344 : 348;
					int h = mode.StartsWith("std344") ? 222 : 224;
					renderer.Configure(character.EquipmentViewCamera);
					bool wasReady = renderer.IsReady;
					renderer.Render(w, h);
					if (!wasReady && mode.Contains("framed") && !renderer.Frame(character.MeshRoot)) Debug.LogWarning("[PreviewLeakProbe] Frame refused");
					if (!wasReady && mode.Contains("+bg"))
					{
						// What the game path does once: the texture becomes the viewport element's background.
						VisualElement previewElement = document.rootVisualElement?.Q("preview-rt");
						if (previewElement == null) Debug.LogWarning("[PreviewLeakProbe] no preview-rt element");
						else previewElement.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(renderer.Texture));
					}
					return;
				}

				case "stdreq":
				{
					renderer.Configure(character.EquipmentViewCamera);
					if (!renderer.IsReady)
					{
						renderer.Render(348, 224);
						renderer.Frame(character.MeshRoot);
						return;
					}
					RenderPipeline.StandardRequest std = new RenderPipeline.StandardRequest { destination = renderer.Texture };
					if (RenderPipeline.SupportsRenderRequest(renderer.Camera, std)) renderer.Camera.SubmitRenderRequest(std);
					else Debug.LogWarning("[PreviewLeakProbe] StandardRequest unsupported");
					return;
				}

				case "single":
				case "single+support":
				case "single+iscreated":
				case "single+both":
				case "camrender":
				{
					renderer.Configure(character.EquipmentViewCamera);
					if (!renderer.IsReady)
					{
						// One StandardRequest to create the texture and adopt the camera; every later frame uses the mode's path.
						renderer.Render(348, 224);
						renderer.Frame(character.MeshRoot);
						return;
					}

					Camera cam = renderer.Camera;
					if (mode == "camrender")
					{
						cam.Render();
						return;
					}

					if (mode == "single+support" || mode == "single+both")
					{
						EnsureSingleRequest(renderer.Texture);
						object supported = typeof(RenderPipeline).GetMethod("SupportsRenderRequest").MakeGenericMethod(singleRequestType).Invoke(null, new[] { cam, singleRequest });
						if (!(bool)supported) Debug.LogWarning("[PreviewLeakProbe] SupportsRenderRequest false");
					}
					if (mode == "single+iscreated" || mode == "single+both")
					{
						if (!renderer.Texture.IsCreated()) Debug.LogWarning("[PreviewLeakProbe] texture not created");
					}
					SubmitSingleCameraRequest(cam, renderer.Texture);
					return;
				}
			}
		}

		/// <summary>A stand-in for the client's main camera: a full-size render every frame, through the same URP renderer as the preview.</summary>
		private static void RenderMain()
		{
			if (mainCamera == null)
			{
				GameObject go = new GameObject("LeakProbeMainCamera") { hideFlags = HideFlags.HideAndDontSave };
				go.transform.SetParent(host.transform, false);
				go.transform.localPosition = new Vector3(0, 1.5f, -5f);
				mainCamera = go.AddComponent<Camera>();
				mainCamera.enabled = false;
				mainCamera.clearFlags = CameraClearFlags.SolidColor;
				mainCamera.backgroundColor = Color.black;
				mainTexture = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { name = "LeakProbeMain" };
				mainTexture.Create();
				mainCamera.targetTexture = mainTexture;
				EnsureSingleRequest(renderer.Texture != null ? renderer.Texture : mainTexture);
				mainRequest = Activator.CreateInstance(singleRequestType);
				singleRequestType.GetField("destination").SetValue(mainRequest, mainTexture);
			}
			submitSingle.Invoke(mainCamera, new[] { mainRequest });
		}

		private static Type singleRequestType;
		private static MethodInfo submitSingle;
		private static object singleRequest;

		/// <summary>URP's SingleCameraRequest lives in the URP runtime assembly, which the scratch asmdef does not reference.</summary>
		private static void SubmitSingleCameraRequest(Camera cam, RenderTexture destination)
		{
			EnsureSingleRequest(destination);
			submitSingle.Invoke(cam, new[] { singleRequest });
		}

		private static void EnsureSingleRequest(RenderTexture destination)
		{
			if (singleRequestType == null)
			{
				foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
				{
					if (asm.GetName().Name != "Unity.RenderPipelines.Universal.Runtime") continue;
					foreach (Type t in asm.GetTypes())
					{
						if (t.Name == "SingleCameraRequest") { singleRequestType = t; break; }
					}
				}
				if (singleRequestType == null)
				{
					throw new InvalidOperationException("SingleCameraRequest type not found");
				}
				singleRequest = Activator.CreateInstance(singleRequestType);
				singleRequestType.GetField("destination").SetValue(singleRequest, destination);
				submitSingle = typeof(Camera).GetMethod("SubmitRenderRequest").MakeGenericMethod(singleRequestType);
			}
			if (singleRequestType.GetField("destination").GetValue(singleRequest) != (object)destination)
			{
				singleRequestType.GetField("destination").SetValue(singleRequest, destination);
			}
		}

		private static void Pump()
		{
			try
			{
				string mode = MODES[modeIndex];
				++frame;
				Step(mode);
				if (mode != "none" && renderer.IsReady) ++renders;
				if (ShaderUtil.anythingCompiling) ++compilingFrames;
				long nowManaged = GC.GetTotalMemory(false);
				if (frame > WARMUP_FRAMES && nowManaged > lastManaged) managedAllocated += nowManaged - lastManaged;
				lastManaged = nowManaged;

				if (frame == WARMUP_FRAMES)
				{
					GC.Collect();
					baseline = Sample.Take(true);
					Debug.Log($"[PreviewLeakProbe] {mode} baseline at frame {frame}: rss {baseline.Rss / 1048576} MB alloc {baseline.Allocated / 1048576} MB objects {baseline.Objects} rts {baseline.RenderTextures}");
					return;
				}

				if (frame > WARMUP_FRAMES && (frame - WARMUP_FRAMES) % SAMPLE_EVERY == 0 && frame < framesPerMode)
				{
					Sample s = Sample.Take(false);
					Debug.Log($"[PreviewLeakProbe] {mode} frame {frame}: {s.Delta(baseline, frame - WARMUP_FRAMES)}");
				}

				if (frame < framesPerMode)
				{
					return;
				}

				GC.Collect();
				Sample final = Sample.Take(true);
				Debug.Log($"[PreviewLeakProbe] {mode} texture at end: {(renderer.Texture == null ? "none" : renderer.Texture.width + "x" + renderer.Texture.height)}, camera adopted {renderer.Camera != null}, renders submitted {renders}, frames with shaders compiling {compilingFrames}, {EditorApplication.timeSinceStartup - modeStart:0}s, managed garbage {managedAllocated / 1024.0 / Math.Max(1, frame - WARMUP_FRAMES):0.0} KB/frame");
				string line = $"{mode,-10} over {frame - WARMUP_FRAMES} frames: {final.Delta(baseline, frame - WARMUP_FRAMES)}";
				Debug.Log("[PreviewLeakProbe] RESULT " + line);
				report.AppendLine(line);

				Release();
				++modeIndex;
				if (modeIndex < MODES.Length)
				{
					BeginMode();
					return;
				}

				EditorApplication.update -= Pump;
				Debug.Log("[PreviewLeakProbe] SUMMARY\n" + report);
				EditorAutomation.Finish(0);
			}
			catch (Exception ex)
			{
				EditorApplication.update -= Pump;
				Debug.LogError($"[PreviewLeakProbe] pump failed: {ex}");
				Release();
				EditorAutomation.Finish(1);
			}
		}

		private static void Release()
		{
			sheet?.ReleasePreview();
			if (mainTexture != null) { mainTexture.Release(); UnityEngine.Object.DestroyImmediate(mainTexture); }
			mainTexture = null;
			mainCamera = null;
			mainRequest = null;
			if (host != null) UnityEngine.Object.DestroyImmediate(host);
			if (settings != null) UnityEngine.Object.DestroyImmediate(settings);
			if (texture != null)
			{
				texture.Release();
				UnityEngine.Object.DestroyImmediate(texture);
			}
			host = null;
			document = null;
			settings = null;
			texture = null;
			panel = null;
			sheet = null;
			renderer = null;
			character = null;
		}
	}
}
