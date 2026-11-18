#if UNITY_EDITOR
using System;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>One progress window for a whole bake, polled within rows so Cancel can interrupt a planet.</summary>
	internal sealed class PlanetSurfaceBakeProgress : IDisposable
	{
		private readonly Func<string, float, bool> display;
		private readonly Stopwatch clock = Stopwatch.StartNew();
		private double nextUpdate;
		private string body;
		private int index, count = 1, loggedBucket = -1;

		public PlanetSurfaceBakeProgress(Func<string, float, bool> display = null) { this.display = display; }
		public void SetBody(string name, int index, int count)
		{
			body = name; this.index = index; this.count = Math.Max(1, count); loggedBucket = -1;
			Report("Checking cache", 0f, true);
		}

		public void Report(string stage, float progress, bool force = false)
		{
			if (display == null && !force && clock.Elapsed.TotalSeconds < nextUpdate) return;
			nextUpdate = clock.Elapsed.TotalSeconds + 0.1;
			string message = $"{index + 1}/{count}: {body} — {stage} ({progress:P0})";
			bool cancel = false;
			if (display != null) cancel = display(stage, progress);
			else if (!Application.isBatchMode)
				cancel = EditorUtility.DisplayCancelableProgressBar("Bake planet surfaces", message, (index + Mathf.Clamp01(progress)) / count);
			else
			{
				int bucket = Mathf.FloorToInt(progress * 4);
				if (bucket != loggedBucket) { UnityEngine.Debug.Log("[PlanetSurfaceBaker] " + message); loggedBucket = bucket; }
			}
			if (cancel) throw new OperationCanceledException("Planet-surface baking cancelled.");
		}

		public void Dispose() { if (display == null && !Application.isBatchMode) EditorUtility.ClearProgressBar(); }
	}
}
#endif
