#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Build.Reporting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FishMMO.Shared
{
	public class CustomBuildTool
	{
		private static string[] clientAddressableGroups = new string[] { "ClientOnly" };
		private static string[] serverAddressableGroups = new string[] { "ServerOnly" };

		public enum CustomBuildType : byte
		{
			Server = 0,
			Client,
			Installer,
		}

		public const string GAMESERVER_BUILD_NAME = "GameServer";

		public static readonly string[] BOOTSTRAP_SCENES = new string[]
		{
			Constants.Configuration.BootstrapScenePath + "MainBootstrap.unity",
		};

		public static string GetBuildTargetShortName(BuildTarget target)
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

		public static void UpdateLinker(string rootPath, string directoryPath)
		{
			string linkerPath = Path.Combine(rootPath, "link.xml");

			try
			{
				// Create a new XML document
				XmlDocument xmlDoc = new XmlDocument();

				// Create the root element named "linker"
				XmlElement rootElement = xmlDoc.CreateElement("linker");
				xmlDoc.AppendChild(rootElement);

				HashSet<string> nameSpaces = new HashSet<string>();

				Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
				foreach (Assembly assembly in assemblies)
				{
					XmlElement assemblyElement = xmlDoc.CreateElement("assembly");
					assemblyElement.SetAttribute("fullname", assembly.FullName);
					rootElement.AppendChild(assemblyElement);

					var assemblyNameSpaces = assembly.GetTypes()
											 .Select(type => type.Namespace)
											 .Where(n => !string.IsNullOrEmpty(n) && !nameSpaces.Contains(n))
											 .Distinct()
											 .OrderBy(n => n);

					foreach (string nameSpace in assemblyNameSpaces)
					{
						nameSpaces.Add(nameSpace);

						XmlElement typeElement = xmlDoc.CreateElement("type");
						typeElement.SetAttribute("fullname", nameSpace);
						typeElement.SetAttribute("preserve", "all");
						assemblyElement.AppendChild(typeElement);
					}
				}

				// Save the XML document to the specified file
				xmlDoc.Save(linkerPath);

				Log.Debug($"XML file '{rootPath}' has been generated successfully.");
			}
			catch (Exception ex)
			{
				Log.Debug($"An error occurred: {ex.Message}");
			}
		}

		// Store original settings
		private static BuildTargetGroup originalGroup;
		private static BuildTarget originalBuildTarget;
		private static StandaloneBuildSubtarget originalBuildSubtarget;

		// PlayerSettings for the original build target group
		private static ScriptingImplementation originalScriptingImp;
		private static Il2CppCompilerConfiguration originalCompilerConf;
		private static Il2CppCodeGeneration originalOptimization;
		private static bool originalBakeCollisionMeshes;
		private static bool originalStripUnusedMeshComponents;
		private static WebGLCompressionFormat originalCompressionFormat;
		private static bool originalDecompressionFallback;
		private static bool originalDataCaching;

		private static BuildTargetGroup SetActiveBuildTarget(BuildTarget buildTarget, StandaloneBuildSubtarget subTarget)
		{
			// Switch active build target so #defines work properly
			BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);
			EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, buildTarget);
			EditorUserBuildSettings.standaloneBuildSubtarget = subTarget;
			return targetGroup;
		}

		private static void PushSettings(BuildTarget buildTarget)
		{
			// Get and store the ORIGINAL active build target settings
			originalGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
			originalBuildTarget = EditorUserBuildSettings.activeBuildTarget;
			originalBuildSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget;

			// Get and store the ORIGINAL Player Settings for the currently selected build target group
			NamedBuildTarget originalNamedBuildTargetGroup = NamedBuildTarget.FromBuildTargetGroup(originalGroup);
			originalScriptingImp = PlayerSettings.GetScriptingBackend(originalNamedBuildTargetGroup);
			originalCompilerConf = PlayerSettings.GetIl2CppCompilerConfiguration(originalNamedBuildTargetGroup);
			originalOptimization = PlayerSettings.GetIl2CppCodeGeneration(originalNamedBuildTargetGroup);

			originalBakeCollisionMeshes = PlayerSettings.bakeCollisionMeshes;
			originalStripUnusedMeshComponents = PlayerSettings.stripUnusedMeshComponents;

			// WebGL specific settings (even if not building WebGL, as we might change to it)
			originalCompressionFormat = PlayerSettings.WebGL.compressionFormat;
			originalDecompressionFallback = PlayerSettings.WebGL.decompressionFallback;
			originalDataCaching = PlayerSettings.WebGL.dataCaching;

			// Switch active build target to the desired one for the build.
			BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);
			EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, buildTarget);

			// Set the standaloneBuildSubtarget for the *newly active* build target if it's a standalone
			if (buildTarget == BuildTarget.StandaloneWindows64 || buildTarget == BuildTarget.StandaloneLinux64 || buildTarget == BuildTarget.StandaloneOSX)
			{
				EditorUserBuildSettings.standaloneBuildSubtarget = originalBuildSubtarget;
			}
			else
			{
				// For non-standalone builds like WebGL, it might not have a meaningful subtarget,
				// or you might want to explicitly set it to StandaloneBuildSubtarget.Player
				// if that's the desired default for non-server builds.
				EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
			}

			// Now, apply the *desired* settings for the buildTarget being built.
			NamedBuildTarget currentNamedBuildTargetGroup = NamedBuildTarget.FromBuildTargetGroup(targetGroup);

			if (buildTarget == BuildTarget.WebGL)
			{
				PlayerSettings.SetScriptingBackend(currentNamedBuildTargetGroup, ScriptingImplementation.IL2CPP);
				PlayerSettings.SetIl2CppCompilerConfiguration(currentNamedBuildTargetGroup, Il2CppCompilerConfiguration.Release);
				PlayerSettings.SetIl2CppCodeGeneration(currentNamedBuildTargetGroup, Il2CppCodeGeneration.OptimizeSize);

				// WebGL specific player settings
				PlayerSettings.bakeCollisionMeshes = false;
				PlayerSettings.stripUnusedMeshComponents = false;
				PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
				PlayerSettings.WebGL.decompressionFallback = true;
				PlayerSettings.WebGL.dataCaching = true;
			}
		}

		/// <summary>
		/// Restores original Editor and Player settings.
		/// </summary>
		private static void PopSettings()
		{
			// Restore Player Settings first, targeting the ORIGINAL build target group.
			// This ensures the settings are put back for the group they came from.
			NamedBuildTarget originalNamedBuildTargetGroup = NamedBuildTarget.FromBuildTargetGroup(originalGroup);

			PlayerSettings.SetScriptingBackend(originalNamedBuildTargetGroup, originalScriptingImp);
			PlayerSettings.SetIl2CppCompilerConfiguration(originalNamedBuildTargetGroup, originalCompilerConf);
			PlayerSettings.SetIl2CppCodeGeneration(originalNamedBuildTargetGroup, originalOptimization);

			// Restore common player settings
			PlayerSettings.bakeCollisionMeshes = originalBakeCollisionMeshes;
			PlayerSettings.stripUnusedMeshComponents = originalStripUnusedMeshComponents;

			// Restore WebGL specific settings
			PlayerSettings.WebGL.compressionFormat = originalCompressionFormat;
			PlayerSettings.WebGL.decompressionFallback = originalDecompressionFallback;
			PlayerSettings.WebGL.dataCaching = originalDataCaching;

			// Restore the original active build target after player settings are restored.
			EditorUserBuildSettings.SwitchActiveBuildTarget(originalGroup, originalBuildTarget);
			EditorUserBuildSettings.standaloneBuildSubtarget = originalBuildSubtarget;

			// Ensure Editor refreshes to reflect changes (might not be strictly necessary for PlayerSettings but good practice)
			AssetDatabase.Refresh();

			ForceEditorScriptRecompile();
		}

		private static void ForceEditorScriptRecompile()
		{
			string[] allScriptGuids = AssetDatabase.FindAssets("t:Script");
			if (allScriptGuids.Length > 0)
			{
				string scriptPath = AssetDatabase.GUIDToAssetPath(allScriptGuids[0]);
				// Only reimport if the file actually exists, to avoid errors with deleted assets
				if (File.Exists(scriptPath))
				{
					Log.Debug($"Forcing editor recompile by reimporting: {scriptPath}");
					AssetDatabase.ImportAsset(scriptPath, ImportAssetOptions.ForceUpdate);
				}
				else
				{
					Log.Warning("Found script GUID but file does not exist to reimport. Define symbols might not update as expected.");
				}
			}
			else
			{
				Log.Warning("No script files found to force editor recompile. Define symbols might not update.");
			}
		}

		private static void BuildExecutable(string executableName, string[] bootstrapScenes, CustomBuildType customBuildType, BuildOptions buildOptions, StandaloneBuildSubtarget subTarget, BuildTarget buildTarget)
		{
			BuildExecutable(null, executableName, bootstrapScenes, customBuildType, buildOptions, subTarget, buildTarget);
		}

		private static void BuildExecutable(string rootPath, string executableName, string[] bootstrapScenes, CustomBuildType customBuildType, BuildOptions buildOptions, StandaloneBuildSubtarget subTarget, BuildTarget buildTarget)
		{
			string tmpPath = rootPath;
			if (string.IsNullOrWhiteSpace(rootPath))
			{
				rootPath = EditorUtility.SaveFolderPanel("Pick a save directory", "", "");
				if (string.IsNullOrWhiteSpace(rootPath))
				{
					return;
				}
			}

			if (string.IsNullOrWhiteSpace(executableName))
			{
				return;
			}

			// Ensure all assets are ready
			AssetDatabase.Refresh();

			PushSettings(buildTarget);

			BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);

			// Append world scene paths to bootstrap scene array
			string[] scenes = customBuildType == CustomBuildType.Server ||
							  customBuildType == CustomBuildType.Client ? AppendWorldScenePaths(bootstrapScenes) : bootstrapScenes;

			string folderName = executableName;
			if (customBuildType != CustomBuildType.Installer &&
				customBuildType != CustomBuildType.Client)
			{
				folderName = Constants.Configuration.ProjectName + " " + folderName;
			}
			if (customBuildType == CustomBuildType.Installer)
			{
				folderName = Constants.Configuration.ProjectName + GetBuildTargetShortName(buildTarget) + " " + folderName;
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

				// Build the project
				BuildReport report = BuildPipeline.BuildPlayer(options);

				// Check the results of the build
				BuildSummary summary = report.summary;
				if (summary.result == BuildResult.Succeeded)
				{
					Log.Debug($"Build Succeeded: {summary.totalSize} bytes {DateTime.UtcNow}");
					Log.Debug($"Build Duration: {summary.totalTime}");
					Log.Debug($"Scenes Included: {string.Join(", ", bootstrapScenes)}");
					Log.Debug($"Build Target: {buildTarget}");
					Log.Debug($"Build Subtarget: {subTarget}");

					// Log details about each build step
					Log.Debug("Build Steps:");
					int i = 0;
					foreach (var step in report.steps)
					{
						Log.Debug($"Step {i}: {step.name}, Duration: {step.duration}");
						if (step.messages.Length > 0)
						{
							foreach (var message in step.messages)
							{
								if (message.type == LogType.Error)
								{
									Log.Error($"Error in step {step.name}: {message.content}");
								}
								else if (message.type == LogType.Warning)
								{
									Log.Warning($"Warning in step {step.name}: {message.content}");
								}
								else
								{
									Log.Debug($"Message in step {step.name}: {message.content}");
								}
							}
						}
						++i;
					}

					string root = Directory.GetParent(Directory.GetCurrentDirectory()).FullName;

					// Copy configuration files
					string configurationPath = WorkingEnvironmentOptions.AppendEnvironmentToPath(Constants.Configuration.SetupDirectory);
					CopyConfigurationFiles(buildTarget, customBuildType, Path.Combine(root, configurationPath), buildPath);

					if (customBuildType == CustomBuildType.Installer)
					{
						BuildSetupFolder(root, buildPath);
					}

					if (buildTarget == BuildTarget.WebGL)
					{
						Log.Debug(@"Please visit https://docs.unity3d.com/2022.3/Documentation/Manual/webgl-server-configuration-code-samples.html for further WebGL WebServer configuration.");
					}
				}
				else if (summary.result == BuildResult.Failed)
				{
					Log.Error($"Build {report.summary.result}!");
					Log.Error($"Total Errors: {summary.totalErrors}");
					Log.Error($"Build Target: {buildTarget}");
					Log.Error($"Build Subtarget: {subTarget}");

					// Log details about each build step
					Log.Debug("Build Steps:");
					int i = 0;
					foreach (var step in report.steps)
					{
						Log.Debug($"Step {i}: {step.name}, Duration: {step.duration}");
						if (step.messages.Length > 0)
						{
							foreach (var message in step.messages)
							{
								if (message.type == LogType.Error)
								{
									Log.Error($"Error in step {step.name}: {message.content}");
								}
								else if (message.type == LogType.Warning)
								{
									Log.Warning($"Warning in step {step.name}: {message.content}");
								}
								else
								{
									Log.Debug($"Message in step {step.name}: {message.content}");
								}
							}
						}
						++i;
					}
				}
			}
			catch (Exception ex)
			{
				Log.Error($"Exception during build: {ex.Message}");
				Log.Error($"Stack trace: {ex.StackTrace}");
			}
			finally
			{
				Log.Debug("Build finished.");

				PopSettings();
			}
		}

		private static string[] AppendWorldScenePaths(string[] requiredPaths)
		{
			HashSet<string> allPaths = new HashSet<string>(requiredPaths);

			HashSet<string> worldScenes = DirectoryExtensions.GetAllFiles(Constants.Configuration.WorldScenePath, ".unity");
			allPaths.UnionWith(worldScenes);

			if (EditorPrefs.GetBool("FishMMOEnableLocalDirectory"))
			{
				HashSet<string> localScenes = DirectoryExtensions.GetAllFiles(Constants.Configuration.LocalScenePath, ".unity");
				allPaths.UnionWith(localScenes);
			}

			return allPaths.ToArray();
		}

		private static void CopyConfigurationFiles(BuildTarget buildTarget, CustomBuildType customBuildType, string configurationPath, string buildPath)
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
					}
					break;
				default: break;
			}
			if (customBuildType != CustomBuildType.Client)
			{
				FileUtil.ReplaceFile(Path.Combine(configurationPath, "appsettings.json"), Path.Combine(buildPath, "appsettings.json"));
			}
		}

		[MenuItem("FishMMO/Build/Windows x64/Database Installer")]
		public static void BuildWindows64Setup()
		{
			BuildExecutable("Installer",
							new string[]
							{
								Constants.Configuration.InstallerPath,
							},
							CustomBuildType.Installer,
							GetBuildOptions(),
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneWindows64);
		}

		[MenuItem("FishMMO/Build/Linux x64/Database Installer")]
		public static void BuildLinuxSetup()
		{
			BuildExecutable("Installer",
							new string[]
							{
								Constants.Configuration.InstallerPath,
							},
							CustomBuildType.Installer,
							GetBuildOptions(),
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneLinux64);
		}

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

		private static BuildOptions GetBuildOptions(BuildTarget? buildTarget = null)
		{
			BuildOptions buildOptions = BuildOptions.CleanBuildCache | BuildOptions.ShowBuiltPlayer;

			WorkingEnvironmentState workingEnvironmentState = WorkingEnvironmentOptions.GetWorkingEnvironmentState();
			switch (workingEnvironmentState)
			{
				case WorkingEnvironmentState.Release:
					break;
				case WorkingEnvironmentState.Development:
					if (buildTarget != null && buildTarget == BuildTarget.WebGL)
					{
						break;
					}
					buildOptions |= BuildOptions.Development;
					break;
				default: break;
			}

			return buildOptions;
		}

		#region Menu
		[MenuItem("FishMMO/Update Linker")]
		public static void UpdateLinker()
		{
			string current = Directory.GetCurrentDirectory();
			string assets = Path.Combine(current, "Assets");
			UpdateLinker(assets, Path.Combine(assets, "Dependencies"));
		}

		[MenuItem("FishMMO/Build/Windows x64/Game Server")]
		public static void BuildWindows64GameServer()
		{
			//WorldSceneDetailsCacheBuilder.Rebuild();
			BuildExecutable(GAMESERVER_BUILD_NAME,
							BOOTSTRAP_SCENES,
							CustomBuildType.Server,
							GetBuildOptions(),
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneWindows64);
		}

		[MenuItem("FishMMO/Build/Windows x64/Game Client")]
		public static void BuildWindows64Client()
		{
			//WorldSceneDetailsCacheBuilder.Rebuild();
			BuildExecutable(Constants.Configuration.ProjectName,
							BOOTSTRAP_SCENES,
							CustomBuildType.Client,
							GetBuildOptions(),
							StandaloneBuildSubtarget.Player,
							BuildTarget.StandaloneWindows64);
		}

		[MenuItem("FishMMO/Build/Linux x64/Game Server")]
		public static void BuildLinux64GameServer()
		{
			//WorldSceneDetailsCacheBuilder.Rebuild();
			BuildExecutable(GAMESERVER_BUILD_NAME,
							BOOTSTRAP_SCENES,
							CustomBuildType.Server,
							GetBuildOptions(),
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneLinux64);
		}

		[MenuItem("FishMMO/Build/Linux x64/Game Client")]
		public static void BuildLinux64Client()
		{
			//WorldSceneDetailsCacheBuilder.Rebuild();
			BuildExecutable(Constants.Configuration.ProjectName,
							BOOTSTRAP_SCENES,
							CustomBuildType.Client,
							GetBuildOptions(),
							StandaloneBuildSubtarget.Player,
							BuildTarget.StandaloneLinux64);
		}

		[MenuItem("FishMMO/Build/WebGL/Game Client")]
		public static void BuildWebGLClient()
		{
			//WorldSceneDetailsCacheBuilder.Rebuild();
			BuildExecutable(Constants.Configuration.ProjectName,
							BOOTSTRAP_SCENES,
							CustomBuildType.Client,
							GetBuildOptions(BuildTarget.WebGL),
							StandaloneBuildSubtarget.Player,
							BuildTarget.WebGL);
		}

		[MenuItem("FishMMO/Build/Windows x64/Addressables/Client Addressables")]
		public static void BuildWindowsClientAddressables()
		{
			PushSettings(BuildTarget.StandaloneWindows64);
			SetActiveBuildTarget(BuildTarget.StandaloneWindows64, StandaloneBuildSubtarget.Player);
			BuildAddressables(serverAddressableGroups);
			PopSettings();
		}

		[MenuItem("FishMMO/Build/Windows x64/Addressables/Server Addressables")]
		public static void BuildWindowsServerAddressables()
		{
			PushSettings(BuildTarget.StandaloneWindows64);
			SetActiveBuildTarget(BuildTarget.StandaloneWindows64, StandaloneBuildSubtarget.Server);
			BuildAddressables(clientAddressableGroups);
			PopSettings();
		}

		[MenuItem("FishMMO/Build/Linux x64/Addressables/Client Addressables")]
		public static void BuildLinuxClientAddressables()
		{
			PushSettings(BuildTarget.StandaloneLinux64);
			SetActiveBuildTarget(BuildTarget.StandaloneLinux64, StandaloneBuildSubtarget.Player);
			BuildAddressables(serverAddressableGroups);
			PopSettings();
		}

		[MenuItem("FishMMO/Build/Linux x64/Addressables/Server Addressables")]
		public static void BuildLinuxServerAddressables()
		{
			PushSettings(BuildTarget.StandaloneLinux64);
			SetActiveBuildTarget(BuildTarget.StandaloneLinux64, StandaloneBuildSubtarget.Server);
			BuildAddressables(clientAddressableGroups);
			PopSettings();
		}

		[MenuItem("FishMMO/Build/WebGL/Addressables/Client Addressables")]
		public static void BuildWebGLAddressables()
		{
			PushSettings(BuildTarget.WebGL);
			SetActiveBuildTarget(BuildTarget.WebGL, StandaloneBuildSubtarget.Player);
			BuildAddressables(serverAddressableGroups);
			PopSettings();
		}

		public static void BuildAddressables(string[] excludeGroups)
		{
			// Get the original AddressableAssetSettings (default settings)
			AddressableAssetSettings originalSettings = AddressableAssetSettingsDefaultObject.GetSettings(true);

			// Loop through each Addressable group and exclude based on the provided group names
			foreach (var group in originalSettings.groups)
			{
				foreach (var exclusion in excludeGroups)
				{
					var schema = group.GetSchema<BundledAssetGroupSchema>();
					if (schema != null)
					{
						if (group.name.Contains(exclusion))
						{
							schema.IncludeInBuild = false;
							Log.Debug($"Group {group.name} has been excluded from the build.");
						}
						else
						{
							schema.IncludeInBuild = true;
							Log.Warning($"Group {group.name} has been included in the build.");
						}
					}
					else
					{
						Log.Warning($"No schema found for group: {group.name}");
					}
				}
			}

			// Clean up old Addressable builds if the build path exists
			string buildPath = Addressables.BuildPath;
			if (Directory.Exists(buildPath))
			{
				try
				{
					Directory.Delete(buildPath, recursive: true);
					Log.Debug($"Deleted previous Addressable build directory at {buildPath}");
				}
				catch (Exception ex)
				{
					Log.Error($"Failed to delete previous build directory: {ex.Message}");
				}
			}

			// Start the Addressables build process
			try
			{
				// Perform the actual Addressables build
				AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);

				// Log the overall build result
				if (!string.IsNullOrEmpty(result.Error))
				{
					Log.Error(result.Error);
					Log.Error("Addressable content build failure (duration: " + TimeSpan.FromSeconds(result.Duration).ToString("g") + ")");
				}
				else
				{
					// Log information about the asset bundles that were built
					if (result.AssetBundleBuildResults != null && result.AssetBundleBuildResults.Count > 0)
					{
						Log.Debug("Built Asset Bundles:");
						foreach (var bundleResult in result.AssetBundleBuildResults)
						{
							Log.Debug($"Bundle: {bundleResult.SourceAssetGroup.Name} | {bundleResult.FilePath}");

							// Log each asset in the bundle
							foreach (var assetPath in bundleResult.SourceAssetGroup.entries)
							{
								Log.Debug($"  - Asset: {assetPath}");
							}
						}
					}
					else
					{
						Log.Debug("No asset bundles were built.");
					}
				}
			}
			catch (Exception ex)
			{
				Log.Error($"Error during Addressables build: {ex.Message}");
			}

			// Optionally, refresh the asset database after the build
			AssetDatabase.Refresh();
		}
		#endregion

		private static void OpenDirectory(string directory)
		{
			directory = directory.Replace('/', Path.DirectorySeparatorChar);
			directory = directory.Replace('\\', Path.DirectorySeparatorChar);

			if (!Directory.Exists(directory))
			{
				return;
			}

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				Process.Start("explorer", directory);
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				Process.Start("xdg-open", directory);
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				Process.Start("open", directory);
			}
		}
	}
}
#endif