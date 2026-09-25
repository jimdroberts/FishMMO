using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

namespace FishMMO.Client
{
	/// <summary>
	/// Volumetric fog you can see light shafts inside, built on a froxel grid and compute (P5).
	/// </summary>
	/// <remarks>
	/// <para>
	/// The frustum is diced into voxels that follow the camera, so the cost is fixed per frame and
	/// does not care how far anything is. One compute pass decides what is in each froxel and how
	/// much light scatters out of it toward the viewer; a second walks each column outward
	/// accumulating that light and the transmittance through it; a full-screen pass then reads the
	/// result at (uv, depth) and composites it.
	/// </para>
	/// <para>
	/// <b>It is cheaper than it sounds — cheaper than what already ships.</b> 160 x 90 x 64 is about
	/// a million froxels at one sample each, at a fixed cost that does not scale with screen
	/// resolution. The volumetric cloud march beside it is 3.6M samples on the CHEAPEST tier and
	/// 37M on the highest, and it does scale with resolution. The expensive way to do volumetric fog
	/// is a second full raymarch per pixel; this is not that.
	/// </para>
	/// <para>
	/// <b>Compute, so WebGPU and desktop only.</b> WebGL2 has no compute shaders at all. There is no
	/// raymarched fallback and there should not be one: the analytic
	/// <see cref="FishHeightFogFeature"/> already runs everywhere, is a closed-form integral with no
	/// loop, and is what WebGL2 gets. Two code paths for the same effect is two things to keep in
	/// step and one of them would never be looked at.
	/// </para>
	/// </remarks>
	public class FishVolumetricFogFeature : ScriptableRendererFeature
	{
		public const string ApplyShaderName = "Hidden/FishMMO/Weather/VolumetricFogApply";
		public const string ComputeName = "FishVolumetricFog";

		[Tooltip("The compute shader. Left empty, the feature finds it by name.")]
		public ComputeShader Compute;

		[Tooltip("The compositing material. Left empty, the feature finds the shader itself.")]
		public Material ApplyMaterial;

		[Header("Froxel grid")]
		[Tooltip("Froxels across the screen. 160 x 90 is a sixth of 1080p and plenty: fog has no fine detail in it.")]
		[Min(32)] public int GridWidth = 160;
		[Min(18)] public int GridHeight = 90;
		[Tooltip("Slices from the near plane to the far. Spaced exponentially, so most sit where the fog is thickest.")]
		[Range(16, 128)] public int GridDepth = 64;

		[Header("Extent")]
		[Tooltip("Metres the volume starts at.")]
		[Min(0.1f)] public float NearDistance = 0.5f;
		[Tooltip("Metres the volume reaches. Beyond this the analytic height fog carries on alone.")]
		[Min(20f)] public float FarDistance = 500f;
		[Tooltip("How hard the slices bunch toward the camera. 1 is even in log space; higher crowds the near field harder.")]
		[Range(0.5f, 3f)] public float DepthCurve = 1.4f;

		[Header("Scattering")]
		[Tooltip("How much light the fog scatters rather than absorbs, per channel.")]
		public Color Albedo = new Color(0.92f, 0.94f, 1f);
		[Tooltip("How forward-biased the scattering is. Higher puts a tighter glow around a light seen through fog.")]
		[Range(0f, 0.95f)] public float Anisotropy = 0.6f;
		[Tooltip("Sky light reaching fog the sun cannot, so fog in shadow is dim rather than black.")]
		[Range(0f, 1f)] public float AmbientFloor = 0.12f;
		[Tooltip("Scales the weather's fog density into the volume.")]
		[Range(0f, 0.2f)] public float DensityScale = 0.03f;

		private FogPass pass;

		/// <summary>Whether this machine can run it at all.</summary>
		public static bool Supported => SystemInfo.supportsComputeShaders;

		public override void Create()
		{
			pass = new FogPass
			{
				// With the height fog, over the opaque world and under transparents.
				renderPassEvent = RenderPassEvent.BeforeRenderingTransparents + 2,
			};
			pass.ConfigureInput(ScriptableRenderPassInput.Depth);
		}

		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			if (!Supported)
			{
				return;
			}
			ComputeShader compute = ResolveCompute();
			Material material = ResolveMaterial();
			if (compute == null || material == null)
			{
				return;
			}
			if (renderingData.cameraData.cameraType != CameraType.Game && renderingData.cameraData.cameraType != CameraType.SceneView)
			{
				return;
			}
			pass.Setup(compute, material, this);
			renderer.EnqueuePass(pass);
		}

