using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// Fog that lies in the low ground instead of filling the world evenly (P5).
	/// </summary>
	/// <remarks>
	/// <para>
	/// Unity's built-in fog is a function of distance alone, so a valley floor and a ridge at the
	/// same range are equally foggy. Mist pools; it does not hang at altitude. This pass reads the
	/// world position behind each pixel out of the depth buffer and integrates an exponential
	/// height falloff along the view ray, which is what fills a valley and leaves a ridge clear.
	/// </para>
	/// <para>
	/// <b>Analytic, not marched.</b> The integral has a closed form, so this is one full-screen pass
	/// with no loop — a few instructions a pixel, against the cloud march's 28 to 72 samples. It
	/// runs on every tier, including WebGL2, and it is the cheap half of P5's fog work; the froxel
	/// volumetric path is separate and needs compute.
	/// </para>
	/// <para>
	/// <b>It composes with the built-in fog rather than replacing it.</b> <c>FogComposer</c> still
	/// owns distance fog and the sky colour; this adds the height term on top, driven by the same
	/// <c>_FishWeatherFog</c> channels the rest of the weather presentation reads, so a mist layer
	/// thickening puts fog in the hollows without anything else having to know.
	/// </para>
	/// </remarks>
	public class FishHeightFogFeature : ScriptableRendererFeature
	{
		public const string ShaderName = "Hidden/FishMMO/Weather/HeightFog";

		[Tooltip("The height-fog material. Left empty, the feature finds the shader itself.")]
		public Material FogMaterial;

		[Tooltip("Metres the fog thins over. Larger is a deeper, softer layer.")]
		[Min(1f)] public float FalloffHeight = 60f;

		[Tooltip("World Y the fog is thickest at. Usually the water line or the valley floor.")]
		public float BaseHeight;

		[Tooltip("How thick the fog can ever get. Below 1 always leaves something visible through it.")]
		[Range(0f, 1f)] public float MaximumOpacity = 0.92f;

		[Tooltip("How much brighter the fog is looking toward the sun. 0 is a flat grey sheet.")]
		[Range(0f, 1f)] public float SunInscatter = 0.6f;

		[Tooltip("Metres before the fog starts, so the camera is never inside a wall of it.")]
		[Min(0f)] public float StartDistance = 4f;

		[Tooltip("Metres the fog integrates to. Beyond this the sky keeps its own colour.")]
		[Min(10f)] public float EndDistance = 1200f;

		[Tooltip("Scales the weather's fog density into this pass. The built-in distance fog keeps its own.")]
		[Range(0f, 0.2f)] public float DensityScale = 0.03f;

		private FogPass pass;

		public override void Create()
		{
			pass = new FogPass
			{
				// After the opaque world and the clouds, before transparents: fog sits over the
				// solid world, and water and glass are drawn through it rather than behind it.
				renderPassEvent = RenderPassEvent.BeforeRenderingTransparents + 1,
			};
			pass.ConfigureInput(ScriptableRenderPassInput.Depth);
		}

		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			Material material = Resolve();
			if (material == null)
			{
				return;
			}
			if (renderingData.cameraData.cameraType != CameraType.Game && renderingData.cameraData.cameraType != CameraType.SceneView)
			{
				return;
			}
			pass.Setup(material, this);
			renderer.EnqueuePass(pass);
		}

		private Material Resolve()
		{
			if (FogMaterial != null)
			{
				return FogMaterial;
			}
			Shader shader = Shader.Find(ShaderName);
			if (shader == null)
			{
				return null;
			}
			FogMaterial = CoreUtils.CreateEngineMaterial(shader);
			return FogMaterial;
		}

		protected override void Dispose(bool disposing)
		{
			if (FogMaterial != null)
			{
				CoreUtils.Destroy(FogMaterial);
				FogMaterial = null;
			}
			pass = null;
		}

		private sealed class FogPass : ScriptableRenderPass
		{
			private const string PassName = "Fish Height Fog";

			private static readonly int InverseVPId = Shader.PropertyToID("_FishFogInverseVP");
			private static readonly int ParamsId = Shader.PropertyToID("_FishFogParams");
			private static readonly int ColorId = Shader.PropertyToID("_FishFogColor");
			private static readonly int SunId = Shader.PropertyToID("_FishFogSun");
			private static readonly int SunColorId = Shader.PropertyToID("_FishFogSunColor");
			private static readonly int RangeId = Shader.PropertyToID("_FishFogRange");
			private static readonly int WeatherFogId = Shader.PropertyToID("_FishWeatherFog");

			/* The same fog, published for TRANSPARENT surfaces. This pass fogs what is already in the
			 * frame, before the transparent queue — so the sea, the shore and anything else drawn
			 * after it came out perfectly clear through the thickest fog. They apply the same integral
			 * themselves, at their own depth, from these. Density zero means no fog this frame. */
			public static readonly int AirParamsId = Shader.PropertyToID("_FishAirFogParams");
			public static readonly int AirColorId = Shader.PropertyToID("_FishAirFogColor");
			public static readonly int AirSunId = Shader.PropertyToID("_FishAirFogSun");
			public static readonly int AirSunColorId = Shader.PropertyToID("_FishAirFogSunColor");
			public static readonly int AirRangeId = Shader.PropertyToID("_FishAirFogRange");

			private Material material;
			private FishHeightFogFeature settings;

			public void Setup(Material fogMaterial, FishHeightFogFeature feature)
			{
				material = fogMaterial;
				settings = feature;
			}

			private class FogData
			{
				public Material Material;
			}

			public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
			{
				// No fog for transparents unless this pass finds some below.
				Shader.SetGlobalVector(AirParamsId, Vector4.zero);
				if (material == null || settings == null)
				{
					return;
				}
				UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
				UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
				if (resourceData.isActiveTargetBackBuffer)
				{
					return;
				}

				/* The weather's own fog channels, which every other presenter reads too:
				 * x density, y the layer's height, z the volumetric share. Nothing is added to the
				 * wire or to the globals for this pass — it reads what is already there. */
				Vector4 weatherFog = Shader.GetGlobalVector(WeatherFogId);
				float density = weatherFog.x * settings.DensityScale;
				if (density <= 1e-5f)
				{
					// No fog worth drawing. Skipping the pass entirely is the point of checking.
					return;
				}

				/* The authored falloff, stretched by the weather's fog height. A mist that the
				 * forecast says is shallow should lie in the hollows; one it says is deep should
				 * reach the ridges. */
				float falloff = Mathf.Max(1f, settings.FalloffHeight * Mathf.Lerp(0.5f, 2f, Mathf.Clamp01(weatherFog.y)));

				Matrix4x4 view = cameraData.GetViewMatrix();
				Matrix4x4 projection = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), true);
				material.SetMatrix(InverseVPId, (projection * view).inverse);
				material.SetVector(ParamsId, new Vector4(density, falloff, settings.BaseHeight, settings.MaximumOpacity));

				/* The sky's own fog colour, so the fog matches the air it is in: at sunset it goes
				 * orange with the horizon instead of staying the grey it was at noon. */
				material.SetVector(ColorId, RenderSettings.fogColor);

				SkySystem sky = SkySystem.Instance;
				Light sun = sky != null ? sky.Sun : null;
				bool hasSun = sun != null && settings.SunInscatter > 0.001f;
				if (hasSun)
				{
					Vector3 toLight = -sun.transform.forward;
					material.SetVector(SunId, new Vector4(toLight.x, toLight.y, toLight.z, settings.SunInscatter));
					Color lit = sun.color * Mathf.Max(0.05f, sun.intensity);
					material.SetVector(SunColorId, new Vector4(lit.r, lit.g, lit.b, 0f));
					material.EnableKeyword("FISH_FOG_SUN");
				}
				else
				{
					material.DisableKeyword("FISH_FOG_SUN");
				}

				material.SetVector(RangeId, new Vector4(settings.StartDistance, settings.EndDistance, cameraData.worldSpaceCameraPos.y, 0f));

				Shader.SetGlobalVector(AirParamsId, new Vector4(density, falloff, settings.BaseHeight, settings.MaximumOpacity));
				Shader.SetGlobalVector(AirColorId, RenderSettings.fogColor);
				if (hasSun)
				{
					Vector3 toLight = -sun.transform.forward;
					Color lit = sun.color * Mathf.Max(0.05f, sun.intensity);
					Shader.SetGlobalVector(AirSunId, new Vector4(toLight.x, toLight.y, toLight.z, settings.SunInscatter));
					Shader.SetGlobalVector(AirSunColorId, new Vector4(lit.r, lit.g, lit.b, 0f));
				}
				else
				{
					Shader.SetGlobalVector(AirSunId, Vector4.zero);
				}
				Shader.SetGlobalVector(AirRangeId, new Vector4(settings.StartDistance, settings.EndDistance, 0f, 0f));

				using (var builder = renderGraph.AddRasterRenderPass<FogData>(PassName, out FogData data))
				{
					data.Material = material;
					builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
					builder.UseAllGlobalTextures(true);
					if (resourceData.cameraDepthTexture.IsValid())
					{
						builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
					}
					builder.AllowPassCulling(false);
					builder.SetRenderFunc((FogData d, RasterGraphContext context) =>
						Blitter.BlitTexture(context.cmd, Vector2.one, d.Material, 0));
				}
			}
		}
	}
}
