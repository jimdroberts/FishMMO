using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// Harness: puts a line longer than the chat input into it and reports whether the field keeps
	/// its width (so Unity's caret-follow scroll can keep the end of the line in view), and whether
	/// the input sits outside the Tab focus ring.
	/// </summary>
	public static class ChatInputProbe
	{
		private static readonly string OUTPUT_DIR = ProbeOutput.Path("PanelRenders");
		private const string PANEL_SETTINGS_PATH = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UXML_PATH = "Assets/Scripts/Client/GUI/World/Chat/UIChat.uxml";
		private const int WIDTH = 1200;
		private const int HEIGHT = 900;
		private const int SETTLE_FRAMES = 30;

		private static GameObject host;
		private static UIDocument document;
		private static PanelSettings settings;
		private static RenderTexture texture;
		private static int framesWaited;
		private static int stage;

		[DashboardTool(DashboardToolAttribute.UITests, "Chat Input Probe", Section = "Renders", Order = 8)]
		public static void Run()
		{
			try
			{
				Directory.CreateDirectory(OUTPUT_DIR);
				Seed.All();
				ClientSettings.EnsureLoaded();

				texture = new RenderTexture(WIDTH, HEIGHT, 24, RenderTextureFormat.ARGB32);
				texture.Create();

				settings = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(PANEL_SETTINGS_PATH));
				settings.hideFlags = HideFlags.HideAndDontSave;
				settings.targetTexture = texture;
				settings.clearColor = true;
				settings.colorClearValue = new Color(0.055f, 0.059f, 0.059f, 1.0f);

				host = new GameObject("Probe_ChatInput") { hideFlags = HideFlags.HideAndDontSave };
				document = host.AddComponent<UIDocument>();
				document.panelSettings = settings;
				document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UXML_PATH);

				Panels.Chat(host, document);

				stage = 0;
				framesWaited = 0;
				EditorApplication.update -= Pump;
				EditorApplication.update += Pump;
			}
			catch (Exception ex)
			{
				EditorApplication.update -= Pump;
				Debug.LogError($"[ChatInputProbe] setup failed: {ex}");
				EditorAutomation.Finish(1);
			}
		}

		private static void Pump()
		{
			try
			{
				++framesWaited;
				document?.rootVisualElement?.MarkDirtyRepaint();
				if (framesWaited < SETTLE_FRAMES)
				{
					return;
				}
				framesWaited = 0;

				TextField input = document.rootVisualElement.Q<TextField>("chat-input");
				switch (stage)
				{
					case 0:
						Report("idle", input);
						/* Headless edit-mode panels never process queued focus or key events, so the
						 * line is set directly. What this measures is whether the FIELD keeps its
						 * width with a long line in it: Unity's caret-follow scroll runs against the
						 * field's own width, so a field that grows with its text hides the caret past
						 * the panel edge however well the scroll works. */
						input.value = "The quick brown fox jumps over the lazy dog while the tide comes in at Sunken Cathedral END";
						break;
					case 1:
						Report("long", input);
						Debug.Log($"[ChatInputProbe] tabIndex field={input.tabIndex} inner={input.Q("unity-text-input")?.tabIndex}" +
							$" (below zero = outside the Tab focus ring)");
						Capture(input, "UIChat-input-long.png");
						break;
					case 2:
						EditorApplication.update -= Pump;
						Release();
						EditorAutomation.Finish(0);
						return;
				}
				++stage;
			}
			catch (Exception ex)
			{
				EditorApplication.update -= Pump;
				Debug.LogError($"[ChatInputProbe] pump failed: {ex}");
				Release();
				EditorAutomation.Finish(1);
			}
		}

		private static void Report(string label, TextField input)
		{
			VisualElement inner = input.Q("unity-text-input");
			TextElement text = inner?.Q<TextElement>();
			Debug.Log($"[ChatInputProbe] ({label}) value.len={input.value?.Length}" +
				$" cursor={input.cursorIndex} field={input.worldBound} inner={inner?.worldBound}" +
				$" innerOverflow={inner?.style.overflow} innerDir={inner?.resolvedStyle.flexDirection}");
			if (text != null)
			{
				Debug.Log($"[ChatInputProbe] ({label}) text={text.worldBound} layout={text.layout}" +
					$" whiteSpace={text.resolvedStyle.whiteSpace} translate={text.resolvedStyle.translate}" +
					$" shrink={text.resolvedStyle.flexShrink} grow={text.resolvedStyle.flexGrow}" +
					$" overflowText={text.resolvedStyle.textOverflow} classes={string.Join(".", text.GetClasses())}" +
					$"");
				Debug.Log($"[ChatInputProbe] ({label}) cursorPos={input.textSelection.cursorPosition}" +
					$" measured={text.MeasureTextSize(input.value ?? string.Empty, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined)}");
			}
			VisualElement row = input.parent;
			VisualElement chatRoot = input.panel?.visualTree.Q("chat-root");
			Debug.Log($"[ChatInputProbe] ({label}) row={row?.worldBound} rowShrink={row?.resolvedStyle.flexShrink}" +
				$" fieldShrink={input.resolvedStyle.flexShrink} fieldGrow={input.resolvedStyle.flexGrow} fieldMinW={input.resolvedStyle.minWidth}" +
				$" innerShrink={inner?.resolvedStyle.flexShrink} innerGrow={inner?.resolvedStyle.flexGrow}" +
				$" root={chatRoot?.worldBound}");
			FieldInfo offset = inner?.GetType().GetField("scrollOffset", BindingFlags.Instance | BindingFlags.NonPublic);
			Debug.Log($"[ChatInputProbe] ({label}) scrollOffset={(offset != null ? offset.GetValue(inner) : "n/a")}");
		}

		private static void Capture(VisualElement element, string fileName)
		{
			Rect b = element.parent.worldBound;
			int x = Mathf.Clamp(Mathf.FloorToInt(b.xMin) - 8, 0, WIDTH - 1);
			int w = Mathf.Clamp(Mathf.CeilToInt(b.width) + 16, 1, WIDTH - x);
			int top = Mathf.Clamp(Mathf.FloorToInt(b.yMin) - 8, 0, HEIGHT - 1);
			int h = Mathf.Clamp(Mathf.CeilToInt(b.height) + 16, 1, HEIGHT - top);
			int y = HEIGHT - top - h;

			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = texture;
			try
			{
				Texture2D image = new Texture2D(w, h, TextureFormat.RGBA32, false);
				image.ReadPixels(new Rect(x, y, w, h), 0, 0);
				image.Apply();
				File.WriteAllBytes(Path.Combine(OUTPUT_DIR, fileName), image.EncodeToPNG());
				UnityEngine.Object.DestroyImmediate(image);
				Debug.Log($"[ChatInputProbe] wrote {fileName}");
			}
			finally
			{
				RenderTexture.active = previous;
			}
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
