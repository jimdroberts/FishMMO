using UnityEngine;
using UnityEditor;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace FishMMO.Shared
{
	public static class DotNetBuilderUtility
	{
		/// <summary>
		/// Initiates the build process for all settings within the given DotNetBuildProfile.
		/// </summary>
		/// <param name="profile">The DotNetBuildProfile containing the build settings and log.</param>
		/// <param name="editorWindow">Optional: The EditorWindow to repaint after logging updates.</param>
		public static async Task BuildAllAndLog(DotNetBuildProfile profile, EditorWindow editorWindow = null)
		{
			profile.LogOutput = "";
			AddLog(profile, "Starting all builds...");

			if (profile.SettingsList == null || profile.SettingsList.Count == 0)
			{
				AddLogError(profile, "No DotNet Build Settings assets selected in the profile. Please add at least one to the list.");
				return;
			}

			List<DotNetBuildSettings> settingsToBuild = profile.SettingsList.ToList();

			foreach (var settings in settingsToBuild)
			{
				if (settings == null)
				{
					AddLogWarning(profile, "Skipping null settings slot in the profile list.");
					continue;
				}

				AddLog(profile, $"--- Starting Build for: {settings.name} ---");

				if (settings.PerformCleanBeforeBuild)
				{
					AddLog(profile, $"Performing clean for '{settings.name}'...");
					bool cleanSuccess = await ExecuteDotNetCommand(profile, settings, "clean");
					if (!cleanSuccess)
					{
						AddLogError(profile, $"Clean for '{settings.name}' FAILED! Skipping build.");
						continue;
					}
					AddLog(profile, $"Clean for '{settings.name}' completed successfully.");
				}

				bool buildSuccess = await ExecuteDotNetCommand(profile, settings, "build");

				if (buildSuccess)
				{
					AddLog(profile, $"Build for '{settings.name}' completed successfully!");
				}
				else
				{
					AddLogError(profile, $"Build for '{settings.name}' FAILED!");
				}
				AddLog(profile, "\n");
			}

			AddLog(profile, "\nAll requested builds finished. Refreshing Unity AssetDatabase...");
			AssetDatabase.Refresh();
			AddLog(profile, "Unity AssetDatabase refreshed.");
		}

		/// <summary>
		/// Executes either 'dotnet build' or 'dotnet clean' based on the command parameter.
		/// </summary>
		/// <param name="profile">The DotNetBuildProfile to log output to.</param>
		/// <param name="settings">The DotNetBuildSettings asset to use for this command.</param>
		/// <param name="command">The dotnet command to execute ("build" or "clean").</param>
		/// <param name="editorWindow">Optional: The EditorWindow to repaint after logging updates.</param>
		/// <returns>True if the command was successful, false otherwise.</returns>
		private static async Task<bool> ExecuteDotNetCommand(DotNetBuildProfile profile, DotNetBuildSettings settings, string command)
		{
			string absoluteCsprojPath = settings.GetAbsoluteCsprojPath();

			string commonArguments = $"\"{absoluteCsprojPath}\" " +
									 $"--configuration {settings.BuildConfiguration} " +
									 $"--framework {settings.TargetFramework}";

			string specificArguments = "";
			if (command == "build")
			{
				specificArguments = settings.SkipDotNetRestore ? " --no-restore" : "";

				if (!settings.UseDefaultOutputPath && !string.IsNullOrEmpty(settings.OutputDirectory))
				{
					string absoluteOutputDirectory = settings.GetAbsoluteOutputDirectory();
					if (!string.IsNullOrWhiteSpace(absoluteOutputDirectory))
					{
						// Create directory only if it doesn't exist
						if (!Directory.Exists(absoluteOutputDirectory))
						{
							AddLogInternal(profile, $"Created output directory: {absoluteOutputDirectory}");
						}
						specificArguments += $" --output \"{absoluteOutputDirectory}\"";
					}
				}
			}

			string fullArguments = $"{command} {commonArguments}{specificArguments}";

			if (!File.Exists(absoluteCsprojPath))
			{
				AddLogErrorInternal(profile, $"Error: Project file not found at '{absoluteCsprojPath}' for command '{command}' on settings '{settings.name}'");
				return false;
			}

			AddLogInternal(profile, $"Executing: {settings.DotnetExecutablePath} {fullArguments}");

			using (Process process = new Process())
			{
				process.StartInfo.FileName = settings.DotnetExecutablePath;
				process.StartInfo.Arguments = fullArguments;
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.CreateNoWindow = true;
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.RedirectStandardError = true;

				try
				{
					process.Start();

					var outputTask = process.StandardOutput.ReadToEndAsync();
					var errorTask = process.StandardError.ReadToEndAsync();

					await Task.WhenAll(outputTask, errorTask);
					process.WaitForExit();

					string output = outputTask.Result;
					string error = errorTask.Result;

					if (!string.IsNullOrEmpty(output))
					{
						AddLogInternal(profile, output);
					}
					if (!string.IsNullOrEmpty(error))
					{
						AddLogErrorInternal(profile, error);
					}

					return process.ExitCode == 0;
				}
				catch (System.Exception ex)
				{
					AddLogException(profile, ex);
					return false;
				}
			}
		}

		/// <summary>
		/// Appends a standard log message to the build profile's output and marks it dirty for Unity serialization.
		/// </summary>
		/// <param name="profile">The build profile to log to.</param>
		/// <param name="message">The message to append.</param>
		private static void AddLogInternal(DotNetBuildProfile profile, string message)
		{
			//Log.Debug(message);
			profile.LogOutput += message + "\n";
			EditorUtility.SetDirty(profile);
		}

		/// <summary>
		/// Appends an error log message (colored red) to the build profile's output and marks it dirty for Unity serialization.
		/// </summary>
		/// <param name="profile">The build profile to log to.</param>
		/// <param name="message">The error message to append.</param>
		private static void AddLogErrorInternal(DotNetBuildProfile profile, string message)
		{
			//Log.Error(message);
			profile.LogOutput += "<color=red>" + message + "</color>\n";
			EditorUtility.SetDirty(profile);
		}

		/// <summary>
		/// Appends a warning log message (colored orange) to the build profile's output and marks it dirty for Unity serialization.
		/// </summary>
		/// <param name="profile">The build profile to log to.</param>
		/// <param name="message">The warning message to append.</param>
		private static void AddLogWarningInternal(DotNetBuildProfile profile, string message)
		{
			//Log.Warning(message);
			profile.LogOutput += "<color=orange>" + message + "</color>\n";
			EditorUtility.SetDirty(profile);
		}

		/// <summary>
		/// Appends a standard log message to the build profile's output.
		/// </summary>
		/// <param name="profile">The build profile to log to.</param>
		/// <param name="message">The message to append.</param>
		private static void AddLog(DotNetBuildProfile profile, string message)
		{
			AddLogInternal(profile, message);
		}

		/// <summary>
		/// Appends an error log message to the build profile's output.
		/// </summary>
		/// <param name="profile">The build profile to log to.</param>
		/// <param name="message">The error message to append.</param>
		private static void AddLogError(DotNetBuildProfile profile, string message)
		{
			AddLogErrorInternal(profile, message);
		}

		/// <summary>
		/// Appends a warning log message to the build profile's output.
		/// </summary>
		/// <param name="profile">The build profile to log to.</param>
		/// <param name="message">The warning message to append.</param>
		private static void AddLogWarning(DotNetBuildProfile profile, string message)
		{
			AddLogWarningInternal(profile, message);
		}

		/// <summary>
		/// Appends an exception message (colored red) to the build profile's output and marks it dirty for Unity serialization.
		/// </summary>
		/// <param name="profile">The build profile to log to.</param>
		/// <param name="ex">The exception to log.</param>
		private static void AddLogException(DotNetBuildProfile profile, System.Exception ex)
		{
			//Log.Exception(ex);
			profile.LogOutput += "<color=red>EXCEPTION: " + ex.Message + "</color>\n";
			EditorUtility.SetDirty(profile);
		}
	}
}