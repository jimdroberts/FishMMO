using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Client
{
	/// <summary>
	/// This renderer's part in the cached main-light shadows (<see cref="CachedMainLightShadows"/>): which of its batch-group
	/// shadow commands are static casters, a second set of indirect args for drawing them outside the batch group, and the
	/// draws themselves into the cache's cascade tiles.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Static casters</b> are the shadow view's commands whose material is not the swaying vegetation's: structures, rocks,
	/// cliffs and props (FishMMO/Weather Lit Indirect has no wind or time in its vertex stage). Trees and plants stay live, so
	/// their shadows keep swaying. While the cache serves the camera, the batch group leaves the static commands out of the
	/// main light's (orthographic) culling, and the cache pass draws them instead — only into the cascades it refreshes.
	/// </para>
	/// <para>
	/// <b>Args</b>: the batch group's args start every command's instances at 0 (DOTS adds the command's visibleOffset). The
	/// cache draws the procedural variants, whose instance index carries the run's start (<see cref="InstanceIdCarriesBase"/>),
	/// so a second buffer holds the same commands with startInstance = their slot's base, its counts written by the same
	/// finalize kernel after the culling. Where the instance index does not carry it (D3D, WebGPU without
	/// indirect-first-instance) the cache is not used (<see cref="CachedMainLightShadows.Supported"/>).
	/// </para>
	/// </remarks>
	public sealed partial class TerrainGpuRenderer
	{
		/// <summary>Every live renderer, for the cache pass (each draws its own static casters).</summary>
		internal static readonly List<TerrainGpuRenderer> Live = new List<TerrainGpuRenderer>();

		private GraphicsBuffer shadowCacheArgs;
		/// <summary>The cache's own culling (<see cref="RecordShadowCacheCull"/>): its slot counts and visible list, the live ones' shapes.</summary>
		private GraphicsBuffer shadowCacheCounts, shadowCacheVisible;
		/// <summary>The cache's draws' block: the batch block's buffers, but the cache's visible list.</summary>
		private readonly MaterialPropertyBlock shadowCacheBlock = new MaterialPropertyBlock();
		/// <summary>The camera's level choice (screen factor, orthographic, maximum level) as its last culling read it.</summary>
		private Vector4 shadowCacheLod;
		private int layoutSerial, flushSerial;
		private readonly Dictionary<Material, int> shadowCasterPasses = new Dictionary<Material, int>();
		/// <summary>The camera the batch group is armed for (<see cref="ArmBrg"/>), to ask the cache whether it serves it.</summary>
		private Camera brgCameraObject;

		/// <summary>This camera's shadow work as instance ranges (x the first, y one past the last), listed by AddWork.</summary>
		private readonly List<Vector2Int> shadowWorkRanges = new List<Vector2Int>();
		/// <summary>The shadow work the cache's last redraw culled, merged (<see cref="ShadowCasterCoverage"/>).</summary>
		private readonly List<Vector2Int> heldShadowRanges = new List<Vector2Int>();
		private int coverageSerial, coverageCheckedWork = -1;

		/// <summary>
		/// Changes whenever the static casters themselves may have changed (a layout or an instance upload) or this camera's
		/// shadow work reaches an instance the cache's last redraw did not cull (<see cref="CoverageSerial"/>). Not the camera's
		/// every move (2026-11-03): the cache culls them itself, from its own anchor and window (<see cref="RecordShadowCacheCull"/>),
		/// so a moving camera no longer redraws every cascade every frame.
		/// </summary>
		public int ShadowContentVersion => HashCode.Combine(layoutSerial, flushSerial, CoverageSerial());

		/// <summary>
		/// Moves on when this camera's shadow work is not wholly inside what the last redraw culled: the gather keeps only the
		/// chunks the camera's frustum or its shadow sweep reaches, and stable cascades hold their windows while the camera turns,
		/// so without this a prop turned towards was in neither the cache nor URP's map (ShadowCasterCoverage). Checked once per
		/// work list. Levels need no check: within a held range the cache's levels are those chosen at its redraw, as designed.
		/// </summary>
		private int CoverageSerial()
		{
			if (coverageCheckedWork != workVersion)
			{
				coverageCheckedWork = workVersion;
				if (!ShadowCasterCoverage.Covers(heldShadowRanges, shadowWorkRanges))
				{
					coverageSerial++;
					CoverageRedraws++;
				}
			}
			return coverageSerial;
		}

		/// <summary>Redraws asked for because the shadow work outgrew the cache (for probes).</summary>
		public static int CoverageRedraws { get; private set; }

		/// <summary>The level choice this camera's culling reads, for the cache's own culling.</summary>
		private void RememberShadowCacheView(in TerrainTreeField.View view)
		{
			shadowCacheLod = new Vector4(view.ScreenFactor, view.Orthographic ? 1f : 0f, view.MaximumLodLevel, 1f);
		}

		/// <summary>Whether this renderer submits through its batch group now and has static shadow commands to give the cache.</summary>
		internal bool ServesShadowCache => brgState && shadowCacheArgs != null && commands.Count > 0;

		/// <summary>The static casters this renderer's batch group left out of the last main-light culling (for probes).</summary>
		public int LastStaticShadowCommandsOmitted { get; private set; }

		/// <summary>Whether a command is a static shadow caster: the shadow view, and not the swaying vegetation.</summary>
		public static bool IsStaticShadowCaster(int view, Material material)
			=> view == 1 && material != null && material.shader != null && material.shader.name != VegetationIndirectName;

		/// <summary>The cache's args: the layout's, with each command's instances starting at its slot's base.</summary>
		private void LayoutShadowCacheArgs(List<GraphicsBuffer.IndirectDrawIndexedArgs> args, int[] bases)
		{
			layoutSerial++;
			if (!brgState || args.Count == 0)
			{
				shadowCacheArgs?.Release();
				shadowCacheArgs = null;
				return;
			}
			var carried = new GraphicsBuffer.IndirectDrawIndexedArgs[args.Count];
			for (int c = 0; c < args.Count; c++)
			{
				GraphicsBuffer.IndirectDrawIndexedArgs a = args[c];
				a.startInstance = (uint)bases[commands[c].Slot];
				carried[c] = a;
			}
			Recreate(ref shadowCacheArgs, GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, carried.Length, GraphicsBuffer.IndirectDrawIndexedArgs.size);
			shadowCacheArgs.SetData(carried);
		}

		/// <summary>
		/// The cache's own culling of this camera's work list, before it redraws (CachedMainLightShadows): the shadow slots only,
		/// from <paramref name="anchor"/> (levels chosen there, whole, no fade), against <paramref name="planes"/> (the cached
		/// window's sides, along the light) within <paramref name="light"/>.w of the anchor, into the cache's own counts and
		/// visible list, then finalized into the cache's args. The live culling's buffers are untouched.
		/// </summary>
		internal void RecordShadowCacheCull(CommandBuffer cmd, Camera camera, Vector3 anchor, Vector4[] planes, Vector4 light)
		{
			if (!ServesShadowCache || workBuffer == null || workCount == 0 || visibleBuffer == null || countBuffer == null)
			{
				return;
			}
			if (shadowCacheCounts == null || shadowCacheCounts.count != countBuffer.count)
			{
				shadowCacheCounts?.Release();
				shadowCacheCounts = new GraphicsBuffer(GraphicsBuffer.Target.Raw, countBuffer.count, 4);
			}
			if (shadowCacheVisible == null || shadowCacheVisible.count != visibleBuffer.count)
			{
				shadowCacheVisible?.Release();
				shadowCacheVisible = new GraphicsBuffer(GraphicsBuffer.Target.Structured, visibleBuffer.count, FishVisible.Stride);
			}
			// What this redraw holds: the shadow work culled now (CoverageSerial asks for the next redraw when it outgrows it).
			ShadowCasterCoverage.Merge(shadowWorkRanges, heldShadowRanges);
			if (CachedMainLightShadows.DebugLiveCull)
			{
				// Diagnosis: the camera's own culling (its counts and visible list, this frame's) drawn by the cache instead of
				// the cache's culling, to split "the live culling lacks a caster" from "URP's drawing of it loses it".
				cmd.SetComputeIntParam(compute, ArgCountId, commands.Count);
				cmd.SetComputeBufferParam(compute, finalizeKernel, ArgSlotsId, argSlotBuffer);
				cmd.SetComputeBufferParam(compute, finalizeKernel, CountsId, countBuffer);
				cmd.SetComputeBufferParam(compute, finalizeKernel, SlotsId, slotBuffer);
				cmd.SetComputeBufferParam(compute, finalizeKernel, ArgsId, shadowCacheArgs);
				cmd.DispatchComputeChecked(compute, finalizeKernel, (commands.Count + 63) / 64, 1, 1);
				Bind(shadowCacheBlock);
				shadowCacheBlock.SetBuffer(VisibleId, visibleBuffer);
				shadowCacheBlock.SetVector(CasterSphereId, Vector4.zero);
				return;
			}
			cmd.SetComputeVectorParam(compute, CameraPositionId, anchor);
			cmd.SetComputeVectorArrayParam(compute, PlanesId, planes);
			cmd.SetComputeVectorParam(compute, LightId, light);
			cmd.SetComputeVectorParam(compute, LodId, shadowCacheLod);
			cmd.SetComputeIntParam(compute, SlotCountId, slotCount);
			cmd.SetComputeIntParam(compute, WorkCountId, workCount);
			cmd.SetComputeIntParam(compute, ArgCountId, commands.Count);
			cmd.SetComputeBufferParam(compute, clearKernel, CountsId, shadowCacheCounts);
			cmd.DispatchComputeChecked(compute, clearKernel, (slotCount + 63) / 64, 1, 1);
			FishDepthPyramid.Bind(cmd, compute, cullKernel, camera, anchor, false);
			cmd.SetComputeBufferParam(compute, cullKernel, InstancesId, instances);
			cmd.SetComputeBufferParam(compute, cullKernel, ModelsId, modelBuffer);
			cmd.SetComputeBufferParam(compute, cullKernel, WorkId, workBuffer);
			cmd.SetComputeBufferParam(compute, cullKernel, SlotsId, slotBuffer);
			cmd.SetComputeBufferParam(compute, cullKernel, CountsId, shadowCacheCounts);
			cmd.SetComputeBufferParam(compute, cullKernel, VisibleOutId, shadowCacheVisible);
			BindPackedOff(cmd);
			Vector2Int groups = TerrainGpuMath.CullGroups(workCount);
			cmd.DispatchComputeChecked(compute, cullKernel, groups.x, groups.y, 1);
			cmd.SetComputeBufferParam(compute, finalizeKernel, ArgSlotsId, argSlotBuffer);
			cmd.SetComputeBufferParam(compute, finalizeKernel, CountsId, shadowCacheCounts);
			cmd.SetComputeBufferParam(compute, finalizeKernel, SlotsId, slotBuffer);
			cmd.SetComputeBufferParam(compute, finalizeKernel, ArgsId, shadowCacheArgs);
			cmd.DispatchComputeChecked(compute, finalizeKernel, (commands.Count + 63) / 64, 1, 1);
			Bind(shadowCacheBlock);
			shadowCacheBlock.SetBuffer(VisibleId, shadowCacheVisible);
			shadowCacheBlock.SetVector(CasterSphereId, Vector4.zero);
			if (CachedMainLightShadows.ReadbackRequested)
			{
				CachedMainLightShadows.ReadbackRequested = false;
				RequestShadowArgsReadback(cmd);
			}
		}

		/// <summary>Diagnostic: both args buffers' instance counts over the static commands (CachedMainLightShadows.Readback*).</summary>
		private void RequestShadowArgsReadback(CommandBuffer cmd)
		{
			var statics = new List<int>();
			for (int c = 0; c < commands.Count && c < brgDraws.Length; c++)
			{
				if (brgDraws[c].StaticShadow)
				{
					statics.Add(c);
				}
			}
			int visibleCount = visibleBuffer != null ? visibleBuffer.count : 0;
			cmd.RequestAsyncReadback(shadowCacheArgs, request =>
			{
				if (request.hasError)
				{
					return;
				}
				var words = request.GetData<uint>();
				long sum = 0, bad = 0;
				foreach (int c in statics)
				{
					uint count = words[c * 5 + 1], start = words[c * 5 + 3];
					sum += count;
					if (count > 0 && start + count > visibleCount)
					{
						bad++;
					}
				}
				CachedMainLightShadows.ReadbackCacheInstances = sum;
				CachedMainLightShadows.ReadbackBadStarts = bad;
				CachedMainLightShadows.ReadbacksLanded++;
			});
			cmd.RequestAsyncReadback(argsBuffer, request =>
			{
				if (request.hasError)
				{
					return;
				}
				var words = request.GetData<uint>();
				long sum = 0;
				foreach (int c in statics)
				{
					sum += words[c * 5 + 1];
				}
				CachedMainLightShadows.ReadbackLiveInstances = sum;
			});
		}

		/// <summary>
		/// Draws this camera's wanted static shadow commands into the bound cascade tile (viewport, matrices and caster
		/// constants already set by the cache pass). Returns the draws.
		/// </summary>
		internal int RecordStaticShadowCasters(RasterCommandBuffer cmd)
		{
			if (!ServesShadowCache || CachedMainLightShadows.DebugDrawNothing)
			{
				return 0;
			}
			int draws = 0;
			for (int w = 0; w < wantedCommands.Count; w++)
			{
				int c = wantedCommands[w];
				if (c >= brgDraws.Length || !brgDraws[c].StaticShadow)
				{
					continue;
				}
				Command command = commands[c];
				Material material = command.Params.material;
				if (!shadowCasterPasses.TryGetValue(material, out int pass))
				{
					pass = material.FindPass("ShadowCaster");
					shadowCasterPasses[material] = pass;
				}
				if (pass < 0)
				{
					continue;
				}
				// The cache's block: the instance buffer and the cache's own visible list, and no caster sphere (as the batch group's draws).
				// Submesh 0: the args already hold the part's own startIndex and baseVertex (as the batch group's do); a part's
				// submesh here would add its index start a second time (the pooled look meshes have a submesh a part).
				cmd.DrawMeshInstancedIndirect(command.Mesh, 0, material, pass, shadowCacheArgs, c * GraphicsBuffer.IndirectDrawIndexedArgs.size, shadowCacheBlock);
				draws++;
			}
			return draws;
		}

		private void ReleaseShadowCache()
		{
			shadowCacheArgs?.Release();
			shadowCacheArgs = null;
			shadowCacheCounts?.Release();
			shadowCacheCounts = null;
			shadowCacheVisible?.Release();
			shadowCacheVisible = null;
			shadowCasterPasses.Clear();
		}
	}
}
