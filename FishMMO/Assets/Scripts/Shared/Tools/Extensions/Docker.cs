using System;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class Docker
{
	public static async Task RunAsync(string command)
	{
		var operatingSystem = "";
		if (SystemInfo.operatingSystem.Contains("Windows"))
		{
			operatingSystem = "windows";
		}
		else if (SystemInfo.operatingSystem.Contains("Mac"))
		{
			operatingSystem = "mac";
		}
		else if (SystemInfo.operatingSystem.Contains("Linux"))
		{
			operatingSystem = "linux";
		}
		else
		{
			Debug.LogError("Unsupported operating system.");
			return;
		}

		var arguments = operatingSystem == "windows" ? "/c docker " + command : "-c \"docker " + command + "\"";

		var process = new Process
		{
			StartInfo = new ProcessStartInfo
			{
				FileName = operatingSystem == "windows" ? "cmd.exe" : "/bin/bash",
				Arguments = arguments,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			}
		};

		try
		{
			await Task.Run(() =>
			{
				if (command.StartsWith("run") && !command.Contains("postgres:14"))
				{
					var pullCommand = operatingSystem == "windows" ? "/c docker pull postgres:14" : "-c \"docker pull postgres:14\"";
					var pullProcess = new Process
					{
						StartInfo = new ProcessStartInfo
						{
							FileName = operatingSystem == "windows" ? "cmd.exe" : "/bin/bash",
							Arguments = pullCommand,
							UseShellExecute = false,
							RedirectStandardOutput = true,
							RedirectStandardError = true
						}
					};
					pullProcess.Start();
					pullProcess.WaitForExit();

					if (pullProcess.ExitCode != 0)
					{
						Debug.LogError($"Docker pull command 'postgres:14' failed with error code {pullProcess.ExitCode}");
						return;
					}
				}

				process.Start();

				var output = process.StandardOutput.ReadToEnd();
				var error = process.StandardError.ReadToEnd();

				process.WaitForExit();

				if (process.ExitCode != 0)
				{
					Debug.LogError($"Docker command '{command}' failed with error code {process.ExitCode}: {error}");
				}
				else
				{
					Debug.Log($"Docker command '{command}' succeeded: {output}");
				}
			});
		}
		catch (Exception ex)
		{
			Debug.LogError($"An error occurred while executing the Docker command '{command}': {ex.Message}");
		}
	}
}