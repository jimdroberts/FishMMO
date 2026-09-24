using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace FishMMO.Client
{
	/// <summary>
	/// What the weather does to the view itself: drops on the lens, frost at the edges, dust hazing
	/// it over (P5).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Shelter gates all of it.</b> These are about being OUT in the weather, so every one fades
	/// as the viewer goes under cover. Rain running down the screen while standing inside is the one
	/// thing that makes an overlay like this feel broken, and it is the easy mistake: the weather is
	/// still falling outside, so the naive version keeps drawing it.
	/// </para>
	/// <para>
	/// Procedural — no texture to author, load, or keep resident, and no tiling seam for the drops
	/// to repeat along. One full-screen pass that early-outs to nothing in clear weather.
	/// </para>
	/// <para>
	/// It reads the substance too, so acid rain beads yellow on the lens and water rain does not,
	/// without this pass knowing what a substance is.
	/// </para>
	/// </remarks>
	public class FishWeatherOverlayFeature : ScriptableRendererFeature
	{
		public const string ShaderName = "Hidden/FishMMO/Weather/Overlay";

		[Tooltip("The overlay material. Left empty, the feature finds the shader itself.")]
		public Material OverlayMaterial;

		[Header("How strong")]
		[Tooltip("Drops caught on the lens in rain. 0 turns them off entirely.")]
		[Range(0f, 2f)] public float Drops = 1f;

		[Tooltip("Frost creeping in from the edges when it is bitterly cold.")]
		[Range(0f, 2f)] public float Frost = 1f;

		[Tooltip("Dust and ash hazing the view over.")]
		[Range(0f, 2f)] public float Dust = 1f;

		private OverlayPass pass;

		public override void Create()
		{
			pass = new OverlayPass
			{
				// After everything, including transparents and the fog: this is on the viewer's
				// lens, in front of the whole world rather than part of it.
				renderPassEvent = RenderPassEvent.AfterRenderingTransparents,
			};
		}

		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			Material material = Resolve();
			if (material == null)
			{
				return;
			}
			// The scene view and previews are not somebody's eyes; putting rain on them would make
			// the editor unusable in a storm.
			if (renderingData.cameraData.cameraType != CameraType.Game)
			{
				return;
			}
			pass.Setup(material, this);
			renderer.EnqueuePass(pass);
		}

		private Material Resolve()
		{
			if (OverlayMaterial != null)
			{
				return OverlayMaterial;
			}
			Shader shader = Shader.Find(ShaderName);
			if (shader == null)
			{
				return null;
			}
			OverlayMaterial = CoreUtils.CreateEngineMaterial(shader);
			return OverlayMaterial;
		}

		protected override void Dispose(bool disposing)
		{
			if (OverlayMaterial != null)
			{
				CoreUtils.Destroy(OverlayMaterial);
				OverlayMaterial = null;
			}
			pass = null;
		}

		private sealed class OverlayPass : ScriptableRenderPass
		{
			private const string PassName = "Fish Weather Overlay";

			private static readonly int ParamsId = Shader.PropertyToID("_FishOverlayParams");
			private static readonly int PrecipId = Shader.PropertyToID("_FishWeatherPrecip");
			private static readonly int MixId = Shader.PropertyToID("_FishWeatherMix");
			private static readonly int MiscId = Shader.PropertyToID("_FishWeatherMisc");

			private Material material;
			private FishWeatherOverlayFeature settings;

			public void Setup(Material overlayMaterial, FishWeatherOverlayFeature feature)
			{
				material = overlayMaterial;
				settings = feature;
			}

			private class OverlayData
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
				if (resourceData.isActiveTargetBackBuffer)
				{
					return;
				}

				/* Is there anything to draw at all? Shelter is in Misc.z, and under full cover none
				 * of the three can contribute, so the pass is skipped rather than run to produce a
				 * transparent frame. Clear weather indoors costs nothing. */
				Vector4 precip = Shader.GetGlobalVector(PrecipId);
				Vector4 mix = Shader.GetGlobalVector(MixId);
				Vector4 misc = Shader.GetGlobalVector(MiscId);
				float exposure = Mathf.Clamp01(1f - misc.z);
				/* Mirrors the shader's share test: a trace of rain inside falling snow is not rain.
				 * If these two ever disagree the pass is skipped while the shader would have drawn,
				 * or runs to produce nothing. */
				float frozen = mix.y + mix.z;
				float total = Mathf.Max(1e-4f, mix.x + frozen);
				float liquidShare = mix.x / total;
				float wet = mix.x * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.7f, liquidShare)) * precip.x * exposure * settings.Drops;
				float sticking = frozen * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.7f, 1f - liquidShare)) * precip.x * exposure * settings.Frost;
				float grit = mix.w * precip.x * exposure * settings.Dust;
				float cold = Mathf.Clamp01(-misc.y - 0.25f) * exposure * settings.Frost;
				if (wet <= 0.001f && sticking <= 0.001f && grit <= 0.001f && cold <= 0.001f)
				{
					return;
				}

				float aspect = cameraData.cameraTargetDescriptor.height > 0
					? cameraData.cameraTargetDescriptor.width / (float)cameraData.cameraTargetDescriptor.height
					: 1.7778f;
				material.SetVector(ParamsId, new Vector4(settings.Drops, settings.Frost, settings.Dust, aspect));

				/* A copy to read from. The pass refracts — it samples the frame at an OFFSET — so it
				 * cannot read and write the same texture: a pixel would sample a neighbour that had
				 * already been overwritten this frame, and the drops would smear into streaks that
				 * grow across the screen. */
				TextureDesc desc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
				desc.name = "FishWeatherOverlaySource";
				desc.clearBuffer = false;
				desc.depthBufferBits = 0;
				TextureHandle copy = renderGraph.CreateTexture(desc);
				using (var builder = renderGraph.AddRasterRenderPass<OverlayData>("Fish Weather Overlay (copy)", out OverlayData data))
				{
					data.Source = resourceData.activeColorTexture;
					builder.SetRenderAttachment(copy, 0);
					builder.UseTexture(resourceData.activeColorTexture, AccessFlags.Read);
					builder.AllowPassCulling(false);
					builder.SetRenderFunc((OverlayData d, RasterGraphContext context) =>
						Blitter.BlitTexture(context.cmd, d.Source, new Vector4(1f, 1f, 0f, 0f), 0f, false));
				}

				using (var builder = renderGraph.AddRasterRenderPass<OverlayData>(PassName, out OverlayData data))
				{
					data.Material = material;
					data.Source = copy;
					builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
					builder.UseTexture(copy, AccessFlags.Read);
					builder.UseAllGlobalTextures(true);
					builder.AllowPassCulling(false);
					builder.SetRenderFunc((OverlayData d, RasterGraphContext context) =>
						Blitter.BlitTexture(context.cmd, d.Source, new Vector4(1f, 1f, 0f, 0f), d.Material, 0));
				}
			}
		}
	}
}
