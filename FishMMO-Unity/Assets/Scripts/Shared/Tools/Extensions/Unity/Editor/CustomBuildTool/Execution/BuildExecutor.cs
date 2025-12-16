#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build.Reporting;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using FishMMO.Logging;
using FishMMO.Shared.CustomBuildTool.Core;

namespace FishMMO.Shared.CustomBuildTool.Execution
{
	/// <summary>
	/// Executes the Unity build process, including scene selection, build options, configuration copying, and result reporting.
	/// </summary>
	public class BuildExecutor : IBuildExecutor
	{
		/// <summary>
		/// Builds an executable with the specified parameters and handles all build steps, configuration, and error reporting.
		/// </summary>
		public void ExecuteBuild(string rootPath, string executableName, string[] bootstrapScenes, CustomBuildType customBuildType, BuildOptions buildOptions, StandaloneBuildSubtarget subTarget, BuildTarget buildTarget)
		{
			string tmpPath = rootPath;
			if (string.IsNullOrWhiteSpace(rootPath))
			{
				rootPath = EditorUtility.SaveFolderPanel("Pick a save directory", "", "");
				if (string.IsNullOrWhiteSpace(rootPath))
				{
					Log.Warning("BuildExecutor", "No build directory selected. Build cancelled.");
					return;
				}
			}

			if (string.IsNullOrWhiteSpace(executableName))
			{
				Log.Error("BuildExecutor", "Executable name is required. Build cancelled.");
				return;
			}

			AssetDatabase.Refresh();

			BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);

			// Append world scene paths to bootstrap scene array if needed
			string[] scenes = (customBuildType == CustomBuildType.Server || customBuildType == CustomBuildType.Client)
				? AppendWorldScenePaths(bootstrapScenes)
				: bootstrapScenes;

			string folderName = executableName;
			if (customBuildType != CustomBuildType.Installer && customBuildType != CustomBuildType.Client)
			{
				folderName = "FishMMO " + folderName;
			}
			if (customBuildType == CustomBuildType.Installer)
			{
				folderName = "FishMMO" + GetBuildTargetShortName(buildTarget) + " " + folderName;
			}
			else if (string.IsNullOrEmpty(tmpPath))
			{
				folderName += GetBuildTargetShortName(buildTarget);
			}
			folderName = folderName.Trim();
			string buildPath = Path.Combine(rootPath, folderName);

			try
			{
				BuildPlayerOptions options = new BuildPlayerOptions()
				{
					locationPathName = Path.Combine(buildPath, executableName + ".exe"),
					options = buildOptions,
					scenes = scenes,
					subtarget = (int)subTarget,
					target = buildTarget,
					targetGroup = targetGroup,
				};

				BuildReport report = BuildPipeline.BuildPlayer(options);
				BuildSummary summary = report.summary;
				if (summary.result == BuildResult.Succeeded)
				{
					Log.Debug("BuildExecutor", $"Build Succeeded: {summary.totalSize} bytes {DateTime.UtcNow}");
					Log.Debug("BuildExecutor", $"Build Duration: {summary.totalTime}");
					Log.Debug("BuildExecutor", $"Scenes Included: {string.Join(", ", bootstrapScenes)}");
					Log.Debug("BuildExecutor", $"Build Target: {buildTarget}");
					Log.Debug("BuildExecutor", $"Build Subtarget: {subTarget}");
					LogBuildSteps(report);

					string root = Directory.GetParent(Directory.GetCurrentDirectory()).FullName;
					string configurationPath = WorkingEnvironmentOptions.AppendEnvironmentToPath(Constants.Configuration.SetupDirectory);
					CopyConfigurationFiles(buildTarget, customBuildType, Path.Combine(root, configurationPath), buildPath);

					if (customBuildType == CustomBuildType.Installer)
					{
						BuildSetupFolder(root, buildPath);
					}

					if (buildTarget == BuildTarget.WebGL)
					{
						Log.Debug("BuildExecutor", "Please visit https://docs.unity3d.com/2022.3/Documentation/Manual/webgl-server-configuration-code-samples.html for further WebGL WebServer configuration.");
					}
				}
				else if (summary.result == BuildResult.Failed)
				{
					Log.Error("BuildExecutor", $"Build {report.summary.result}!");
					Log.Error("BuildExecutor", $"Total Errors: {summary.totalErrors}");
					Log.Error("BuildExecutor", $"Build Target: {buildTarget}");
					Log.Error("BuildExecutor", $"Build Subtarget: {subTarget}");
					LogBuildSteps(report);
				}
			}
			catch (Exception ex)
			{
				Log.Error("BuildExecutor", $"Exception during build: {ex.Message}");
				Log.Error("BuildExecutor", $"Stack trace: {ex.StackTrace}");
			}
			finally
			{
				Log.Debug("BuildExecutor", "Build finished.");
			}
		}

