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
		/// <para><b>IMPORTANT:</b> This method returns an IEnumerator and the caller
		/// MUST invoke it via <c>MonoBehaviour.StartCoroutine</c>. The implementation
		/// uses <c>yield return new WaitForSeconds(...)</c> which requires a Unity
		/// coroutine host with a main-thread UnitySynchronizationContext. Calling this
		/// method outside of a Unity coroutine context will result in undefined behavior.</para>
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
			// WebGL builds run in the browser sandbox (MEMFS in-memory virtual filesystem);
			// there is no executable file to launch, and the browser tab does not allow
			// spawning child processes. Consequently, the standalone updater cannot be
			// invoked on this platform.
			//
			// IMPORTANT: WebGL builds MUST always be deployed with the latest version.
			// The build pipeline must ensure the WebGL build matches the server's
			// expected version before deployment. Unlike desktop clients, WebGL users
			// cannot apply a patch -- they must refresh the browser tab to receive an
			// updated build from the web server.
			//
			// If a WebGL client version is outdated, ClientLauncher.GetLatestVersion()
			// shows a user-friendly message directing the player to refresh the page.
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
			// Runtime check: LaunchUpdater must be called via
			// MonoBehaviour.StartCoroutine on the Unity main thread. If
			// SynchronizationContext.Current is null, there is no UnitySynchronizationContext
			// installed (e.g., called from a background thread or a non-Unity context).
			if (unityContext == null)
			{
				onError?.Invoke("LaunchUpdater must be invoked via MonoBehaviour.StartCoroutine on the Unity main thread. SynchronizationContext.Current is null.");
				yield break;
			}

			try
			{
				// Prepare process start info with required arguments and settings
				// Using ArgumentList instead of Arguments string to avoid injection
				// from unescaped spaces in paths or version strings.
				ProcessStartInfo startInfo = new ProcessStartInfo
				{
					FileName = updaterPath,
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					CreateNoWindow = true
				};
				startInfo.ArgumentList.Add($"-version={currentClientVersion}");
				startInfo.ArgumentList.Add($"-latestversion={latestServerVersion}");
				startInfo.ArgumentList.Add($"-pid={Process.GetCurrentProcess().Id}");
				startInfo.ArgumentList.Add($"-exe={Constants.Configuration.ClientExecutable}");

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
