using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FishMMO.Logging;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace FishMMO.Client
{
	/// <summary>
	/// Compute dispatches that skip an empty grid instead of failing it.
	/// </summary>
	/// <remarks>
	/// Unity refuses a dispatch with a zero (or negative) group count and logs "Thread group size must be above zero" for it,
	/// every time. A grid worked out as (count + 63) / 64 is empty whenever the count is: nothing to do, which skipping does
	/// exactly. The message names nothing (a web player's console shows no managed stack), and a player on Firefox had it
	/// repeat without end (2026-11-11), so the first skip from each call site is reported once, by file and line, with the
	/// counts it was given: what makes it empty there can then be looked at.
	/// </remarks>
	public static class GpuDispatch
	{
		private static readonly HashSet<string> reported = new HashSet<string>();

		public static void DispatchComputeChecked(this CommandBuffer cmd, ComputeShader compute, int kernel, int x, int y, int z,
			[CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
		{
			if (Empty(x, y, z, file, line))
			{
				return;
			}
			cmd.DispatchCompute(compute, kernel, x, y, z);
		}

		public static void DispatchComputeChecked(this ComputeCommandBuffer cmd, ComputeShader compute, int kernel, int x, int y, int z,
			[CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
		{
			if (Empty(x, y, z, file, line))
			{
				return;
			}
			cmd.DispatchCompute(compute, kernel, x, y, z);
		}

		public static void DispatchComputeChecked(this UnsafeCommandBuffer cmd, ComputeShader compute, int kernel, int x, int y, int z,
			[CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
		{
			if (Empty(x, y, z, file, line))
			{
				return;
			}
			cmd.DispatchCompute(compute, kernel, x, y, z);
		}

		/// <summary>True when the grid has no groups; reports the call site the first time it happens there.</summary>
		private static bool Empty(int x, int y, int z, string file, int line)
		{
			if (x > 0 && y > 0 && z > 0)
			{
				return false;
			}
			string site = System.IO.Path.GetFileName(file) + ":" + line;
			if (reported.Add(site))
			{
				Log.Warning("GpuDispatch", $"Skipped an empty compute dispatch at {site} ({x} x {y} x {z} groups); reported once per call site.");
			}
			return true;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			reported.Clear();
		}
	}
}
