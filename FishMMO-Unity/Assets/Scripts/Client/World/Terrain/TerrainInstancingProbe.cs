using System;
using System.Diagnostics;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace FishMMO.Client
{
	/// <summary>
	/// What the instanced terrain renderers cost in one frame, to find spikes: Sync (adoption), chunk
	/// builds, uploads, grows, relayouts (and why), flushes, compute recording, draw submission and Gen-0
	/// collections. One line is logged for a frame over <see cref="SpikeMilliseconds"/> or one in which a
	/// relayout or a grow happened — never otherwise.
	/// </summary>
	/// <remarks>
	/// On in development players; in the editor behind its own "Toggle terrain spike probe" switch
	/// (<see cref="Enabled"/>), not the upload log, whose per-upload lines are themselves a spike.
	/// Allocation-free while nothing is logged: the reasons are only recorded when the probe is on.
	/// <para>
	/// A spike line also names where the rest of the frame went: Unity's own profiler markers (shader
	/// compiles, GC, the editor loop, GPU waits, scripts, physics), read with <see cref="ProfilerRecorder"/>,
	/// which needs no Profiler window. A 1.2 s frame whose terrain costs were 10 ms is not the terrain's.
	/// </para>
	/// </remarks>
	public static class TerrainInstancingProbe
	{
		public const float SpikeMilliseconds = 33f;

		/// <summary>SessionState key (editor): the dashboard switch sets it, play mode starts from it.</summary>
		public const string EnabledKey = "FishMMO.TerrainInstancing.Probe";

		/// <summary>The editor switch (development players are always on).</summary>
		public static bool Enabled;

		public static bool On => Enabled || (Debug.isDebugBuild && !Application.isEditor);

		/// <summary>Markers whose time a spike line reports when above <see cref="AttributeMilliseconds"/> (unknown names just read 0).</summary>
		private static readonly (ProfilerCategory Category, string Name)[] markers =
		{
			(ProfilerCategory.Render, "Shader.CreateGPUProgram"),
			(ProfilerCategory.Loading, "Shader.Parse"),
			(ProfilerCategory.Memory, "GC.Collect"),
			(ProfilerCategory.Loading, "GarbageCollectAssetsProfile"),
			(ProfilerCategory.Internal, "EditorLoop"),
			(ProfilerCategory.Render, "Gfx.WaitForPresentOnGfxThread"),
			(ProfilerCategory.Render, "Gfx.WaitForGfxCommandsFromMainThread"),
			(ProfilerCategory.Render, "Gfx.ReadbackImage"),
			(ProfilerCategory.Internal, "Semaphore.WaitForSignal"),
			(ProfilerCategory.Scripts, "Update.ScriptRunBehaviourUpdate"),
			(ProfilerCategory.Scripts, "PreLateUpdate.ScriptRunBehaviourLateUpdate"),
			(ProfilerCategory.Scripts, "FixedUpdate.ScriptRunBehaviourFixedUpdate"),
			(ProfilerCategory.Physics, "FixedUpdate.PhysicsFixedUpdate"),
			(ProfilerCategory.Render, "PostLateUpdate.FinishFrameRendering"),
			(ProfilerCategory.Loading, "Loading.ReadObject"),
			(ProfilerCategory.Loading, "Loading.AwakeFromLoad"),
			(ProfilerCategory.Internal, "PlayerLoop"),
		};

		/// <summary>A marker is named on a spike line when it took at least this long that frame.</summary>
		public const float AttributeMilliseconds = 5f;

		private static ProfilerRecorder[] recorders;
		private static readonly StringBuilder line = new StringBuilder();

		private static readonly Stopwatch watch = Stopwatch.StartNew();
		private static int frame = -1;
		private static int gc0;
		private static readonly StringBuilder reasons = new StringBuilder();

		public static double SyncMs, BuildMs, RecordMs, DrawMs;
		public static int Builds, UploadInstances, Grows, Relayouts, FlushRanges, FlushInstances;

		/// <summary>A timestamp, milliseconds.</summary>
		public static double Now => watch.Elapsed.TotalMilliseconds;

		/// <summary>Called at the start of each frame's first sync: reports the frame before, then resets.</summary>
		public static void BeginFrame()
		{
			int now = Time.frameCount;
			if (now == frame)
			{
				return;
			}
			if (frame >= 0 && On)
			{
				float frameMs = Time.unscaledDeltaTime * 1000f;
				int collections = GC.CollectionCount(0) - gc0;
				if (frameMs > SpikeMilliseconds || Relayouts > 0 || Grows > 0)
				{
					Debug.Log($"[Terrain probe] frame {frame}: {frameMs:F1} ms; sync {SyncMs:F2} ms, {Builds} chunk builds {BuildMs:F2} ms, {UploadInstances} instances uploaded, {Grows} grows, {Relayouts} relayouts{(reasons.Length > 0 ? " (" + reasons + ")" : "")}, flush {FlushRanges} ranges / {FlushInstances} instances, compute record {RecordMs:F2} ms, draw submit {DrawMs:F2} ms, Gen0 GCs {collections}. Elsewhere: {Elsewhere()}.");
				}
			}
			if (On && recorders == null)
			{
				StartRecorders();
			}
			frame = now;
			gc0 = GC.CollectionCount(0);
			SyncMs = BuildMs = RecordMs = DrawMs = 0;
			Builds = UploadInstances = Grows = Relayouts = FlushRanges = FlushInstances = 0;
			reasons.Clear();
		}

		private static void StartRecorders()
		{
			recorders = new ProfilerRecorder[markers.Length];
			for (int i = 0; i < markers.Length; i++)
			{
				recorders[i] = ProfilerRecorder.StartNew(markers[i].Category, markers[i].Name, 1, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame);
			}
			Application.quitting += StopRecorders;
#if UNITY_EDITOR
			UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += StopRecorders;
#endif
		}

		private static void StopRecorders()
		{
			if (recorders == null)
			{
				return;
			}
			foreach (ProfilerRecorder r in recorders)
			{
				r.Dispose();
			}
			recorders = null;
			Application.quitting -= StopRecorders;
#if UNITY_EDITOR
			UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= StopRecorders;
#endif
		}

		/// <summary>The last frame's markers at or above <see cref="AttributeMilliseconds"/>, largest first.</summary>
		private static string Elsewhere()
		{
			if (recorders == null)
			{
				return "markers not recording yet";
			}
			line.Clear();
			var shown = new bool[recorders.Length];
			while (true)
			{
				int best = -1;
				double bestMs = AttributeMilliseconds;
				for (int i = 0; i < recorders.Length; i++)
				{
					if (shown[i] || !recorders[i].Valid)
					{
						continue;
					}
					double ms = recorders[i].LastValue * 1e-6;
					if (ms >= bestMs)
					{
						best = i;
						bestMs = ms;
					}
				}
				if (best < 0)
				{
					break;
				}
				shown[best] = true;
				line.Append(line.Length > 0 ? ", " : "").Append(markers[best].Name).Append(' ').Append(bestMs.ToString("F1")).Append(" ms");
			}
			return line.Length > 0 ? line.ToString() : $"no marker over {AttributeMilliseconds:F0} ms";
		}

		/// <summary>Notes why a relayout or grow happened (only while the probe is on).</summary>
		public static void Reason(string text)
		{
			if (!On)
			{
				return;
			}
			if (reasons.Length > 0)
			{
				reasons.Append("; ");
			}
			if (reasons.Length < 600)
			{
				reasons.Append(text);
			}
		}
	}
}