		/// <summary>
		/// Logs details about each build step in the build report.
		/// </summary>
		private void LogBuildSteps(BuildReport report)
		{
			Log.Debug("BuildExecutor", "Build Steps:");
			int i = 0;
			foreach (var step in report.steps)
			{
				Log.Debug("BuildExecutor", $"Step {i}: {step.name}, Duration: {step.duration}");
				if (step.messages.Length > 0)
				{
					foreach (var message in step.messages)
					{
						if (message.type == UnityEngine.LogType.Error)
						{
							Log.Error("BuildExecutor", $"Error in step {step.name}: {message.content}");
						}
						else if (message.type == UnityEngine.LogType.Warning)
						{
							Log.Warning("BuildExecutor", $"Warning in step {step.name}: {message.content}");
						}
						else
						{
							Log.Debug("BuildExecutor", $"Message in step {step.name}: {message.content}");
						}
					}
				}
				++i;
			}
		}

		/// <summary>
		/// Appends all world scene paths (and optionally local scenes) to the required bootstrap scenes.
		/// </summary>
		private string[] AppendWorldScenePaths(string[] requiredPaths)
		{
			HashSet<string> allPaths = new HashSet<string>(requiredPaths);
			HashSet<string> worldScenes = DirectoryExtensions.GetAllFiles(Constants.Configuration.WorldScenePath, ".unity");
			allPaths.UnionWith(worldScenes);
			if (UnityEditor.EditorPrefs.GetBool("FishMMOEnableLocalDirectory"))
			{
				HashSet<string> localScenes = DirectoryExtensions.GetAllFiles(Constants.Configuration.LocalScenePath, ".unity");
				allPaths.UnionWith(localScenes);
			}
			return allPaths.ToArray();
		}

		/// <summary>
		/// Copies configuration files to the build output directory based on build type and target.
		/// </summary>
		private void CopyConfigurationFiles(BuildTarget buildTarget, CustomBuildType customBuildType, string configurationPath, string buildPath)
		{
			switch (customBuildType)
			{
				case CustomBuildType.Server:
					FileUtil.ReplaceFile(Path.Combine(configurationPath, "LoginServer.cfg"), Path.Combine(buildPath, "LoginServer.cfg"));
					FileUtil.ReplaceFile(Path.Combine(configurationPath, "WorldServer.cfg"), Path.Combine(buildPath, "WorldServer.cfg"));
					FileUtil.ReplaceFile(Path.Combine(configurationPath, "SceneServer.cfg"), Path.Combine(buildPath, "SceneServer.cfg"));
					break;
				case CustomBuildType.Client:
					if (buildTarget == BuildTarget.WebGL)
					{
						// WebGL-specific config copy logic if needed
					}
					break;
				default: break;
			}
			if (customBuildType != CustomBuildType.Client)
			{
				FileUtil.ReplaceFile(Path.Combine(configurationPath, "appsettings.json"), Path.Combine(buildPath, "appsettings.json"));
			}
		}

		/// <summary>
		/// Copies setup and database files to the installer build directory, cleaning up old binaries.
		/// </summary>
		private static void BuildSetupFolder(string rootPath, string buildPath)
		{
			string setup = Path.Combine(rootPath, Constants.Configuration.SetupDirectory);

			// Copy appsettings to installer directory
			string envConfigurationPath = WorkingEnvironmentOptions.AppendEnvironmentToPath(setup);
			string appsettingsTarget = Path.Combine(buildPath, "appsettings.json");
			FileUtil.DeleteFileOrDirectory(appsettingsTarget);
			FileUtil.CopyFileOrDirectory(Path.Combine(envConfigurationPath, "appsettings.json"), appsettingsTarget);

			// Copy database project to installer directory
			string dbBuildDirectory = Path.Combine(buildPath, Constants.Configuration.DatabaseDirectory);
			FileUtil.DeleteFileOrDirectory(dbBuildDirectory);
			FileUtil.CopyFileOrDirectory(Path.Combine(rootPath, Constants.Configuration.DatabaseDirectory), dbBuildDirectory);

			// Delete DB/bin
			FileUtil.DeleteFileOrDirectory(Path.Combine(Path.Combine(dbBuildDirectory, Constants.Configuration.DatabaseProjectDirectory), "bin"));

			// Delete Migrator/bin
			FileUtil.DeleteFileOrDirectory(Path.Combine(Path.Combine(dbBuildDirectory, Constants.Configuration.DatabaseMigratorProjectDirectory), "bin"));
		}

		/// <summary>
		/// Returns a short name string for the given build target.
		/// </summary>
		private string GetBuildTargetShortName(BuildTarget target)
		{
			switch (target)
			{
				case BuildTarget.StandaloneWindows:
					return " Windows(x86)";
				case BuildTarget.StandaloneWindows64:
					return " Windows";
				case BuildTarget.StandaloneLinux64:
					return " Linux";
				case BuildTarget.WebGL:
					return " WebGL";
				default:
					return "";
			}
		}
	}
}
#endif