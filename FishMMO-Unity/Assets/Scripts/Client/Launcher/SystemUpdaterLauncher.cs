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
	/// Starts the standalone updater executable and hands control over to it.
	/// </summary>
	/// <remarks>
	/// <para>The updater takes ownership of the install as soon as it starts: its first
	/// action is to terminate this process (it is given our PID) so that the client
	/// binaries are not in use while they are patched, and its last action is to relaunch
	/// the client. This launcher therefore must NOT wait for the updater to exit — doing
	/// so is a mutual wait that the updater resolves by killing us, which makes every
	/// completion callback unreachable dead code.</para>
	/// <para>Instead we watch the updater only long enough to catch a fast failure
	/// (missing runtime, bad arguments, unreadable patch), then report success and let the
	/// caller shut the launcher down cleanly. If the updater is still running at the end
	/// of that window it has gotten far enough to own the handoff.</para>
	/// </remarks>
	public class SystemUpdaterLauncher : IUpdaterLauncher
	{
		/// <summary>
		/// Interval in seconds between process-exit polls.
		/// </summary>
		private const float pollIntervalSeconds = 0.25f;

		/// <summary>
		/// How long to watch the updater for an immediate failure before handing off.
		/// Long enough for a process that cannot start (missing .NET runtime, bad
		/// arguments, unreadable patch archive) to fail and set an exit code; short
		/// enough that the player is not left staring at a static screen.
		/// </summary>
		private const float handoffGraceSeconds = 10f;

		/// <summary>
		/// Launches the updater executable and hands off to it via a coroutine, ensuring
		/// callbacks execute on the Unity main thread.
		/// <para><b>IMPORTANT:</b> This method returns an IEnumerator and the caller
		/// MUST invoke it via <c>MonoBehaviour.StartCoroutine</c>. The implementation
		/// uses <c>yield return new WaitForSeconds(...)</c> which requires a Unity
		/// coroutine host with a main-thread UnitySynchronizationContext. Calling this
		/// method outside of a Unity coroutine context will result in undefined behavior.</para>
		/// <para><paramref name="onComplete"/> means "the updater is running and owns the
		/// install from here" — NOT "the patch has been applied". The caller should shut
		/// the launcher down promptly so the updater can replace the client binaries.</para>
		/// </summary>
		/// <param name="updaterPath">Path to the updater executable.</param>
		/// <param name="currentClientVersion">Current client version string.</param>
		/// <param name="latestServerVersion">Latest server version string.</param>
		/// <param name="onComplete">Callback invoked once the updater has started successfully and taken over.</param>
		/// <param name="onError">Callback invoked when updater fails to start or exits with an error before handoff.</param>
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
				try
				{
					startInfo.ArgumentList.Add($"-version={currentClientVersion}");
					startInfo.ArgumentList.Add($"-latestversion={latestServerVersion}");
					startInfo.ArgumentList.Add($"-pid={Process.GetCurrentProcess().Id}");
					startInfo.ArgumentList.Add($"-exe={Constants.Configuration.ClientExecutable}");
				}
				catch (NotSupportedException)
				{
					// IL2CPP fallback: some Mono/IL2CPP versions do not support ArgumentList.
					// Build the arguments string manually with proper shell quoting.
					startInfo.Arguments = $"-version=\"{currentClientVersion}\"" + " "
						+ $"-latestversion=\"{latestServerVersion}\"" + " "
						+ $"-pid={Process.GetCurrentProcess().Id}" + " "
						+ $"-exe=\"{Constants.Configuration.ClientExecutable}\"";
				}

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

				Log.Debug("Updater", $"Updater launched: {updaterPath} (client {currentClientVersion} -> {latestServerVersion})");
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

			/* Watch briefly for a fast failure, then hand off.
			 *
			 * We deliberately do NOT wait for the updater to exit. The updater kills this
			 * process by PID before it touches any files, so a wait-for-exit loop can only
			 * ever end one of two ways: we get killed mid-wait (and every callback below
			 * becomes unreachable), or the kill fails and we wait out a timeout for no
			 * reason. Watching only for an early non-zero exit gives us real error
			 * reporting for the cases the player can act on, without the deadlock.
			 *
			 * Uses Time.realtimeSinceStartup rather than accumulating WaitForSeconds
			 * durations, which drift with frame-rate variance. */
			float startTime = Time.realtimeSinceStartup;
			while (Time.realtimeSinceStartup - startTime < handoffGraceSeconds)
			{
				if (process.HasExited)
				{
					break;
				}
				yield return new WaitForSeconds(pollIntervalSeconds);
			}

			if (!process.HasExited)
			{
				// Still running: it is past the point where it would have failed to start,
				// and it now owns the install. Leave the process alone (disposing here
				// would not kill it, but detaching the readers loses its diagnostics) and
				// let the caller quit so the binaries are released.
				Log.Debug("Updater", "Updater is running and has taken over the install. Handing off and shutting down the launcher.");
				onComplete?.Invoke();
				yield break;
			}

			// Exited within the grace window. WaitForExit ensures the process handle is
			// fully updated before reading ExitCode; without it, reading ExitCode can
			// throw InvalidOperationException even on the normal exit path.
			process.WaitForExit();
			int exitCode = process.ExitCode;
			process.OutputDataReceived -= outputHandler;
			process.ErrorDataReceived -= errorHandler;
			process.Dispose();

			Log.Debug("Updater", $"Updater process exited with code: {exitCode}");
			if (exitCode == 0)
			{
				// A clean, immediate exit means the updater decided there was nothing to
				// do (already up to date). Treat it as a successful handoff.
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
