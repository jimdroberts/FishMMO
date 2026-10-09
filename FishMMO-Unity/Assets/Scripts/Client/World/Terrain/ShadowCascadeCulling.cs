using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace FishMMO.Client
{
	/// <summary>
	/// This camera's main-light shadow cascades, handed to the vegetation's shadow caster before the shadow maps are drawn,
	/// so a GPU-driven tree draws only into the cascades that need it (FishVegetationPasses.hlsl VegCasterCulled).
	/// </summary>
	/// <remarks>
	/// <para>
	/// An indirect draw (Graphics.RenderMeshIndirect) has one set of bounds round the camera, so URP puts every one into every
	/// cascade's caster list, and URP's own cross-cascade culling is off (ShadowUtils sets shadowCascadeBlendCullingFactor
	/// 1): every tree near the camera was drawn into all four cascades, its whole vertex stage run four times, and every far
	/// one into the three maps that do not even cover it. The trees' shadows were 0.65 ms, all of it vertex work
	/// (ScenePerfProbe, Flo Monolith's meadow, 2026-10-08).
	/// </para>
	/// <para>
	/// The caster cannot cull without knowing which cascade it is drawing and where each one is. URP keeps both to itself
	/// (its slices are internal), so they are worked out again here exactly as URP works them out (ShadowCulling
	/// .CullShadowCasters → ShadowUtils.ExtractDirectionalLightMatrix: the same light, cascade count, split, tile
	/// resolution and near plane, through the public CullingResults call), for this camera and this frame, and set as
	/// globals by a pass just before the shadows — never stale, and right for each camera when the Scene and Game views
	/// alternate. Which cascade a caster is drawing it tells from the projection itself: each cascade's orthographic half
	/// size is its culling sphere's radius, and the radii differ several-fold.
	/// </para>
	/// </remarks>
	public static class ShadowCascadeCulling
	{
		/// <summary>Off: every caster is drawn into every cascade, as before (the probe's A/B, variant `nocascadecull`).</summary>
		public static bool Enabled = true;

		/// <summary>xyz a cascade's culling sphere's centre, w its radius (not squared, unlike URP's receiver globals).</summary>
		private static readonly int SpheresId = Shader.PropertyToID("_FishCasterSpheres");
		/// <summary>How many cascades the spheres hold; under 2 the caster culls nothing.</summary>
		private static readonly int CascadesId = Shader.PropertyToID("_FishCasterCascades");

		private const int MaxCascades = 4;

		/// <summary>What the last game camera published, for probes: each cascade's sphere radius against its projection's half size.</summary>
		public static string LastReport { get; private set; } = "not run";

		/// <summary>The pass that publishes them; enqueued by FishCloudsFeature for every camera (every renderer carries it).</summary>
		public static readonly ScriptableRenderPass Pass = new PublishPass();

		private sealed class PassData
		{
			public readonly Vector4[] Spheres = new Vector4[MaxCascades];
			public int Cascades;
		}

		private sealed class PublishPass : ScriptableRenderPass
		{
			public PublishPass()
			{
				// Before the main light's shadow pass, which URP schedules at BeforeRenderingShadows.
				renderPassEvent = RenderPassEvent.BeforeRenderingShadows - 1;
			}

			public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
			{
				using (var builder = renderGraph.AddUnsafePass<PassData>("Fish Shadow Cascade Culling", out PassData data))
				{
					data.Cascades = Enabled ? Spheres(frameData, data.Spheres, frameData.Get<UniversalCameraData>().cameraType == CameraType.Game) : 0;
					builder.AllowPassCulling(false);
					builder.AllowGlobalStateModification(true);
					builder.SetRenderFunc((PassData d, UnsafeGraphContext context) =>
					{
						context.cmd.SetGlobalVectorArray(SpheresId, d.Spheres);
						context.cmd.SetGlobalFloat(CascadesId, d.Cascades);
					});
				}
			}
		}

		/// <summary>This camera's cascade spheres, as URP's own shadow culling computes them. Returns how many (0: no culling).</summary>
		private static int Spheres(ContextContainer frameData, Vector4[] spheres, bool report)
		{
			if (report)
			{
				LastReport = "none published";
			}
			UniversalRenderingData rendering = frameData.Get<UniversalRenderingData>();
			UniversalLightData lights = frameData.Get<UniversalLightData>();
			UniversalShadowData shadows = frameData.Get<UniversalShadowData>();
			int light = lights.mainLightIndex;
			int count = shadows.mainLightShadowCascadesCount;
			if (!shadows.supportsMainLightShadows || light < 0 || count < 2 || count > MaxCascades || light >= lights.visibleLights.Length)
			{
				if (report)
				{
					LastReport = $"none: shadows {shadows.supportsMainLightShadows}, main light {light}, cascades {count}";
				}
				return 0;
			}
			Light source = lights.visibleLights[light].light;
			if (source == null || lights.visibleLights[light].lightType != LightType.Directional)
			{
				return 0;
			}
			// UniversalRenderPipeline.InitializeMainLightShadowResolution's sizes.
			int width = shadows.mainLightShadowmapWidth;
			int height = count == 2 ? shadows.mainLightShadowmapHeight >> 1 : shadows.mainLightShadowmapHeight;
			int resolution = ShadowUtils.GetMaxTileResolutionInAtlas(width, height, count);
			CullingResults cull = rendering.cullResults;
			var text = report ? new System.Text.StringBuilder($"{count} cascades, tile {resolution}:") : null;
			for (int i = 0; i < count; i++)
			{
				if (!cull.ComputeDirectionalShadowMatricesAndCullingPrimitives(light, i, count, shadows.mainLightShadowCascadesSplit, resolution,
					source.shadowNearPlane, out Matrix4x4 view, out Matrix4x4 projection, out ShadowSplitData split))
				{
					if (report)
					{
						LastReport = $"none: cascade {i} not computed";
					}
					return 0;
				}
				spheres[i] = split.cullingSphere;
				if (text != null)
				{
					Vector3 centre = view.MultiplyPoint(split.cullingSphere);
					Vector4 clip = (projection * view) * new Vector4(split.cullingSphere.x, split.cullingSphere.y, split.cullingSphere.z, 1f);
					text.Append($" [{i}: r {split.cullingSphere.w:0.0} half {1f / Mathf.Abs(projection.m00):0.0}/{1f / Mathf.Abs(projection.m11):0.0} centre view {centre.x:0.00},{centre.y:0.00} clip {clip.x:0.000},{clip.y:0.000}]");
				}
			}
			if (text != null)
			{
				LastReport = text.ToString();
			}
			for (int i = count; i < MaxCascades; i++)
			{
				spheres[i] = Vector4.zero;
			}
			return count;
		}
	}
}
