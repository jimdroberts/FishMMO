using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace FishMMO.Client
{
	/// <summary>
	/// WebGPU: URP's per-light arrays (additional lights, their shadow parameters and slices, their cookies, and the Forward+
	/// reflection probes) gathered once per camera into one storage buffer the lit passes read (FishLightTable.hlsl), so they
	/// are no longer members of every lit program's $Globals.
	/// </summary>
	/// <remarks>
	/// <para>
	/// On WebGPU URP's lighting cbuffers are plain globals (FishShadowVariants.hlsl: the pipelines must stay within the device's
	/// 10 dynamic uniform buffers), which put ~14 KB of arrays into each lit program's $Globals; Unity copies a program's whole
	/// $Globals on every pass application, and that copy was the largest engine cost left in the browser
	/// (Docs/Rendering/2026-11-03/webgpu-light-buffer).
	/// </para>
	/// <para>
	/// The table is not a second account of URP's light setup. A compute kernel (FishLightTableGather, in the profile's
	/// terrain-instancing compute, as the depth pyramid's kernels are) declares URP's arrays by URP's names and sizes, so its
	/// constants are filled from the very globals URP has just set, and copies them bit for bit. It runs after the shadow
	/// passes, when the last of them (the additional lights' shadow parameters and slices) has been set, and before anything
	/// is lit; every camera runs its own, so each camera's draws read its own lights.
	/// </para>
	/// </remarks>
	public static class AdditionalLightTable
	{
		// FishLightTableLayout.hlsl's sizes (WebGpuLightTableTests pins them equal, and equal to URP's limits on WebGPU).
		public const int Lights = 32;
		public const int Probes = 32;
		public const int LightRows = 12;
		public const int SliceRows = 4;
		public const int ProbeRows = 11;
		/// <summary>Lights, then shadow slices, then probes, then the cookie-enable word.</summary>
		public const int Rows = Lights * LightRows + Lights * SliceRows + Probes * ProbeRows + 1;
		/// <summary>One uint4 per row.</summary>
		public const int RowStride = 16;
		public const string KernelName = "FishLightTableGather";

		private static readonly int TableId = Shader.PropertyToID("_FishLightTable");
		private static readonly int OutId = Shader.PropertyToID("_FishLightTableOut");

		private static GraphicsBuffer table;
		private static ComputeShader compute;
		private static int kernel = -1;
		private static bool warned;

		/// <summary>Cameras gathered (probes and tests).</summary>
		public static int Gathers { get; private set; }

		/// <summary>The lit shaders read the table on this device (they are compiled to on WebGPU only).</summary>
		public static bool Needed => SystemInfo.graphicsDeviceType == GraphicsDeviceType.WebGPU;

		/// <summary>The pass that fills it; enqueued by FishCloudsFeature for every camera (every renderer carries it).</summary>
		public static readonly ScriptableRenderPass Pass = new GatherPass();

		/// <summary>The table, created zeroed and bound globally on first use, so a draw before any gather reads no light.</summary>
		public static GraphicsBuffer Table
		{
			get
			{
				if (table == null || !table.IsValid())
				{
					table = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Rows, RowStride) { name = "Fish Light Table" };
					table.SetData(new uint[Rows * 4]);
					Shader.SetGlobalBuffer(TableId, table);
				}
				return table;
			}
		}

		public static void Release()
		{
			table?.Release();
			table = null;
		}

		/// <summary>The gather kernel and its compute: the active profile's, or found by path in the editor.</summary>
		public static bool EnsureCompute(ComputeShader assigned = null)
		{
			if (assigned != null && assigned != compute)
			{
				compute = assigned;
				kernel = -1;
			}
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
				kernel = -1;
			}
			if (compute != null && kernel < 0 && compute.HasKernel(KernelName))
			{
				kernel = compute.FindKernel(KernelName);
			}
			return compute != null && kernel >= 0;
		}

		/// <summary>Records the gather into a command buffer (the pass, and tests that dispatch it by hand).</summary>
		public static void Gather(CommandBuffer cmd, GraphicsBuffer into)
		{
			cmd.SetComputeBufferParam(compute, kernel, OutId, into);
			cmd.DispatchCompute(compute, kernel, (Rows + 63) / 64, 1, 1);
			cmd.SetGlobalBuffer(TableId, into);
		}

		private sealed class PassData
		{
			public ComputeShader Compute;
			public int Kernel;
			public GraphicsBuffer Table;
		}

		private sealed class GatherPass : ScriptableRenderPass
		{
			public GatherPass()
			{
				// URP's shadow passes set the additional lights' shadow parameters and slices; its "Setup Forward Lights" pass,
				// earlier still, the lights, cookies and reflection probes. Opaques (the first lit draws) come after.
				renderPassEvent = RenderPassEvent.AfterRenderingShadows;
			}

			public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
			{
				if (!Needed)
				{
					return;
				}
				if (!EnsureCompute())
				{
					if (!warned)
					{
						warned = true;
						Debug.LogWarning("[AdditionalLightTable] No " + KernelName + " kernel: WebGPU lit passes read no additional lights or probes.");
					}
					return;
				}
				using (var builder = renderGraph.AddUnsafePass<PassData>("Fish Light Table", out PassData data))
				{
					data.Compute = compute;
					data.Kernel = kernel;
					data.Table = Table;
					builder.AllowPassCulling(false);
					builder.AllowGlobalStateModification(true);
					builder.SetRenderFunc(static (PassData d, UnsafeGraphContext context) =>
					{
						context.cmd.SetComputeBufferParam(d.Compute, d.Kernel, OutId, d.Table);
						context.cmd.DispatchCompute(d.Compute, d.Kernel, (Rows + 63) / 64, 1, 1);
						context.cmd.SetGlobalBuffer(TableId, d.Table);
						Gathers++;
					});
				}
			}
		}
	}
}
