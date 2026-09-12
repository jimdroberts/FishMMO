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
	/// <para><b>Why the panel's private RefreshPreview is called by reflection.</b> The real driver
	/// is <c>UITKControl.Update</c>, and <c>Update</c> does not run in edit mode: <c>UITKControl</c>
	/// is not <c>ExecuteAlways</c>. Calling the same hook the update would call is the closest this
	/// harness gets to the panel's own tick, and it is the hook that sizes the texture, frames the
	/// camera and submits the render.</para>
	///
	/// <para><b>What it does not cover.</b> The subject is a capsule, not a skinned race model, and
	/// the camera is found by the character's name lookup rather than from its serialized field —
	/// which does exercise that fallback, but not the prefab wiring. The prefab wiring is asserted
	/// in <c>EquipmentPreviewTests</c>.</para>
	/// </remarks>
	public static class PreviewProbe
	{
		private const string OUTPUT_DIR = "/home/jim/Dev/FishMMO-Dev/PanelRenders";
		private const string PANEL_SETTINGS_PATH = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UXML_PATH = "Assets/Scripts/Client/GUI/World/Equipment/UIEquipment.uxml";
		private const string MODEL_PATH = "Assets/Prefabs/Client/Models/EthanCharacter/Prefabs/Ethan.prefab";
		private const string OUTPUT_NAME = "UIEquipment-preview.png";
		private const int WIDTH = 1200;
		private const int HEIGHT = 900;
		private const int SETTLE_FRAMES = 90;

		/// <summary>Height of the stand-in character, in metres.</summary>
		private const float SubjectHeight = 1.8f;

		private static GameObject host;
		private static GameObject visualRoot;
		private static UIDocument document;
		private static PanelSettings settings;
		private static RenderTexture texture;
		private static UITKEquipment panel;
		private static MethodInfo refreshPreview;
		private static int framesWaited;

		[MenuItem("FishMMO/UI Toolkit/Render Equipment Preview")]
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
				BuildVisuals(host, character);

				panel = host.AddComponent<UITKEquipment>();
				panel.Document = document;
				panel.OnStarting();
				panel.SetCharacter(character);

				refreshPreview = typeof(UITKEquipment).GetMethod("RefreshPreview",
					BindingFlags.NonPublic | BindingFlags.Instance);
				if (refreshPreview == null)
				{
					throw new InvalidOperationException("UITKEquipment.RefreshPreview is gone; this probe drives the preview through it.");
				}

				document.rootVisualElement?.MarkDirtyRepaint();

				framesWaited = 0;
				EditorApplication.update -= Pump;
				EditorApplication.update += Pump;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[PreviewProbe] setup failed: {ex}");
				EditorApplication.Exit(1);
			}
		}

		/// <summary>
		/// Builds the character hierarchy the preview photographs: a mesh root under a Smoothing
		/// transform, a standing subject on the visual layer, and the preview camera authored the
		/// way the playable prefabs author it.
		/// </summary>
		/// <remarks>
		/// The camera is deliberately <b>not</b> assigned to the character's serialized field. Its
		/// name is the only thing that finds it, which is the fallback the real prefabs do not
		/// need — so this probe would fail loudly if that fallback were removed.
		/// </remarks>
		private static void BuildVisuals(GameObject host, PlayerCharacter character)
		{
			GameObject smoothing = new GameObject("Smoothing");
			smoothing.transform.SetParent(host.transform, false);

			visualRoot = new GameObject("MeshRoot");
			visualRoot.transform.SetParent(smoothing.transform, false);

			/* The real race model, not a stand-in: a skinned mesh reports the bounds of its bind
			 * pose, which is what the framing has to fit, and a capsule would not exercise that. */
			GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(MODEL_PATH);
			if (model != null)
			{
				GameObject body = UnityEngine.Object.Instantiate(model, visualRoot.transform);
				body.name = "Body";
				body.transform.localPosition = Vector3.zero;
				body.transform.localRotation = Quaternion.identity;
			}
			else
			{
				/* A capsule is 2 units tall at unit scale, centred on its own origin, so half of
				 * SubjectHeight puts its feet on y = 0 — the same ground the real models stand on. */
				Debug.LogWarning($"[PreviewProbe] no model at {MODEL_PATH}; falling back to a capsule, which does NOT exercise skinned bounds.");
				GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
				body.name = "Body";
				body.transform.SetParent(visualRoot.transform, false);
				body.transform.localPosition = new Vector3(0.0f, SubjectHeight * 0.5f, 0.0f);
				body.transform.localScale = new Vector3(0.45f, SubjectHeight * 0.5f, 0.45f);
			}

			GameObject cameraObject = new GameObject("EquipmentViewCamera");
			cameraObject.transform.SetParent(smoothing.transform, false);
			cameraObject.transform.localPosition = new Vector3(0.0f, 0.0f, EquipmentPreviewRenderer.CameraDistance);
			cameraObject.transform.localRotation = Quaternion.Euler(0.0f, 180.0f, 0.0f);

			Camera camera = cameraObject.AddComponent<Camera>();
			camera.orthographic = true;

			// Authored disabled, exactly as the prefabs author it.
			cameraObject.SetActive(false);

			// The subject and its equipment go on the visual layer; the camera does not need to.
			BaseCharacter.ApplyVisualLayer(visualRoot);

			FieldInfo meshRoot = typeof(BaseCharacter).GetField("meshRoot",
				BindingFlags.NonPublic | BindingFlags.Instance);
			meshRoot.SetValue(character, visualRoot.transform);
		}

		private static void Pump()
		{
			try
			{
				++framesWaited;

				// The panel's own per-frame hook, which edit mode never calls for it.
				refreshPreview.Invoke(panel, null);
				document?.rootVisualElement?.MarkDirtyRepaint();

				if (framesWaited < SETTLE_FRAMES)
				{
					return;
				}

				EditorApplication.update -= Pump;
				Report();
				Capture();
				Release();
				EditorApplication.Exit(0);
			}
			catch (Exception ex)
			{
				EditorApplication.update -= Pump;
				Debug.LogError($"[PreviewProbe] pump failed: {ex}");
				Release();
				EditorApplication.Exit(1);
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

			FieldInfo rendererField = typeof(UITKEquipment).GetField("previewRenderer",
				BindingFlags.NonPublic | BindingFlags.Instance);
			EquipmentPreviewRenderer renderer = rendererField?.GetValue(panel) as EquipmentPreviewRenderer;

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
		}
	}
}
