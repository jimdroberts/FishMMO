using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// Throwaway harness: captures the chat panel with mock messages and prints the metrics that
	/// decide how big the log reads — the resolved font size of each half of a line, the height of
	/// a row, and the gap between rows.
	/// </summary>
	/// <remarks>
	/// <para>The message rows are built in C# by <see cref="UITKChat"/>, but every number that
	/// sizes them lives in <c>UIChat.uss</c>, so the panel can be retuned without touching the
	/// panel. That also means a font or padding change is checkable by reading the USS — which is
	/// exactly why this probe prints what the layout RESOLVED rather than only writing a PNG. A
	/// viewer looking at a small dark panel cannot tell 12px from 16px with confidence, and the
	/// PNG is far too small to measure by eye.</para>
	/// <para>Messages come from <see cref="Panels.Chat"/>, the same populator the all-panels sweep
	/// uses, so this capture stays comparable with that set.</para>
	/// <para><b>Two captures, not one.</b> The second is taken after moving the Chat Font Size
	/// setting through the same path the options slider does, which is what proves the two halves
	/// that a PNG cannot show: that the setting reaches the panel at all, and that it resizes the
	/// rows <i>already on screen</i> rather than only the next one appended. A size that only
	/// applied to new rows would look perfectly correct in a single capture.</para>
	/// </remarks>
	public static class ChatRenderProbe
	{
		private const string OUTPUT_DIR = "/home/jim/Dev/FishMMO-Dev/PanelRenders";
		private const string PANEL_SETTINGS_PATH = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UXML_PATH = "Assets/Scripts/Client/GUI/World/Chat/UIChat.uxml";
		private const string OUTPUT_NAME = "UIChat-messages.png";
		private const string OUTPUT_NAME_ENLARGED = "UIChat-messages-16pt.png";
		private const string CHAT_INPUT_NAME = "chat-input";
		private const string INPUT_TEXT_ELEMENT_NAME = "unity-text-input";
		private const int WIDTH = 1200;
		private const int HEIGHT = 900;
		private const int SETTLE_FRAMES = 40;
		private const int CROP_MARGIN = 12;

		/// <summary>The size the second capture is taken at — the top of the slider's range less four.</summary>
		private const float ENLARGED_SIZE = 16.0f;

		private static GameObject host;
		private static UIDocument document;
		private static PanelSettings settings;
		private static RenderTexture texture;
		private static int framesWaited;
		private static int stage;

		[MenuItem("FishMMO/UI Toolkit/Render Chat Messages")]
		public static void Render()
		{
			try
			{
				Directory.CreateDirectory(OUTPUT_DIR);
				Seed.All();

				/* The setting is stored in the configuration, which nothing has created yet — a
				 * Set against a null store is a silent no-op, so the enlarged capture would come
				 * back at 12 and look like the setting not working. This is the same call the
				 * options panel makes when it opens. */
				ClientSettings.EnsureLoaded();

				texture = new RenderTexture(WIDTH, HEIGHT, 24, RenderTextureFormat.ARGB32);
				texture.Create();

				settings = UnityEngine.Object.Instantiate(
					AssetDatabase.LoadAssetAtPath<PanelSettings>(PANEL_SETTINGS_PATH));
				settings.hideFlags = HideFlags.HideAndDontSave;
				settings.targetTexture = texture;
				settings.clearColor = true;
				settings.colorClearValue = new Color(0.055f, 0.059f, 0.059f, 1.0f);

				host = new GameObject("Render_UIChat") { hideFlags = HideFlags.HideAndDontSave };
				document = host.AddComponent<UIDocument>();
				document.panelSettings = settings;
				document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UXML_PATH);

				Panels.Chat(host, document);

				document.rootVisualElement?.MarkDirtyRepaint();

				stage = 0;
				framesWaited = 0;
				EditorApplication.update -= Pump;
				EditorApplication.update += Pump;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[ChatProbe] setup failed: {ex}");
				EditorApplication.Exit(1);
			}
		}

		private static void Pump()
		{
			try
			{
				++framesWaited;

				/* Capture at the stored size first, then at the enlarged one. Each stage gets a
				 * full settle so the resize above it has been laid out before anything measures it. */
				if (stage == 0 && framesWaited >= SETTLE_FRAMES)
				{
					Report("default");
					Capture(OUTPUT_NAME);

					stage = 1;
					framesWaited = 0;

					/* Straight through the public setter, which is what the options slider calls —
					 * so this exercises the OnChanged subscription rather than a private shortcut. */
					Debug.Log($"[ChatProbe] setting font size to {ENLARGED_SIZE:0.##}");
					ClientChatSettings.SetFontSize(ENLARGED_SIZE);
					return;
				}

				if (stage == 1 && framesWaited >= SETTLE_FRAMES)
				{
					Report("enlarged");
					Capture(OUTPUT_NAME_ENLARGED);

					ClientChatSettings.SetFontSize(ClientChatSettings.DefaultFontSize);

					EditorApplication.update -= Pump;
					Release();
					EditorApplication.Exit(0);
					return;
				}

				document?.rootVisualElement?.MarkDirtyRepaint();
			}
			catch (Exception ex)
			{
				EditorApplication.update -= Pump;
				Debug.LogError($"[ChatProbe] pump failed: {ex}");
				Release();
				EditorApplication.Exit(1);
			}
		}

		/// <summary>
		/// Prints the resolved geometry of the log, which is the part a PNG cannot be asked about.
		/// </summary>
		private static void Report(string label)
		{
			VisualElement root = document?.rootVisualElement;
			if (root == null) { return; }

			VisualElement panel = root.Q("chat-root");
			VisualElement log = root.Q("chat-messages");
			VisualElement scroll = root.Q("chat-scroll");

			Debug.Log($"[ChatProbe] ({label}) setting={ClientChatSettings.FontSize:0.##}pt" +
				$" panel={(panel != null ? panel.worldBound.ToString() : "MISSING")}" +
				$" scroll={(scroll != null ? scroll.worldBound.ToString() : "MISSING")}" +
				$" padding(scroll)={Pad(scroll)}");

			TextField input = root.Q<TextField>(CHAT_INPUT_NAME);
			VisualElement inputText = input?.Q(INPUT_TEXT_ELEMENT_NAME);
			Debug.Log($"[ChatProbe] ({label})   inputFont={input?.resolvedStyle.fontSize:0.##}" +
				$" inputTextFont={inputText?.resolvedStyle.fontSize:0.##}" +
				$" inputTextH={inputText?.resolvedStyle.height:0.##}");

			if (log == null)
			{
				Debug.LogWarning("[ChatProbe] chat-messages not found");
				return;
			}

			int rows = 0;
			float previousBottom = float.NaN;
			int shown = 0;
			int namedShown = 0;
			float previousNamedBottom = float.NaN;

			foreach (VisualElement row in log.Children())
			{
				++rows;

				Label name = row.Q<Label>(className: "chat-message__name");
				Label text = row.Q<Label>(className: "chat-message__text");

				// The visible rows are the ones that matter; the log scrolls.
				if (shown < 8 && row.resolvedStyle.display != DisplayStyle.None)
				{
					float gap = float.IsNaN(previousBottom)
						? 0.0f
						: row.worldBound.yMin - previousBottom;

					Debug.Log($"[ChatProbe] ({label})   row[{shown}] h={row.resolvedStyle.height:0.##}" +
						$" gapAbove={gap:0.##}" +
						$" marginBottom={row.resolvedStyle.marginBottom:0.##}" +
						$" textFont={text?.resolvedStyle.fontSize:0.##}" +
						$" textH={text?.resolvedStyle.height:0.##}" +
						$" text='{Trim(text?.text)}'");

					/* What a line actually costs, split into the parts that could be responsible:
					 * the label's own box, its padding and margin, and the row's. */
					if (shown < 2)
					{
						Debug.Log($"[ChatProbe] ({label})     rowPad={Pad(row)}" +
							$" textPad={Pad(text)}" +
							$" textMargin={Margin(text)}");
					}

					previousBottom = row.worldBound.yMax;
					++shown;
				}

				/* The sender column is only present on rows that have one — the welcome block and
				 * the command list are sender-less to the last line, and they are the first thing
				 * in the log. Measuring only the first few rows would therefore never see a name
				 * label at all, which is how a rule could be changed and have nothing report on it. */
				if (namedShown < 3 && name != null && row.resolvedStyle.display != DisplayStyle.None)
				{
					float gap = float.IsNaN(previousNamedBottom)
						? 0.0f
						: row.worldBound.yMin - previousNamedBottom;

					Debug.Log($"[ChatProbe] ({label})   named[{namedShown}] h={row.resolvedStyle.height:0.##}" +
						$" gapAbove={gap:0.##}" +
						$" nameFont={name.resolvedStyle.fontSize:0.##}" +
						$" nameH={name.resolvedStyle.height:0.##}" +
						$" namePad={Pad(name)}" +
						$" nameMargin={Margin(name)}" +
						$" name='{Trim(name.text)}'");

					previousNamedBottom = row.worldBound.yMax;
					++namedShown;
				}
			}

			Debug.Log($"[ChatProbe] ({label}) rows={rows}" +
				$" pitch={(shown > 1 && rows > 0 ? (previousBottom - log.worldBound.yMin) / Mathf.Max(1, shown - 1) : 0.0f):0.##}");

			Button tab = root.Q<Button>(className: "chat-tab");
			Debug.Log($"[ChatProbe] ({label}) tabFont={tab?.resolvedStyle.fontSize:0.##}");
		}

		private static string Trim(string s)
		{
			if (string.IsNullOrEmpty(s)) { return string.Empty; }
			return s.Length <= 34 ? s : s.Substring(0, 34) + "…";
		}

		private static string Pad(VisualElement e)
		{
			if (e == null) { return "?"; }
			return $"{e.resolvedStyle.paddingLeft:0.##},{e.resolvedStyle.paddingTop:0.##}," +
				$"{e.resolvedStyle.paddingRight:0.##},{e.resolvedStyle.paddingBottom:0.##}";
		}

		private static string Margin(VisualElement e)
		{
			if (e == null) { return "?"; }
			return $"{e.resolvedStyle.marginLeft:0.##},{e.resolvedStyle.marginTop:0.##}," +
				$"{e.resolvedStyle.marginRight:0.##},{e.resolvedStyle.marginBottom:0.##}";
		}

		private static void Capture(string fileName)
		{
			Rect crop = PanelRect();
			int x = Mathf.Clamp(Mathf.FloorToInt(crop.x), 0, WIDTH - 1);
			int y = Mathf.Clamp(Mathf.FloorToInt(crop.y), 0, HEIGHT - 1);
			int w = Mathf.Clamp(Mathf.CeilToInt(crop.width), 1, WIDTH - x);
			int h = Mathf.Clamp(Mathf.CeilToInt(crop.height), 1, HEIGHT - y);

			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = texture;
			try
			{
				Texture2D image = new Texture2D(w, h, TextureFormat.RGBA32, false);
				image.ReadPixels(new Rect(x, y, w, h), 0, 0);
				image.Apply();
				File.WriteAllBytes(Path.Combine(OUTPUT_DIR, fileName), image.EncodeToPNG());
				UnityEngine.Object.DestroyImmediate(image);
				Debug.Log($"[ChatProbe] wrote {fileName} ({w}x{h} from {x},{y})");
			}
			finally
			{
				RenderTexture.active = previous;
			}
		}

		/// <summary>
		/// The panel root's bounds in the render target's pixel space, padded and flipped —
		/// <c>worldBound</c> is top-left origin, <c>ReadPixels</c> is bottom-left.
		/// </summary>
		private static Rect PanelRect()
		{
			VisualElement root = document?.rootVisualElement?.Q("chat-root");
			Rect bound = root?.worldBound ?? default;

			if (root == null || bound.width < 1.0f || bound.height < 1.0f)
			{
				Debug.LogWarning("[ChatProbe] chat-root has no layout; capturing the full frame");
				return new Rect(0, 0, WIDTH, HEIGHT);
			}

			float left = Mathf.Max(0.0f, bound.xMin - CROP_MARGIN);
			float right = Mathf.Min(WIDTH, bound.xMax + CROP_MARGIN);
			float top = Mathf.Max(0.0f, bound.yMin - CROP_MARGIN);
			float bottom = Mathf.Min(HEIGHT, bound.yMax + CROP_MARGIN);

			return new Rect(left, HEIGHT - bottom, right - left, bottom - top);
		}

		private static void Release()
		{
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
		}
	}
}
