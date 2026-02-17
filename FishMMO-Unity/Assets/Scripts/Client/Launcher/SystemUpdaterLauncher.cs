using FishMMO.Logging;
using System;
using System.Collections;
using System.Diagnostics;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Starts the standalone updater executable and polls for its exit on the main thread,
	/// reporting completion/failure through callbacks safely usable with Unity APIs.
	/// </summary>
	public class SystemUpdaterLauncher : IUpdaterLauncher
	{
		/// <summary>
		/// Interval in seconds between process-exit polls.
		/// </summary>
		private const float PollIntervalSeconds = 0.5f;

		/// <summary>
		/// Launches the updater executable and polls for process exit via a coroutine,
		/// ensuring callbacks execute on the Unity main thread.
		/// </summary>
		/// <param name="updaterPath">Path to the updater executable.</param>
		/// <param name="currentClientVersion">Current client version string.</param>
		/// <param name="latestServerVersion">Latest server version string.</param>
		/// <param name="onComplete">Callback invoked when updater completes successfully.</param>
		/// <param name="onError">Callback invoked when updater fails or errors occur.</param>
		/// <returns>An IEnumerator for use in a Unity Coroutine.</returns>
		public IEnumerator LaunchUpdater(string updaterPath, string currentClientVersion, string latestServerVersion, Action onComplete, Action<string> onError)
		{
			// Check if the updater executable exists before launching
			if (!System.IO.File.Exists(updaterPath))
			{
				onError?.Invoke($"Updater executable not found at: {updaterPath}");
				yield break;
			}

			Process process;
			try
			{
				// Prepare process start info with required arguments and settings
				ProcessStartInfo startInfo = new ProcessStartInfo
				{
					FileName = updaterPath,
					Arguments = $"-version={currentClientVersion} -latestversion={latestServerVersion} -pid={Process.GetCurrentProcess().Id} -exe={Constants.Configuration.ClientExecutable}",
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					CreateNoWindow = true
				};

				process = new Process { StartInfo = startInfo };

				// Subscribe to output and error events for logging
				process.OutputDataReceived += (sender, args) =>
				{
					if (!string.IsNullOrEmpty(args.Data)) Log.Debug("UpdaterOutput", args.Data);
				};
				process.ErrorDataReceived += (sender, args) =>
				{
					if (!string.IsNullOrEmpty(args.Data)) Log.Error("UpdaterError", args.Data);
				};

				process.Start();
				process.BeginOutputReadLine();
				process.BeginErrorReadLine();

				Log.Debug("Updater", $"Updater launched: {updaterPath} with arguments: {startInfo.Arguments}");
			}
			catch (Exception ex)
			{
				// Log and report any exceptions during process launch
				onError?.Invoke($"Failed to start updater process: {ex.Message}");
				Log.Error("Updater", $"Exception during updater launch: {ex.Message}");
				yield break;
			}

			// Poll for process exit on the main thread
			WaitForSeconds wait = new WaitForSeconds(PollIntervalSeconds);
			while (!process.HasExited)
			{
				yield return wait;
			}

			int exitCode = process.ExitCode;
			process.Dispose();

			Log.Debug("Updater", $"Updater process exited with code: {exitCode}");
			if (exitCode == 0)
			{
				onComplete?.Invoke();
			}
			else
			{
				onError?.Invoke($"Updater process exited with code {exitCode}. See logs for details.");
			}
		}
	}
}