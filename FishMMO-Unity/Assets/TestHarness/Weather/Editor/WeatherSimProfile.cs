using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine.Profiling;

namespace FishMMO.TestHarness.Weather.Editor
{
	/// <summary>
	/// Where a weather stage's frame goes: the profiler recorded over the stage and flattened into self
	/// time per marker, for the main thread and the render thread. Switched on for the render probe by
	/// <c>FISHMMO_WEATHER_PROFILE=1</c>; the stages then settle for <see cref="SettleSeconds"/> so there
	/// are frames enough to average.
	/// </summary>
	/// <remarks>
	/// A flat table, not the call tree: the same marker is summed wherever it is called from, which is
	/// what says what to make cheaper. Under software rendering (llvmpipe) the GPU's work lands on the
	/// CPU, so the render thread's and the main thread's waits say nothing about a real GPU — the
	/// scripts' own markers do.
	/// </remarks>
	internal static class WeatherSimProfile
	{
		/// <summary>How long a stage runs when profiled, s.</summary>
		public const float SettleSeconds = 25f;

		/// <summary>The share of a stage's frames skipped at its start, while the weather hands over.</summary>
		private const float WarmShare = 0.4f;

		private const int TopMain = 40;
		private const int TopRender = 15;

		public static bool Enabled => Environment.GetEnvironmentVariable("FISHMMO_WEATHER_PROFILE") == "1";

		private static bool variantApplied;

		/// <summary>
		/// A cost or quality experiment, from <c>FISHMMO_WEATHER_VARIANT</c>: comma-separated scales on the
		/// loaded render profile, applied once and never saved — <c>res=0.5</c> (march resolution),
		/// <c>steps=0.5</c> (march steps), <c>light=3</c> (light-march steps) — and <c>quality=2</c>, which
		/// renders every stage at that quality level instead of its own (<see cref="VariantQuality"/>).
		/// The same frame under each says what each lever is worth. Applied to any render run, profiled or
		/// not: the cloud options' option E ("more rays a frame") is <c>res=</c> on the probe's stills.
		/// </summary>
		/// <remarks>
		/// The sky probe (SkySimRender) keeps a copy of this and of <see cref="VariantQuality"/>: its
		/// assembly does not reference this one, and a few lines of harness parsing are not worth a shared
		/// assembly. Change both together.
		/// </remarks>
		public static string ApplyVariant(FishMMO.Client.WeatherRenderProfile profile)
		{
			string variant = Environment.GetEnvironmentVariable("FISHMMO_WEATHER_VARIANT");
			if (variantApplied || profile == null || string.IsNullOrEmpty(variant))
			{
				return variant;
			}
			variantApplied = true;
			foreach (string part in variant.Split(','))
			{
				string[] kv = part.Split('=');
				if (kv.Length != 2 || !float.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value))
				{
					continue;
				}
				foreach (FishMMO.Client.WeatherTierSettings tier in new[] { profile.Performant, profile.Balanced, profile.HighFidelity })
				{
					switch (kv[0].Trim())
					{
						case "res": tier.CloudResolution *= value; break;
						case "steps": tier.CloudSteps = Math.Max(4, (int)(tier.CloudSteps * value)); break;
					}
				}
				if (kv[0].Trim() == "light")
				{
					profile.Clouds.LightSteps = Math.Max(1, (int)value);
				}
			}
			return variant;
		}

		/// <summary>
		/// The quality level <c>FISHMMO_WEATHER_VARIANT</c>'s <c>quality=</c> asks every stage to render at,
		/// or <paramref name="stageQuality"/> when it asks for none.
		/// </summary>
		public static int VariantQuality(int stageQuality)
		{
			string variant = Environment.GetEnvironmentVariable("FISHMMO_WEATHER_VARIANT");
			if (string.IsNullOrEmpty(variant))
			{
				return stageQuality;
			}
			foreach (string part in variant.Split(','))
			{
				string[] kv = part.Split('=');
				if (kv.Length == 2 && kv[0].Trim() == "quality" && int.TryParse(kv[1].Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int level))
				{
					return level;
				}
			}
			return stageQuality;
		}

		/// <summary>Starts a stage's recording afresh.</summary>
		public static void Begin()
		{
			ProfilerDriver.ClearAllFrames();
			ProfilerDriver.profileEditor = false;
			ProfilerDriver.enabled = true;
			Profiler.enabled = true;
		}

