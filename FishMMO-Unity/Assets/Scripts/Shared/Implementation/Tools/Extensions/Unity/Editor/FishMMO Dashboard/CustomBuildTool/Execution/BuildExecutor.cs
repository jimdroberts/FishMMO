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
		/// Copies the standalone Updater executable into a standalone client build,
		/// publishing it for the target platform first if that has not already been done.
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
			string projectFile = Path.Combine(updaterProject, "Updater.csproj");
			if (!File.Exists(projectFile))
			{
				Log.Error("BuildExecutor",
					$"The Updater project was not found at '{projectFile}'. The client will NOT be able to apply patches.");
				return;
			}

			/* Derive the executable name from the build target rather than from
			 * Constants.Configuration.UpdaterExecutable. That constant is resolved by the
			 * UNITY_STANDALONE_* defines active when this editor assembly was compiled —
			 * i.e. the editor's current build target, which is not necessarily the target
			 * being built here. Getting this wrong copies a Linux apphost into a Windows
			 * build (or vice versa) and the mismatch only surfaces when a player tries to
			 * patch. */
			bool targetIsWindows = buildTarget == BuildTarget.StandaloneWindows ||
								   buildTarget == BuildTarget.StandaloneWindows64;
			string executableName = targetIsWindows ? "Updater.exe" : "Updater";

			string[] runtimeIdentifiers = GetRuntimeIdentifiers(buildTarget);
			if (runtimeIdentifiers.Length == 0)
			{
				Log.Error("BuildExecutor",
					$"No .NET runtime identifier is known for build target '{buildTarget}', so the Updater cannot be " +
					"published for it. The client will NOT be able to apply patches.");
				return;
			}

			/* Only ever accept a per-RID publish directory.
			 *
			 * The RID-less output (bin/<config>/net8.0[/publish]) carries a native apphost
			 * built for whatever machine produced it. Accepting it as a fallback is how a
			 * Linux editor host ends up shipping its own apphost inside a macOS client, or
			 * an Arch host ships an 'arch-x64' apphost that only resolves against a locally
			 * installed runtime. Both look like a successful build and fail on the player's
			 * machine, at the one moment the client has already shut itself down. A
			 * RID-tagged directory is the only output whose target platform is knowable. */
			string sourceDirectory = FindUpdaterPublishDirectory(updaterProject, runtimeIdentifiers, executableName);

			// Publish on demand when nothing usable is present, or when the Updater sources
			// have moved on since the last publish — a client that ships a stale updater is
			// the same class of bug as one that ships none, just harder to spot.
			if (sourceDirectory == null || IsUpdaterPublishStale(updaterProject, sourceDirectory, executableName))
			{
				string reason = sourceDirectory == null ? "no publish output was found" : "the publish output is older than the Updater sources";
				Log.Info("BuildExecutor", $"Publishing the Updater for '{runtimeIdentifiers[0]}' ({reason}).");

				if (TryPublishUpdater(projectFile, runtimeIdentifiers[0]))
				{
					sourceDirectory = FindUpdaterPublishDirectory(updaterProject, runtimeIdentifiers, executableName)
									  ?? sourceDirectory;
				}
				else if (sourceDirectory != null)
				{
					// Publishing failed but a previous output is still on disk. Shipping the
					// stale one beats shipping nothing — a client with no updater cannot patch
					// at all — but it must not pass silently, because the updater that ends up
					// in this build is not the one in the working tree.
					Log.Warning("BuildExecutor",
						$"Publishing the Updater failed; falling back to the existing output in '{sourceDirectory}'. " +
						"It may not match the current Updater sources.");
				}
			}

			if (sourceDirectory == null)
			{
				Log.Error("BuildExecutor",
					$"No Updater build output containing '{executableName}' was found for {buildTarget}, and publishing one failed. " +
					"The client will NOT be able to apply patches. Publish it manually with: " +
					$"dotnet publish -c Release -r {runtimeIdentifiers[0]} FishMMO-Patcher/Updater/Updater.csproj " +
					"(or run FishMMO-Patcher/publish-updater.sh).");
				return;
			}

			try
			{
				int copiedCount = CopyUpdaterFiles(sourceDirectory, buildPath);
				Log.Info("BuildExecutor", $"Copied Updater ({copiedCount} file(s), '{executableName}') from '{sourceDirectory}' to '{buildPath}'.");

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
		/// Returns the first per-RID publish directory that actually contains
		/// <paramref name="executableName"/>, or null when there is none.
		/// </summary>
		/// <remarks>
		/// Release is preferred over Debug, and the RIDs are tried in the order
		/// <see cref="GetRuntimeIdentifiers"/> returns them (most likely first).
		/// </remarks>
		private static string FindUpdaterPublishDirectory(string updaterProject, string[] runtimeIdentifiers, string executableName)
		{
			string binRoot = Path.Combine(updaterProject, "bin");

			foreach (string configuration in new[] { "Release", "Debug" })
			{
				string frameworkDir = Path.Combine(binRoot, configuration, "net8.0");
				foreach (string rid in runtimeIdentifiers)
				{
					// publish/ first: that is the self-contained single-file output the
					// updater ships as. The sibling build directory is accepted as a
					// fallback because it is still RID-correct, just framework-dependent.
					foreach (string candidate in new[]
					{
						Path.Combine(frameworkDir, rid, "publish"),
						Path.Combine(frameworkDir, rid),
					})
					{
						if (File.Exists(Path.Combine(candidate, executableName)))
						{
							return candidate;
						}
					}
				}
			}
			return null;
		}

		/// <summary>
		/// True when any Updater source file is newer than the published executable.
		/// </summary>
		/// <remarks>
		/// Deliberately coarse — a timestamp comparison, not a real dependency graph. Its
		/// job is to catch the common case of editing the Updater and then building a
		/// client without republishing; MSBuild does the accurate incremental work once we
		/// decide to invoke it. Errors resolve to "not stale" so an unreadable directory
		/// cannot wedge the build in a publish loop.
		/// </remarks>
		private static bool IsUpdaterPublishStale(string updaterProject, string publishDirectory, string executableName)
		{
			try
			{
				DateTime publishedAt = File.GetLastWriteTimeUtc(Path.Combine(publishDirectory, executableName));

				foreach (string sourceFile in Directory.EnumerateFiles(updaterProject, "*.*", SearchOption.AllDirectories))
				{
					// bin/ and obj/ are outputs, not sources; including them would compare
					// the publish output against itself.
					string relative = sourceFile.Substring(updaterProject.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
					if (relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
						relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					string extension = Path.GetExtension(sourceFile);
					if (!extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) &&
						!extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					if (File.GetLastWriteTimeUtc(sourceFile) > publishedAt)
					{
						return true;
					}
				}
			}
			catch (Exception ex)
			{
				Log.Warning("BuildExecutor", $"Could not determine whether the published Updater is up to date: {ex.Message}. Using the existing publish output.");
			}
			return false;
		}

		/// <summary>
		/// Runs <c>dotnet publish</c> for the Updater against a single runtime identifier.
		/// </summary>
		/// <remarks>
		/// Self-contained, single-file and compression settings all live in Updater.csproj
		/// under a <c>RuntimeIdentifier != ''</c> condition, so passing <c>-r</c> is enough
		/// to get the shipping shape and there is no second copy of those settings to drift.
		/// </remarks>
		/// <returns>True when publish exited 0.</returns>
		private static bool TryPublishUpdater(string projectFile, string runtimeIdentifier)
		{
			try
			{
				System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo("dotnet")
				{
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					CreateNoWindow = true,
					WorkingDirectory = Path.GetDirectoryName(projectFile),
				};
				startInfo.ArgumentList.Add("publish");
				startInfo.ArgumentList.Add(projectFile);
				startInfo.ArgumentList.Add("-c");
				startInfo.ArgumentList.Add("Release");
				startInfo.ArgumentList.Add("-r");
				startInfo.ArgumentList.Add(runtimeIdentifier);
				startInfo.ArgumentList.Add("--nologo");

				using (System.Diagnostics.Process publish = System.Diagnostics.Process.Start(startInfo))
				{
					if (publish == null)
					{
						Log.Error("BuildExecutor", "Could not start 'dotnet publish' for the Updater.");
						return false;
					}

					/* Read both pipes to completion before waiting on the process. A publish
					 * writes more than a pipe buffer holds, and a process blocked writing to
					 * a full pipe never exits — waiting first would deadlock the editor. */
					string standardOutput = publish.StandardOutput.ReadToEnd();
					string standardError = publish.StandardError.ReadToEnd();

					const int PublishTimeoutMs = 300000;
					if (!publish.WaitForExit(PublishTimeoutMs))
					{
						Log.Error("BuildExecutor", $"'dotnet publish' for the Updater did not finish within {PublishTimeoutMs / 1000}s; giving up.");
						try { publish.Kill(); } catch { /* already gone */ }
						return false;
					}

					if (publish.ExitCode != 0)
					{
						Log.Error("BuildExecutor",
							$"'dotnet publish -r {runtimeIdentifier}' failed with exit code {publish.ExitCode}.\n{standardOutput}\n{standardError}");
						return false;
					}

					Log.Debug("BuildExecutor", $"Published the Updater for '{runtimeIdentifier}'.\n{standardOutput}");
					return true;
				}
			}
			catch (Exception ex)
			{
				// Most often: dotnet is not on PATH. Unity launched from a desktop shell
				// does not always inherit the shell's PATH, so this is a normal-enough
				// outcome to explain rather than just report.
				Log.Error("BuildExecutor",
					$"Could not run 'dotnet publish' for the Updater: {ex.Message}. " +
					"Ensure the .NET 8 SDK is installed and 'dotnet' is on the PATH Unity was launched with, " +
					"or publish it ahead of time with FishMMO-Patcher/publish-updater.sh.");
				return false;
			}
		}

		/// <summary>
		/// Copies the Updater publish output into the build root, skipping debug symbols.
		/// </summary>
		/// <remarks>
		/// The updater ships as a self-contained single file, but the framework-dependent
		/// fallback needs its managed dependencies and runtime config alongside it, so the
		/// whole directory is copied rather than just the executable. <c>.pdb</c> files are
		/// excluded: they are build artefacts of no use to a player, and every byte here is
		/// also a byte the patch generator has to diff.
		/// </remarks>
		/// <returns>The number of files copied.</returns>
		private static int CopyUpdaterFiles(string sourceDirectory, string destinationDirectory)
		{
			Directory.CreateDirectory(destinationDirectory);

			int copiedCount = 0;
			foreach (string filePath in Directory.GetFiles(sourceDirectory))
			{
				if (Path.GetExtension(filePath).Equals(".pdb", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				File.Copy(filePath, Path.Combine(destinationDirectory, Path.GetFileName(filePath)), true);
				++copiedCount;
			}

			foreach (string subDirectory in Directory.GetDirectories(sourceDirectory))
			{
				copiedCount += CopyUpdaterFiles(subDirectory, Path.Combine(destinationDirectory, Path.GetFileName(subDirectory)));
			}
			return copiedCount;
		}

		/// <summary>
		/// Returns the .NET runtime identifiers that match a Unity standalone build target,
		/// most likely first. Used to locate — or publish — a per-RID Updater output.
		/// </summary>
		/// <remarks>
		/// Portable RIDs only. A distro-specific RID (an Arch/CachyOS SDK reports its own
		/// as <c>arch-x64</c>) has no runtime pack on nuget.org and would pin the output to
		/// that distro; <c>linux-x64</c> runs on Arch and every other glibc distro because
		/// it is not distro-specific. Every RID listed here is declared in Updater.csproj.
		/// </remarks>
		private static string[] GetRuntimeIdentifiers(BuildTarget buildTarget)
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
					// Unity's macOS player is a universal binary; the updater is not, so the
					// architecture the editor host runs on is the best available guess at the
					// one the build is meant for.
					return UnityEngine.SystemInfo.processorType.IndexOf("Apple", StringComparison.OrdinalIgnoreCase) >= 0
						? new[] { "osx-arm64", "osx-x64" }
						: new[] { "osx-x64", "osx-arm64" };
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