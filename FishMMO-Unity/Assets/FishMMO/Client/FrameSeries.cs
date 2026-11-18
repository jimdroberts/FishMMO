#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;

namespace FishMMO.Client
{
	/// <summary>
	/// How a play session's frame rate and memory develop from the first frame on, as rows in the log: one a second for
	/// <see cref="DetailSeconds"/>, then one every <see cref="TrendSeconds"/> for as long as play lasts. Each row has the frame
	/// time (median and worst), the GPU's and the two CPU threads' frame times, the memory Unity and the GC hold, the GC
	/// runs, whether the grass and the detail scatter are still taking terrains on, and the systems that cost the most main
	/// thread, with what the worst frame of the row spent. Editor only; off unless switched on (Dashboard → Maintenance →
	/// Diagnostics → Toggle frame series, or FISHMMO_FRAME_SERIES=1 for a probe run). Search Editor.log for "[FrameSeries]".
	/// </summary>
	/// <remarks>
	/// Cheap on purpose, unlike <see cref="StartupTimeline"/> (which reads every marker Unity has each frame, ~7 ms of the main
	/// thread while it runs): a fixed list of markers (<see cref="Watched"/>), each read once a frame.
	/// </remarks>
	public sealed class FrameSeries : MonoBehaviour
	{
		public const float DetailSeconds = 180f;
		public const float TrendSeconds = 15f;
		/// <summary>A frame at least this long counts as a hitch in its row.</summary>
		public const float HitchMilliseconds = 50f;

		/// <summary>EditorPrefs switch: true records a series on every Play. Off by default.</summary>
		public const string EnabledKey = "FishMMO.FrameSeries.Enabled";

		public static bool Enabled
		{
			get => UnityEditor.EditorPrefs.GetBool(EnabledKey, false) || Environment.GetEnvironmentVariable("FISHMMO_FRAME_SERIES") == "1";
			set => UnityEditor.EditorPrefs.SetBool(EnabledKey, value);
		}

		/// <summary>The main-thread markers a row ranks: the systems' camera hooks and builders, and what Unity's own frame waits on or spends.</summary>
		private static readonly string[] Watched =
		{
			"GrassBladeSystem.BeginContext",
			"GrassTerrain.Builder.Step",
			"DetailScatterSystem.BeginContext",
			"DetailScatterTerrain.Builder.Step",
			"CliffRockInstancing.BeginContext",
			"TerrainDetailInstancing.BeginContext",
			"TerrainTreeInstancing.BeginContext",
			"AmbientLifeSystem.BeginContext",
			"GroundColourMap.BeginContext",
			"SiteLightPool.Update",
			"Sky.Globals",
			"Sky.Bodies",
			"CloudAirMap.Step",
			"RenderLoop.Draw",
			"Shadows.Draw",
			"StdRender.ApplyShader",
			"CreateGraphicsPipelineImpl",
			"Shader.EditorLoadVariantFromCache",
			"Graphics.Blit",
			"WaitForLastPresentation",
			"Gfx.WaitForPresentOnGfxThread",
			"Gfx.WaitForRenderThread",
			"VKUploadBuffer.Allocate",
			"Loading.ReadObject",
			"Loading.AwakeFromLoad",
			"LargeAllocation.Free",
			"GC.Collect",
			"UIElements.UpdateRenderData",
			"EditorLoop",
			"PlayerLoop",
		};

		private readonly ProfilerRecorder[] recorders = new ProfilerRecorder[Watched.Length];
		private readonly double[] sums = new double[Watched.Length];
		private readonly double[] worst = new double[Watched.Length];
		private readonly double[] frame = new double[Watched.Length];
		private readonly List<float> times = new List<float>();
		private readonly List<double> gpu = new List<double>(), main = new List<double>(), render = new List<double>();
		private readonly FrameTiming[] timings = new FrameTiming[1];
		/// <summary>Rows not yet logged: written every <see cref="RowsPerLog"/>, so a crash loses at most that many.</summary>
		private readonly StringBuilder pending = new StringBuilder();
		private int pendingRows;
		private const int RowsPerLog = 10;