		private ComputeShader ResolveCompute()
		{
			if (Compute == null)
			{
				// Editor-only lookup; a build must have the reference assigned on the feature.
				#if UNITY_EDITOR
				string[] found = UnityEditor.AssetDatabase.FindAssets($"{ComputeName} t:ComputeShader");
				if (found.Length > 0)
				{
					Compute = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(UnityEditor.AssetDatabase.GUIDToAssetPath(found[0]));
				}
				#endif
			}
			return Compute;
		}

		private Material ResolveMaterial()
		{
			if (ApplyMaterial != null)
			{
				return ApplyMaterial;
			}
			Shader shader = Shader.Find(ApplyShaderName);
			if (shader == null)
			{
				return null;
			}
			ApplyMaterial = CoreUtils.CreateEngineMaterial(shader);
			return ApplyMaterial;
		}

		protected override void Dispose(bool disposing)
		{
			pass?.Dispose();
			pass = null;
			if (ApplyMaterial != null)
			{
				CoreUtils.Destroy(ApplyMaterial);
				ApplyMaterial = null;
			}
		}

		private sealed class FogPass : ScriptableRenderPass
		{
			private const string ScatterName = "Fish Volumetric Fog (scatter)";
			private const string IntegrateName = "Fish Volumetric Fog (integrate)";
			private const string ApplyName = "Fish Volumetric Fog (apply)";

			private static readonly int ScatterTexId = Shader.PropertyToID("_FishVolFogScatter");
			private static readonly int IntegratedTexId = Shader.PropertyToID("_FishVolFogIntegrated");
			private static readonly int VolumeId = Shader.PropertyToID("_FishVolFogVolume");
			private static readonly int InverseVPId = Shader.PropertyToID("_FishVolFogInverseVP");
			private static readonly int SizeId = Shader.PropertyToID("_FishVolFogSize");
			private static readonly int ParamsId = Shader.PropertyToID("_FishVolFogParams");
			private static readonly int RangeId = Shader.PropertyToID("_FishVolFogRange");
			private static readonly int AlbedoId = Shader.PropertyToID("_FishVolFogAlbedo");
			private static readonly int SunDirId = Shader.PropertyToID("_FishVolFogSunDir");
			private static readonly int SunColorId = Shader.PropertyToID("_FishVolFogSunColor");
			private static readonly int CameraId = Shader.PropertyToID("_FishVolFogCamera");
			private static readonly int WeatherFogId = Shader.PropertyToID("_FishWeatherFog");

			/* The integrated volume, published for TRANSPARENT surfaces: this pass fogs what is
			 * already in the frame, before the transparent queue, so the sea drawn after it came out
			 * clear through the thickest fog. It samples the same volume at its own depth. w is 1 when
			 * the volume is live this frame, 0 when it is not. */
			public static readonly int AirVolumeId = Shader.PropertyToID("_FishAirFogVolume");
			public static readonly int AirVolumeRangeId = Shader.PropertyToID("_FishAirFogVolumeRange");

			private ComputeShader compute;
			private Material material;
			private FishVolumetricFogFeature settings;
			private RenderTexture scatter, integrated;
			private int scatterKernel = -1, integrateKernel = -1;
			private int frame;

			public void Setup(ComputeShader computeShader, Material applyMaterial, FishVolumetricFogFeature feature)
			{
				compute = computeShader;
				material = applyMaterial;
				settings = feature;
			}

			public void Dispose()
			{
				if (scatter != null) { scatter.Release(); scatter = null; }
				if (integrated != null) { integrated.Release(); integrated = null; }
			}

