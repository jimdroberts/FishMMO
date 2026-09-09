using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// Throwaway harness: measures the colour picker's hex field after a real layout pass.
	/// </summary>
	/// <remarks>
	/// Reported as "it just says Hex; the hex code is not available". The label draws and the
	/// input box does not, which is a layout answer, not a colour or a content one — so this
	/// mounts the panel on a real UIDocument, lets the layout settle, and writes down what every
	/// part of the field actually resolved to.
	/// </remarks>
	public static class HexFieldProbe
	{
		private const string PANEL_SETTINGS_PATH = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UXML_PATH = "Assets/Scripts/Client/GUI/Shared/ColorPicker/UIColorPicker.uxml";
		private const string REPORT_PATH = "/home/jim/Dev/FishMMO-Dev/PanelRenders/hex-field-probe.txt";
		private const int WIDTH = 1200;
		private const int HEIGHT = 900;
		private const int SETTLE_FRAMES = 40;

		private static GameObject host;
		private static UIDocument document;
		private static PanelSettings settings;
		private static RenderTexture texture;
		private static int framesWaited;

		[MenuItem("FishMMO/UI Toolkit/Probe Hex Field")]
		public static void Run()
		{
			try
			{
				texture = new RenderTexture(WIDTH, HEIGHT, 24, RenderTextureFormat.ARGB32);
				texture.Create();

				settings = UnityEngine.Object.Instantiate(
					AssetDatabase.LoadAssetAtPath<PanelSettings>(PANEL_SETTINGS_PATH));
				settings.hideFlags = HideFlags.HideAndDontSave;
				settings.targetTexture = texture;
				settings.clearColor = true;
				settings.colorClearValue = new Color(0.055f, 0.059f, 0.059f, 1.0f);

				host = new GameObject("Probe_HexField") { hideFlags = HideFlags.HideAndDontSave };
				document = host.AddComponent<UIDocument>();
				document.panelSettings = settings;
				document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UXML_PATH);

				UITKColorPicker picker = host.AddComponent<UITKColorPicker>();
				picker.Document = document;
				picker.OnStarting();
				picker.Open(new Color32(0, 115, 192, 255), _ => { });

				framesWaited = 0;
				EditorApplication.update -= Pump;
				EditorApplication.update += Pump;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[HexProbe] setup failed: {ex}");
				EditorApplication.Exit(1);
			}
		}

		private static void Pump()
		{
			try
			{
				++framesWaited;
				if (framesWaited < SETTLE_FRAMES)
				{
					document?.rootVisualElement?.MarkDirtyRepaint();
					return;
				}

				EditorApplication.update -= Pump;
				Report();
				Release();
				EditorApplication.Exit(0);
			}
			catch (Exception ex)
			{
				EditorApplication.update -= Pump;
				Debug.LogError($"[HexProbe] pump failed: {ex}");
				Release();
				EditorApplication.Exit(1);
			}
		}

		private static void Report()
		{
			VisualElement root = document.rootVisualElement;
			StringBuilder sb = new StringBuilder();

			Describe(sb, "colorpicker-side", root.Q("colorpicker-side"));

			TextField hex = root.Q<TextField>("hex-input");
			Describe(sb, "hex-input (TextField)", hex);
			sb.AppendLine($"hex-input value = '{hex?.value}'");

			if (hex != null)
			{
				Describe(sb, "  label", hex.Q(className: "unity-base-field__label"));
				Describe(sb, "  #unity-text-input", hex.Q("unity-text-input"));
				Describe(sb, "  inner text element", hex.Q("unity-text-input")?.Q(className: "unity-text-element"));

				sb.AppendLine("  full subtree:");
				Walk(sb, hex, 2);
			}

			// A plain unlabelled TextField in the same panel, as a control.
			TextField control = new TextField();
			control.AddToClassList("fish-input");
			control.value = "CONTROL";
			root.Q("colorpicker-side")?.Add(control);

			string text = sb.ToString();
			Directory.CreateDirectory(Path.GetDirectoryName(REPORT_PATH));
			File.WriteAllText(REPORT_PATH, text);
			Debug.Log("[HexProbe]\n" + text);
		}

		private static void Describe(StringBuilder sb, string label, VisualElement e)
		{
			if (e == null)
			{
				sb.AppendLine($"{label}: MISSING");
				return;
			}
			sb.AppendLine($"{label}: rect={e.layout.width:0.##}x{e.layout.height:0.##} " +
				$"minW={e.resolvedStyle.minWidth} flexGrow={e.resolvedStyle.flexGrow} " +
				$"display={e.resolvedStyle.display} visible={e.visible}");
		}

		private static void Walk(StringBuilder sb, VisualElement e, int depth)
		{
			foreach (VisualElement child in e.Children())
			{
				sb.AppendLine($"{new string(' ', depth * 2)}<{child.GetType().Name}> " +
					$"name='{child.name}' classes='{string.Join(",", child.GetClasses())}' " +
					$"rect={child.layout.width:0.##}x{child.layout.height:0.##}");
				Walk(sb, child, depth + 1);
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
