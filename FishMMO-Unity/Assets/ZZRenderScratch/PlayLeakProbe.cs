using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// Throwaway harness: measures the character preview's memory behaviour in PLAY MODE, in a
	/// GUI editor that presents frames, so per-frame GPU memory recycles the way it does in the
	/// client. The edit-mode probe cannot do that: a headless editor never presents, and on the
	/// NVIDIA Vulkan backend every out-of-loop render then looks like a 240 KB leak.
	/// </summary>
	/// <remarks>
	/// Launch: <c>xvfb-run ... Unity -projectPath . -executeMethod FishMMO.RenderScratch.PlayLeakProbe.Run</c>
	/// (no <c>-batchmode</c>). Modes via <c>FISHMMO_LEAK_MODES</c>: <c>none</c> (presenting main
	/// camera only), <c>game</c> (plus the sheet's RefreshPreview every Update, exactly as
	/// UITKEquipment.OnTick does), <c>enabled</c> (the preview camera enabled with the same target
	/// texture, rendered by URP inside the frame instead of by a request from Update).
	/// </remarks>
	public static class PlayLeakProbe
	{
		private const string ARMED_KEY = "FishMMO.PlayLeakProbe.Armed";
		private const string SCENE_PATH = "Assets/ZZRenderScratch/PlayLeakProbeScene.unity";

		[MenuItem("FishMMO/UI Toolkit/Probe Preview Leak (Play Mode)")]
		public static void Run()
		{
			try
			{
				Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
				EditorSceneManager.SaveScene(scene, SCENE_PATH);

				// A Game view must exist for cameras to render and frames to present.
				Type gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
				if (gameViewType != null)
				{
					EditorWindow.GetWindow(gameViewType);
				}

				SessionState.SetBool(ARMED_KEY, true);
				Debug.Log("[PlayLeakProbe] entering play mode");
				EditorApplication.EnterPlaymode();
			}
			catch (Exception ex)
			{
				Debug.LogError($"[PlayLeakProbe] setup failed: {ex}");
				EditorApplication.Exit(1);
			}
		}

		[InitializeOnLoadMethod]
		private static void OnLoad()
		{
			EditorApplication.playModeStateChanged += state =>
			{
				if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(ARMED_KEY, false))
				{
					SessionState.SetBool(ARMED_KEY, false);
					Begin();
				}
			};
		}

		private static void Begin()
		{
			try
			{
				Seed.All();
				GameObject go = new GameObject("PlayLeakPump");
				go.AddComponent<PlayLeakPump>();
				Debug.Log("[PlayLeakProbe] pump started");
			}
			catch (Exception ex)
			{
				Debug.LogError($"[PlayLeakProbe] begin failed: {ex}");
				EditorApplication.Exit(1);
			}
		}
	}

	/// <summary>Runs the modes from a real Update loop and reports memory movement per mode.</summary>
	public sealed class PlayLeakPump : MonoBehaviour
	{
		private const string PANEL_SETTINGS_PATH = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UXML_PATH = "Assets/Scripts/Client/GUI/World/CharacterSheet/UICharacterSheet.uxml";
		private const int WARMUP_FRAMES = 240;
		private const int SAMPLE_EVERY = 300;

		private string[] modes = { "none", "game", "enabled" };
		private int framesPerMode = 3000;
		private int modeIndex;
		private int frame;
		private int renders;
		private double modeStart;
		private long baselineRss, baselineAlloc, baselineGfx, baselineManaged;
		private int baselineObjects;
		private long lastManaged, managedGarbage;
		private readonly StringBuilder report = new StringBuilder();

		private GameObject host;
		private UIDocument document;
		private PanelSettings settings;
		private UITKEquipment panel;
		private CharacterSheetView sheet;
		private EquipmentPreviewRenderer renderer;
		private PlayerCharacter character;
		private bool enabledCameraArmed;

		// Minimap modes: the real renderer and map view, in a document attached to a screen panel.
		private MinimapCameraRenderer minimapRenderer;
		private UITKMapView mapView;
		private GameObject minimapCameraObject;
		private int minimapRenders;
		private double nextGiTime;

		private void Start()
		{
			string modesEnv = Environment.GetEnvironmentVariable("FISHMMO_LEAK_MODES");
			if (!string.IsNullOrEmpty(modesEnv)) modes = modesEnv.Split(',');
			string framesEnv = Environment.GetEnvironmentVariable("FISHMMO_LEAK_FRAMES");
			if (!string.IsNullOrEmpty(framesEnv) && int.TryParse(framesEnv, out int parsed)) framesPerMode = parsed;

			Application.targetFrameRate = -1;
			QualitySettings.vSyncCount = 0;
			BeginMode();
		}

		private void BeginMode()
		{
			frame = 0;
			renders = 0;
			managedGarbage = 0;
			enabledCameraArmed = false;
			modeStart = Time.realtimeSinceStartupAsDouble;
			lastManaged = GC.GetTotalMemory(false);

			settings = Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(PANEL_SETTINGS_PATH));
			settings.hideFlags = HideFlags.DontSave;
			settings.targetTexture = null;

			host = new GameObject("Leak_" + modes[modeIndex]);
			// Configure the document while disabled so OnEnable clones the tree into a live panel.
			host.SetActive(false);
			document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UXML_PATH);
			host.SetActive(true);
			EnsureDocumentAttached();

			minimapRenders = 0;
			nextGiTime = 0;
			nextStandardTime = 0;
			if (IsMinimapMode(modes[modeIndex]) || modes[modeIndex].StartsWith("gi"))
			{
				if (IsMinimapMode(modes[modeIndex]))
				{
					BuildMinimap();
				}
				Debug.Log($"[PlayLeakProbe] === mode {modes[modeIndex]} for {framesPerMode} frames ===");
				return;
			}

			character = Rig.Build(host);
			Rig.AttachPreview(character);

			// The presenting main camera looks at the same character, as the client's does.
			Camera main = Camera.main;
			if (main != null)
			{
				main.transform.position = new Vector3(0f, 1.5f, -4f);
				main.transform.LookAt(new Vector3(0f, 1f, 0f));
			}

			panel = host.AddComponent<UITKEquipment>();
			panel.Document = document;
			panel.OnStarting();
			panel.SetCharacter(character);
			// The real panel only measures (and so only renders) while it is shown.
			panel.Show();

			sheet = typeof(UITKEquipment).GetField("sheet", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(panel) as CharacterSheetView;
			renderer = sheet == null ? null : typeof(CharacterSheetView).GetField("previewRenderer", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(sheet) as EquipmentPreviewRenderer;
			if (sheet == null || renderer == null) throw new InvalidOperationException("sheet/renderer not found");

			Debug.Log($"[PlayLeakProbe] === mode {modes[modeIndex]} for {framesPerMode} frames ===");
		}

		private void Update()
		{
			try
			{
				string mode = modes[modeIndex];
				++frame;
				Step(mode);
				if (renderer != null && mode != "none" && renderer.IsReady) ++renders;

				long nowManaged = GC.GetTotalMemory(false);
				if (frame > WARMUP_FRAMES && nowManaged > lastManaged) managedGarbage += nowManaged - lastManaged;
				lastManaged = nowManaged;

				if (frame == 60 || frame == 300)
				{
					VisualElement rootElement = document?.rootVisualElement;
					string map = mapView == null ? "n/a" : $"{mapView.resolvedStyle.width}x{mapView.resolvedStyle.height} panel={(mapView.panel == null ? "none" : "attached")} texture={(minimapRenderer?.Texture == null ? "none" : minimapRenderer.Texture.width + "x" + minimapRenderer.Texture.height)} renders={minimapRenders}";
					Debug.Log($"[PlayLeakProbe] DIAG {mode} frame {frame}: docPanel={(rootElement?.panel == null ? "none" : "attached")} rootSize={rootElement?.resolvedStyle.width}x{rootElement?.resolvedStyle.height} map={map} previewTexture={(renderer?.Texture == null ? "none" : "yes")}");
				}

				if (frame == WARMUP_FRAMES)
				{
					GC.Collect();
					baselineRss = ReadRss(); baselineAlloc = Profiler.GetTotalAllocatedMemoryLong();
					baselineGfx = Profiler.GetAllocatedMemoryForGraphicsDriver(); baselineManaged = GC.GetTotalMemory(false);
					baselineObjects = Resources.FindObjectsOfTypeAll<UnityEngine.Object>().Length;
					Debug.Log($"[PlayLeakProbe] {mode} baseline: rss {baselineRss / 1048576} MB alloc {baselineAlloc / 1048576} MB gfx {baselineGfx / 1048576} MB objects {baselineObjects}");
					return;
				}

				if (frame > WARMUP_FRAMES && (frame - WARMUP_FRAMES) % SAMPLE_EVERY == 0 && frame < framesPerMode)
				{
					Debug.Log($"[PlayLeakProbe] {mode} frame {frame}: {Delta(frame - WARMUP_FRAMES, false)}");
				}

				if (frame < framesPerMode) return;

				GC.Collect();
				string line = $"{mode,-8} over {frame - WARMUP_FRAMES} frames ({Time.realtimeSinceStartupAsDouble - modeStart:0}s, {renders} renders, texture {(renderer?.Texture == null ? "none" : renderer.Texture.width + "x" + renderer.Texture.height)}, minimap renders {minimapRenders}, managed garbage {managedGarbage / 1024.0 / (frame - WARMUP_FRAMES):0.0} KB/frame): {Delta(frame - WARMUP_FRAMES, true)}";
				Debug.Log("[PlayLeakProbe] RESULT " + line);
				report.AppendLine(line);

				Release();
				++modeIndex;
				if (modeIndex < modes.Length) { BeginMode(); return; }

				Debug.Log("[PlayLeakProbe] SUMMARY\n" + report);
				EditorApplication.Exit(0);
			}
			catch (Exception ex)
			{
				Debug.LogError($"[PlayLeakProbe] pump failed: {ex}");
				EditorApplication.Exit(1);
			}
		}

		private void Step(string mode)
		{
			switch (mode)
			{
				case "none":
					return;

				case "minimap":
				case "minimap-render":
				case "minimap-refresh":
					StepMinimap(mode);
					return;

				case "minimap-standard":
					StepMinimapStandard();
					return;

				case "gi":
					if (Time.unscaledTimeAsDouble >= nextGiTime)
					{
						nextGiTime = Time.unscaledTimeAsDouble + 1.0;
						DynamicGI.UpdateEnvironment();
					}
					return;

				case "gi-everyframe":
					DynamicGI.UpdateEnvironment();
					return;

				case "game":
					sheet.RefreshPreview();
					return;

				case "render":
					// The renderer alone, as the sheet would drive it once the viewport measures: one request per Update.
					renderer.Configure(character.EquipmentViewCamera);
					bool wasReady = renderer.IsReady;
					renderer.Render(344, 222);
					if (!wasReady) renderer.Frame(character.MeshRoot);
					return;

				case "enabled":
					if (!enabledCameraArmed)
					{
						// Size the texture and frame the camera once, then hand the camera to the frame loop.
						renderer.Configure(character.EquipmentViewCamera);
						renderer.Render(344, 222);
						renderer.Frame(character.MeshRoot);
						if (renderer.IsReady && renderer.Camera != null && frame > 3)
						{
							renderer.Camera.targetTexture = renderer.Texture;
							renderer.Camera.gameObject.SetActive(true);
							renderer.Camera.enabled = true;
							enabledCameraArmed = true;
							Debug.Log("[PlayLeakProbe] preview camera enabled in the frame loop");
						}
					}
					return;
			}
		}

		private static bool IsMinimapMode(string mode) => mode.StartsWith("minimap");

		/// <summary>
		/// Reports the document's state after enabling and, when it built no root, runs its own
		/// rebuild and attach routines. Without a root attached to a panel nothing repaints, and a
		/// measurement of UI work silently measures nothing.
		/// </summary>
		private void EnsureDocumentAttached()
		{
			Debug.Log($"[PlayLeakProbe] DOC after enable: enabled={document.enabled} activeAndEnabled={document.isActiveAndEnabled} tree={(document.visualTreeAsset != null)} settings={(document.panelSettings != null)} root={(document.rootVisualElement != null)} panel={(document.rootVisualElement?.panel != null)} isPlaying={EditorApplication.isPlaying} willChange={EditorApplication.isPlayingOrWillChangePlaymode}");

			if (document.rootVisualElement != null && document.rootVisualElement.panel != null)
			{
				return;
			}

			const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
			try
			{
				if (document.rootVisualElement == null)
				{
					typeof(UIDocument).GetMethod("RecreateUI", flags)?.Invoke(document, null);
				}
				if (document.rootVisualElement != null && document.rootVisualElement.panel == null)
				{
					typeof(UIDocument).GetMethod("AddRootVisualElementToTree", flags)?.Invoke(document, null);
				}
			}
			catch (Exception ex)
			{
				Debug.LogError($"[PlayLeakProbe] DOC fallback threw: {ex}");
			}

			Debug.Log($"[PlayLeakProbe] DOC after fallback: root={(document.rootVisualElement != null)} panel={(document.rootVisualElement?.panel != null)}");
		}

		/// <summary>Builds the minimap exactly as UITKMinimap does: a view-aligned map view fed by the shared renderer.</summary>
		private void BuildMinimap()
		{
			minimapCameraObject = new GameObject("LeakProbeMinimapCamera");
			Camera camera = minimapCameraObject.AddComponent<Camera>();
			camera.enabled = false;

			minimapRenderer = new MinimapCameraRenderer();
			minimapRenderer.Configure(camera, 0, Cartography.MinimapResolution);
			minimapRenderer.FramesPerSecond = 30.0f;

			mapView = new UITKMapView();
			mapView.MapTextureIsViewAligned = true;
			mapView.style.position = Position.Absolute;
			mapView.style.left = 20;
			mapView.style.top = 20;
			mapView.style.width = 256;
			mapView.style.height = 256;
			if (document.rootVisualElement == null || document.rootVisualElement.panel == null)
			{
				throw new InvalidOperationException("the document has no root attached to a panel, so the minimap would never repaint; the measurement would be meaningless");
			}
			document.rootVisualElement.Add(mapView);
		}

		/// <summary>UITKMinimap.LateUpdate's render path, optionally split into its render and surface-refresh halves.</summary>
		private void StepMinimap(string mode)
		{
			if (minimapRenderer == null || mapView == null)
			{
				return;
			}

			MapViewTransform view = new MapViewTransform(new Vector3(Mathf.Sin(frame * 0.01f) * 5f, 0f, 0f), 25.0f, 0.0f);
			mapView.View = view;

			if (mode == "minimap-refresh")
			{
				// Refresh half only: one render to have a texture, then reassign and repaint at the render rate.
				if (minimapRenderer.Texture == null || minimapRenders == 0)
				{
					if (minimapRenderer.Render(view, true)) { ++minimapRenders; mapView.MapTexture = minimapRenderer.Texture; }
					return;
				}
				if (frame % 2 == 0)
				{
					mapView.MapTexture = minimapRenderer.Texture;
					mapView.RefreshSurface();
				}
				return;
			}

			if (minimapRenderer.Render(view))
			{
				++minimapRenders;
				if (mode == "minimap")
				{
					mapView.MapTexture = minimapRenderer.Texture;
					mapView.RefreshSurface();
				}
			}
		}

		private double nextStandardTime;

		/// <summary>
		/// The minimap path exactly as the shipped client ran it before the working-tree change: the
		/// renderer's texture and settings, but a RenderPipeline.StandardRequest at 30 per second,
		/// then the surface refresh.
		/// </summary>
		private void StepMinimapStandard()
		{
			if (minimapRenderer == null || mapView == null)
			{
				return;
			}

			MapViewTransform view = new MapViewTransform(new Vector3(Mathf.Sin(frame * 0.01f) * 5f, 0f, 0f), 25.0f, 0.0f);
			mapView.View = view;

			if (minimapRenderer.Texture == null)
			{
				// One ordinary render creates the texture and applies the camera settings.
				if (minimapRenderer.Render(view, true)) { ++minimapRenders; mapView.MapTexture = minimapRenderer.Texture; }
				return;
			}

			double now = Time.unscaledTimeAsDouble;
			if (now < nextStandardTime)
			{
				return;
			}
			nextStandardTime = now + (1.0 / 30.0);

			Camera camera = minimapCameraObject.GetComponent<Camera>();
			UnityEngine.Rendering.RenderPipeline.StandardRequest request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = minimapRenderer.Texture };
			if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(camera, request))
			{
				camera.SubmitRenderRequest(request);
				++minimapRenders;
				mapView.MapTexture = minimapRenderer.Texture;
				mapView.RefreshSurface();
			}
		}

		private string Delta(int frames, bool objects)
		{
			long rss = ReadRss() - baselineRss;
			long alloc = Profiler.GetTotalAllocatedMemoryLong() - baselineAlloc;
			long gfx = Profiler.GetAllocatedMemoryForGraphicsDriver() - baselineGfx;
			long managed = GC.GetTotalMemory(false) - baselineManaged;
			string objs = objects ? $" objects {Resources.FindObjectsOfTypeAll<UnityEngine.Object>().Length - baselineObjects:+#;-#;0}" : "";
			return $"rss {rss / 1048576.0:+0.00;-0.00;0.00} MB ({rss / 1024.0 / frames:+0.0;-0.0;0.0} KB/frame) alloc {alloc / 1048576.0:+0.00;-0.00;0.00} MB gfx {gfx / 1048576.0:+0.00;-0.00;0.00} MB managed {managed / 1048576.0:+0.00;-0.00;0.00} MB{objs}";
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

		private void Release()
		{
			if (mapView != null) { mapView.RemoveFromHierarchy(); mapView = null; }
			minimapRenderer?.Dispose();
			minimapRenderer = null;
			if (minimapCameraObject != null) { Destroy(minimapCameraObject); minimapCameraObject = null; }
			sheet?.ReleasePreview();
			if (host != null) Destroy(host);
			if (settings != null) Destroy(settings);
			host = null; document = null; settings = null; panel = null; sheet = null; renderer = null; character = null;
		}
	}
}
