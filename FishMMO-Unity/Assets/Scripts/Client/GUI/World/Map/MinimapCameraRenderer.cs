using System;
using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// Owns the overhead camera that photographs the world for the minimap, and renders it at a
	/// capped rate into a texture the UI draws.
	/// </summary>
	/// <remarks>
	/// <para><b>Why the camera is disabled and rendered by hand.</b> A <c>Camera</c> component
	/// that is enabled renders its target once per frame, for as long as it is enabled, whether or
	/// not anything is looking at the result. A minimap does not need sixty overhead renders of
	/// the world every second — it needs about thirty, and only while it is on screen. Keeping the
	/// component disabled and submitting an explicit render request is the supported way to say
	/// that under a scriptable render pipeline; <see cref="FramesPerSecond"/> then costs what it
	/// says it costs.</para>
	///
	/// <para><b>Why every setting is re-applied on every render.</b> The camera's field of view is
	/// the one piece of the map subsystem that turns into real information when it is widened: a
	/// client that doubles the orthographic size gets a genuinely larger photograph of the world
	/// than it is meant to have. Setting the size once at start-up leaves that value sitting in a
	/// component for the rest of the session; re-applying the whole configuration each time means
	/// any tampering survives at most one frame — and, just as importantly, that a camera assigned
	/// in a scene cannot silently contribute a stale culling mask or a clipping plane that hides
	/// the ground. The previous minimap did exactly that, and shipped for months rendering
	/// nothing but black because the scene's far clip plane sat precisely on the terrain.</para>
	///
	/// <para><b>What it does not defend against.</b> A process that can edit the camera's memory
	/// can also edit this. The point is not that widening is impossible; it is that widening the
	/// picture reveals terrain, which is public, and never reveals a marker — markers are drawn by
	/// the UI from <see cref="MapMarkerFilter"/>, which is a different code path with its own
	/// rules, fed only by entities the server chose to stream.</para>
	/// </remarks>
	public sealed class MinimapCameraRenderer : IDisposable
	{
		/// <summary>How far above the follow target the camera sits, in metres.</summary>
		/// <remarks>
		/// High enough to clear any terrain or building the player can stand under, low enough
		/// that the near plane can stay small without the depth range becoming useless. The old
		/// value was 1000 against a far plane of 1000, which put the ground exactly on the far
		/// plane and clipped the entire world away.
		/// </remarks>
		public const float CameraHeight = 400.0f;

		/// <summary>
		/// How far below the follow target the camera can still see, in metres.
		/// </summary>
		/// <remarks>
		/// Added to <see cref="CameraHeight"/> to give the far plane. Generous because a player
		/// standing on a tower must still see the ground at the bottom of the valley below them,
		/// and because the cost of an over-long orthographic depth range is precision in the depth
		/// buffer rather than anything the player can see on a top-down view of static terrain.
		/// </remarks>
		public const float CameraDepth = 900.0f;

		/// <summary>Layer names the minimap always photographs, when the project defines them.</summary>
		/// <remarks>
		/// By name rather than by mask so the set survives a layer being renumbered in project
		/// settings, and so a project missing one of them logs a warning instead of silently
		/// photographing the wrong layer. Characters are deliberately absent: they are drawn as UI
		/// markers, which is what lets the map apply a visibility rule to them at all.
		/// </remarks>
		private static readonly string[] DefaultLayerNames = { "Default", "Ground", "Water" };

		/// <summary>The camera doing the rendering.</summary>
		private Camera camera;

		/// <summary>Whether this renderer created the camera and must destroy it.</summary>
		private bool ownsCamera;

		/// <summary>The texture the camera renders into.</summary>
		private RenderTexture texture;

		/// <summary>Edge length of <see cref="texture"/> in pixels.</summary>
		private int resolution;

		/// <summary>Layers the camera photographs.</summary>
		private int cullingMask;

		/// <summary>Time, on the unscaled clock, at which the next render is due.</summary>
		private double nextRenderTime;

		/// <summary>Whether a warning about the render request path has already been logged.</summary>
		private bool loggedUnsupportedRequest;

		/// <summary>The texture the UI should draw. Null until <see cref="Configure"/> has run.</summary>
		public RenderTexture Texture => texture;

		/// <summary>
		/// How many times a second the world is photographed.
		/// </summary>
		/// <remarks>
		/// Zero or less means "every frame", which is what the old minimap did implicitly. Values
		/// are clamped by the caller from the player's setting rather than here, so that a
		/// deliberate uncapped mode stays expressible.
		/// </remarks>
		public float FramesPerSecond { get; set; } = 30.0f;

		/// <summary>Whether the renderer has a camera and a texture and can be asked to render.</summary>
		public bool IsReady => camera != null && texture != null;

		/// <summary>
		/// Prepares the camera and its texture.
		/// </summary>
		/// <param name="existing">
		/// A camera assigned in the scene to use instead of creating one. Its entire configuration
		/// is overwritten. May be null.
		/// </param>
		/// <param name="additionalLayers">Extra layers to photograph beyond the defaults.</param>
		/// <param name="pixelResolution">Edge length of the render texture, in pixels.</param>
		/// <remarks>
		/// Safe to call again with a different resolution: the texture is rebuilt only when the
		/// size actually changes, so a settings panel can push the value on every slider frame.
		/// </remarks>
		public void Configure(Camera existing, LayerMask additionalLayers, int pixelResolution)
		{
			pixelResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(pixelResolution), 64, 1024);

			if (camera == null)
			{
				if (existing != null)
				{
					camera = existing;
					ownsCamera = false;
				}
				else
				{
					GameObject host = new GameObject("MinimapCamera");
					host.hideFlags = HideFlags.HideAndDontSave;
					camera = host.AddComponent<Camera>();
					ownsCamera = true;
				}
			}

			cullingMask = BuildCullingMask(additionalLayers);

			if (texture == null || resolution != pixelResolution)
			{
				ReleaseTexture();
				resolution = pixelResolution;
				texture = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.Default)
				{
					name = "MinimapRenderTexture",
					filterMode = FilterMode.Bilinear,
					wrapMode = TextureWrapMode.Clamp,
					antiAliasing = 1,
					useMipMap = false,
					autoGenerateMips = false,
				};
				texture.Create();
			}

			/* Disabled, and kept disabled. An enabled camera renders every frame under the
			 * pipeline's own scheduling, which is precisely the cost this class exists to avoid —
			 * and it would render into the same texture on a different cadence, so the frame the
			 * UI is drawing would not be the frame this class thinks it produced. */
			camera.enabled = false;
			camera.targetTexture = texture;

			ApplyCameraSettings(new MapViewTransform(Vector3.zero, 25.0f, 0.0f));
		}

		/// <summary>
		/// Renders the world into <see cref="Texture"/> if the frame cap allows it.
		/// </summary>
		/// <param name="view">The window to photograph.</param>
		/// <param name="force">Render now regardless of the frame cap.</param>
		/// <returns>True when a render was submitted.</returns>
		/// <remarks>
		/// The unscaled clock, so a paused or slowed game still updates the map. Time scale is a
		/// gameplay concept and the minimap is a readout of where things are, not a simulation of
		/// them.
		/// </remarks>
		public bool Render(MapViewTransform view, bool force = false)
		{
			if (!IsReady)
			{
				return false;
			}

			double now = Time.unscaledTimeAsDouble;
			if (!force && FramesPerSecond > 0.0f && now < nextRenderTime)
			{
				return false;
			}

			nextRenderTime = FramesPerSecond > 0.0f ? now + (1.0 / FramesPerSecond) : now;

			ApplyCameraSettings(view);

			if (!texture.IsCreated())
			{
				/* A device reset destroys the surface behind a RenderTexture without destroying
				 * the object, and rendering into one in that state produces a texture the UI
				 * happily draws as garbage. Recreating is cheap and only ever happens on a
				 * resolution change or an alt-tab on some drivers. */
				texture.Create();
				camera.targetTexture = texture;
			}

			RenderPipeline.StandardRequest request = new RenderPipeline.StandardRequest()
			{
				destination = texture,
			};

			if (RenderPipeline.SupportsRenderRequest(camera, request))
			{
				camera.SubmitRenderRequest(request);
				return true;
			}

			if (!loggedUnsupportedRequest)
			{
				loggedUnsupportedRequest = true;
				Log.Warning("MinimapCameraRenderer", "The active render pipeline does not accept a StandardRequest; falling back to Camera.Render. The minimap will still work, but it will not respect the frame cap as precisely.");
			}

			camera.Render();
			return true;
		}

		/// <summary>
		/// Writes the whole hardened configuration onto the camera.
		/// </summary>
		/// <param name="view">The window being photographed.</param>
		/// <remarks>
		/// Every field the minimap depends on is set here, including the ones that "should" already
		/// be right. That is the contract: after this call the camera is known-good regardless of
		/// what the scene authored, what a previous frame left behind, or what anything else in
		/// the process has written to it.
		/// </remarks>
		private void ApplyCameraSettings(MapViewTransform view)
		{
			Transform cameraTransform = camera.transform;

			cameraTransform.position = new Vector3(view.Center.x, view.Center.y + CameraHeight, view.Center.z);

			/* X of 90 looks straight down; Y carries the map's rotation. Rotating the camera
			 * rather than the texture is what makes a rotating minimap free: the photograph comes
			 * out already turned, and MapViewTransform applies the same rotation to every marker,
			 * so the two cannot disagree. */
			cameraTransform.rotation = Quaternion.Euler(90.0f, view.RotationDegrees, 0.0f);

			camera.orthographic = true;
			camera.orthographicSize = view.Range;

			camera.nearClipPlane = 0.3f;
			camera.farClipPlane = CameraHeight + CameraDepth;

			camera.clearFlags = CameraClearFlags.SolidColor;
			camera.backgroundColor = Color.black;
			camera.cullingMask = cullingMask;
			camera.useOcclusionCulling = false;
			camera.allowHDR = false;
			camera.allowMSAA = false;
			camera.depth = -100.0f;
			camera.rect = new Rect(0.0f, 0.0f, 1.0f, 1.0f);
			camera.targetTexture = texture;
			camera.enabled = false;
		}

		/// <summary>
		/// Works out which layers the camera photographs.
		/// </summary>
		/// <param name="additionalLayers">Extra layers from the panel's inspector.</param>
		/// <returns>The culling mask.</returns>
		/// <remarks>
		/// The defaults are unioned in rather than used only as a fallback. The previous minimap
		/// took its mask solely from an inspector field, that field was left at zero in the scene,
		/// and the result was a camera whose mask the code overwrote with nothing — a black map
		/// that no amount of reading the scene explained, because the scene's own mask was
		/// correct and was being discarded.
		/// </remarks>
		private static int BuildCullingMask(LayerMask additionalLayers)
		{
			int mask = additionalLayers.value;

			for (int i = 0; i < DefaultLayerNames.Length; ++i)
			{
				int layer = LayerMask.NameToLayer(DefaultLayerNames[i]);
				if (layer < 0)
				{
					Log.Warning("MinimapCameraRenderer", $"Layer '{DefaultLayerNames[i]}' is not defined in this project, so the minimap will not draw it. Add it in Project Settings > Tags and Layers, or remove it from the minimap's default layers.");
					continue;
				}
				mask |= 1 << layer;
			}

			return mask;
		}

		/// <summary>
		/// Releases the render texture.
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
			UnityEngine.Object.Destroy(texture);
			texture = null;
		}

		/// <summary>
		/// Destroys the texture, and the camera if this renderer created it.
		/// </summary>
		public void Dispose()
		{
			ReleaseTexture();

			if (camera != null)
			{
				if (ownsCamera)
				{
					UnityEngine.Object.Destroy(camera.gameObject);
				}
				else
				{
					/* A camera that came from the scene outlives this renderer. Leaving it enabled
					 * would put an unowned overhead render back into every frame for the rest of
					 * the session, which is the exact cost this class was written to remove. */
					camera.enabled = false;
					camera.targetTexture = null;
				}
				camera = null;
			}
		}
	}
}
