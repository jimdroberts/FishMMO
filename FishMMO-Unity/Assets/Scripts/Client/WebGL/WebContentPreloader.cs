using System;
using System.Collections;
using System.Collections.Generic;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using System.Text;
using FishMMO.Logging;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace FishMMO.Client
{
	/// <summary>
	/// Downloads every Addressables bundle into the browser before the player can log in, so nothing
	/// is fetched during play. Only web builds do this; see <see cref="IsRequired"/>.
	/// </summary>
	/// <remarks>
	/// Addressables' own <c>DownloadDependenciesAsync</c> is no use here: caching is compiled out of
	/// it on WebGL, so it always reports everything as missing and would fetch the full set through
	/// the engine on every visit. The list of bundles comes from the catalog; the download itself
	/// happens in WebContentPreloader.jslib, which also explains where the files go.
	/// </remarks>
	public static class WebContentPreloader
	{
		private const int StatusRunning = 1;
		private const int StatusComplete = 2;
		private const int StatusFailed = 3;
		private const int StatusCancelled = 4;

		/// <summary>Catalog locations examined per frame, so reading a large catalog cannot freeze the page.</summary>
		private const int LocationsPerFrame = 2000;

		private const float PollIntervalSeconds = 0.25f;

		/// <summary>Weight of the newest sample in the smoothed transfer rate.</summary>
		private const double RateSmoothing = 0.2;

#if UNITY_WEBGL && !UNITY_EDITOR
		public static bool IsRequired => true;

		[DllImport("__Internal")] private static extern void FishMMOContentPreloadStart(string manifest);
		[DllImport("__Internal")] private static extern void FishMMOContentPreloadCancel();
		[DllImport("__Internal")] private static extern int FishMMOContentPreloadStatus();
		[DllImport("__Internal")] private static extern double FishMMOContentPreloadDoneBytes();
		[DllImport("__Internal")] private static extern double FishMMOContentPreloadTotalBytes();
		[DllImport("__Internal")] private static extern double FishMMOContentPreloadSessionBytes();
		[DllImport("__Internal")] private static extern string FishMMOContentPreloadError();
#else
		/// <summary>True when this build must have the full content set before the player can log in.</summary>
		public static bool IsRequired => false;

		private static void FishMMOContentPreloadStart(string manifest) { }
		private static void FishMMOContentPreloadCancel() { }
		private static int FishMMOContentPreloadStatus() => StatusComplete;
		private static double FishMMOContentPreloadDoneBytes() => 0d;
		private static double FishMMOContentPreloadTotalBytes() => 0d;
		private static double FishMMOContentPreloadSessionBytes() => 0d;
		private static string FishMMOContentPreloadError() => string.Empty;
#endif

		/// <summary>
		/// Stops a running download. Bundles already finished stay cached, so the next
		/// <see cref="Run"/> continues from them.
		/// </summary>
		public static void Cancel()
		{
			FishMMOContentPreloadCancel();
		}

		/// <summary>
		/// Brings the browser's copy of the content up to date. Exactly one of
		/// <paramref name="onComplete"/> and <paramref name="onError"/> is invoked, unless the
		/// download is cancelled, which invokes neither.
		/// </summary>
		public static IEnumerator Run(Action<DownloadStats> onProgress, Action onComplete, Action<string> onError)
		{
			AsyncOperationHandle<UnityEngine.AddressableAssets.ResourceLocators.IResourceLocator> initialize = Addressables.InitializeAsync(false);
			yield return initialize;
			bool initialized = initialize.Status == AsyncOperationStatus.Succeeded;
			Addressables.Release(initialize);
			if (!initialized)
			{
				onError?.Invoke("The game's content catalog could not be loaded. Press the button to try again.");
				yield break;
			}

			var manifest = new StringBuilder();
			var seen = new HashSet<string>(StringComparer.Ordinal);
			int examined = 0;
			foreach (var locator in Addressables.ResourceLocators)
			{
				foreach (var location in locator.AllLocations)
				{
					if (++examined % LocationsPerFrame == 0)
					{
						yield return null;
					}
					if (location.Data is AssetBundleRequestOptions options)
					{
						string url = Addressables.ResourceManager.TransformInternalId(location);
						if (seen.Add(url))
						{
							manifest.Append(options.BundleSize).Append('\t').Append(url).Append('\n');
						}
					}
				}
			}

			if (seen.Count == 0)
			{
				Log.Warning("WebContentPreloader", "The content catalog lists no bundles; nothing to download.");
				onComplete?.Invoke();
				yield break;
			}

			Log.Info("WebContentPreloader", $"Checking {seen.Count} content bundles against the browser cache.");
			FishMMOContentPreloadStart(manifest.ToString());

			var tick = new WaitForSecondsRealtime(PollIntervalSeconds);
			double lastSessionBytes = 0d;
			double bytesPerSecond = 0d;
			float lastSampleTime = Time.realtimeSinceStartup;
			int status;
			while ((status = FishMMOContentPreloadStatus()) == StatusRunning)
			{
				yield return tick;

				float now = Time.realtimeSinceStartup;
				double sessionBytes = FishMMOContentPreloadSessionBytes();
				double elapsed = now - lastSampleTime;
				if (elapsed > 0d)
				{
					double sample = Math.Max(0d, sessionBytes - lastSessionBytes) / elapsed;
					bytesPerSecond = bytesPerSecond <= 0d ? sample : bytesPerSecond + (sample - bytesPerSecond) * RateSmoothing;
				}
				lastSessionBytes = sessionBytes;
				lastSampleTime = now;

				onProgress?.Invoke(CreateStats(FishMMOContentPreloadDoneBytes(), FishMMOContentPreloadTotalBytes(), bytesPerSecond));
			}

			switch (status)
			{
				case StatusComplete:
					onComplete?.Invoke();
					break;
				case StatusFailed:
					onError?.Invoke(FishMMOContentPreloadError());
					break;
				case StatusCancelled:
					break;
				default:
					onError?.Invoke("The content download stopped unexpectedly. Press the button to try again.");
					break;
			}
		}

		/// <summary>Builds the launcher's progress snapshot from the raw byte counts.</summary>
		internal static DownloadStats CreateStats(double doneBytes, double totalBytes, double bytesPerSecond)
		{
			double done = Math.Max(0d, Math.Min(doneBytes, totalBytes));
			double? secondsRemaining = bytesPerSecond > 0d ? (totalBytes - done) / bytesPerSecond : (double?)null;
			float progress = totalBytes > 0d ? (float)(done / totalBytes) : 0f;
			return new DownloadStats((ulong)done, (long)totalBytes, bytesPerSecond, secondsRemaining, progress);
		}
	}
}
