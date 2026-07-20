using FishMMO.Logging;
using System;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Starts the standalone updater executable and polls for its exit on the main thread,
	/// reporting completion/failure through callbacks safely usable with Unity APIs.
	/// Includes a hard timeout to prevent the launcher from hanging indefinitely
	/// if the updater process deadlocks or hangs on a corrupted patch file.
	/// </summary>
	public class SystemUpdaterLauncher : IUpdaterLauncher
	{
		/// <summary>
		/// Interval in seconds between process-exit polls.
		/// </summary>
		private const float pollIntervalSeconds = 0.5f;

		/// <summary>
		/// Maximum total time the launcher will wait for the updater process to exit.
		/// Patches are typically small delta files; even a large patch plus rollback
		/// should complete well within this window. If the updater hasn't exited by
		/// this deadline, the launcher force-kills it and reports failure.
		/// </summary>
		private const float updaterTimeoutSeconds = 300f;

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
#if UNITY_WEBGL && !UNITY_EDITOR
			Log.Error("SystemUpdaterLauncher", "LaunchUpdater is not supported on WebGL platform.");
			onError?.Invoke("Updater is not supported on WebGL. Please use a desktop client.");
			yield break;
#else
			// Check if the updater executable exists before launching
			if (!System.IO.File.Exists(updaterPath))
			{
				onError?.Invoke($"Updater executable not found at: {updaterPath}");
				yield break;
			}

			Process process = null;
			DataReceivedEventHandler outputHandler = null;
			DataReceivedEventHandler errorHandler = null;

			// Capture the Unity main-thread SynchronizationContext so log calls
			// from Process output/error handlers (which fire on background threads)
			// are marshalled back to the main thread.
			SynchronizationContext unityContext = SynchronizationContext.Current;

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

				// Subscribe to output and error events for logging.
				// Store handler references so they can be detached in the finally block.
				// Marshal log calls to the main thread via Unity SynchronizationContext
				// because Process event handlers fire on background threads.
				outputHandler = (sender, args) =>
				{
					if (!string.IsNullOrEmpty(args.Data))
					{
						string msg = args.Data;
						unityContext.Post(_ => Log.Debug("UpdaterOutput", msg), null);
					}
				};
				errorHandler = (sender, args) =>
				{
					if (!string.IsNullOrEmpty(args.Data))
					{
						string msg = args.Data;
						unityContext.Post(_ => Log.Error("UpdaterError", msg), null);
					}
				};
				process.OutputDataReceived += outputHandler;
				process.ErrorDataReceived += errorHandler;

				process.Start();
				process.BeginOutputReadLine();
				process.BeginErrorReadLine();

				Log.Debug("Updater", $"Updater launched: {updaterPath} with arguments: {startInfo.Arguments}");
			}
			catch (Exception ex)
			{
				// Log and report any exceptions during process launch.
				// Dispose the partially-created process to prevent a handle leak.
				onError?.Invoke($"Failed to start updater process: {ex.Message}");
				Log.Error("Updater", $"Exception during updater launch: {ex.Message}");
				process?.Dispose();
				yield break;
			}
			finally
			{
				// Detach event handlers before the process is disposed,
				// but NOT here — the polling loop below still needs them
				// to capture output/error from the running updater.
				// Handlers are detached in the exit paths below.
			}

			// Poll for process exit on the main thread with a hard timeout.
			// Without this timeout, a hung updater process (e.g. deadlocked on a
			// corrupted patch or a filesystem stall) would lock the launcher UI
			// forever with no path forward for the player.
			// Uses Time.realtimeSinceStartup for accurate elapsed-time measurement
			// rather than accumulating WaitForSeconds durations, which drift due
			// to frame-rate variance.
			float startTime = Time.realtimeSinceStartup;
			while (!process.HasExited)
			{
				if (Time.realtimeSinceStartup - startTime > updaterTimeoutSeconds)
				{
					Log.Critical("Updater", $"Updater process timed out after {updaterTimeoutSeconds}s. Force-killing.");
					try
					{
						process.Kill();
						// Give the process a brief window to terminate.
						process.WaitForExit(5000);
					}
					catch (Exception ex)
					{
						Log.Error("Updater", $"Error force-killing timed-out updater: {ex.Message}");
					}
					finally
					{
						process.OutputDataReceived -= outputHandler;
						process.ErrorDataReceived -= errorHandler;
						process.Dispose();
					}
					onError?.Invoke($"Updater process timed out after {updaterTimeoutSeconds} seconds. The patch may be corrupted or the system is under heavy load.");
					yield break;
				}
				yield return new WaitForSeconds(pollIntervalSeconds);
			}

			// WaitForExit ensures the process handle is fully updated before reading ExitCode.
			// Without this, reading ExitCode can throw InvalidOperationException in the
			// normal exit path (not just the timeout path).
			process.WaitForExit();
			int exitCode = process.ExitCode;
			process.OutputDataReceived -= outputHandler;
			process.ErrorDataReceived -= errorHandler;
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
#endif
		}
	}
}
