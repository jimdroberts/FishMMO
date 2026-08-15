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

			// Align EditorUserBuildSettings.development with this build request.
			// ClientSecurityBuildValidator gates TLS pins / secrets / hosts when
			// development is false. CustomBuildTool may pass BuildOptions.Development
			// and/or WorkingEnvironment=Development without flipping the EditorUser flag
			// (and WebGL Development intentionally omits BuildOptions.Development).
			// Without this sync, Development Dashboard builds still hit the production pin gate.
			bool previousDevelopment = EditorUserBuildSettings.development;
			bool isDevelopmentBuild =
				(buildOptions & BuildOptions.Development) != 0 ||
				WorkingEnvironmentOptions.GetWorkingEnvironmentState() == WorkingEnvironmentState.Development;

			try
			{
				EditorUserBuildSettings.development = isDevelopmentBuild;

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
					Log.Debug("BuildExecutor", $"Development build (security gate skip): {isDevelopmentBuild}");
					LogBuildSteps(report);

					string root = Directory.GetParent(Directory.GetCurrentDirectory()).FullName;
					string configurationPath = WorkingEnvironmentOptions.AppendEnvironmentToPath(Constants.Configuration.SetupDirectory);
					string setupRoot = Path.Combine(root, Constants.Configuration.SetupDirectory);
					CopyConfigurationFiles(buildTarget, customBuildType, Path.Combine(root, configurationPath), buildPath, setupRoot);
					CopyRemoteAddressablesToBuild(buildPath, executableName, buildTarget, customBuildType);
					CopyUpdaterToBuild(root, buildPath, buildTarget, customBuildType);

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
				EditorUserBuildSettings.development = previousDevelopment;
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
		private void CopyConfigurationFiles(BuildTarget buildTarget, CustomBuildType customBuildType, string configurationPath, string buildPath, string setupRoot)
		{
			switch (customBuildType)
			{
				case CustomBuildType.Server:
					FileUtil.ReplaceFile(Path.Combine(configurationPath, "LoginServer.cfg"), Path.Combine(buildPath, "LoginServer.cfg"));
					FileUtil.ReplaceFile(Path.Combine(configurationPath, "WorldServer.cfg"), Path.Combine(buildPath, "WorldServer.cfg"));
					FileUtil.ReplaceFile(Path.Combine(configurationPath, "SceneServer.cfg"), Path.Combine(buildPath, "SceneServer.cfg"));
					// Shared logging configuration from FishMMO-Setup.
					FileUtil.ReplaceFile(Path.Combine(setupRoot, "logging.json"), Path.Combine(buildPath, "logging.json"));
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
		/// Copies the standalone Updater executable and its runtime dependencies into a
		/// standalone client build.
		/// </summary>
		/// <remarks>
		/// <para>The launcher resolves the updater as
		/// <c>Constants.GetWorkingDirectory()/Constants.Configuration.UpdaterExecutable</c>
		/// — the install root. Without this copy that file never exists in a shipped build,
		/// so every attempt to apply a patch fails with "Updater executable not found" and
		/// the player is stuck on a version they cannot update away from.</para>
		/// <para>Skipped for server and WebGL builds: servers are deployed, not patched,
		/// and the browser sandbox cannot spawn a child process.</para>
		/// </remarks>
		/// <param name="root">The FishMMO repository root (parent of the Unity project).</param>
		/// <param name="buildPath">The root build output path.</param>
		/// <param name="buildTarget">The active build target.</param>
		/// <param name="customBuildType">The build type — only standalone clients need the updater.</param>
		private void CopyUpdaterToBuild(string root, string buildPath, BuildTarget buildTarget, CustomBuildType customBuildType)
		{
			if (customBuildType != CustomBuildType.Client || buildTarget == BuildTarget.WebGL)
			{
				return;
			}

			string updaterProject = Path.Combine(root, "FishMMO-Patcher", "Updater");
			string binRoot = Path.Combine(updaterProject, "bin");

			/* Derive the executable name from the build target rather than from
			 * Constants.Configuration.UpdaterExecutable. That constant is resolved by the
			 * UNITY_STANDALONE_* defines active when this editor assembly was compiled —
			 * i.e. the editor's current build target, which is not necessarily the target
			 * being built here. Getting this wrong copies a Linux apphost into a Windows
			 * build (or vice versa) and the mismatch only surfaces when a player tries to
			 * patch. The .NET apphost is platform-specific, so the Updater must have been
			 * published for this target. */
			bool targetIsWindows = buildTarget == BuildTarget.StandaloneWindows ||
								   buildTarget == BuildTarget.StandaloneWindows64;
			string executableName = targetIsWindows ? "Updater.exe" : "Updater";

			// Runtime-identifier publish output first, then plain build output (which only
			// carries a native apphost for the machine that produced it).
			List<string> candidateDirectories = new List<string>();
			foreach (string configuration in new[] { "Release", "Debug" })
			{
				string frameworkDir = Path.Combine(binRoot, configuration, "net8.0");
				foreach (string rid in GetRuntimeIdentifiers(buildTarget))
				{
					candidateDirectories.Add(Path.Combine(frameworkDir, rid, "publish"));
					candidateDirectories.Add(Path.Combine(frameworkDir, rid));
				}
				candidateDirectories.Add(Path.Combine(frameworkDir, "publish"));
				candidateDirectories.Add(frameworkDir);
			}

			// The updater ships framework-dependent: the apphost plus its managed
			// dependencies and runtime config must travel together, so we copy the whole
			// output directory rather than just the executable.
			string sourceDirectory = candidateDirectories.FirstOrDefault(
				dir => Directory.Exists(dir) && File.Exists(Path.Combine(dir, executableName)));

			if (sourceDirectory == null)
			{
				Log.Error("BuildExecutor",
					$"No Updater build output containing '{executableName}' was found under '{binRoot}'. " +
					"The client will NOT be able to apply patches. Publish the Updater for this target, e.g.: " +
					$"dotnet publish -c Release -r {GetRuntimeIdentifiers(buildTarget).FirstOrDefault() ?? "<rid>"} FishMMO-Patcher/Updater");
				return;
			}

			try
			{
				CopyDirectoryRecursive(sourceDirectory, buildPath);
				Log.Info("BuildExecutor", $"Copied Updater from '{sourceDirectory}' to '{buildPath}'.");

				// Mark the updater executable runnable. File.Copy does not preserve the
				// executable bit, and a non-executable updater fails at launch with a
				// permission error the player cannot act on. Only meaningful when the
				// editor host itself is Unix.
				if (!targetIsWindows &&
					(UnityEngine.Application.platform == UnityEngine.RuntimePlatform.LinuxEditor ||
					 UnityEngine.Application.platform == UnityEngine.RuntimePlatform.OSXEditor))
				{
					TrySetExecutableBit(Path.Combine(buildPath, executableName));
				}
			}
			catch (Exception ex)
			{
				Log.Error("BuildExecutor", $"Failed to copy the Updater into the build: {ex.Message}. The client will NOT be able to apply patches.");
			}
		}

		/// <summary>
		/// Returns the .NET runtime identifiers that match a Unity standalone build target,
		/// most likely first. Used to locate a per-RID Updater publish directory.
		/// </summary>
		private static IEnumerable<string> GetRuntimeIdentifiers(BuildTarget buildTarget)
		{
			switch (buildTarget)
			{
				case BuildTarget.StandaloneWindows64:
					return new[] { "win-x64" };
				case BuildTarget.StandaloneWindows:
					return new[] { "win-x86", "win-x64" };
				case BuildTarget.StandaloneLinux64:
					return new[] { "linux-x64" };
				case BuildTarget.StandaloneOSX:
					return new[] { "osx-arm64", "osx-x64" };
				default:
					return Array.Empty<string>();
			}
		}

		/// <summary>
		/// Best-effort <c>chmod +x</c> on a copied executable. Failures are logged, not thrown.
		/// </summary>
		private static void TrySetExecutableBit(string filePath)
		{
			if (!File.Exists(filePath))
			{
				return;
			}
			try
			{
				using (System.Diagnostics.Process chmod = System.Diagnostics.Process.Start(
					new System.Diagnostics.ProcessStartInfo("chmod", $"+x \"{filePath}\"")
					{
						UseShellExecute = false,
						CreateNoWindow = true,
					}))
				{
					chmod?.WaitForExit(5000);
				}
			}
			catch (Exception ex)
			{
				Log.Warning("BuildExecutor", $"Could not mark '{filePath}' executable: {ex.Message}. Run 'chmod +x' on it manually before distributing.");
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