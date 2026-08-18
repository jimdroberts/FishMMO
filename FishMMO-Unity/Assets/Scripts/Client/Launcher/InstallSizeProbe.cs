using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// Measures how much disk space the installation occupies, without blocking the launcher.
	/// </summary>
	/// <remarks>
	/// The walk runs on a thread-pool thread. <see cref="Directory.EnumerateFiles(string, string,
	/// SearchOption)"/> and <see cref="FileInfo.Length"/> are plain .NET and safe off the main
	/// thread — no Unity API is touched from the worker, which would throw. Only the completed
	/// total crosses back, and it is delivered from a coroutine so callers stay on the main
	/// thread and can touch UI freely.
	/// <para>
	/// A full client install is tens of thousands of files. Doing this synchronously would
	/// freeze the launcher for seconds at startup, and doing it per-frame would keep a core
	/// busy for a readout nobody is watching — so it runs once, on request, and the result is
	/// cached.
	/// </para>
	/// </remarks>
	public static class InstallSizeProbe
	{
		/// <summary>
		/// Last successfully measured size in bytes, or null when never measured.
		/// </summary>
		public static long? CachedSizeBytes { get; private set; }

		/// <summary>
		/// True while a measurement is running, so overlapping requests do not start a second
		/// walk over the same tree.
		/// </summary>
		private static bool inProgress;

		/// <summary>
		/// Measures <paramref name="rootPath"/> and reports the result.
		/// </summary>
		/// <param name="rootPath">Directory to measure, typically the install root.</param>
		/// <param name="onComplete">
		/// Invoked on the main thread with the total in bytes, or null when the size could not
		/// be determined. Never invoked with a partial total.
		/// </param>
		public static IEnumerator Measure(string rootPath, Action<long?> onComplete)
		{
			if (CachedSizeBytes.HasValue)
			{
				onComplete?.Invoke(CachedSizeBytes);
				yield break;
			}

			if (inProgress)
			{
				// Another caller is already walking the same tree. Reporting "unavailable"
				// rather than starting a duplicate walk; the first one will populate the cache.
				onComplete?.Invoke(null);
				yield break;
			}

			if (string.IsNullOrWhiteSpace(rootPath))
			{
				onComplete?.Invoke(null);
				yield break;
			}

			inProgress = true;
			Task<long?> task = Task.Run(() => ComputeDirectorySize(rootPath));

			while (!task.IsCompleted)
			{
				yield return null;
			}

			inProgress = false;

			long? result = null;
			if (task.IsFaulted)
			{
				// Task.Run captures exceptions into the task rather than raising them, so
				// without this an unreadable install directory would fail silently and the
				// readout would just never appear.
				Log.Warning("InstallSizeProbe", $"Install size measurement failed: {task.Exception?.GetBaseException().Message}");
			}
			else
			{
				result = task.Result;
			}

			if (result.HasValue)
			{
				CachedSizeBytes = result;
			}

			onComplete?.Invoke(result);
		}

		/// <summary>
		/// Discards the cached total so the next <see cref="Measure"/> re-walks the tree.
		/// Call after a patch has been applied.
		/// </summary>
		public static void Invalidate()
		{
			CachedSizeBytes = null;
		}

		/// <summary>
		/// Sums the length of every file beneath <paramref name="rootPath"/>.
		/// </summary>
		/// <remarks>
		/// Enumerated lazily and per-file guarded rather than using
		/// <c>GetFiles</c> plus a Sum: a live install directory changes underneath the walk —
		/// a log rotating, the patcher writing — and a file disappearing between being listed
		/// and being measured is normal, not an error. One vanished file should cost its own
		/// bytes, not the whole measurement.
		/// </remarks>
		/// <returns>The total in bytes, or null when the directory could not be read at all.</returns>
		private static long? ComputeDirectorySize(string rootPath)
		{
			try
			{
				if (!Directory.Exists(rootPath))
				{
					return null;
				}

				long total = 0;

				foreach (string file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
				{
					try
					{
						total += new FileInfo(file).Length;
					}
					catch (FileNotFoundException) { }
					catch (DirectoryNotFoundException) { }
					catch (UnauthorizedAccessException) { }
					catch (IOException) { }
				}

				return total;
			}
			catch (UnauthorizedAccessException ex)
			{
				Log.Warning("InstallSizeProbe", $"Access denied measuring '{rootPath}': {ex.Message}");
				return null;
			}
			catch (Exception ex)
			{
				Log.Warning("InstallSizeProbe", $"Could not measure '{rootPath}': {ex.Message}");
				return null;
			}
		}
	}
}
