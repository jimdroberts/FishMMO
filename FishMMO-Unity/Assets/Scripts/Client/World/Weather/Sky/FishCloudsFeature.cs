using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace FishMMO.Client
{
	/// <summary>
	/// Draws the volumetric clouds: a raymarch at a fraction of the screen, steadied against the
	/// last frame, then composited over the sky and behind the world.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The clouds are a volume, not a picture painted on the sky. Everything about their shape,
	/// place and lighting comes from the globals <see cref="SkySystem"/> sets each frame, so this
	/// feature only has to march them: it owns the buffers and the order, nothing else.
	/// </para>
	/// <para>
	/// One system on every tier, scaled: the resolution, the number of steps, the detail and the
	/// temporal blend all come from the quality tier, so a browser and a desktop draw the same sky
	/// at different costs. The pass asks for the depth texture itself, so no pipeline asset has to
	/// be changed for it.
	/// </para>
	/// </remarks>
	public class FishCloudsFeature : ScriptableRendererFeature
	{
		public const string ShaderName = "Hidden/FishMMO/Weather/Clouds";

		[Tooltip("The cloud shader's material. Left empty, the feature finds the shader itself.")]
		public Material CloudMaterial;

		private CloudPass pass;

		/// <summary>
		/// The cloud buffer the last frame ended with: rgb scattered light, a transmittance (1 clear
		/// sky, 0 solid cloud), at the tier's resolution. The probe reads coverage off this instead
		/// of guessing it from pixel colours, which glare and sunlit tops both fool.
		/// </summary>
		public static Texture LastCloudBuffer => lastPass != null ? lastPass.LastBuffer : null;
		private static CloudPass lastPass;

		public override void Create()
		{
			pass = new CloudPass
			{
				// After the skybox and the opaque world, before transparents: the clouds sit behind
				// anything on the ground and in front of the sky.
				renderPassEvent = RenderPassEvent.BeforeRenderingTransparents,
			};
			pass.ConfigureInput(ScriptableRenderPassInput.Depth);
		}

		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			Material material = Resolve();
			if (material == null || !SkySystem.CloudsReady)
			{
				return;
			}
			if (renderingData.cameraData.cameraType != CameraType.Game && renderingData.cameraData.cameraType != CameraType.SceneView)
			{
				return;
			}
			pass.Setup(material);
			lastPass = pass;
			renderer.EnqueuePass(pass);
		}

		private Material Resolve()
		{
			if (CloudMaterial != null)
			{
				return CloudMaterial;
			}
			Shader shader = Shader.Find(ShaderName);
			if (shader == null)
			{
				return null;
			}
			CloudMaterial = CoreUtils.CreateEngineMaterial(shader);
			return CloudMaterial;
		}

		protected override void Dispose(bool disposing)
		{
			pass?.Dispose();
			pass = null;
		}

		/// <summary>The march, the steadying and the composite, in one pass object.</summary>
		private sealed class CloudPass : ScriptableRenderPass
		{
			private const string MarchName = "Fish Clouds (march)";
			private const string TemporalName = "Fish Clouds (steady)";
			private const string CompositeName = "Fish Clouds (composite)";
			private const string RayName = "Fish Clouds (god rays)";
			private const string RayCompositeName = "Fish Clouds (god rays composite)";

			private static readonly int InverseVPId = Shader.PropertyToID("_FishCloudInverseVP");
			private static readonly int PreviousVPId = Shader.PropertyToID("_FishCloudPreviousVP");
			private static readonly int MarchParamsId = Shader.PropertyToID("_FishCloudMarchParams");
			private static readonly int LodParamsId = Shader.PropertyToID("_FishCloudLodParams");
			private static readonly int TemporalId = Shader.PropertyToID("_FishCloudTemporal");
			private static readonly int CurrentId = Shader.PropertyToID("_FishCloudCurrent");
			private static readonly int HistoryId = Shader.PropertyToID("_FishCloudHistory");
			private static readonly int BufferId = Shader.PropertyToID("_FishCloudBuffer");
			private static readonly int ScreenId = Shader.PropertyToID("_FishCloudScreen");
			private static readonly int RaySourceId = Shader.PropertyToID("_FishGodRaySource");
			private static readonly int RayBufferId = Shader.PropertyToID("_FishGodRayBuffer");
			private static readonly int RayCloudsId = Shader.PropertyToID("_FishGodRayClouds");
			private static readonly int RayDirId = Shader.PropertyToID("_FishGodRayDir");
			private static readonly int RayParamsId = Shader.PropertyToID("_FishGodRayParams");
			private static readonly int RayColorId = Shader.PropertyToID("_FishGodRayColor");
			private static readonly int RayMaskId = Shader.PropertyToID("_FishGodRayMask");
			private static readonly int RayVPId = Shader.PropertyToID("_FishGodRayVP");

			private Material material;
			private RTHandle[] history;

			/// <summary>The buffer the last frame's composite was drawn from.</summary>
			public Texture LastBuffer => history != null && history[historyIndex] != null ? history[historyIndex].rt : null;
			private int historyIndex;
			private bool historyValid;
			private Matrix4x4 previousViewProjection = Matrix4x4.identity;
			private int frame;

			public void Setup(Material cloudMaterial)
			{
				material = cloudMaterial;
			}

			public void Dispose()
			{
				if (history == null)
				{
					return;
				}
				foreach (RTHandle handle in history)
				{
					handle?.Release();
				}
				history = null;
			}

			private class MarchData
			{
				public Material Material;
				public int Pass;
				public TextureHandle Source;
				public TextureHandle History;
				public TextureHandle Clouds;
			}

			public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
			{
				if (material == null || !SkySystem.CloudsReady)
				{
					return;
				}
				UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
				UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
				if (resourceData.isActiveTargetBackBuffer)
				{
					return;
				}
				SkySystem sky = SkySystem.Instance;
				CloudTierSettings tier = sky.CloudTier;

				int width = Mathf.Max(16, Mathf.RoundToInt(cameraData.cameraTargetDescriptor.width * tier.Resolution));
				int height = Mathf.Max(16, Mathf.RoundToInt(cameraData.cameraTargetDescriptor.height * tier.Resolution));
				EnsureHistory(width, height);

				Matrix4x4 view = cameraData.GetViewMatrix();
				Matrix4x4 projection = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), true);
				Matrix4x4 viewProjection = projection * view;
				material.SetMatrix(InverseVPId, viewProjection.inverse);
				material.SetMatrix(PreviousVPId, previousViewProjection);
				material.SetVector(MarchParamsId, new Vector4(tier.Steps, tier.Detail, frame, sky.CloudFarDistance));
				// How wide one marched pixel's cone opens, in metres per metre of distance. The
				// projection's [1][1] is 1/tan(halfFov), so 2/(m11 * height) is the height of one
				// pixel a metre in front of the camera. It has to be worked out here and nowhere
				// else: this is the only place that knows the buffer is a fraction of the screen,
				// and a half-resolution march has pixels twice as wide as the display's.
				float m11 = Mathf.Abs(cameraData.GetProjectionMatrix().m11);
				float spread = m11 > 1e-4f ? 2f / (m11 * height) : 0f;
				Shader.SetGlobalVector(LodParamsId, new Vector4(spread, sky.CloudMaxLod, 1f / sky.CloudLodSharpness, 0f));
				bool temporal = tier.Temporal && historyValid;
				material.SetVector(TemporalId, new Vector4(temporal ? tier.TemporalBlend : 0f, temporal ? 1f : 0f, sky.CloudLayerCentre, 0f));

				var marchDesc = new TextureDesc(width, height)
				{
					colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat,
					name = "FishCloudsMarch",
					clearBuffer = false,
					filterMode = FilterMode.Bilinear,
					wrapMode = TextureWrapMode.Clamp,
				};
				TextureHandle marched = renderGraph.CreateTexture(marchDesc);
				TextureHandle historyRead = renderGraph.ImportTexture(history[historyIndex]);
				TextureHandle historyWrite = renderGraph.ImportTexture(history[1 - historyIndex]);

				// 1. March the volume at the tier's resolution.
				using (var builder = renderGraph.AddRasterRenderPass<MarchData>(MarchName, out MarchData data))
				{
					data.Material = material;
					data.Pass = 0;
					builder.SetRenderAttachment(marched, 0);
					builder.UseAllGlobalTextures(true);
					if (resourceData.cameraDepthTexture.IsValid())
					{
						builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
					}
					builder.AllowPassCulling(false);
					builder.SetRenderFunc((MarchData d, RasterGraphContext context) =>
						Blitter.BlitTexture(context.cmd, Vector2.one, d.Material, d.Pass));
				}

				// 2. Blend it with the last frame, so the jitter averages out instead of fizzing.
				TextureHandle steadied = marched;
				if (tier.Temporal)
				{
					using (var builder = renderGraph.AddRasterRenderPass<MarchData>(TemporalName, out MarchData data))
					{
						data.Material = material;
						data.Pass = 1;
						data.Source = marched;
						data.History = historyRead;
						builder.SetRenderAttachment(historyWrite, 0);
						builder.UseTexture(marched, AccessFlags.Read);
						builder.UseTexture(historyRead, AccessFlags.Read);
						builder.AllowPassCulling(false);
						builder.SetRenderFunc((MarchData d, RasterGraphContext context) =>
						{
							d.Material.SetTexture(CurrentId, d.Source);
							d.Material.SetTexture(HistoryId, d.History);
							Blitter.BlitTexture(context.cmd, Vector2.one, d.Material, d.Pass);
						});
					}
					steadied = historyWrite;
				}

				// 3. Composite: scattered light added, what is behind kept by the transmittance.
				using (var builder = renderGraph.AddRasterRenderPass<MarchData>(CompositeName, out MarchData data))
				{
					data.Material = material;
					data.Pass = 2;
					data.Source = steadied;
					builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
					builder.UseTexture(steadied, AccessFlags.Read);
					builder.AllowPassCulling(false);
					builder.SetRenderFunc((MarchData d, RasterGraphContext context) =>
					{
						d.Material.SetTexture(BufferId, d.Source);
						Blitter.BlitTexture(context.cmd, Vector2.one, d.Material, d.Pass);
					});
				}

				// 4. God rays: shafts from the sun, or from around a body eclipsing it. The clouds
				// are already in the frame, so they cast the shafts they should.
				if (sky.GodRayIntensity > 0.001f && SkySystem.DrawGodRays)
				{
					DrawGodRays(renderGraph, resourceData, sky, steadied, viewProjection, width / 2, height / 2);
				}

				// The sky bodies are drawn in the transparent queue, which runs after this composite,
				// so a moon or a planet is otherwise painted over the cloud that should be in front
				// of it. They read this buffer's transmittance to put themselves back behind it.
				//
				// Published here, on the CPU, and not with SetGlobalTexture inside the composite
				// pass: binding a graph-managed target as a global from within a raster pass upsets
				// RenderGraph's tracking of it and the whole frame comes out empty. The steadied
				// result lives in an imported RTHandle, which is a plain texture the rest of the
				// frame can read, so hand that over directly.
				bool published = tier.Temporal && history[1 - historyIndex] != null && history[1 - historyIndex].rt != null;
				if (published)
				{
					Shader.SetGlobalTexture(BufferId, history[1 - historyIndex].rt);
				}
				// SkySystem clears this each frame before anything renders; raising it here is what
				// says the buffer above is real. Left at zero, the bodies do not attenuate at all,
				// which is the right way to fail: an unbound buffer samples as zero and would
				// otherwise read as "fully occluded" and empty the sky.
				Shader.SetGlobalVector(ScreenId, new Vector4(published ? 1f : 0f, 0f, 0f, 0f));

				previousViewProjection = viewProjection;
				historyValid = tier.Temporal;
				historyIndex = 1 - historyIndex;
				frame++;
			}

			/// <summary>
			/// Gathers the light still reaching the camera from around the sun into shafts, and adds
			/// them to the frame.
			/// </summary>
			/// <remarks>
			/// The shafts are gathered from the frame itself: a pixel contributes only where the sky
			/// shows through, so the world blocks a shaft by being solid and a cloud blocks it by being
			/// dark. During an eclipse the light comes from the ring around the body covering the sun,
			/// which is exactly where the rays should rake out from.
			/// </remarks>
			private void DrawGodRays(RenderGraph renderGraph, UniversalResourceData resourceData, SkySystem sky, TextureHandle clouds, Matrix4x4 viewProjection, int width, int height)
			{
				material.SetMatrix(RayVPId, viewProjection);
				Vector3 direction = sky.GodRayDirection;
				material.SetVector(RayDirId, new Vector4(direction.x, direction.y, direction.z, 0f));
				// Decay per tap, how sharply a shaft narrows, how far across the screen it reaches, taps.
				// An eclipse's rays belong around the body, not across the whole sky, so they reach a
				// shorter way — what they rake around is the silhouette itself.
				float reach = sky.GodRayEclipse > 0.02f ? 0.35f : 0.8f;
				material.SetVector(RayParamsId, new Vector4(0.975f, 1.2f, reach, 28f));
				material.SetVector(RayMaskId, new Vector4(0.25f, 0.2f, 0f, 0f));
				Color colour = sky.GodRayColor;
				colour.a = sky.GodRayIntensity * 1.5f;
				material.SetColor(RayColorId, colour);

				var rayDesc = new TextureDesc(Mathf.Max(8, width), Mathf.Max(8, height))
				{
					colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat,
					name = "FishGodRays",
					clearBuffer = false,
					filterMode = FilterMode.Bilinear,
					wrapMode = TextureWrapMode.Clamp,
				};
				TextureHandle rays = renderGraph.CreateTexture(rayDesc);

				using (var builder = renderGraph.AddRasterRenderPass<MarchData>(RayName, out MarchData data))
				{
					data.Material = material;
					data.Pass = 4;
					data.Source = resourceData.activeColorTexture;
					data.Clouds = clouds;
					builder.SetRenderAttachment(rays, 0);
					builder.UseTexture(resourceData.activeColorTexture, AccessFlags.Read);
					builder.UseTexture(clouds, AccessFlags.Read);
					// The depth texture has to be asked for here too: by this point in the frame its last
					// reader was the march, and without this it is gone and every pixel reads as solid.
					if (resourceData.cameraDepthTexture.IsValid())
					{
						builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
					}
					builder.UseAllGlobalTextures(true);
					builder.AllowPassCulling(false);
					builder.SetRenderFunc((MarchData d, RasterGraphContext context) =>
					{
						d.Material.SetTexture(RaySourceId, d.Source);
						d.Material.SetTexture(RayCloudsId, d.Clouds);
						Blitter.BlitTexture(context.cmd, Vector2.one, d.Material, d.Pass);
					});
				}

				using (var builder = renderGraph.AddRasterRenderPass<MarchData>(RayCompositeName, out MarchData data))
				{
					data.Material = material;
					data.Pass = 5;
					data.Source = rays;
					builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
					builder.UseTexture(rays, AccessFlags.Read);
					builder.AllowPassCulling(false);
					builder.SetRenderFunc((MarchData d, RasterGraphContext context) =>
					{
						d.Material.SetTexture(RayBufferId, d.Source);
						Blitter.BlitTexture(context.cmd, Vector2.one, d.Material, d.Pass);
					});
				}
			}

			private void EnsureHistory(int width, int height)
			{
				if (history != null && history[0] != null && history[0].rt != null
					&& history[0].rt.width == width && history[0].rt.height == height)
				{
					return;
				}
				Dispose();
				history = new RTHandle[2];
				for (int i = 0; i < 2; i++)
				{
					history[i] = RTHandles.Alloc(width, height, colorFormat:
						UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat,
						filterMode: FilterMode.Bilinear, wrapMode: TextureWrapMode.Clamp, name: $"FishCloudsHistory{i}");
				}
				historyValid = false;
			}
		}
	}
}
