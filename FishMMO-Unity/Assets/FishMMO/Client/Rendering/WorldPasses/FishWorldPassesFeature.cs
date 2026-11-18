using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FishMMO.Client
{
	/// <summary>
	/// The world's shared render passes, on every renderer: the depth pyramid (occlusion culling for instanced props), the
	/// shadow cascade bounds (casters skip cascades that do not need them), the WebGPU light table (every lit pass reads its
	/// additional lights and probes from it) and the cached main-light shadows.
	/// </summary>
	/// <remarks>
	/// These were queued by <c>FishCloudsFeature</c>, the one feature every renderer happened to carry, so the clouds
	/// owned the world's lighting: a game without the weather module would have lost its light table and shadow cache with
	/// them. They are core now (Phase 4 of the core and modules plan, 2026-11-17), and the clouds draw clouds.
	/// </remarks>
	public class FishWorldPassesFeature : ScriptableRendererFeature
	{
		[Tooltip("The cached main-light shadows' merge shader (Hidden/FishMMO/Shadow Cache Merge). Referenced here so a build " +
			"carries it: CachedMainLightShadows merges its cached static casters into URP's shadow map with it.")]
		public Shader ShadowCacheMergeShader;

		[Tooltip("The compute holding the depth pyramid and light table kernels (Assets/FishMMO/Shaders/FishWorldPasses.compute). " +
			"Left empty, the passes find it by path in the editor; a build needs it assigned.")]
		[UnityEngine.Serialization.FormerlySerializedAs("PassesCompute")]
		public ComputeShader PassesCompute;

		/// <summary>The compute the passes take their kernels from, as the live feature assigned it.</summary>
		public static ComputeShader Compute { get; private set; }

		public override void Create()
		{
			if (PassesCompute != null)
			{
				Compute = PassesCompute;
			}
		}

		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			if (PassesCompute != null && Compute != PassesCompute)
			{
				Compute = PassesCompute;
			}

			// Every game camera's depth pyramid, for the instanced props' occlusion culling next frame.
			if (renderingData.cameraData.cameraType == CameraType.Game)
			{
				renderer.EnqueuePass(FishDepthPyramid.Pass);
			}
			// Every camera's shadow cascades, for the GPU-driven casters to skip the cascades that do not need them: every
			// camera, since each draws its own shadows and must not read another's.
			renderer.EnqueuePass(ShadowCascadeCulling.Pass);
			// WebGPU: every camera's light table, after URP's shadow passes and before anything is lit.
			if (AdditionalLightTable.Needed)
			{
				renderer.EnqueuePass(AdditionalLightTable.Pass);
			}
			// The cached main-light shadows (off by default): the cache pass before URP's shadows, the merge after; both
			// return at once for a camera they do not serve (CachedMainLightShadows.AppliesTo).
			if (renderingData.cameraData.cameraType == CameraType.Game)
			{
				if (CachedMainLightShadows.MergeShader == null)
				{
					CachedMainLightShadows.MergeShader = ShadowCacheMergeShader;
				}
				renderer.EnqueuePass(CachedMainLightShadows.CachePass);
				renderer.EnqueuePass(CachedMainLightShadows.MergePass);
			}
		}
	}
}
