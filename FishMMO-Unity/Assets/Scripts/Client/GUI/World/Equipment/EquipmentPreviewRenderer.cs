using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using FishMMO.Logging;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Owns the camera and the render texture behind the equipment panel's character preview,
	/// frames the character in it, and photographs it on demand.
	/// </summary>
	/// <remarks>
	/// <para><b>Why the panel owns the texture, not the camera's prefab.</b> The camera authored in
	/// the character prefabs targets a fixed 256×256 asset. The panel's viewport is roughly 348×224
	/// — a different aspect — so no background scale mode can show that texture correctly: scaling
	/// to fit pillarboxes the character, stretching distorts it by half again, and cropping cuts
	/// the head and feet off. A texture sized to the viewport makes the question moot, and the
	/// texture can only be sized by whoever knows how big the viewport is.</para>
	///
	/// <para><b>Why the camera is disabled and rendered by hand.</b> Same reason as
	/// <see cref="MinimapCameraRenderer"/>: a <c>Camera</c> component that is enabled renders once
	/// per frame for as long as it is enabled, whether or not the panel is on screen. Here the
	/// subject is one character rather than the world, so a render is cheap — but "cheap" is not
	/// "free", and submitting explicitly is also what makes the camera's state (target texture,
	/// enablement) something this class can guarantee rather than something a scene authored.</para>
	///
	/// <para><b>Why the render is a <see cref="UniversalRenderPipeline.SingleCameraRequest"/> and not a
	/// <see cref="RenderPipeline.StandardRequest"/>.</b> Both photograph one camera into one
	/// texture, but URP services a StandardRequest by running its whole per-frame <c>Render</c>
	/// path (per-frame constants, camera sorting, render-graph end-of-frame) for that one camera,
	/// while a SingleCameraRequest renders just the camera. The single-camera path is the one URP
	/// documents for this use. Measured on 2026-09-15 with <c>PreviewLeakProbe</c>: the two are
	/// indistinguishable on memory — neither leaks per request — so this is a cost choice, not a
	/// leak fix.</para>

	/// <para><b>Why the subject is moved to its own layer for each render.</b> Every character's
	/// visuals sit on the Player layer, so a camera that culls to that layer photographs whichever
	/// characters stand inside its orthographic box — the player and anyone next to them. There is
	/// no per-character layer, and URP's camera culling knows only GameObject layers. So
	/// <see cref="Render(int, int, Transform)"/> moves the subject's renderers onto the otherwise
	/// empty CharacterPreview layer, submits, and puts them back before returning. Culling happens
	/// synchronously inside the submit, and nothing else runs between the two writes, so no other
	/// system can observe the swap.</para>
	///
	/// <para><b>What it puts back.</b> The camera belongs to the character prefab and outlives any
	/// one opening of the panel, so <see cref="Dispose"/> restores the target texture the prefab
	/// authored and leaves the component disabled. Leaving it enabled, or leaving it enabled with
	/// no target texture, are both worse than they sound: the first renders the character a second
	/// time every frame forever, and the second draws it straight onto the screen.</para>
	/// </remarks>
	public sealed class EquipmentPreviewRenderer : IDisposable
	{
		/// <summary>
		/// How far in front of the character the camera sits, in metres.
		/// </summary>
		/// <remarks>
		/// Matches the distance the character prefabs author for their equipment view camera, so
		/// the perspective's framing of a character that fills its bounds is unchanged by this
		/// class taking the camera over.
		/// </remarks>
		public const float CameraDistance = 3.0f;

		/// <summary>
		/// Fraction of empty space left around the framed subject.
		/// </summary>
		/// <remarks>
		/// Small on purpose. The subject's bounds come from the bind pose of the body mesh, which
		/// is wider than any idle pose, so the frame is already generous before this is applied.
		/// </remarks>
		private const float FramingPadding = 1.08f;

		/// <summary>Smallest render texture this will create, per edge.</summary>
		private const int MinimumTextureEdge = 32;

		/// <summary>Largest render texture this will create, per edge.</summary>
		/// <remarks>
		/// A cap rather than a preference: the size comes from a UI element's measured layout, and
		/// a bad measurement on a high-DPI panel should cost a soft preview, not a several-hundred-
		/// megabyte allocation.
		/// </remarks>
		private const int MaximumTextureEdge = 1024;

		/// <summary>The camera being driven, or null when none is assigned.</summary>
		private Camera camera;

		/// <summary>The texture the camera renders into. Null until the first render.</summary>
		private RenderTexture texture;

		/// <summary>Width of <see cref="texture"/> in pixels.</summary>
		private int textureWidth;

		/// <summary>Height of <see cref="texture"/> in pixels.</summary>
		private int textureHeight;

		/// <summary>
		/// The target texture the camera had when it was adopted, restored on release.
		/// </summary>
		private RenderTexture adoptedTargetTexture;

		/// <summary>Whether a warning about the render request path has already been logged.</summary>
		private bool loggedUnsupportedRequest;

		/// <summary>
		/// The request submitted each frame. One instance, retargeted when the texture changes,
		/// because the request is a class and a fresh one per frame is garbage for nothing.
		/// </summary>
		private readonly UniversalRenderPipeline.SingleCameraRequest request = new UniversalRenderPipeline.SingleCameraRequest();

		/// <summary>The subject's renderers during a render, in the order their layers were saved.</summary>
		private readonly List<Renderer> isolated = new List<Renderer>();

		/// <summary>The layer each entry of <see cref="isolated"/> came from.</summary>
		private readonly List<int> isolatedLayers = new List<int>();

		/// <summary>How many renderers the last render isolated. Exposed so a test can see the swap happened.</summary>
		public int LastIsolatedCount { get; private set; }

		/// <summary>The texture the panel should draw. Null until a render has been submitted.</summary>
		public RenderTexture Texture => texture;

		/// <summary>The camera being driven, or null. Exposed so callers can assert the handover.</summary>
		public Camera Camera => camera;

		/// <summary>Whether there is a camera and a texture, so a render can be submitted.</summary>
		public bool IsReady => camera != null && texture != null;

		/// <summary>
		/// Adopts <paramref name="existing"/> as the preview camera, releasing whichever camera was
		/// held before.
		/// </summary>
		/// <param name="existing">The character's equipment view camera. May be null to release.</param>
		/// <remarks>
		/// Releasing the outgoing camera is not optional. The panel is handed a fresh camera on
		/// every keypress, including the one that closes it, so a handover without a release leaves
		/// the previous character's camera enabled and rendering into a texture nobody draws — for
		/// the rest of the session.
		/// </remarks>
		public void Configure(Camera existing)
		{
			if (camera == existing)
			{
				return;
			}

			ReleaseCamera();
			camera = existing;

			if (camera != null)
			{
				adoptedTargetTexture = camera.targetTexture;
				ApplyCameraSettings();
			}
		}

		/// <summary>
		/// Resizes the render texture to the viewport and renders the character into it.
		/// </summary>
		/// <param name="pixelWidth">Viewport width in pixels. Values below 2 are refused.</param>
		/// <param name="pixelHeight">Viewport height in pixels. Values below 2 are refused.</param>
		/// <param name="subject">
		/// The hierarchy to photograph, alone. Its renderers are moved to the CharacterPreview
		/// layer for this render and restored before the call returns. Null renders whatever the
		/// camera's mask sees, which on a shared layer includes any neighbour.
		/// </param>
		/// <returns>True when a render was submitted.</returns>
		/// <remarks>
		/// A refusal is not an error: the first tick after a panel opens can run before the UI
		/// layout engine has measured anything, and those measurements arrive a frame later. The
		/// caller is expected to keep asking.
		/// </remarks>
		public bool Render(int pixelWidth, int pixelHeight, Transform subject = null)
		{
			if (camera == null || !EnsureTexture(pixelWidth, pixelHeight))
			{
				return false;
			}

			Isolate(subject);
			try
			{
				return Submit();
			}
			finally
			{
				RestoreIsolated();
			}
		}

		/// <summary>Submits the render into the current texture.</summary>
		private bool Submit()
		{

			if (!texture.IsCreated())
			{
				/* A device reset destroys the surface behind a RenderTexture without destroying the
				 * object, and rendering into one in that state produces a texture the UI draws as
				 * garbage. Recreating is cheap and only happens on a resolution change or an
				 * alt-tab on some drivers. */
				texture.Create();
				camera.targetTexture = texture;
			}

			request.destination = texture;

			if (RenderPipeline.SupportsRenderRequest(camera, request))
			{
				camera.SubmitRenderRequest(request);
				return true;
			}

			/* A pipeline that refuses a SingleCameraRequest is not URP, so there is no point
			 * offering it a StandardRequest either; Camera.Render is the portable fallback. */
			if (!loggedUnsupportedRequest)
			{
				loggedUnsupportedRequest = true;
				Log.Warning("EquipmentPreviewRenderer", "The active render pipeline does not accept a SingleCameraRequest; falling back to Camera.Render. The character preview will still work.");
			}

			camera.Render();
			return true;
		}

		/// <summary>
		/// Moves every renderer beneath <paramref name="subject"/> onto the preview layer,
		/// remembering where each came from.
		/// </summary>
		/// <param name="subject">The hierarchy to isolate. May be null.</param>
		/// <remarks>
		/// Gathered every render rather than cached: equipment meshes come and go under the
		/// mesh root as items are equipped, and a stale list would leave a new piece on the
		/// shared layer, invisible to the preview. The non-allocating overload keeps this off
		/// the garbage collector.
		/// </remarks>
		private void Isolate(Transform subject)
		{
			isolated.Clear();
			isolatedLayers.Clear();
			LastIsolatedCount = 0;

			int layer = Constants.Layers.Index.CharacterPreview;
			if (subject == null || layer < 0)
			{
				return;
			}

			subject.GetComponentsInChildren(true, isolated);
			for (int i = 0; i < isolated.Count; ++i)
			{
				GameObject go = isolated[i].gameObject;
				isolatedLayers.Add(go.layer);
				go.layer = layer;
			}

			LastIsolatedCount = isolated.Count;
		}

		/// <summary>Puts every renderer <see cref="Isolate"/> moved back on its own layer.</summary>
		private void RestoreIsolated()
		{
			for (int i = 0; i < isolated.Count; ++i)
			{
				Renderer renderer = isolated[i];
				if (renderer != null)
				{
					renderer.gameObject.layer = isolatedLayers[i];
				}
			}

			isolated.Clear();
			isolatedLayers.Clear();
		}

		/// <summary>
		/// Frames everything drawn beneath <paramref name="subject"/> so it fits the viewport.
		/// </summary>
		/// <param name="subject">The character's mesh root. May be null.</param>
		/// <returns>True when the subject had drawable renderers and the camera was framed.</returns>
		/// <remarks>
		/// <para>Derived from the subject's own renderer bounds rather than from constants, because
		/// the character's height is a property of its race model and the game has hundreds of
		/// them. The camera prefabs were authored against one placeholder model, at a height that
		/// frames a standing human from the knees up.</para>
		/// <para>False means "ask again": the race model is loaded from an addressable and the
		/// character may legitimately have no renderers yet on the frame the panel opens.</para>
		/// </remarks>
		public bool Frame(Transform subject)
		{
			if (camera == null || subject == null || texture == null)
			{
				return false;
			}

			Transform space = camera.transform.parent;
			if (space == null || !TryComputeBounds(subject, space, out Bounds bounds))
			{
				return false;
			}

			float aspect = (float)textureWidth / textureHeight;
			camera.orthographicSize = ComputeOrthographicSize(bounds, aspect);
			camera.transform.localPosition = ComputeCameraLocalPosition(bounds, CameraDistance);

			/* The subject's own depth decides the far plane. A cape or a two-handed weapon shifts
			 * the bounds along the view axis, and a far plane pinned to a constant would clip it
			 * back out of the preview that was just sized to include it. */
			camera.nearClipPlane = 0.05f;
			camera.farClipPlane = CameraDistance + bounds.extents.z + 1.0f;
			return true;
		}

		/// <summary>
		/// The orthographic half-height that fits <paramref name="bounds"/> in a viewport of
		/// <paramref name="aspect"/>, with a little room around it.
		/// </summary>
		/// <param name="bounds">The subject, in camera-parent space.</param>
		/// <param name="aspect">Viewport width divided by height.</param>
		/// <returns>The orthographic size to assign.</returns>
		/// <remarks>
		/// An orthographic camera's size is half its visible <em>height</em>, so a subject wider
		/// than the viewport's aspect allows has to be fitted by width instead — dividing its
		/// half-width by the aspect converts it into the height that would contain it. Taking the
		/// larger of the two is what keeps a broad subject from being cropped at the sides.
		/// </remarks>
		public static float ComputeOrthographicSize(Bounds bounds, float aspect)
		{
			if (aspect <= 0.0f || float.IsNaN(aspect) || float.IsInfinity(aspect))
			{
				aspect = 1.0f;
			}

			float halfHeight = Mathf.Max(bounds.extents.y, bounds.extents.x / aspect);

			// A degenerate subject — one bone, or a renderer with no size — would otherwise
			// produce a zero-size camera, which renders nothing and cannot be recovered from.
			return Mathf.Max(halfHeight, 0.01f) * FramingPadding;
		}

		/// <summary>
		/// Where the camera sits, in its parent's space, to look at the centre of
		/// <paramref name="bounds"/> from <paramref name="distance"/> in front of it.
		/// </summary>
		/// <param name="bounds">The subject, in camera-parent space.</param>
		/// <param name="distance">How far in front of the subject to sit, in metres.</param>
		/// <returns>The local position to assign.</returns>
		/// <remarks>
		/// Centring on the subject rather than on the character's origin is what makes a short race
		/// and a tall one both appear centred. The camera prefabs set their height through a
		/// RectTransform's <c>anchoredPosition</c>, which does nothing at all under a plain
		/// Transform parent — so the authored offset never applied and the camera sat at the
		/// character's feet.
		/// </remarks>
		public static Vector3 ComputeCameraLocalPosition(Bounds bounds, float distance)
		{
			return new Vector3(bounds.center.x, bounds.center.y, bounds.center.z + distance);
		}

		/// <summary>
		/// Bounds of everything drawn beneath <paramref name="subject"/>, expressed in
		/// <paramref name="space"/>.
		/// </summary>
		/// <param name="subject">The hierarchy to measure.</param>
		/// <param name="space">The frame the result is expressed in.</param>
		/// <param name="bounds">The measured bounds.</param>
		/// <returns>False when nothing beneath the subject can be drawn.</returns>
		/// <remarks>
		/// <para>Transformed corner by corner rather than by centre-and-extents. <c>Renderer.bounds</c>
		/// is a world-space axis-aligned box, and the camera's parent is rotated to the character's
		/// facing — so converting only its centre would give a box aligned to the character and a
		/// size measured against the world's axes, which is wrong by however far the character is
		/// turned from north.</para>
		/// <para>Disabled renderers are skipped: hiding a body region is how the appearance system
		/// removes it, and a hidden region must not keep padding the frame.</para>
		/// </remarks>
		private static bool TryComputeBounds(Transform subject, Transform space, out Bounds bounds)
		{
			bounds = default;

			Renderer[] renderers = subject.GetComponentsInChildren<Renderer>(true);
			if (renderers == null || renderers.Length == 0)
			{
				return false;
			}

			bool measured = false;
			Vector3 min = Vector3.zero;
			Vector3 max = Vector3.zero;

			for (int i = 0; i < renderers.Length; ++i)
			{
				Renderer renderer = renderers[i];
				if (renderer == null || !renderer.enabled)
				{
					continue;
				}

				Bounds local = renderer.bounds;
				if (local.size == Vector3.zero)
				{
					continue;
				}

				for (int corner = 0; corner < 8; ++corner)
				{
					Vector3 point = space.InverseTransformPoint(new Vector3(
						(corner & 1) == 0 ? local.min.x : local.max.x,
						(corner & 2) == 0 ? local.min.y : local.max.y,
						(corner & 4) == 0 ? local.min.z : local.max.z));

					if (!measured)
					{
						min = point;
						max = point;
						measured = true;
						continue;
					}

					min = Vector3.Min(min, point);
					max = Vector3.Max(max, point);
				}
			}

			if (!measured)
			{
				return false;
			}

			bounds = new Bounds((min + max) * 0.5f, max - min);
			return true;
		}

		/// <summary>
		/// Writes the whole configuration onto the camera.
		/// </summary>
		/// <remarks>
		/// Every field the preview depends on is set here, including the ones the character prefab
		/// "should" already have right. That is the contract: after this call the camera is
		/// known-good regardless of what the prefab authored or what anything else in the process
		/// has written to it since.
		/// </remarks>
		private void ApplyCameraSettings()
		{
			camera.orthographic = true;
			camera.clearFlags = CameraClearFlags.SolidColor;
			camera.backgroundColor = Color.clear;
			camera.cullingMask = BuildCullingMask();
			camera.useOcclusionCulling = false;
			camera.allowHDR = false;
			camera.allowMSAA = false;
			camera.rect = new Rect(0.0f, 0.0f, 1.0f, 1.0f);

			/* Depth left at the prefab's zero: this camera draws only into its own texture, so it
			 * never competes with the main camera for a place in the screen's draw order. */
			camera.enabled = false;
		}

		/// <summary>
		/// The layers the preview photographs.
		/// </summary>
		/// <returns>The culling mask.</returns>
		/// <remarks>
		/// The CharacterPreview layer alone. Nothing is authored on it; <see cref="Isolate"/>
		/// puts the subject there for exactly one render, so the camera sees that character and
		/// nobody standing beside them. When the project lacks the layer the mask falls back to
		/// the shared visual layer, which shows the subject — and any neighbour inside the frame.
		/// </remarks>
		private static int BuildCullingMask()
		{
			int layer = Constants.Layers.Index.CharacterPreview;
			if (layer >= 0)
			{
				return 1 << layer;
			}

			Log.Warning("EquipmentPreviewRenderer", "Layer 'CharacterPreview' is not defined in this project, so the equipment preview cannot isolate its subject and will also show any character standing next to it. Add it in Project Settings > Tags and Layers.");

			int shared = Constants.Layers.Index.Player;
			if (shared < 0)
			{
				Log.Warning("EquipmentPreviewRenderer", "Layer 'Player' is not defined in this project either, so the equipment preview will stay blank.");
				return 0;
			}

			return 1 << shared;
		}

		/// <summary>
		/// Creates the render texture, or rebuilds it when the viewport's size has changed.
		/// </summary>
		/// <param name="pixelWidth">Requested width in pixels.</param>
		/// <param name="pixelHeight">Requested height in pixels.</param>
		/// <returns>True when a texture of the requested size is available.</returns>
		private bool EnsureTexture(int pixelWidth, int pixelHeight)
		{
			/* Capped uniformly before the per-edge clamp. The request is in device pixels, so on a
			 * large display a tall viewport can pass the cap on one edge only; clamping the edges
			 * independently then changed the texture's aspect, and the frame — which reads that
			 * aspect — no longer matched the box the texture is stretched into. */
			int longest = Mathf.Max(pixelWidth, pixelHeight);
			if (longest > MaximumTextureEdge)
			{
				float fit = (float)MaximumTextureEdge / longest;
				pixelWidth = Mathf.RoundToInt(pixelWidth * fit);
				pixelHeight = Mathf.RoundToInt(pixelHeight * fit);
			}

			pixelWidth = Mathf.Clamp(pixelWidth, MinimumTextureEdge, MaximumTextureEdge);
			pixelHeight = Mathf.Clamp(pixelHeight, MinimumTextureEdge, MaximumTextureEdge);

			if (texture != null && textureWidth == pixelWidth && textureHeight == pixelHeight)
			{
				return true;
			}

			ReleaseTexture();

			textureWidth = pixelWidth;
			textureHeight = pixelHeight;
			texture = new RenderTexture(textureWidth, textureHeight, 24, RenderTextureFormat.ARGB32)
			{
				name = "EquipmentPreviewRenderTexture",
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp,
				antiAliasing = 1,
				useMipMap = false,
				autoGenerateMips = false,
			};
			texture.Create();

			camera.targetTexture = texture;
			return true;
		}

		/// <summary>
		/// Releases the render texture, detaching it from the camera first.
		/// </summary>
		private void ReleaseTexture()
		{
			if (texture == null)
			{
				return;
			}

			if (camera != null && camera.targetTexture == texture)
			{
				camera.targetTexture = null;
			}

			texture.Release();
			// Destroy is an error outside play mode, which is where the tests and the probes run.
			if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
			else UnityEngine.Object.DestroyImmediate(texture);
			texture = null;
			textureWidth = 0;
			textureHeight = 0;
		}

		/// <summary>
		/// Gives the camera back: disabled, on the target texture its prefab authored.
		/// </summary>
		/// <remarks>
		/// The camera is the character's, not this class's, and the character outlives the panel.
		/// Handing it back enabled would put a second render of the character into every frame for
		/// the rest of the session; handing it back pointing at a texture this class has destroyed
		/// would draw it over the screen the moment anything else enabled it.
		/// </remarks>
		private void ReleaseCamera()
		{
			ReleaseTexture();

			if (camera != null)
			{
				camera.enabled = false;
				camera.targetTexture = adoptedTargetTexture;
				camera = null;
			}

			adoptedTargetTexture = null;
		}

		/// <summary>
		/// Releases the texture and hands the camera back.
		/// </summary>
		public void Dispose()
		{
			ReleaseCamera();
		}
	}
}