			private bool EnsureVolumes(int w, int h, int d)
			{
				if (scatter != null && scatter.width == w && scatter.height == h && scatter.volumeDepth == d)
				{
					return true;
				}
				Dispose();
				var desc = new RenderTextureDescriptor(w, h, GraphicsFormat.R16G16B16A16_SFloat, 0)
				{
					dimension = TextureDimension.Tex3D,
					volumeDepth = d,
					enableRandomWrite = true,
					msaaSamples = 1,
				};
				scatter = new RenderTexture(desc) { name = "FishVolFogScatter", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
				integrated = new RenderTexture(desc) { name = "FishVolFogIntegrated", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
				return scatter.Create() && integrated.Create();
			}

			private class ApplyData
			{
				public Material Material;
				public Texture Volume;
			}

			public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
			{
				// No volume for transparents unless this pass fills one below.
				Shader.SetGlobalVector(AirVolumeRangeId, Vector4.zero);
				if (compute == null || material == null || settings == null)
				{
					return;
				}
				UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
				UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
				if (resourceData.isActiveTargetBackBuffer)
				{
					return;
				}

				// The weather's own fog channels. z is the volumetric share: a forecast can ask for
				// thick fog that is not volumetric, and this pass should stay out of the way then.
				Vector4 weatherFog = Shader.GetGlobalVector(WeatherFogId);
				float density = weatherFog.x * weatherFog.z * settings.DensityScale;
				if (density <= 1e-5f)
				{
					return;
				}

				if (scatterKernel < 0)
				{
					scatterKernel = compute.FindKernel("Scatter");
					integrateKernel = compute.FindKernel("Integrate");
				}
				int w = settings.GridWidth, h = settings.GridHeight, d = settings.GridDepth;
				if (!EnsureVolumes(w, h, d))
				{
					return;
				}

				Matrix4x4 view = cameraData.GetViewMatrix();
				Matrix4x4 projection = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), true);
				Vector3 camera = cameraData.worldSpaceCameraPos;

				float falloff = Mathf.Max(1f, 60f * Mathf.Lerp(0.5f, 2f, Mathf.Clamp01(weatherFog.y)));
				SkySystem sky = SkySystem.Instance;
				Light sun = sky != null ? sky.Sun : null;
				Vector3 toLight = sun != null ? -sun.transform.forward : Vector3.up;
				Color lit = sun != null ? sun.color * Mathf.Max(0.05f, sun.intensity) : Color.white;

				frame = (frame + 1) % 8;
				// Halton-ish jitter along the slice, so slice banding averages out across frames.
				float jitter = (frame * 0.618034f) % 1f;

				compute.SetMatrix(InverseVPId, (projection * view).inverse);
				compute.SetVector(SizeId, new Vector4(w, h, d, 0f));
				compute.SetVector(ParamsId, new Vector4(density, falloff, 0f, cameraData.camera.nearClipPlane));
				compute.SetVector(RangeId, new Vector4(settings.NearDistance, settings.FarDistance, settings.DepthCurve, Time.timeSinceLevelLoad));
				compute.SetVector(AlbedoId, new Vector4(settings.Albedo.r, settings.Albedo.g, settings.Albedo.b, settings.Anisotropy));
				compute.SetVector(SunDirId, new Vector4(toLight.x, toLight.y, toLight.z, 1f));
				compute.SetVector(SunColorId, new Vector4(lit.r, lit.g, lit.b, settings.AmbientFloor));
				compute.SetVector(CameraId, new Vector4(camera.x, camera.y, camera.z, jitter));

				/* Dispatched outside the render graph, against textures the pass owns. The graph's
				 * compute support wants its resources imported and its dependencies declared, and
				 * these two volumes are read only by the apply pass immediately after — the ordering
				 * is already guaranteed by the pass order, and importing them buys nothing. */
				var cmd = CommandBufferPool.Get();
				cmd.SetComputeTextureParam(compute, scatterKernel, ScatterTexId, scatter);
				cmd.DispatchCompute(compute, scatterKernel, Mathf.CeilToInt(w / 8f), Mathf.CeilToInt(h / 8f), Mathf.CeilToInt(d / 4f));
				cmd.SetComputeTextureParam(compute, integrateKernel, ScatterTexId, scatter);
				cmd.SetComputeTextureParam(compute, integrateKernel, IntegratedTexId, integrated);
				cmd.DispatchCompute(compute, integrateKernel, Mathf.CeilToInt(w / 8f), Mathf.CeilToInt(h / 8f), 1);
				Graphics.ExecuteCommandBuffer(cmd);
				CommandBufferPool.Release(cmd);

				material.SetTexture(VolumeId, integrated);
				material.SetVector(RangeId, new Vector4(settings.NearDistance, settings.FarDistance, settings.DepthCurve, d));
				Shader.SetGlobalTexture(AirVolumeId, integrated);
				Shader.SetGlobalVector(AirVolumeRangeId, new Vector4(settings.NearDistance, settings.FarDistance, settings.DepthCurve, 1f));

				using (var builder = renderGraph.AddRasterRenderPass<ApplyData>(ApplyName, out ApplyData data))
				{
					data.Material = material;
					data.Volume = integrated;
					builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
					builder.UseAllGlobalTextures(true);
					if (resourceData.cameraDepthTexture.IsValid())
					{
						builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
					}
					builder.AllowPassCulling(false);
					builder.SetRenderFunc((ApplyData a, RasterGraphContext context) =>
						Blitter.BlitTexture(context.cmd, Vector2.one, a.Material, 0));
				}
			}
		}
	}
}
