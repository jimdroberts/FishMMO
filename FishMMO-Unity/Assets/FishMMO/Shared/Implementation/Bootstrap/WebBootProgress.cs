using System.Globalization;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace FishMMO.Shared
{
	/// <summary>
	/// Tells the web page how the boot download is going, so its loader stays up until the game's own loading screen can
	/// be drawn.
	/// </summary>
	/// <remarks>
	/// The game's loading screen lives in the first Addressables scene, whose bundles and their dependencies must download
	/// before it exists. Unity's loader bar ends with the engine's own download, so on a first visit the page was black
	/// for the whole of that download (a minute on a slower line) with nothing saying anything was happening.
	/// <see cref="MainBootstrapSystem"/> reports the download here; BootProgress.jslib passes it to the template, which keeps
	/// its loader up and fills it, and hides it on <see cref="Done"/>. Does nothing off the web.
	/// </remarks>
	public static class WebBootProgress
	{
#if UNITY_WEBGL && !UNITY_EDITOR
		[DllImport("__Internal")]
		private static extern void FishMMOBootProgress(float fraction, string text);

		[DllImport("__Internal")]
		private static extern void FishMMOBootDone();
#endif

		private static bool done;

		/// <summary>Shows <paramref name="fraction"/> (0..1) and <paramref name="text"/> on the page's loader.</summary>
		public static void Report(float fraction, string text)
		{
			if (done)
			{
				return;
			}
#if UNITY_WEBGL && !UNITY_EDITOR
			FishMMOBootProgress(fraction < 0f ? 0f : fraction > 1f ? 1f : fraction, text ?? string.Empty);
#endif
		}

		/// <summary>Hides the page's loader: the game draws its own from here on. Once only.</summary>
		public static void Done()
		{
			if (done)
			{
				return;
			}
			done = true;
#if UNITY_WEBGL && !UNITY_EDITOR
			FishMMOBootDone();
#endif
		}

		/// <summary>
		/// The loader's line for a download: "Downloading game data: 45.2 of 163.4 MB", or "Starting" when there is nothing
		/// to download (everything was in the browser's cache).
		/// </summary>
		public static string Describe(long downloadedBytes, long totalBytes)
		{
			if (totalBytes <= 0)
			{
				return "Starting";
			}
			if (downloadedBytes < 0)
			{
				downloadedBytes = 0;
			}
			if (downloadedBytes > totalBytes)
			{
				downloadedBytes = totalBytes;
			}
			const double mb = 1024.0 * 1024.0;
			return string.Format(CultureInfo.InvariantCulture, "Downloading game data: {0:0.0} of {1:0.0} MB",
				downloadedBytes / mb, totalBytes / mb);
		}
	}
}
