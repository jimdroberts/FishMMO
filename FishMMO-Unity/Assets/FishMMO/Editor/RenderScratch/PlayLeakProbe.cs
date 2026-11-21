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
		/// <summary>A probe mode a module adds (the Map module's minimap and marker modes), registered at editor load.</summary>
		public interface ILeakProbeMode
		{
			/// <summary>Whether this handles the named mode (<c>FISHMMO_LEAK_MODES</c>).</summary>
			bool Handles(string mode);

			/// <summary>Builds the mode's content into the probe's document, which is attached to a panel.</summary>
			void Build(UIDocument document);

			/// <summary>One frame of the mode.</summary>
			void Step(string mode);

			/// <summary>The mode's state and counters, for the DIAG and RESULT lines.</summary>
			string Describe();

			/// <summary>Destroys what <see cref="Build"/> made.</summary>
			void Release();
		}

		/// <summary>The modes modules add.</summary>
		public static readonly System.Collections.Generic.List<ILeakProbeMode> ModuleModes = new System.Collections.Generic.List<ILeakProbeMode>();

		private const string ARMED_KEY = "FishMMO.PlayLeakProbe.Armed";
		private const string SCENE_PATH = "Assets/FishMMO/Editor/RenderScratch/PlayLeakProbeScene.unity";

		[DashboardTool(DashboardToolAttribute.UITests, "Probe Preview Leak (Play Mode)", Section = "Probes", Order = 7, Tooltip = "Replaces the open scene with a scratch scene, enters play mode for several minutes, then leaves play mode.", Confirm = "This replaces the open scene with a scratch scene and runs in play mode for several minutes. Continue?")]
		public static void Run()
		{
			// In an open editor the scratch scene replaces the user's; let them keep their work.
			if (!EditorAutomation.LaunchedHeadless && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
			{
				return;
			}

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
				EditorAutomation.Finish(1);
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
				EditorAutomation.Finish(1);
			}
		}
	}

	/// <summary>Runs the modes from a real Update loop and reports memory movement per mode.</summary>
	public sealed class PlayLeakPump : MonoBehaviour
	{
		private const string PANEL_SETTINGS_PATH = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UXML_PATH = "Assets/FishMMO/Client/GUI/World/CharacterSheet/UICharacterSheet.uxml";
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
		/// <summary>The equipment window (the Items module's; found by name so the probe builds without it).</summary>
		private UITKCharacterControl panel;

		/// <summary>The Items module's equipment window, or null when the module is not installed.</summary>
		private static Type EquipmentPanelType => Type.GetType("FishMMO.Client.UITKEquipment, FishMMO.Items.Client");
		private CharacterSheetView sheet;
		private EquipmentPreviewRenderer renderer;
		private PlayerCharacter character;
		private bool enabledCameraArmed;

		// A module's modes (the Map module's minimap and marker modes): built into the document, stepped each frame.
		private PlayLeakProbe.ILeakProbeMode moduleMode;
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

			nextGiTime = 0;
			toggles = 0;
			moduleMode = PlayLeakProbe.ModuleModes.Find(m => m.Handles(modes[modeIndex]));
			if (moduleMode != null || modes[modeIndex].StartsWith("gi") || modes[modeIndex].StartsWith("showhide") || modes[modeIndex].StartsWith("doc-toggle") || modes[modeIndex].StartsWith("launcher"))
			{
				if (moduleMode != null)
				{
					moduleMode.Build(document);
				}
				if (modes[modeIndex].StartsWith("showhide"))
				{
					BuildTargetPanel();
				}
				if (modes[modeIndex].StartsWith("doc-toggle"))
				{
					BuildBareDocument();
				}
				if (modes[modeIndex].StartsWith("launcher"))
				{
					BuildLauncher();
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

			Type equipmentPanel = EquipmentPanelType;
			if (equipmentPanel == null)
			{
				throw new InvalidOperationException("The equipment preview modes need the Items module (its equipment window); run the module modes only.");
			}
			panel = (UITKCharacterControl)host.AddComponent(equipmentPanel);
			panel.Document = document;
			panel.OnStarting();
			panel.SetCharacter(character);
			// The real panel only measures (and so only renders) while it is shown.
			panel.Show();

			sheet = equipmentPanel.GetField("sheet", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(panel) as CharacterSheetView;
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
					Debug.Log($"[PlayLeakProbe] DIAG {mode} frame {frame}: docPanel={(rootElement?.panel == null ? "none" : "attached")} rootSize={rootElement?.resolvedStyle.width}x{rootElement?.resolvedStyle.height} module={moduleMode?.Describe() ?? "n/a"} previewTexture={(renderer?.Texture == null ? "none" : "yes")}");
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
				GC.WaitForPendingFinalizers();
				GC.Collect();
				string line = $"{mode,-8} over {frame - WARMUP_FRAMES} frames ({Time.realtimeSinceStartupAsDouble - modeStart:0}s, {renders} renders, texture {(renderer?.Texture == null ? "none" : renderer.Texture.width + "x" + renderer.Texture.height)}, {moduleMode?.Describe() ?? "no module mode"}, show cycles {toggles}, theme roots {ThemeRootCount()}, managed garbage {managedGarbage / 1024.0 / (frame - WARMUP_FRAMES):0.0} KB/frame): {Delta(frame - WARMUP_FRAMES, true)}";
				Debug.Log("[PlayLeakProbe] RESULT " + line);
				report.AppendLine(line);

				Release();
				++modeIndex;
				if (modeIndex < modes.Length) { BeginMode(); return; }

				Debug.Log("[PlayLeakProbe] SUMMARY\n" + report);
				enabled = false;
				EditorAutomation.Finish(0);
			}
			catch (Exception ex)
			{
				Debug.LogError($"[PlayLeakProbe] pump failed: {ex}");
				enabled = false;
				EditorAutomation.Finish(1);
			}
		}

		private void Step(string mode)
		{
			if (moduleMode != null)
			{
				moduleMode.Step(mode);
				return;
			}
			switch (mode)
			{
				case "none":
					return;

				case "showhide":
					// Hide then show on alternate frames: one full tree rebuild every two frames.
					if (targetPanel != null)
					{
						if (targetPanel.Visible) targetPanel.Hide(); else { targetPanel.Show(); ++toggles; }
					}
					return;

				case "doc-toggle":
					// The same markup and panel settings, but a bare UIDocument with no panel code:
					// disable and re-enable it on alternate frames. Isolates Unity's own rebuild.
					if (bareDocument != null)
					{
						if (bareDocument.enabled) bareDocument.enabled = false; else { bareDocument.enabled = true; ++toggles; }
					}
					return;

				case "launcher-toggle":
					// Exactly what UITKClientLauncher.ToggleSettings does: flip fish-hidden on the
					// settings scroll view. Alternate frames, one open/close pair every two frames.
					if (launcherSettings != null)
					{
						bool hidden = launcherSettings.ClassListContains("fish-hidden");
						launcherSettings.EnableInClassList("fish-hidden", !hidden);
						if (hidden) ++toggles;
					}
					return;

				case "launcher-inline":
					// Inline display instead of the class: skips selector re-matching, still leaves layout.
					if (launcherSettings != null)
					{
						launcherSettings.RemoveFromClassList("fish-hidden");
						bool shown = launcherSettings.resolvedStyle.display == DisplayStyle.Flex && launcherSettings.style.display.keyword != StyleKeyword.Null && launcherSettings.style.display.value == DisplayStyle.Flex;
						launcherSettings.style.display = shown ? DisplayStyle.None : DisplayStyle.Flex;
						if (!shown) ++toggles;
					}
					return;

				case "launcher-visibility":
					// Visibility instead of display: the overlay stays laid out and keeps its render
					// data, and is only not drawn and not picked while hidden.
					if (launcherSettings != null)
					{
						launcherSettings.RemoveFromClassList("fish-hidden");
						bool visible = launcherSettings.style.visibility.keyword == StyleKeyword.Null || launcherSettings.style.visibility.value == Visibility.Visible;
						if (!visibleInitialised)
						{
							launcherSettings.style.visibility = Visibility.Hidden;
							launcherSettings.pickingMode = PickingMode.Ignore;
							visibleInitialised = true;
							return;
						}
						launcherSettings.style.visibility = visible ? Visibility.Hidden : Visibility.Visible;
						if (!visible) ++toggles;
					}
					return;

				case "launcher-still":
					return;

				case "showhide-still":
					// Mounted and shown once, never toggled: the control.
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

		private const string TARGET_UXML_PATH = "Assets/FishMMO/Client/GUI/World/Target/UITarget.uxml";
		private UITKTarget targetPanel;
		private GameObject targetHost;
		private int toggles;

		/// <summary>
		/// Mounts the real target frame the way HudOverlapProbe does: its own host and document,
		/// its own markup, OnStarting run once. Hide disables the document and Show re-enables it,
		/// which clones a fresh tree and re-runs the panel's startup — the exact cycle a hover
		/// target change drives in the client.
		/// </summary>
		private void BuildTargetPanel()
		{
			VisualTreeAsset tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(TARGET_UXML_PATH);
			if (tree == null)
			{
				throw new InvalidOperationException("no target frame markup at " + TARGET_UXML_PATH);
			}

			targetHost = new GameObject("LeakProbeTarget");
			targetHost.SetActive(false);
			UIDocument targetDocument = targetHost.AddComponent<UIDocument>();
			targetDocument.panelSettings = settings;
			targetDocument.visualTreeAsset = tree;
			targetHost.SetActive(true);

			targetPanel = targetHost.AddComponent<UITKTarget>();
			targetPanel.Document = targetDocument;
			targetPanel.OnStarting();
			targetPanel.Show();
			Debug.Log($"[PlayLeakProbe] target frame mounted: root={(targetDocument.rootVisualElement != null)} panel={(targetDocument.rootVisualElement?.panel != null)} visible={targetPanel.Visible}");
		}

		private UIDocument bareDocument;
		private GameObject bareHost;

		private void BuildBareDocument()
		{
			VisualTreeAsset tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(TARGET_UXML_PATH);
			if (tree == null)
			{
				throw new InvalidOperationException("no target frame markup at " + TARGET_UXML_PATH);
			}
			bareHost = new GameObject("LeakProbeBareDocument");
			bareHost.SetActive(false);
			bareDocument = bareHost.AddComponent<UIDocument>();
			bareDocument.panelSettings = settings;
			bareDocument.visualTreeAsset = tree;
			bareHost.SetActive(true);
			Debug.Log($"[PlayLeakProbe] bare document mounted: root={(bareDocument.rootVisualElement != null)} panel={(bareDocument.rootVisualElement?.panel != null)}");
		}

		private const string LAUNCHER_UXML_PATH = "Assets/FishMMO/Client/GUI/Launcher/UILauncher.uxml";
		private GameObject launcherHost;
		private VisualElement launcherSettings;
		private bool visibleInitialised;

		/// <summary>
		/// The client launcher's own markup on the shared panel settings, with its news area filled
		/// with wrapped text the way a rendered HTML feed fills it, so opening settings reflows it.
		/// </summary>
		private void BuildLauncher()
		{
			VisualTreeAsset tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(LAUNCHER_UXML_PATH);
			if (tree == null)
			{
				throw new InvalidOperationException("no launcher markup at " + LAUNCHER_UXML_PATH);
			}
			launcherHost = new GameObject("LeakProbeLauncher");
			launcherHost.SetActive(false);
			UIDocument launcherDocument = launcherHost.AddComponent<UIDocument>();
			launcherDocument.panelSettings = settings;
			launcherDocument.visualTreeAsset = tree;
			launcherHost.SetActive(true);

			VisualElement root = launcherDocument.rootVisualElement;
			launcherSettings = root?.Q("launcher-settings");
			visibleInitialised = false;
			VisualElement news = root?.Q("launcher-news");
			if (launcherSettings == null || news == null)
			{
				throw new InvalidOperationException("launcher markup is missing #launcher-settings or #launcher-news");
			}
			for (int i = 0; i < 300; ++i)
			{
				VisualElement block = new VisualElement();
				block.AddToClassList("launcher-news__block");
				Label text = new Label($"Patch note {i}: the quick brown fox jumps over the lazy dog while the servers restart and the guild halls reopen for the evening event, bring friends and bring potions.");
				text.AddToClassList(i % 25 == 0 ? "launcher-news__h2" : "launcher-news__text");
				block.Add(text);
				news.Add(block);
			}
			Debug.Log($"[PlayLeakProbe] launcher mounted: panel={(root.panel != null)} settingsHidden={launcherSettings.ClassListContains("fish-hidden")} newsChildren={news.childCount}");
		}

		private static int ThemeRootCount()
		{
			FieldInfo field = typeof(UITKThemeManager).GetField("roots", BindingFlags.NonPublic | BindingFlags.Static);
			object set = field?.GetValue(null);
			PropertyInfo count = set?.GetType().GetProperty("Count");
			return count != null ? (int)count.GetValue(set) : -1;
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
			if (targetHost != null) { Destroy(targetHost); targetHost = null; targetPanel = null; }
			if (bareHost != null) { Destroy(bareHost); bareHost = null; bareDocument = null; }
			if (launcherHost != null) { Destroy(launcherHost); launcherHost = null; launcherSettings = null; }
			moduleMode?.Release();
			moduleMode = null;
			sheet?.ReleasePreview();
			if (host != null) Destroy(host);
			if (settings != null) Destroy(settings);
			host = null; document = null; settings = null; panel = null; sheet = null; renderer = null; character = null;
		}
	}
}
