using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// Heat shimmer: the far, sun-baked ground boiling in the hot air over it, and the sky's sliver of "water"
	/// on it at the horizon — a refraction of the frame, as strong as the weather makes the surface layer
	/// (<see cref="HeatShimmer"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Where in the frame.</b> After the opaque world, the sky, the clouds and the fog are in it, before the
	/// transparent queue: the shimmer bends what lies behind the hot air, which is the ground and the sky
	/// over the horizon, and the fog over them is smooth enough that bending it with them changes nothing. Not
	/// after the transparents: rain, leaves and the water write no depth, so the pass could not tell a drop in
	/// front of the boiling air from the ground behind it and would smear every particle; and water is
	/// cool, it does not shimmer, and its refraction already reads the opaque frame.
	/// </para>
	/// <para>
	/// <b>MSAA.</b> The frame is copied (resolved) once and the pass writes the resolved, displaced colour back
	/// into every sample — what a full-screen pass over a multisampled target always does, and harmless, since
	/// the copy is already antialiased. What MSAA does break is the one depth a pixel has against four colour
	/// samples (msaa-fullscreen-depth-split): an edge pixel's colour is half foreground. So nothing nearer than
	/// the shimmering pixel is ever pulled onto it — the depth at the displaced position is tested, as the
	/// nearest of a 3×3 cross, and a displacement that lands on something nearer falls back to the pixel's own
	/// colour — and the strength itself is taken at the nearest depth round the pixel, so a foreground edge
	/// is never displaced at all. Distant things trade colour freely among themselves: they are all inside
	/// the boiling air.
	/// </para>
	/// <para>
	/// <b>Cost.</b> When the air is still (night, cloud, wind, wet or green ground) the feature enqueues nothing.
	/// When it runs: one full-screen copy and one full-screen pass of ~12 depth taps, two colour taps and four
	/// octaves of value noise — a few tenths of a millisecond at 1440p. Off on the Performant renderer (the
	/// feature's own active toggle, which the renderer setup leaves off there).
	/// </para>
	/// </remarks>
	public class FishHeatShimmerFeature : ScriptableRendererFeature
	{
		public const string ShaderName = "Hidden/FishMMO/Weather/HeatShimmer";

		/// <summary>The lava surface's shader, whose renderer marks a scene's lava and its level.</summary>
		public const string LavaShaderName = "FishMMO/Water/Lava";

		[Tooltip("The heat-shimmer shader (Hidden/FishMMO/Weather/HeatShimmer). Referenced here so a build carries it: Shader.Find " +
			"only finds a shader something in the build references. Left empty, it is found by name, which works in the editor alone.")]
		public Shader ShimmerShader;

		// The material made from the shader, never serialized (see FishHeightFogFeature's own).
		[System.NonSerialized] private Material created;

		[Header("Strength")]
		[Tooltip("The jitter a ray gets after a kilometre along the ground on a still, clear desert noon, milliradians rms. " +
			"Measured angle-of-arrival jitter for decimetre eddies is ~0.15 mrad; the default is several times that, because a " +
			"screen pixel (~1 mrad at 1080p) is three times coarser than the eye and what the eye reads as boiling is the wander of the larger eddies too.")]
		[Range(0f, 6f)] public float Amplitude = 1.2f;

		[Tooltip("How deep the hot, turbulent layer lies over the ground, m. Cn² falls as height^(-4/3): most of it is in the first metre or two.")]
		[Range(0.5f, 6f)] public float LayerDepth = 2f;

		[Tooltip("The inferior mirage: the sky shown, upside down, in the hot layer just below the horizon. 0 off, 1 as strong as the physics says.")]
		[Range(0f, 1f)] public float Mirage = 0.85f;

		[Tooltip("Widens the mirage's band beyond the physical critical angle (~0.4° over a desert), which is a sliver a few pixels tall. 1 is physical.")]
		[Range(1f, 4f)] public float MirageWidening = 1.5f;

		[Tooltip("The most the frame is ever displaced, pixels: keeps a misjudged pixel from tearing.")]
		[Range(1f, 16f)] public float MaxOffsetPixels = 6f;

		[Header("Look")]
		[Tooltip("The size of a shimmer cell across, degrees of view. The eddies near the ground are decimetres; at a few hundred metres that is a few hundredths of a degree, below a pixel, so what shows is the wander of the larger ones.")]
		[Range(0.05f, 2f)] public float CellDegrees = 0.35f;

		[Tooltip("How much flatter a cell is than wide: the refraction gradient is vertical, so heat shimmer wavers in horizontal bands.")]
		[Range(1f, 6f)] public float CellFlattening = 2.5f;

		[Header("Lava")]
		[Tooltip("Shimmer over molten ground, by day and night alike: a lava surface at 1100 °C heats its air by hundreds of kelvin.")]
		public bool Lava = true;

		[Tooltip("The jitter over lava at a kilometre of path, milliradians rms (against Amplitude's desert noon).")]
		[Range(0f, 20f)] public float LavaAmplitude = 6f;

		[Tooltip("Metres above the lava surface its hot air reaches.")]
		[Range(2f, 60f)] public float LavaReach = 25f;

		/// <summary>The path length the amplitude is quoted at, m.</summary>
		public const float ReferencePath = 1000f;

		/// <summary>The skin-over-air excess of a lava surface's air, K, for its mirage.</summary>
		public const float LavaSkinExcess = 400f;

		/// <summary>The eye depth past which everything is taken to lie inside the boiling air, m: such pixels trade colour freely.</summary>
		public const float FarAccept = 250f;

		/// <summary>Below this the pass is not worth running, rad rms at the reference path.</summary>
		public const float MinimumAngle = 2e-5f;

		private ShimmerPass pass;

		public override void Create()
		{
			pass = new ShimmerPass
			{
				// After the opaque world, the clouds and both fog passes (+1, +2), before the transparents.
				renderPassEvent = RenderPassEvent.BeforeRenderingTransparents + 4,
			};
			pass.ConfigureInput(ScriptableRenderPassInput.Depth);
		}

		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			if (renderingData.cameraData.cameraType != CameraType.Game && renderingData.cameraData.cameraType != CameraType.SceneView)
			{
				return;
			}
			if (renderingData.cameraData.renderType == CameraRenderType.Overlay)
			{
				return;
			}
			Material material = Resolve();
			if (material == null)
			{
				return;
			}
			Camera camera = renderingData.cameraData.camera;
			if (SurfaceWater.IsUnder(camera.transform.position))
			{
				return;   // nothing boils under the sea
			}
			HeatShimmer.State air = HeatShimmer.Current;
			float angle = Amplitude * 1e-3f * Mathf.Sqrt(air.Turbulence);
			float lavaLevel = 0f;
			bool lava = Lava && LavaScan.Find(out lavaLevel);
			if (angle < MinimumAngle && !lava)
			{
				return;
			}
			pass.Setup(material, this, air, angle, lava, lavaLevel);
			renderer.EnqueuePass(pass);
		}

		private Material Resolve()
		{
			if (created != null)
			{
				return created;
			}
			Shader shader = ShimmerShader != null ? ShimmerShader : Shader.Find(ShaderName);
			if (shader == null || !shader.isSupported)
			{
				return null;
			}
			created = CoreUtils.CreateEngineMaterial(shader);
			return created;
		}

		protected override void Dispose(bool disposing)
		{
			if (created != null)
			{
				CoreUtils.Destroy(created);
				created = null;
			}
			pass = null;
		}

		/// <summary>The loaded scenes' lava surface, found once each time a scene loads or unloads.</summary>
		private static class LavaScan
		{
			private static bool dirty = true, hooked, found;
			private static float level;

			public static bool Find(out float lavaLevel)
			{
				if (!hooked)
				{
					hooked = true;
					SceneManager.sceneLoaded += (s, m) => dirty = true;
					SceneManager.sceneUnloaded += s => dirty = true;
				}
				if (dirty)
				{
					dirty = false;
					found = Scan(out level);
				}
				lavaLevel = level;
				return found;
			}

			/// <summary>The renderer drawing the lava material, as the volcanic plumes find it (VolcanicPlumePresenter.LavaLevel).</summary>
			private static bool Scan(out float lavaLevel)
			{
				lavaLevel = 0f;
				for (int s = 0; s < SceneManager.sceneCount; s++)
				{
					Scene scene = SceneManager.GetSceneAt(s);
					if (!scene.isLoaded)
					{
						continue;
					}
					foreach (GameObject root in scene.GetRootGameObjects())
					{
						foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
						{
							Material m = renderer.sharedMaterial;
							if (m != null && m.shader != null && m.shader.name == LavaShaderName)
							{
								lavaLevel = renderer.transform.position.y;
								return true;
							}
						}
					}
				}
				return false;
			}
		}

		private sealed class ShimmerPass : ScriptableRenderPass
		{
			private const string CopyName = "Fish Heat Shimmer Copy";
			private const string PassName = "Fish Heat Shimmer";

			private static readonly int InverseVPId = Shader.PropertyToID("_HeatInverseVP");
			private static readonly int DirectionVPId = Shader.PropertyToID("_HeatDirectionVP");
			private static readonly int ParamsId = Shader.PropertyToID("_HeatParams");
			private static readonly int MirageId = Shader.PropertyToID("_HeatMirage");
			private static readonly int NoiseId = Shader.PropertyToID("_HeatNoise");
			private static readonly int LavaId = Shader.PropertyToID("_HeatLava");
			private static readonly int ScreenId = Shader.PropertyToID("_HeatScreen");
			private static readonly int EyeId = Shader.PropertyToID("_HeatEye");

			private Material material;
			private FishHeatShimmerFeature settings;
			private HeatShimmer.State air;
			private float angle;
			private bool lava;
			private float lavaLevel;

			public void Setup(Material shimmer, FishHeatShimmerFeature feature, HeatShimmer.State state, float jitter, bool hasLava, float level)
			{
				material = shimmer;
				settings = feature;
				air = state;
				angle = jitter;
				lava = hasLava;
				lavaLevel = level;
			}

			private class ShimmerData
			{
				public Material Material;
				public TextureHandle Source;
			}

			public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
			{
				if (material == null || settings == null)
				{
					return;
				}
				UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
				UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
				if (resourceData.isActiveTargetBackBuffer || !resourceData.cameraDepthTexture.IsValid())
				{
					return;
				}

				Matrix4x4 view = cameraData.GetViewMatrix();
				Matrix4x4 projection = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), true);
				material.SetMatrix(InverseVPId, (projection * view).inverse);
				// Directions only, for the mirage's mirrored ray: the view without its translation.
				Matrix4x4 rotation = view;
				rotation.m03 = 0f;
				rotation.m13 = 0f;
				rotation.m23 = 0f;
				material.SetMatrix(DirectionVPId, projection * rotation);

				Vector3 eye = cameraData.worldSpaceCameraPos;
				float ground = FogLayerView.GroundUnder(eye);
				material.SetVector(EyeId, new Vector4(eye.x, eye.y, eye.z, ground));

				Matrix4x4 p = cameraData.GetProjectionMatrix();
				int height = Mathf.Max(1, cameraData.cameraTargetDescriptor.height);
				// One radian of view, in uv, across and up (small angles near the centre: tan θ ≈ θ).
				float uvPerRadianX = 0.5f * Mathf.Abs(p.m00);
				float uvPerRadianY = 0.5f * Mathf.Abs(p.m11);
				// And the cap, MaxOffsetPixels, in uv up.
				float maxUv = settings.MaxOffsetPixels / height;
				material.SetVector(ScreenId, new Vector4(uvPerRadianX, uvPerRadianY, maxUv, 0f));

				material.SetVector(ParamsId, new Vector4(angle, Mathf.Max(0.25f, settings.LayerDepth), ReferencePath, FarAccept));

				float critical = HeatShimmer.MirageAngle(air.SkinExcess) * settings.MirageWidening;
				// z, w: the same widening applied to the index change over lava, whose skin excess the shader scales by the molten share.
				material.SetVector(MirageId, new Vector4(critical, settings.Mirage, HeatShimmer.IndexPerKelvin * settings.MirageWidening * settings.MirageWidening, LavaSkinExcess));

				// The cells, in radians of view; they rise as the plumes do, drift across with the wind and boil.
				float cell = Mathf.Max(0.01f, settings.CellDegrees) * Mathf.Deg2Rad;
				float boil = 0.6f + 0.25f * air.Wind;   // eddies turn over faster in a wind
				material.SetVector(NoiseId, new Vector4(1f / cell, settings.CellFlattening / cell, 0.6f, boil));

				material.SetVector(LavaId, new Vector4(lava ? 1f : 0f, lavaLevel, lava ? settings.LavaAmplitude * 1e-3f : 0f, settings.LavaReach));

				TextureDesc desc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
				desc.name = "_FishHeatShimmerSource";
				desc.msaaSamples = MSAASamples.None;
				desc.bindTextureMS = false;
				desc.clearBuffer = false;
				desc.filterMode = FilterMode.Bilinear;
				TextureHandle source = renderGraph.CreateTexture(desc);
				// Resolves the multisampled frame as it copies (RenderGraphUtils.AddBlitPass: "the source is sampled resolved").
				renderGraph.AddBlitPass(resourceData.activeColorTexture, source, Vector2.one, Vector2.zero, passName: CopyName);

				using (var builder = renderGraph.AddRasterRenderPass<ShimmerData>(PassName, out ShimmerData data))
				{
					data.Material = material;
					data.Source = source;
					builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
					builder.UseTexture(source, AccessFlags.Read);
					builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
					builder.UseAllGlobalTextures(true);
					builder.AllowPassCulling(false);
					builder.SetRenderFunc((ShimmerData d, RasterGraphContext context) =>
						Blitter.BlitTexture(context.cmd, d.Source, new Vector4(1f, 1f, 0f, 0f), d.Material, 0));
				}
			}
		}
	}
}