		private double firstFrame = -1.0, rowStart, nextRetry;
		private float worstFrame;
		private int hitches, gcStart;
		private long allocatedStart, allocatedLast;
		private bool grassBuilt, scatterBuilt;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Begin()
		{
			if (!Enabled)
			{
				return;
			}
			var host = new GameObject("Frame Series") { hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave };
			DontDestroyOnLoad(host);
			host.AddComponent<FrameSeries>();
		}

		private void Awake()
		{
			StartRecorders();
		}

		/// <summary>
		/// Starts the recorders whose markers exist, found by name whatever category Unity files them under (a system's marker
		/// exists once its code first ran: retried every few seconds for the first minute).
		/// </summary>
		private void StartRecorders()
		{
			bool missing = false;
			for (int i = 0; i < Watched.Length && !missing; i++)
			{
				missing = !recorders[i].Valid;
			}
			if (!missing)
			{
				return;
			}
			handles.Clear();
			ProfilerRecorderHandle.GetAvailable(handles);
			foreach (ProfilerRecorderHandle handle in handles)
			{
				ProfilerRecorderDescription d = ProfilerRecorderHandle.GetDescription(handle);
				int i = Array.IndexOf(Watched, d.Name);
				if (i >= 0 && !recorders[i].Valid && d.UnitType == ProfilerMarkerDataUnit.TimeNanoseconds)
				{
					recorders[i] = ProfilerRecorder.StartNew(d.Category, d.Name, 1);
				}
			}
		}

		private readonly List<ProfilerRecorderHandle> handles = new List<ProfilerRecorderHandle>();

		private void OnDestroy()
		{
			Flush();
			if (firstFrame >= 0.0)
			{
				Debug.Log($"[FrameSeries] Ended after {Time.realtimeSinceStartupAsDouble - firstFrame:0} s; Unity allocated {allocatedStart / 1048576.0:0} → {Profiler.GetTotalAllocatedMemoryLong() / 1048576.0:0} MB.");
			}
			for (int i = 0; i < recorders.Length; i++)
			{
				recorders[i].Dispose();
			}
		}

		private void Update()
		{
			double now = Time.realtimeSinceStartupAsDouble;
			if (firstFrame < 0.0)
			{
				firstFrame = rowStart = now;
				gcStart = GC.CollectionCount(0);
				allocatedStart = allocatedLast = Profiler.GetTotalAllocatedMemoryLong();
				Debug.Log($"[FrameSeries] Recording from the first frame: a row a second for {DetailSeconds:0} s (logged {RowsPerLog} at a time), then one every {TrendSeconds:0} s. Columns: s; frame ms median/worst, frames ≥50 ms | GPU, CPU main, CPU render ms medians | Unity allocated MB (change), graphics driver MB, GC heap MB, GC runs | g/s = a grass or scatter take-on running | top main-thread systems ms a frame | the worst frame's.");
				return;
			}

			float ms = Time.unscaledDeltaTime * 1000f;
			times.Add(ms);
			bool hitch = ms >= HitchMilliseconds;
			hitches += hitch ? 1 : 0;
			for (int i = 0; i < recorders.Length; i++)
			{
				ProfilerRecorder r = recorders[i];
				frame[i] = r.Valid && r.Count > 0 ? r.LastValue * 1e-6 : 0.0;
				sums[i] += frame[i];
			}
			if (ms > worstFrame)
			{
				worstFrame = ms;
				Array.Copy(frame, worst, frame.Length);
			}
			FrameTimingManager.CaptureFrameTimings();
			if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
			{
				gpu.Add(timings[0].gpuFrameTime);
				main.Add(timings[0].cpuMainThreadFrameTime);
				render.Add(timings[0].cpuRenderThreadFrameTime);
			}
			grassBuilt |= GrassBladeSystem.Building;
			scatterBuilt |= DetailScatterSystem.Building;

			if (now >= nextRetry && now - firstFrame <= 60.0)
			{
				nextRetry = now + 3.0;
				StartRecorders();
			}

			double since = now - firstFrame;
			double rowLength = since <= DetailSeconds ? 1.0 : TrendSeconds;
			if (now - rowStart < rowLength)
			{
				return;
			}
			pending.AppendLine(Row(since, now - rowStart));
			pendingRows++;
			if (since > DetailSeconds || pendingRows >= RowsPerLog)
			{
				Flush();
			}
			rowStart = now;
		}

