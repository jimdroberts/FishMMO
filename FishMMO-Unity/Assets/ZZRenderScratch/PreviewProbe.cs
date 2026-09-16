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
	/// Throwaway harness: runs the equipment panel's character preview for real and reports how
	/// much of the viewport the character actually covers.
	/// </summary>
	/// <remarks>
	/// <para>The preview is a render, so reading its code proves nothing. This builds the smallest
	/// hierarchy the preview needs — a standing subject on the character visual layer, and a camera
	/// authored exactly the way the playable prefabs author theirs — and measures the result.</para>
	///
	/// <para><b>Why the sheet's RefreshPreview is driven by hand.</b> The real driver is
	/// <c>UITKControl.Update</c>, and <c>Update</c> does not run in edit mode: <c>UITKControl</c> is
	/// not <c>ExecuteAlways</c>. Calling the same hook the panel's own tick would call is the closest
	/// this harness gets to that tick, and it is the hook that sizes the texture, frames the camera and
	/// submits the render. The hook lives on <see cref="CharacterSheetView"/> now — the equipment panel
	/// delegates its preview there — so the panel is asked for its sheet first.</para>
	///
	/// <para><b>What it does not cover.</b> The camera is found by the character's name lookup rather
	/// than from its serialized field — which does exercise that fallback, but not the prefab wiring.
	/// The prefab wiring is asserted in <c>EquipmentPreviewTests</c>.</para>
	/// </remarks>
	public static class PreviewProbe
	{
		private const string OUTPUT_DIR = "/home/jim/Dev/FishMMO-Dev/PanelRenders";
		private const string PANEL_SETTINGS_PATH = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UXML_PATH = "Assets/Scripts/Client/GUI/World/CharacterSheet/UICharacterSheet.uxml";
		private const string OUTPUT_NAME = "UIEquipment-preview.png";
		private const int WIDTH = 1200;
		private const int HEIGHT = 900;
		private const int SETTLE_FRAMES = 90;

		private static GameObject host;
		private static Transform visualRoot;
		private static UIDocument document;
		private static PanelSettings settings;
		private static RenderTexture texture;
		private static UITKEquipment panel;
		private static CharacterSheetView sheet;
		private static int framesWaited;

		[DashboardTool(DashboardToolAttribute.UITests, "Render Equipment Preview", Section = "Renders", Order = 10)]
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
				settings.colorClearValue = new Color(0.055f, 0.059f, 0.071f, 1.0f);

				host = new GameObject("Render_UIEquipmentPreview") { hideFlags = HideFlags.HideAndDontSave };
				document = host.AddComponent<UIDocument>();
				document.panelSettings = settings;
				document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UXML_PATH);

				PlayerCharacter character = Rig.Build(host);
				visualRoot = Rig.AttachPreview(character);

				panel = host.AddComponent<UITKEquipment>();
				panel.Document = document;
				panel.OnStarting();
				panel.SetCharacter(character);

				/* The preview moved off the panel and onto the shared sheet when the equipment panel
				 * and the inspect window were put on one markup, so the hook is looked up there. The
				 * panel is still the way in: it owns the sheet, and the sheet is a plain object with no
				 * path back to it. */
				FieldInfo sheetField = typeof(UITKEquipment).GetField("sheet",
					BindingFlags.NonPublic | BindingFlags.Instance);
				sheet = sheetField?.GetValue(panel) as CharacterSheetView;
				if (sheet == null)
				{
					throw new InvalidOperationException("UITKEquipment no longer holds a CharacterSheetView; this probe drives the preview through it.");
				}

				document.rootVisualElement?.MarkDirtyRepaint();

				framesWaited = 0;
				EditorApplication.update -= Pump;
				EditorApplication.update += Pump;
			}
			catch (Exception ex)
			{
				EditorApplication.update -= Pump;
				Debug.LogError($"[PreviewProbe] setup failed: {ex}");
				EditorAutomation.Finish(1);
			}
		}

		private static void Pump()
		{
			try
			{
				++framesWaited;

				// The sheet's own per-frame hook, which edit mode never calls for the panel.
				sheet?.RefreshPreview();
				document?.rootVisualElement?.MarkDirtyRepaint();

				if (framesWaited < SETTLE_FRAMES)
				{
					return;
				}

				EditorApplication.update -= Pump;
				Report();
				Capture();
				Release();
				EditorAutomation.Finish(0);
			}
			catch (Exception ex)
			{
				EditorApplication.update -= Pump;
				Debug.LogError($"[PreviewProbe] pump failed: {ex}");
				Release();
				EditorAutomation.Finish(1);
			}
		}

		/// <summary>
		/// Reports what the preview produced, and whether the panel is displaying it.
		/// </summary>
		private static void Report()
		{
			VisualElement root = document.rootVisualElement;
			VisualElement previewElement = root?.Q("preview-rt");

			Debug.Log($"[PreviewProbe] viewport measured {previewElement?.resolvedStyle.width} x {previewElement?.resolvedStyle.height}");
			Debug.Log($"[PreviewProbe] background image set: {previewElement?.style.backgroundImage.keyword != StyleKeyword.None}");

			FieldInfo rendererField = typeof(CharacterSheetView).GetField("previewRenderer",
				BindingFlags.NonPublic | BindingFlags.Instance);
			EquipmentPreviewRenderer renderer = rendererField?.GetValue(sheet) as EquipmentPreviewRenderer;

			if (renderer == null)
			{
				Debug.LogError("[PreviewProbe] the panel has no preview renderer at all");
				return;
			}

			ReportSubjectBounds();

			Camera camera = renderer.Camera;
			Debug.Log($"[PreviewProbe] camera adopted: {camera != null}, enabled: {camera != null && camera.enabled}");
			if (camera != null)
			{
				Debug.Log($"[PreviewProbe] culling mask {camera.cullingMask} (Player layer index {Constants.Layers.Index.Player}), " +
					$"ortho size {camera.orthographicSize}, local position {camera.transform.localPosition}");
			}

			RenderTexture preview = renderer.Texture;
			if (preview == null)
			{
				Debug.LogError("[PreviewProbe] no preview render texture; the preview never rendered");
				return;
			}

			float coverage = MeasureCoverage(preview);
			Debug.Log($"[PreviewProbe] RESULT preview texture {preview.width}x{preview.height}, " +
				$"covered {coverage:P1} of the viewport");
		}

		/// <summary>
		/// Reports the world-space box the subject's renderers actually occupy.
		/// </summary>
		/// <remarks>
		/// The framing is derived from exactly this, so reporting it is what lets a human check the
		/// orthographic size by hand instead of taking it on trust — a 1.8 m model at half-height
		/// plus padding is a little under 1.0, and a model authored at the wrong scale would show
		/// up here rather than only in the picture.
		/// </remarks>
		private static void ReportSubjectBounds()
		{
			if (visualRoot == null)
			{
				Debug.LogWarning("[PreviewProbe] the subject was never rigged; Rig.AttachPreview returned nothing");
				return;
			}

			Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
			if (renderers == null || renderers.Length == 0)
			{
				Debug.LogWarning("[PreviewProbe] the subject has no renderers; the framing will refuse");
				return;
			}

			bool measured = false;
			Bounds total = default;
			int skinned = 0;
			for (int i = 0; i < renderers.Length; ++i)
			{
				if (renderers[i] is SkinnedMeshRenderer) ++skinned;
				if (!renderers[i].enabled || renderers[i].bounds.size == Vector3.zero) continue;

				if (!measured) { total = renderers[i].bounds; measured = true; continue; }
				total.Encapsulate(renderers[i].bounds);
			}

			Debug.Log($"[PreviewProbe] subject: {renderers.Length} renderers ({skinned} skinned), " +
				$"world bounds centre {total.center} size {total.size}");
		}

		/// <summary>
		/// Fraction of the texture the character covers, measured by alpha.
		/// </summary>
		/// <remarks>
		/// The preview camera clears to a transparent solid colour, so anything the character draws
		/// arrives with alpha. A blank preview — the failure this whole change is about — reads as
		/// zero, whichever way the culling or the framing went wrong.
		/// </remarks>
		private static float MeasureCoverage(RenderTexture source)
		{
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = source;
			try
			{
				Texture2D image = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
				image.ReadPixels(new Rect(0.0f, 0.0f, source.width, source.height), 0, 0);
				image.Apply();

				Color32[] pixels = image.GetPixels32();
				int covered = 0;
				for (int i = 0; i < pixels.Length; ++i)
				{
					if (pixels[i].a > 8)
					{
						++covered;
					}
				}

				UnityEngine.Object.DestroyImmediate(image);
				return pixels.Length == 0 ? 0.0f : (float)covered / pixels.Length;
			}
			finally
			{
				RenderTexture.active = previous;
			}
		}

		/// <summary>Writes the whole panel capture, so a human can look at the box.</summary>
		private static void Capture()
		{
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = texture;
			try
			{
				Texture2D image = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGBA32, false);
				image.ReadPixels(new Rect(0.0f, 0.0f, WIDTH, HEIGHT), 0, 0);
				image.Apply();
				File.WriteAllBytes(Path.Combine(OUTPUT_DIR, OUTPUT_NAME), image.EncodeToPNG());
				UnityEngine.Object.DestroyImmediate(image);
				Debug.Log($"[PreviewProbe] wrote {Path.Combine(OUTPUT_DIR, OUTPUT_NAME)}");
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
			visualRoot = null;
			document = null;
			settings = null;
			texture = null;
			panel = null;
			sheet = null;
		}
	}
}
