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

			BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);

			// Append world scene paths to bootstrap scene array
			string[] scenes = AppendWorldScenePaths(bootstrapScenes);

			string folderName = executableName;
			if (customBuildType != CustomBuildType.Client)
			{
				folderName = "FishMMO " + folderName;
			}
			if (string.IsNullOrEmpty(tmpPath))
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
					CopyRemoteAddressablesToBuild(buildPath, executableName, buildTarget, customBuildType);

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
		/// Copies remote addressable bundles from the project-level ServerData directory
		/// into the built player's StreamingAssets so the server can load them via file://.
		/// Only executes for server builds where DynamicAddressableLoadPathSystem expects
		/// bundles at StreamingAssets/ServerData/[BuildTarget]/.
		/// </summary>
		/// <param name="buildPath">The root build output path.</param>
		/// <param name="executableName">The executable name (used to derive the _Data folder).</param>
		/// <param name="buildTarget">The active build target (e.g. StandaloneLinux64).</param>
		/// <param name="customBuildType">The build type — only server builds need the copy.</param>
		private void CopyRemoteAddressablesToBuild(string buildPath, string executableName, BuildTarget buildTarget, CustomBuildType customBuildType)
		{
			if (customBuildType != CustomBuildType.Server)
			{
				return;
			}

			string buildTargetName = buildTarget.ToString();
			string serverDataSource = Path.Combine(Directory.GetCurrentDirectory(), "ServerData", buildTargetName);

			if (!Directory.Exists(serverDataSource))
			{
				Log.Debug("BuildExecutor", $"No ServerData directory at '{serverDataSource}'. Remote addressable copy skipped.");
				return;
			}

			// Standalone builds place data in <buildPath>/<execName>_Data/StreamingAssets/
			string dataFolderName = executableName + "_Data";
			string streamingAssetsDest = Path.Combine(buildPath, dataFolderName, "StreamingAssets", "ServerData", buildTargetName);

			try
			{
				CopyDirectoryRecursive(serverDataSource, streamingAssetsDest);
				Log.Info("BuildExecutor", $"Copied remote addressable bundles from '{serverDataSource}' to '{streamingAssetsDest}'.");
			}
			catch (Exception ex)
			{
				Log.Error("BuildExecutor", $"Failed to copy remote addressable bundles: {ex.Message}");
			}
		}

		/// <summary>
		/// Recursively copies all files and subdirectories from source to destination.
		/// Creates the destination directory if it does not exist.
		/// </summary>
		/// <param name="sourceDir">The source directory path.</param>
		/// <param name="destDir">The destination directory path.</param>
		private static void CopyDirectoryRecursive(string sourceDir, string destDir)
		{
			Directory.CreateDirectory(destDir);

			foreach (string filePath in Directory.GetFiles(sourceDir))
			{
				string fileName = Path.GetFileName(filePath);
				string destFile = Path.Combine(destDir, fileName);
				File.Copy(filePath, destFile, true);
			}

			foreach (string subDir in Directory.GetDirectories(sourceDir))
			{
				string dirName = Path.GetFileName(subDir);
				CopyDirectoryRecursive(subDir, Path.Combine(destDir, dirName));
			}
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