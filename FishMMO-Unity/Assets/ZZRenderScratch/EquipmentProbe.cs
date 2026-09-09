using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// Throwaway harness: captures the equipment panel with a character on it.
	/// </summary>
	/// <remarks>
	/// <see cref="AllPanelsRender"/> cannot show this panel populated. Its shared
	/// <c>Character&lt;T&gt;</c> helper sets the character and only then calls
	/// <c>OnStarting</c>, so <c>OnPostSetCharacter</c> writes into element references the panel
	/// has not cached yet and every one of its writes is dropped — which is why the capture comes
	/// back with empty slots, an empty attribute list and a status bar reading "—". The panel
	/// itself is fine: the real client covers that order through
	/// <c>UITKCharacterControl.OnAfterStarting</c>, which the harness never calls because it never
	/// calls <c>Show</c>.
	/// <para>
	/// This probe mounts the panel the other way round — tree first, character second — which is
	/// the order a panel that is already open sees, and is enough to draw the real thing.
	/// </para>
	/// </remarks>
	public static class EquipmentProbe
	{
		private const string OUTPUT_DIR = "/home/jim/Dev/FishMMO-Dev/PanelRenders";
		private const string PANEL_SETTINGS_PATH = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UXML_PATH = "Assets/Scripts/Client/GUI/World/Equipment/UIEquipment.uxml";
		private const string OUTPUT_NAME = "UIEquipment-populated.png";
		private const int WIDTH = 1200;
		private const int HEIGHT = 900;
		private const int SETTLE_FRAMES = 40;

		private static GameObject host;
		private static UIDocument document;
		private static PanelSettings settings;
		private static RenderTexture texture;
		private static int framesWaited;

		[MenuItem("FishMMO/UI Toolkit/Render Equipment Populated")]
		public static void Render()
		{
			try
			{
				Directory.CreateDirectory(OUTPUT_DIR);
				Seed.All();

				texture = new RenderTexture(WIDTH, HEIGHT, 24, RenderTextureFormat.ARGB32);
				texture.Create();

				settings = UnityEngine.Object.Instantiate(
					AssetDatabase.LoadAssetAtPath<PanelSettings>(PANEL_SETTINGS_PATH));
				settings.hideFlags = HideFlags.HideAndDontSave;
				settings.targetTexture = texture;
				settings.clearColor = true;
				settings.colorClearValue = new Color(0.055f, 0.059f, 0.059f, 1.0f);

				host = new GameObject("Render_UIEquipment") { hideFlags = HideFlags.HideAndDontSave };
				document = host.AddComponent<UIDocument>();
				document.panelSettings = settings;
				document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UXML_PATH);

				PlayerCharacter character = Rig.Build(host);

				UITKEquipment panel = host.AddComponent<UITKEquipment>();
				panel.Document = document;

				// Tree first. This is the whole point of the probe; see the class remarks.
				panel.OnStarting();
				panel.SetCharacter(character);

				document.rootVisualElement?.MarkDirtyRepaint();

				framesWaited = 0;
				EditorApplication.update -= Pump;
				EditorApplication.update += Pump;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[EquipmentProbe] setup failed: {ex}");
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
				Capture();
				Release();
				EditorApplication.Exit(0);
			}
			catch (Exception ex)
			{
				EditorApplication.update -= Pump;
				Debug.LogError($"[EquipmentProbe] pump failed: {ex}");
				Release();
				EditorApplication.Exit(1);
			}
		}

		private static void Capture()
		{
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = texture;
			try
			{
				Texture2D image = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGBA32, false);
				image.ReadPixels(new Rect(0, 0, WIDTH, HEIGHT), 0, 0);
				image.Apply();
				File.WriteAllBytes(Path.Combine(OUTPUT_DIR, OUTPUT_NAME), image.EncodeToPNG());
				UnityEngine.Object.DestroyImmediate(image);
				Debug.Log($"[EquipmentProbe] wrote {OUTPUT_NAME}");
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