		private void Flush()
		{
			if (pendingRows == 0)
			{
				return;
			}
			Debug.Log("[FrameSeries]\n" + pending.ToString().TrimEnd());
			pending.Clear();
			pendingRows = 0;
		}

		/// <summary>One row, and the accumulators cleared for the next.</summary>
		private string Row(double since, double length)
		{
			long allocated = Profiler.GetTotalAllocatedMemoryLong();
			var sb = new StringBuilder();
			int n = Mathf.Max(1, times.Count);
			times.Sort();
			sb.Append($"{since,6:0} {times[times.Count / 2],5:0.0}/{times[times.Count - 1],5:0} {hitches,2}");
			sb.Append($" | {Median(gpu),5:0.0} {Median(main),5:0.0} {Median(render),5:0.0}");
			sb.Append($" | {allocated / 1048576.0,6:0} ({(allocated - allocatedLast) / 1048576.0,5:+0;-0;0}) {Profiler.GetAllocatedMemoryForGraphicsDriver() / 1048576.0,5:0} {GC.GetTotalMemory(false) / 1048576.0,5:0} {GC.CollectionCount(0) - gcStart,3}");
			sb.Append($" | {(grassBuilt ? "g" : "-")}{(scatterBuilt ? "s" : "-")} |");
			AppendTop(sb, sums, n, 4);
			if (worstFrame >= HitchMilliseconds)
			{
				// What the player loop does not account for is the editor's (its windows, imports, its own GC) or the OS's.
				int loop = Array.IndexOf(Watched, "PlayerLoop");
				sb.Append($" | worst {worstFrame:0} ms, {Mathf.Max(0f, worstFrame - (float)worst[loop]):0} of them outside the player loop:");
				AppendTop(sb, worst, 1, 4);
			}
			allocatedLast = allocated;
			times.Clear();
			gpu.Clear();
			main.Clear();
			render.Clear();
			Array.Clear(sums, 0, sums.Length);
			hitches = 0;
			worstFrame = 0f;
			grassBuilt = GrassBladeSystem.Building;
			scatterBuilt = DetailScatterSystem.Building;
			return sb.ToString();
		}

		/// <summary>The <paramref name="count"/> biggest of <paramref name="values"/> (summed over <paramref name="frames"/>), as ms a frame, umbrellas (the loops) left out.</summary>
		private static void AppendTop(StringBuilder sb, double[] values, int frames, int count)
		{
			Span<int> best = stackalloc int[count];
			best.Fill(-1);
			for (int i = 0; i < values.Length; i++)
			{
				string name = Watched[i];
				if (name == "PlayerLoop" || name == "EditorLoop" || values[i] / frames < 0.05)
				{
					continue;
				}
				for (int k = 0; k < count; k++)
				{
					if (best[k] < 0 || values[i] > values[best[k]])
					{
						for (int m = count - 1; m > k; m--)
						{
							best[m] = best[m - 1];
						}
						best[k] = i;
						break;
					}
				}
			}
			for (int k = 0; k < count && best[k] >= 0; k++)
			{
				sb.Append($" {Watched[best[k]]} {values[best[k]] / frames:0.0}");
			}
			int loop = Array.IndexOf(Watched, "PlayerLoop"), editor = Array.IndexOf(Watched, "EditorLoop");
			sb.Append($"; player loop {values[loop] / frames:0.0}, editor {values[editor] / frames:0.0}");
		}

		private static double Median(List<double> values)
		{
			if (values.Count == 0)
			{
				return 0.0;
			}
			values.Sort();
			return values[values.Count / 2];
		}
	}
}
#endif
