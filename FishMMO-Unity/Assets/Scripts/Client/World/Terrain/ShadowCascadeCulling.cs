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
		/// <remarks>
		/// Formatted only when read. The pass records the raw numbers every frame (no allocation); building this string in the
		/// pass itself was ~2.7 KB of garbage every frame for a line a probe reads once.
		/// </remarks>
		public static string LastReport => FormatReport();

		private enum ReportKind { NotRun, NonePublished, NoShadows, CascadeNotComputed, Published }

		private struct CascadeReport
		{
			public float Radius;
			public float HalfX;
			public float HalfY;
			public Vector2 CentreView;
			public Vector2 Clip;
		}

		private static ReportKind reportKind = ReportKind.NotRun;
		private static bool reportShadows;
		private static int reportLight;
		private static int reportCount;
		private static int reportResolution;
		private static int reportFailedCascade;
		private static readonly CascadeReport[] reportCascades = new CascadeReport[MaxCascades];

		private static string FormatReport()
		{
			switch (reportKind)
			{
				case ReportKind.NotRun:
					return "not run";
				case ReportKind.NonePublished:
					return "none published";
				case ReportKind.NoShadows:
					return $"none: shadows {reportShadows}, main light {reportLight}, cascades {reportCount}";
				case ReportKind.CascadeNotComputed:
					return $"none: cascade {reportFailedCascade} not computed";
			}
			var text = new System.Text.StringBuilder($"{reportCount} cascades, tile {reportResolution}:");
			for (int i = 0; i < reportCount; i++)
			{
				CascadeReport c = reportCascades[i];
				text.Append($" [{i}: r {c.Radius:0.0} half {c.HalfX:0.0}/{c.HalfY:0.0} centre view {c.CentreView.x:0.00},{c.CentreView.y:0.00} clip {c.Clip.x:0.000},{c.Clip.y:0.000}]");
			}
			return text.ToString();
		}

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
				reportKind = ReportKind.NonePublished;
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
					reportKind = ReportKind.NoShadows;
					reportShadows = shadows.supportsMainLightShadows;
					reportLight = light;
					reportCount = count;
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
			for (int i = 0; i < count; i++)
			{
				if (!cull.ComputeDirectionalShadowMatricesAndCullingPrimitives(light, i, count, shadows.mainLightShadowCascadesSplit, resolution,
					source.shadowNearPlane, out Matrix4x4 view, out Matrix4x4 projection, out ShadowSplitData split))
				{
					if (report)
					{
						reportKind = ReportKind.CascadeNotComputed;
						reportFailedCascade = i;
					}
					return 0;
				}
				spheres[i] = split.cullingSphere;
				if (report)
				{
					Vector3 centre = view.MultiplyPoint(split.cullingSphere);
					Vector4 clip = (projection * view) * new Vector4(split.cullingSphere.x, split.cullingSphere.y, split.cullingSphere.z, 1f);
					reportCascades[i] = new CascadeReport
					{
						Radius = split.cullingSphere.w,
						HalfX = 1f / Mathf.Abs(projection.m00),
						HalfY = 1f / Mathf.Abs(projection.m11),
						CentreView = new Vector2(centre.x, centre.y),
						Clip = new Vector2(clip.x, clip.y),
					};
				}
			}
			if (report)
			{
				reportKind = ReportKind.Published;
				reportCount = count;
				reportResolution = resolution;
			}
			for (int i = count; i < MaxCascades; i++)
			{
				spheres[i] = Vector4.zero;
			}
			return count;
		}
	}
}
