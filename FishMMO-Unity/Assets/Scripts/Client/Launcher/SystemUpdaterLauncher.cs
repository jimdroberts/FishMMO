using FishMMO.Logging;
using System;
using System.Diagnostics;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class SystemUpdaterLauncher : IUpdaterLauncher
	{
		/// <inheritdoc/>
		public void LaunchUpdater(string updaterPath, string currentClientVersion, string latestServerVersion, Action onComplete, Action<string> onError)
		{
			if (!System.IO.File.Exists(updaterPath))
			{
				onError?.Invoke($"Updater executable not found at: {updaterPath}");
				return;
			}

			try
			{
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

				process.OutputDataReceived += (sender, args) =>
				{
					if (!string.IsNullOrEmpty(args.Data)) Log.Debug("UpdaterOutput", args.Data);
				};
				process.ErrorDataReceived += (sender, args) =>
				{
					if (!string.IsNullOrEmpty(args.Data)) Log.Error("UpdaterError", args.Data);
				};

				process.EnableRaisingEvents = true;
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
				onError?.Invoke($"Failed to start updater process: {ex.Message}");
				Log.Error("Updater", $"Exception during updater launch: {ex.Message}");
			}
		}
	}
}