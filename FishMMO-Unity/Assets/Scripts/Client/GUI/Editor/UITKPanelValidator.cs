using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.Client.Editor
{
	/// <summary>
	/// Batch-mode validation and preview rendering for every UI Toolkit panel in the project.
	/// </summary>
	/// <remarks>
	/// Static analysis can confirm that a <c>Q&lt;T&gt;("name")</c> matches an element in the UXML
	/// and that every USS class resolves, but it cannot confirm that Unity's own importers accept
	/// the files, that a stylesheet has no syntax error, or that a panel actually lays out to a
	/// non-empty rectangle. Those need the editor, which is what this runs in.
	///
	/// Rendering goes through <see cref="PanelSettings.targetTexture"/>: a panel pointed at a
	/// RenderTexture draws into it during the normal UI Toolkit repaint, and the result is read
	/// back to a PNG. That is the only supported way to capture a runtime panel without entering
	/// play mode, and it needs a real graphics device — so the caller must not pass -nographics.
	/// </remarks>
	public static class UITKPanelValidator
	{
		/// <summary>Where preview images are written.</summary>
		private const string OUTPUT_DIR = "Assets/UITKValidationImages";

		/// <summary>Panel Settings used by every panel in the project.</summary>
		private const string PANEL_SETTINGS_PATH = "Assets/UI Toolkit/PanelSettings.asset";

		/// <summary>Root the panel UXML files live under.</summary>
		private const string GUI_ROOT = "Assets/Scripts/Client/GUI";

		/// <summary>Capture size, matching the PanelSettings reference resolution.</summary>
		private const int CAPTURE_WIDTH = 1200;
		private const int CAPTURE_HEIGHT = 800;

		/// <summary>Collected problems, reported together at the end.</summary>
		private static readonly List<string> problems = new List<string>();

		/// <summary>Collected successes, for the summary line.</summary>
		private static readonly List<string> rendered = new List<string>();

		/// <summary>
		/// Validates every UXML and USS without rendering. Safe under -nographics.
		/// </summary>
		[MenuItem("FishMMO/UI Toolkit/Validate Panels")]
		public static void Validate()
		{
			problems.Clear();
			Log("── UI Toolkit asset validation ──");

			int uxmlCount = 0;
			int ussCount = 0;

			foreach (string path in AssetPaths("*.uss"))
			{
				++ussCount;
				StyleSheet sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
				if (sheet == null)
				{
					problems.Add($"USS failed to import: {path}");
				}
			}

			/*
			 * Keyword cursors are editor-only. A runtime panel can only change the cursor from a
			 * texture, and a keyword one makes UIElements and the EventSystem each log
			 * "Runtime cursors other than the default cursor need to be defined using a texture."
			 * on every frame the pointer is over the element. The warning names no file, so the
			 * offending rule is genuinely hard to find by hand — which is why it is checked here.
			 */
			foreach (string path in AssetPaths("*.uss"))
			{
				string[] lines = File.ReadAllLines(path);
				for (int i = 0; i < lines.Length; ++i)
				{
					string line = lines[i].Trim();
					if (!line.StartsWith("cursor:", StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
					// The texture form is the supported one and is left alone.
					if (line.Contains("url("))
					{
						continue;
					}
					problems.Add($"keyword cursor at runtime (use a texture or remove): {path}:{i + 1} — {line}");
				}
			}

			foreach (string path in AssetPaths("*.uxml"))
			{
				++uxmlCount;
				VisualTreeAsset tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
				if (tree == null)
				{
					problems.Add($"UXML failed to import: {path}");
					continue;
				}

				VisualElement root;
				try
				{
					root = tree.Instantiate();
				}
				catch (Exception ex)
				{
					problems.Add($"UXML failed to instantiate: {path} — {ex.Message}");
					continue;
				}

				if (root.childCount == 0)
				{
					problems.Add($"UXML instantiates to an empty tree: {path}");
				}

				/* A panel that declares no stylesheet is not necessarily broken, but every panel
				 * in this project is supposed to load the theme first, and a missed <Style> is
				 * invisible until the panel renders unthemed. */
				List<StyleSheet> sheets = CollectStyleSheets(root);
				if (sheets.Count == 0)
				{
					problems.Add($"UXML loads no stylesheet: {path}");
				}
				else if (!sheets.Any(s => s != null && s.name.Contains("FishMMO-Theme")))
				{
					problems.Add($"UXML does not load FishMMO-Theme.uss: {path}");
				}
			}

			Log($"UXML checked: {uxmlCount}");
			Log($"USS checked:  {ussCount}");
			Report("validation");
		}

		/// <summary>
		/// Validates, then renders a preview PNG for every panel.
		/// </summary>
		/// <remarks>
		/// Runs as a state machine driven by <c>EditorApplication.update</c> rather than as a
		/// straight loop. A UI Toolkit panel only lays out and repaints between editor frames, and
		/// a method invoked by <c>-executeMethod</c> holds the main thread for its whole duration —
		/// so a loop that rendered every panel in one call would read back the texture before a
		/// single frame had been drawn and write 45 identical blank images. Yielding back to the
		/// editor between panels is what makes the capture real.
		///
		/// The caller must therefore <b>not</b> pass -quit; this exits the editor itself once the
		/// queue is drained.
		/// </remarks>
		[MenuItem("FishMMO/UI Toolkit/Render Panel Previews")]
		public static void RenderPreviews()
		{
			problems.Clear();
			rendered.Clear();

			Log("── UI Toolkit panel previews ──");

			if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
			{
				problems.Add("No graphics device; previews cannot be rendered. Re-run without -nographics.");
				Report("render");
				return;
			}
			Log($"graphics device: {SystemInfo.graphicsDeviceType} ({SystemInfo.graphicsDeviceName})");

			Directory.CreateDirectory(OUTPUT_DIR);

			panelSettingsSource = AssetDatabase.LoadAssetAtPath<PanelSettings>(PANEL_SETTINGS_PATH);
			if (panelSettingsSource == null)
			{
				problems.Add($"PanelSettings not found at {PANEL_SETTINGS_PATH}");
				Report("render");
				return;
			}

			queue = new Queue<string>(AssetPaths("*.uxml"));
			Log($"queued {queue.Count} panels");

			EditorApplication.update -= Pump;
			EditorApplication.update += Pump;
		}

		/// <summary>Panels still to render.</summary>
		private static Queue<string> queue;

		/// <summary>The project PanelSettings previews are cloned from.</summary>
		private static PanelSettings panelSettingsSource;

		/// <summary>The panel currently being rendered, or null between panels.</summary>
		private static PendingCapture pending;

		/// <summary>Editor frames elapsed since the current panel was mounted.</summary>
		private static int framesWaited;

		/// <summary>
		/// Frames to let a panel settle before reading it back.
		/// </summary>
		/// <remarks>
		/// One frame is enough for a flat panel, but ScrollViews measure their content and fire
		/// geometry callbacks that reflow on the following frame, so a single-frame capture caught
		/// several panels mid-layout with their lists still collapsed.
		/// </remarks>
		private const int SETTLE_FRAMES = 6;

		/// <summary>One panel mounted and waiting to be captured.</summary>
		private sealed class PendingCapture
		{
			public string Name;
			public GameObject Host;
			public UIDocument Document;
			public PanelSettings Settings;
			public RenderTexture Texture;
		}

		/// <summary>
		/// Advances the render queue by one editor frame.
		/// </summary>
		private static void Pump()
		{
			try
			{
				if (pending != null)
				{
					++framesWaited;
					if (framesWaited < SETTLE_FRAMES)
					{
						pending.Document.rootVisualElement?.MarkDirtyRepaint();
						return;
					}
					Capture(pending);
					Teardown(pending);
					pending = null;
					return;
				}

				if (queue == null || queue.Count == 0)
				{
					EditorApplication.update -= Pump;
					AssetDatabase.Refresh();
					Log($"rendered: {rendered.Count}");
					Report("render");
					return;
				}

				pending = Mount(queue.Dequeue());
				framesWaited = 0;
			}
			catch (Exception ex)
			{
				problems.Add($"render pump: {ex.GetType().Name} — {ex.Message}");
				EditorApplication.update -= Pump;
				Report("render");
			}
		}

		/// <summary>
		/// Builds a document for one UXML and mounts it so the editor will lay it out.
		/// </summary>
		/// <param name="path">Asset path of the UXML.</param>
		/// <returns>The mounted capture, or null when the asset could not be loaded.</returns>
		private static PendingCapture Mount(string path)
		{
			string name = Path.GetFileNameWithoutExtension(path);
			VisualTreeAsset tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
			if (tree == null)
			{
				problems.Add($"{name}: UXML failed to load");
				return null;
			}

			RenderTexture rt = new RenderTexture(CAPTURE_WIDTH, CAPTURE_HEIGHT, 24, RenderTextureFormat.ARGB32)
			{
				name = $"UITKPreview_{name}",
			};
			rt.Create();

			/* The project's own PanelSettings is cloned rather than reused: assigning a
			 * targetTexture to the shared asset would redirect every live panel in the editor
			 * into this RenderTexture and dirty a checked-in asset. */
			PanelSettings settings = UnityEngine.Object.Instantiate(panelSettingsSource);
			settings.hideFlags = HideFlags.HideAndDontSave;
			settings.targetTexture = rt;
			settings.clearColor = true;
			settings.colorClearValue = new Color(0.055f, 0.059f, 0.059f, 1.0f); // --ob900

			GameObject host = new GameObject($"UITKPreview_{name}") { hideFlags = HideFlags.HideAndDontSave };
			UIDocument document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			document.visualTreeAsset = tree;

			VisualElement root = document.rootVisualElement;
			if (root != null)
			{
				// Several panels start hidden; a preview of a hidden panel is a blank image.
				RevealHiddenSubtrees(root);
				root.MarkDirtyRepaint();
			}
			else
			{
				problems.Add($"{name}: document produced no root element");
			}

			return new PendingCapture
			{
				Name = name,
				Host = host,
				Document = document,
				Settings = settings,
				Texture = rt,
			};
		}

		/// <summary>
		/// Reads a mounted panel back into a PNG.
		/// </summary>
		/// <param name="capture">The mounted panel.</param>
		private static void Capture(PendingCapture capture)
		{
			if (capture == null)
			{
				return;
			}

			VisualElement root = capture.Document != null ? capture.Document.rootVisualElement : null;
			if (root != null)
			{
				Rect content = MeasureContent(root);
				if (content.width <= 1.0f || content.height <= 1.0f)
				{
					problems.Add($"{capture.Name}: laid out to an empty rectangle ({content.width:0}x{content.height:0})");
				}
			}

			Texture2D shot = new Texture2D(CAPTURE_WIDTH, CAPTURE_HEIGHT, TextureFormat.RGBA32, false);
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = capture.Texture;
			shot.ReadPixels(new Rect(0, 0, CAPTURE_WIDTH, CAPTURE_HEIGHT), 0, 0);
			shot.Apply();
			RenderTexture.active = previous;

			if (IsBlank(shot))
			{
				problems.Add($"{capture.Name}: rendered blank (no visible pixels)");
			}

			File.WriteAllBytes(Path.Combine(OUTPUT_DIR, capture.Name + ".png"), shot.EncodeToPNG());
			rendered.Add(capture.Name);
			UnityEngine.Object.DestroyImmediate(shot);
		}

		/// <summary>
		/// Destroys the temporary objects a capture created.
		/// </summary>
		/// <param name="capture">The capture to release.</param>
		private static void Teardown(PendingCapture capture)
		{
			if (capture == null)
			{
				return;
			}
			if (capture.Host != null) UnityEngine.Object.DestroyImmediate(capture.Host);
			if (capture.Settings != null) UnityEngine.Object.DestroyImmediate(capture.Settings);
			if (capture.Texture != null)
			{
				capture.Texture.Release();
				UnityEngine.Object.DestroyImmediate(capture.Texture);
			}
		}

		/// <summary>
		/// Clears display:none on the panel root's own children so a panel that starts hidden
		/// still produces a meaningful preview.
		/// </summary>
		/// <param name="root">Panel root.</param>
		private static void RevealHiddenSubtrees(VisualElement root)
		{
			foreach (VisualElement child in root.Children())
			{
				if (child.resolvedStyle.display == DisplayStyle.None)
				{
					child.style.display = DisplayStyle.Flex;
				}
			}
		}

		/// <summary>
		/// Returns the union of the laid-out rectangles of the root's descendants.
		/// </summary>
		/// <param name="root">Panel root.</param>
		/// <returns>The bounding rectangle of visible content.</returns>
		private static Rect MeasureContent(VisualElement root)
		{
			Rect union = Rect.zero;
			bool any = false;
			foreach (VisualElement child in root.Children())
			{
				Rect r = child.worldBound;
				if (float.IsNaN(r.width) || float.IsNaN(r.height) || r.width <= 0.0f || r.height <= 0.0f)
				{
					continue;
				}
				union = any ? Rect.MinMaxRect(
					Mathf.Min(union.xMin, r.xMin), Mathf.Min(union.yMin, r.yMin),
					Mathf.Max(union.xMax, r.xMax), Mathf.Max(union.yMax, r.yMax)) : r;
				any = true;
			}
			return any ? union : Rect.zero;
		}

		/// <summary>
		/// True when every pixel matches the clear colour.
		/// </summary>
		/// <param name="shot">Captured image.</param>
		/// <returns>True when nothing was drawn.</returns>
		private static bool IsBlank(Texture2D shot)
		{
			Color32[] pixels = shot.GetPixels32();
			Color32 first = pixels[0];
			for (int i = 1; i < pixels.Length; i += 7)
			{
				Color32 p = pixels[i];
				if (Mathf.Abs(p.r - first.r) > 2 || Mathf.Abs(p.g - first.g) > 2 || Mathf.Abs(p.b - first.b) > 2)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Collects every stylesheet attached anywhere in a tree.
		/// </summary>
		/// <param name="root">Root to walk.</param>
		/// <returns>All stylesheets found.</returns>
		private static List<StyleSheet> CollectStyleSheets(VisualElement root)
		{
			List<StyleSheet> found = new List<StyleSheet>();
			void Walk(VisualElement e)
			{
				for (int i = 0; i < e.styleSheets.count; ++i)
				{
					found.Add(e.styleSheets[i]);
				}
				foreach (VisualElement child in e.Children())
				{
					Walk(child);
				}
			}
			Walk(root);
			return found;
		}

		/// <summary>
		/// Asset paths of every file of a pattern under the GUI root, sorted.
		/// </summary>
		/// <param name="pattern">Filename pattern, e.g. "*.uxml".</param>
		/// <returns>Asset-relative paths.</returns>
		private static IEnumerable<string> AssetPaths(string pattern)
		{
			return Directory.GetFiles(GUI_ROOT, pattern, SearchOption.AllDirectories)
				.Select(p => p.Replace('\\', '/'))
				.OrderBy(p => p);
		}

		/// <summary>
		/// Prints the collected problems and sets the batch exit code.
		/// </summary>
		/// <param name="stage">Label for the summary line.</param>
		private static void Report(string stage)
		{
			if (problems.Count == 0)
			{
				Log($"RESULT {stage}: OK — no problems found");
				if (Application.isBatchMode)
				{
					EditorApplication.Exit(0);
				}
				return;
			}

			StringBuilder sb = new StringBuilder();
			sb.AppendLine($"RESULT {stage}: {problems.Count} problem(s)");
			foreach (string p in problems)
			{
				sb.AppendLine("  - " + p);
			}
			Log(sb.ToString());

			if (Application.isBatchMode)
			{
				EditorApplication.Exit(1);
			}
		}

		/// <summary>
		/// Writes a line that survives batch-mode log filtering.
		/// </summary>
		/// <param name="message">Message to write.</param>
		private static void Log(string message)
		{
			Debug.Log("[UITKValidator] " + message);
			Console.WriteLine("[UITKValidator] " + message);
		}
	}
}