		/// <summary>The stage's table, as report lines.</summary>
		public static string Collect(string stageName)
		{
			int first = ProfilerDriver.firstFrameIndex;
			int last = ProfilerDriver.lastFrameIndex;
			var sb = new StringBuilder();
			if (first < 0 || last < first)
			{
				sb.AppendLine($"== {stageName}: no frames recorded");
				return sb.ToString();
			}
			int start = first + (int)((last - first) * WarmShare);
			int frames = 0;
			double frameMs = 0.0;
			var mainSelf = new Dictionary<string, double>();
			var mainCalls = new Dictionary<string, double>();
			var renderSelf = new Dictionary<string, double>();
			var children = new List<int>();
			var stack = new Stack<(int id, string parent)>();
			for (int frame = start; frame <= last; frame++)
			{
				using (HierarchyFrameDataView view = ProfilerDriver.GetHierarchyFrameDataView(frame, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnSelfTime, false))
				{
					if (view == null || !view.valid)
					{
						continue;
					}
					frames++;
					frameMs += view.frameTimeMs;
					Flatten(view, mainSelf, mainCalls, children, stack);
				}
				int renderThread = RenderThreadIndex(frame);
				if (renderThread > 0)
				{
					using (HierarchyFrameDataView view = ProfilerDriver.GetHierarchyFrameDataView(frame, renderThread, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnSelfTime, false))
					{
						if (view != null && view.valid)
						{
							Flatten(view, renderSelf, null, children, stack);
						}
					}
				}
			}
			if (frames == 0)
			{
				sb.AppendLine($"== {stageName}: no valid frames");
				return sb.ToString();
			}
			string variant = Environment.GetEnvironmentVariable("FISHMMO_WEATHER_VARIANT");
			sb.AppendLine($"== {stageName}{(string.IsNullOrEmpty(variant) ? "" : $" [{variant}]")}: {frames} frames, {frameMs / frames:0.0} ms a frame (main thread self time per frame, ms; calls per frame)");
			foreach (KeyValuePair<string, double> item in mainSelf.OrderByDescending(p => p.Value).Take(TopMain))
			{
				double calls = mainCalls.TryGetValue(item.Key, out double c) ? c / frames : 0.0;
				sb.AppendLine($"  {item.Value / frames,9:0.000}  {calls,7:0.#}  {item.Key}");
			}
			if (renderSelf.Count > 0)
			{
				sb.AppendLine("  -- render thread --");
				foreach (KeyValuePair<string, double> item in renderSelf.OrderByDescending(p => p.Value).Take(TopRender))
				{
					sb.AppendLine($"  {item.Value / frames,9:0.000}           {item.Key}");
				}
			}
			return sb.ToString();
		}

		/// <summary>Appends a stage's table to the profile report next to the renders.</summary>
		public static void Write(string outputDirectory, string stageName)
		{
			File.AppendAllText(Path.Combine(outputDirectory ?? ".", "WeatherSim-profile.txt"), Collect(stageName));
		}

		/// <summary>
		/// Markers that say what was done but not for whom — a draw, a blit — are keyed by the pass that
		/// called them, so a render pass's cost is its own line.
		/// </summary>
		private static readonly HashSet<string> Generic = new HashSet<string>
		{
			"Graphics.DrawProcedural", "Graphics.Blit", "Render.Mesh", "DrawBuffersBatchMode", "RenderLoop.Draw",
			"Graphics.DrawMesh", "Graphics.DrawMeshInstanced", "Graphics.DrawProceduralIndirect", "ComputeShader.Dispatch",
		};

		private static void Flatten(HierarchyFrameDataView view, Dictionary<string, double> self, Dictionary<string, double> calls, List<int> children, Stack<(int id, string parent)> stack)
		{
			stack.Clear();
			int root = view.GetRootItemID();
			stack.Push((root, "(root)"));
			while (stack.Count > 0)
			{
				(int id, string parent) = stack.Pop();
				string name = id == root ? "(root)" : view.GetItemName(id);
				if (id != root)
				{
					string key = Generic.Contains(name) ? $"{parent} > {name}" : name;
					self.TryGetValue(key, out double ms);
					self[key] = ms + view.GetItemColumnDataAsDouble(id, HierarchyFrameDataView.columnSelfTime);
					if (calls != null)
					{
						calls.TryGetValue(key, out double n);
						calls[key] = n + view.GetItemColumnDataAsDouble(id, HierarchyFrameDataView.columnCalls);
					}
				}
				view.GetItemChildren(id, children);
				for (int i = 0; i < children.Count; i++)
				{
					stack.Push((children[i], name));
				}
			}
		}

		private static int RenderThreadIndex(int frame)
		{
			for (int thread = 1; thread < 64; thread++)
			{
				using (RawFrameDataView raw = ProfilerDriver.GetRawFrameDataView(frame, thread))
				{
					if (raw == null || !raw.valid)
					{
						return -1;
					}
					if (raw.threadName == "Render Thread")
					{
						return thread;
					}
				}
			}
			return -1;
		}
	}
}
