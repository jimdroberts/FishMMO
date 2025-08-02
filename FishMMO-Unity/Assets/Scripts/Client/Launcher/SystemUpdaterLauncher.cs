using FishMMO.Logging;
using System;
using System.Diagnostics;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class SystemUpdaterLauncher : IUpdaterLauncher
	{
		/// <summary>
		/// Launches the updater executable with the provided arguments and handles process output, errors, and completion.
		/// </summary>
		/// <param name="updaterPath">Path to the updater executable.</param>
		/// <param name="currentClientVersion">Current client version string.</param>
		/// <param name="latestServerVersion">Latest server version string.</param>
		/// <param name="onComplete">Callback invoked when updater completes successfully.</param>
		/// <param name="onError">Callback invoked when updater fails or errors occur.</param>
		public void LaunchUpdater(string updaterPath, string currentClientVersion, string latestServerVersion, Action onComplete, Action<string> onError)
		{
			// Check if the updater executable exists before launching
			if (!System.IO.File.Exists(updaterPath))
			{
				onError?.Invoke($"Updater executable not found at: {updaterPath}");
				return;
			}

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

				Process process = new Process { StartInfo = startInfo };

				// Subscribe to output and error events for logging
				process.OutputDataReceived += (sender, args) =>
				{
					if (!string.IsNullOrEmpty(args.Data)) Log.Debug("UpdaterOutput", args.Data);
				};
				process.ErrorDataReceived += (sender, args) =>
				{
					if (!string.IsNullOrEmpty(args.Data)) Log.Error("UpdaterError", args.Data);
				};

				process.EnableRaisingEvents = true;
				// Handle process exit, invoke completion or error callback
				process.Exited += (sender, args) =>
				{
					Log.Debug("Updater", $"Updater process exited with code: {process.ExitCode}");
					if (process.ExitCode == 0)
					{
						onComplete?.Invoke();
					}
					else
					{
						onError?.Invoke($"Updater process exited with code {process.ExitCode}. See logs for details.");
					}
					process.Dispose(); // Clean up the process object.
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
			}
		}
	}
}