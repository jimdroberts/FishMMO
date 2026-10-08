using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace FishMMO.Client
{
	/// <summary>
	/// Each game camera's depth as a pyramid of farthest depths, built after its opaques, for the GPU culling of the
	/// instanced props, trees and details to drop what was hidden behind what was drawn (FishTerrainInstancing.compute
	/// FishOccluded, main view only).
	/// </summary>
	/// <remarks>
	/// Unity's own occlusion culling works only for static scene renderers; the props are data drawn indirectly
	/// (<see cref="CliffRockInstancing"/>), so nothing culled them behind a hill. The pyramid is last frame's: a
	/// prop's sphere is grown by how far the camera has moved since, and anything off last frame's screen is drawn,
	/// so a step reveals what it should. One atlas holds every level (level 0 at half the screen, the rest beside
	/// it), so one read-write texture builds and serves it.
	/// </remarks>
	public static class FishDepthPyramid
	{
		/// <summary>Off: nothing is culled by occlusion (the probe's A/B).</summary>
		public static bool Enabled = true;
		/// <summary>A camera that has moved further than this since its pyramid was drawn culls nothing by it.</summary>
		public const float MaxMoveMetres = 30f;

		private static readonly int ParamsId = Shader.PropertyToID("_FishHiZParams");
		private static readonly int TextureId = Shader.PropertyToID("_FishHiZ");
		private static readonly int ViewProjId = Shader.PropertyToID("_FishHiZViewProj");
		private static readonly int EyeId = Shader.PropertyToID("_FishHiZEye");
		private static readonly int ForwardId = Shader.PropertyToID("_FishHiZForward");
		private static readonly int SourceId = Shader.PropertyToID("_FishHiZSource");
		private static readonly int AtlasId = Shader.PropertyToID("_FishHiZAtlas");
		private static readonly int SourceSizeId = Shader.PropertyToID("_FishHiZSourceSize");
		private static readonly int ZBufferId = Shader.PropertyToID("_FishHiZZBuffer");
		private static readonly int LevelId = Shader.PropertyToID("_FishHiZLevel");

		private sealed class State
		{
			public RTHandle Atlas;
			public int Width, Height, Levels;
			public Matrix4x4 ViewProjection;
			public Vector3 Eye, Forward;
			public int Frame = -100;
		}

		private static readonly Dictionary<Camera, State> states = new Dictionary<Camera, State>();
		private static ComputeShader compute;
		private static int copyKernel = -1, reduceKernel = -1;

		/// <summary>The pass that builds a camera's pyramid; enqueued by FishCloudsFeature for game cameras.</summary>
		public static readonly ScriptableRenderPass Pass = new BuildPass();

		/// <summary>
		/// Hands the culling kernel this camera's pyramid, or tells it to cull nothing by occlusion: none yet, an old
		/// one, a camera that has jumped, or <paramref name="allowed"/> false (a diagnostic comparing with the CPU
		/// mirror, which knows nothing of depth).
		/// </summary>
		public static void Bind(CommandBuffer cmd, ComputeShader shader, int kernel, Camera camera, Vector3 eye, bool allowed)
		{
			bool usable = Enabled && allowed && camera != null && states.TryGetValue(camera, out State state)
				&& state.Atlas != null && state.Atlas.rt != null && Time.frameCount - state.Frame <= 2
				&& (eye - state.Eye).sqrMagnitude < MaxMoveMetres * MaxMoveMetres;
			if (!usable)
			{
				cmd.SetComputeVectorParam(shader, ParamsId, Vector4.zero);
				cmd.SetComputeTextureParam(shader, kernel, TextureId, Texture2D.blackTexture);
				return;
			}
			State s = states[camera];
			cmd.SetComputeVectorParam(shader, ParamsId, new Vector4(s.Width, s.Height, s.Levels, 1f));
			cmd.SetComputeTextureParam(shader, kernel, TextureId, s.Atlas);
			cmd.SetComputeMatrixParam(shader, ViewProjId, s.ViewProjection);
			cmd.SetComputeVectorParam(shader, EyeId, new Vector4(s.Eye.x, s.Eye.y, s.Eye.z, Vector3.Distance(eye, s.Eye) + 0.5f));
			cmd.SetComputeVectorParam(shader, ForwardId, new Vector4(s.Forward.x, s.Forward.y, s.Forward.z, SystemInfo.graphicsUVStartsAtTop ? 1f : 0f));
		}

		/// <summary>Forgets every camera's pyramid (a scene unloaded, the renderer torn down).</summary>
		public static void Clear()
		{
			foreach (State state in states.Values)
			{
				state.Atlas?.Release();
			}
			states.Clear();
		}

		private static bool EnsureCompute()
		{
			if (compute == null)
			{
				WeatherRenderProfile profile = WeatherRenderProfile.Active;
				compute = profile != null ? profile.TerrainInstancingCompute : null;
#if UNITY_EDITOR
				if (compute == null)
				{
					compute = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(TerrainGpuRenderer.ComputeAssetPath);
				}
#endif
				if (compute == null)
				{
					return false;
				}
				copyKernel = compute.FindKernel("FishHiZCopy");
				reduceKernel = compute.FindKernel("FishHiZReduce");
			}
			return copyKernel >= 0 && reduceKernel >= 0;
		}

		private static Vector4 ZBufferParams(Camera camera)
		{
			float near = camera.nearClipPlane;
			float far = camera.farClipPlane;
			float invNear = Mathf.Approximately(near, 0f) ? 0f : 1f / near;
			float invFar = Mathf.Approximately(far, 0f) ? 0f : 1f / far;
			float zc0 = 1f - far * invNear;
			float zc1 = far * invNear;
			var zBuffer = new Vector4(zc0, zc1, zc0 * invFar, zc1 * invFar);
			if (SystemInfo.usesReversedZBuffer)
			{
				zBuffer.y += zBuffer.x;
				zBuffer.x = -zBuffer.x;
				zBuffer.w += zBuffer.z;
				zBuffer.z = -zBuffer.z;
			}
			return zBuffer;
		}

		private sealed class PassData
		{
			public ComputeShader Compute;
			public int Copy, Reduce;
			public TextureHandle Depth, Atlas;
			public int Width, Height, Levels;
			public Vector4 SourceSize, ZBuffer;
		}

		private sealed class BuildPass : ScriptableRenderPass
		{
			public BuildPass()
			{
				renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
				ConfigureInput(ScriptableRenderPassInput.Depth);
			}

			public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
			{
				UniversalResourceData resources = frameData.Get<UniversalResourceData>();
				UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
				Camera camera = cameraData.camera;
				if (!Enabled || camera == null || !resources.cameraDepthTexture.IsValid() || !EnsureCompute())
				{
					return;
				}
				int screenWidth = cameraData.cameraTargetDescriptor.width;
				int screenHeight = cameraData.cameraTargetDescriptor.height;
				int width = Mathf.Max(1, (screenWidth + 1) / 2);
				int height = Mathf.Max(1, (screenHeight + 1) / 2);
				int levels = Mathf.FloorToInt(Mathf.Log(Mathf.Max(width, height), 2f)) + 1;
				if (!states.TryGetValue(camera, out State state))
				{
					state = new State();
					states.Add(camera, state);
				}
				if (state.Atlas == null || state.Width != width || state.Height != height)
				{
					state.Atlas?.Release();
					state.Atlas = RTHandles.Alloc(width + (width + 1) / 2, height + levels, colorFormat: GraphicsFormat.R32_SFloat,
						filterMode: FilterMode.Point, wrapMode: TextureWrapMode.Clamp, enableRandomWrite: true, name: "FishDepthPyramid");
					state.Width = width;
					state.Height = height;
					state.Levels = levels;
					state.Frame = -100;
				}

				TextureHandle atlas = renderGraph.ImportTexture(state.Atlas);
				using (var builder = renderGraph.AddComputePass<PassData>("Fish Depth Pyramid", out PassData data))
				{
					data.Compute = compute;
					data.Copy = copyKernel;
					data.Reduce = reduceKernel;
					data.Depth = resources.cameraDepthTexture;
					data.Atlas = atlas;
					data.Width = width;
					data.Height = height;
					data.Levels = levels;
					data.SourceSize = new Vector4(screenWidth, screenHeight, 0f, 0f);
					data.ZBuffer = ZBufferParams(camera);
					builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
					builder.UseTexture(atlas, AccessFlags.ReadWrite);
					builder.AllowPassCulling(false);
					builder.SetRenderFunc((PassData d, ComputeGraphContext context) =>
					{
						ComputeCommandBuffer cmd = context.cmd;
						cmd.SetComputeVectorParam(d.Compute, ParamsId, new Vector4(d.Width, d.Height, d.Levels, 1f));
						cmd.SetComputeVectorParam(d.Compute, SourceSizeId, d.SourceSize);
						cmd.SetComputeVectorParam(d.Compute, ZBufferId, d.ZBuffer);
						cmd.SetComputeTextureParam(d.Compute, d.Copy, SourceId, d.Depth);
						cmd.SetComputeTextureParam(d.Compute, d.Copy, AtlasId, d.Atlas);
						cmd.DispatchCompute(d.Compute, d.Copy, (d.Width + 7) / 8, (d.Height + 7) / 8, 1);
						int w = d.Width, h = d.Height;
						for (int level = 1; level < d.Levels; level++)
						{
							w = (w + 1) / 2;
							h = (h + 1) / 2;
							cmd.SetComputeIntParam(d.Compute, LevelId, level);
							cmd.SetComputeTextureParam(d.Compute, d.Reduce, AtlasId, d.Atlas);
							cmd.DispatchCompute(d.Compute, d.Reduce, (w + 7) / 8, (h + 7) / 8, 1);
						}
					});
				}

				// The camera this pyramid is of, as the clouds take it: its own projection, never URP's jittered one.
				Matrix4x4 projection = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
				state.ViewProjection = projection * cameraData.GetViewMatrix();
				state.Eye = cameraData.worldSpaceCameraPos;
				state.Forward = camera.transform.forward;
				state.Frame = Time.frameCount;
			}
		}
	}
}
