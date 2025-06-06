// --------- DO NOT FORMAT DOCUMENT ---------

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
using Debug = UnityEngine.Debug;
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
			AllInOne = 0,
			Login,
			World,
			Scene,
			Client,
			Installer,
		}

		public const string ALL_IN_ONE_SERVER_BUILD_NAME = "All-In-One";
		public const string LOGIN_SERVER_BUILD_NAME = "Login";
		public const string WORLD_SERVER_BUILD_NAME = "World";
		public const string SCENE_SERVER_BUILD_NAME = "Scene";

		public static readonly string ALL_IN_ONE_SERVER_BAT_SCRIPT = @"@echo off
start All-In-One.exe LOGIN
start All-In-One.exe WORLD
start All-In-One.exe SCENE";

		public static readonly string LINUX_ALL_IN_ONE_SERVER_BAT_SCRIPT = @"./All-In-One.exe LOGIN &
./All-In-One.exe WORLD &
./All-In-One.exe SCENE";

		public static readonly string LOGIN_SERVER_BAT_SCRIPT = @"@echo off
start Login.exe LOGIN";
		public static readonly string LINUX_LOGIN_SERVER_BAT_SCRIPT = @"./Login.exe LOGIN";

		public static readonly string WORLD_SERVER_BAT_SCRIPT = @"@echo off
start World.exe WORLD";
		public static readonly string LINUX_WORLD_SERVER_BAT_SCRIPT = @"./World.exe WORLD";

		public static readonly string SCENE_SERVER_BAT_SCRIPT = @"@echo off
start Scene.exe SCENE";
		public static readonly string LINUX_SCENE_SERVER_BAT_SCRIPT = @"./Scene.exe SCENE";

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

				Debug.Log($"XML file '{rootPath}' has been generated successfully.");
			}
			catch (Exception ex)
			{
				Debug.Log($"An error occurred: {ex.Message}");
			}
		}

		private static BuildTargetGroup originalGroup;
		private static NamedBuildTarget originalNamedBuildTargetGroup;
		private static BuildTarget originalBuildTarget;
		private static StandaloneBuildSubtarget originalBuildSubtarget;
		private static ScriptingImplementation originalScriptingImp;
		private static Il2CppCompilerConfiguration originalCompilerConf;
		private static Il2CppCodeGeneration originalOptimization;
		private static bool bakeCollisionMeshes;
		private static bool stripUnusedMeshComponents;
		private static WebGLCompressionFormat compressionFormat;
		private static bool decompressionFallback;
		private static bool dataCaching;

		private static BuildTargetGroup SetActiveBuildTarget(BuildTarget buildTarget, StandaloneBuildSubtarget subTarget)
		{
			// Switch active build target so #defines work properly
			BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);
			EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, buildTarget);
			EditorUserBuildSettings.standaloneBuildSubtarget = subTarget;
			return targetGroup;
		}

		private static void RestoreActiveBuildTarget()
		{
			EditorUserBuildSettings.SwitchActiveBuildTarget(originalGroup, originalBuildTarget);
			EditorUserBuildSettings.standaloneBuildSubtarget = originalBuildSubtarget;
		}

		private static void PushSettings(BuildTarget buildTarget)
		{
			// Get the original active build info
			originalGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
			originalNamedBuildTargetGroup = NamedBuildTarget.FromBuildTargetGroup(originalGroup);
			originalBuildTarget = EditorUserBuildSettings.activeBuildTarget;
			originalBuildSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget;
			originalScriptingImp = PlayerSettings.GetScriptingBackend(originalNamedBuildTargetGroup);
			originalCompilerConf = PlayerSettings.GetIl2CppCompilerConfiguration(originalNamedBuildTargetGroup);
			originalOptimization = PlayerSettings.GetIl2CppCodeGeneration(originalNamedBuildTargetGroup);

			// Enable IL2CPP for webgl
			bakeCollisionMeshes = PlayerSettings.bakeCollisionMeshes;
			stripUnusedMeshComponents = PlayerSettings.stripUnusedMeshComponents;
			compressionFormat = PlayerSettings.WebGL.compressionFormat;
			decompressionFallback = PlayerSettings.WebGL.decompressionFallback;
			dataCaching = PlayerSettings.WebGL.dataCaching;
			if (buildTarget == BuildTarget.WebGL)
			{
				PlayerSettings.SetScriptingBackend(originalNamedBuildTargetGroup, ScriptingImplementation.IL2CPP);
				PlayerSettings.SetIl2CppCompilerConfiguration(originalNamedBuildTargetGroup, Il2CppCompilerConfiguration.Release);
				PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.WebGL, Il2CppCodeGeneration.OptimizeSize);

				// Disable pre-baked meshes and mesh stripping in WebGL
				PlayerSettings.bakeCollisionMeshes = false;
				PlayerSettings.stripUnusedMeshComponents = false;

				// Force Decompression Fallback and GZIP
				PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
				PlayerSettings.WebGL.decompressionFallback = true;

				// Enable data caching on clients so they don't redownload without clearing their cache
				PlayerSettings.WebGL.dataCaching = true;
			}
		}

		private static void PopSettings(BuildTarget buildTarget)
		{
			// Return IL2CPP settings to original
			if (buildTarget == BuildTarget.WebGL)
			{
				PlayerSettings.SetScriptingBackend(originalNamedBuildTargetGroup, originalScriptingImp);
				PlayerSettings.SetIl2CppCompilerConfiguration(originalNamedBuildTargetGroup, originalCompilerConf);
				PlayerSettings.SetIl2CppCodeGeneration(originalNamedBuildTargetGroup, originalOptimization);
				PlayerSettings.bakeCollisionMeshes = bakeCollisionMeshes;
				PlayerSettings.stripUnusedMeshComponents = stripUnusedMeshComponents;
				PlayerSettings.WebGL.compressionFormat = compressionFormat;
				PlayerSettings.WebGL.decompressionFallback = decompressionFallback;
				PlayerSettings.WebGL.dataCaching = dataCaching;
			}

			SetActiveBuildTarget(originalBuildTarget, originalBuildSubtarget);
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

			if (string.IsNullOrWhiteSpace(executableName) ||
				bootstrapScenes == null ||
				bootstrapScenes.Length < 1)
			{
				return;
			}

			// Ensure all assets are included
			AssetDatabase.Refresh();

			PushSettings(buildTarget);

			// Switch active build target so #defines work properly
			BuildTargetGroup targetGroup = SetActiveBuildTarget(buildTarget, subTarget);

			// Append world scene paths to bootstrap scene array
			/*string[] scenes = customBuildType == CustomBuildType.AllInOne ||
							  customBuildType == CustomBuildType.Scene ||
							  customBuildType == CustomBuildType.Client ? AppendWorldScenePaths(bootstrapScenes) : bootstrapScenes;*/

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
					scenes = bootstrapScenes,
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
					Debug.Log($"Build Succeeded: {summary.totalSize} bytes {DateTime.UtcNow}");
					Debug.Log($"Build Duration: {summary.totalTime}");
					Debug.Log($"Scenes Included: {string.Join(", ", bootstrapScenes)}");
					Debug.Log($"Build Target: {buildTarget}");
					Debug.Log($"Build Subtarget: {subTarget}");

					// Log details about each build step
					Debug.Log("Build Steps:");
					int i = 0;
					foreach (var step in report.steps)
					{
						Debug.Log($"Step {i}: {step.name}, Duration: {step.duration}");
						if (step.messages.Length > 0)
						{
							foreach (var message in step.messages)
							{
								if (message.type == LogType.Error)
								{
									Debug.LogError($"Error in step {step.name}: {message.content}");
								}
								else if (message.type == LogType.Warning)
								{
									Debug.LogWarning($"Warning in step {step.name}: {message.content}");
								}
								else
								{
									Debug.Log($"Message in step {step.name}: {message.content}");
								}
							}
						}
						++i;
					}

					string root = Directory.GetParent(Directory.GetCurrentDirectory()).FullName;

					// Copy the configuration files if it's a server build
					if (subTarget == StandaloneBuildSubtarget.Server)
					{
						if (buildTarget == BuildTarget.StandaloneWindows64)
						{
							switch (customBuildType)
							{
								case CustomBuildType.AllInOne:
									CreateScript(Path.Combine(buildPath, "Start.bat"), ALL_IN_ONE_SERVER_BAT_SCRIPT);
									break;
								case CustomBuildType.Login:
									CreateScript(Path.Combine(buildPath, "Start.bat"), LOGIN_SERVER_BAT_SCRIPT);
									break;
								case CustomBuildType.World:
									CreateScript(Path.Combine(buildPath, "Start.bat"), WORLD_SERVER_BAT_SCRIPT);
									break;
								case CustomBuildType.Scene:
									CreateScript(Path.Combine(buildPath, "Start.bat"), SCENE_SERVER_BAT_SCRIPT);
									break;
								case CustomBuildType.Client:
								default:
									break;
							}
						}
						else if (buildTarget == BuildTarget.StandaloneLinux64)
						{
							switch (customBuildType)
							{
								case CustomBuildType.AllInOne:
									CreateScript(Path.Combine(buildPath, "Start.sh"), LINUX_ALL_IN_ONE_SERVER_BAT_SCRIPT);
									break;
								case CustomBuildType.Login:
									CreateScript(Path.Combine(buildPath, "Start.sh"), LINUX_LOGIN_SERVER_BAT_SCRIPT);
									break;
								case CustomBuildType.World:
									CreateScript(Path.Combine(buildPath, "Start.sh"), LINUX_WORLD_SERVER_BAT_SCRIPT);
									break;
								case CustomBuildType.Scene:
									CreateScript(Path.Combine(buildPath, "Start.sh"), LINUX_SCENE_SERVER_BAT_SCRIPT);
									break;
								case CustomBuildType.Client:
								default:
									break;
							}
						}
					}

					// Copy configuration files
					string configurationPath = WorkingEnvironmentOptions.AppendEnvironmentToPath(Constants.Configuration.SetupDirectory);
					CopyConfigurationFiles(buildTarget, customBuildType, Path.Combine(root, configurationPath), buildPath);

					if (customBuildType == CustomBuildType.Installer)
					{
						BuildSetupFolder(root, buildPath);
					}

					if (buildTarget == BuildTarget.WebGL)
					{
						Debug.Log(@"Please visit https://docs.unity3d.com/2022.3/Documentation/Manual/webgl-server-configuration-code-samples.html for further WebGL WebServer configuration.");
					}
				}
				else if (summary.result == BuildResult.Failed)
				{
					Debug.LogError($"Build {report.summary.result}!");
					Debug.LogError($"Total Errors: {summary.totalErrors}");
					Debug.LogError($"Build Target: {buildTarget}");
					Debug.LogError($"Build Subtarget: {subTarget}");

					// Log details about each build step
					Debug.Log("Build Steps:");
					int i = 0;
					foreach (var step in report.steps)
					{
						Debug.Log($"Step {i}: {step.name}, Duration: {step.duration}");
						if (step.messages.Length > 0)
						{
							foreach (var message in step.messages)
							{
								if (message.type == LogType.Error)
								{
									Debug.LogError($"Error in step {step.name}: {message.content}");
								}
								else if (message.type == LogType.Warning)
								{
									Debug.LogWarning($"Warning in step {step.name}: {message.content}");
								}
								else
								{
									Debug.Log($"Message in step {step.name}: {message.content}");
								}
							}
						}
						++i;
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogError($"Exception during build: {ex.Message}");
				Debug.LogError($"Stack trace: {ex.StackTrace}");
			}

			PopSettings(buildTarget);

			RestoreActiveBuildTarget();
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

		/*private static void CopyIPFetchFiles(BuildTarget buildTarget, string ipFetchPath, string configurationPath, string buildPath, string certificatePath = null)
		{
			if (Directory.Exists(buildPath))
			{
				Directory.Delete(buildPath, true);
			}
			Directory.CreateDirectory(buildPath);

			FileUtil.ReplaceFile(Path.Combine(ipFetchPath, "IPFetchServer.py"), Path.Combine(buildPath, "IPFetchServer.py"));
			FileUtil.ReplaceFile(Path.Combine(configurationPath, "appsettings.json"), Path.Combine(buildPath, "appsettings.json"));

			if (!string.IsNullOrWhiteSpace(certificatePath))
			{
				string certPath = Path.Combine(certificatePath, "certificate.pfx");
				if (File.Exists(certPath))
				{
					FileUtil.ReplaceFile(certPath, Path.Combine(buildPath, "certificate.pfx"));
				}
			}
		}

		private static void CopyPatcherFiles(BuildTarget buildTarget, string patcherPath, string configurationPath, string buildPath, string certificatePath = null)
		{
			if (Directory.Exists(buildPath))
			{
				Directory.Delete(buildPath, true);
			}
			Directory.CreateDirectory(buildPath);

			FileUtil.ReplaceFile(Path.Combine(patcherPath, "PatchServer.py"), Path.Combine(buildPath, "PatchServer.py"));
			FileUtil.ReplaceFile(Path.Combine(configurationPath, "appsettings.json"), Path.Combine(buildPath, "appsettings.json"));

			if (!string.IsNullOrWhiteSpace(certificatePath))
			{
				string certPath = Path.Combine(certificatePath, "certificate.pfx");
				if (File.Exists(certPath))
				{
					FileUtil.ReplaceFile(certPath, Path.Combine(buildPath, "certificate.pfx"));
				}
			}
		}*/

		private static void CopyConfigurationFiles(BuildTarget buildTarget, CustomBuildType customBuildType, string configurationPath, string buildPath)
		{
			switch (customBuildType)
			{
				case CustomBuildType.AllInOne:
					FileUtil.ReplaceFile(Path.Combine(configurationPath, "LoginServer.cfg"), Path.Combine(buildPath, "LoginServer.cfg"));
					FileUtil.ReplaceFile(Path.Combine(configurationPath, "WorldServer.cfg"), Path.Combine(buildPath, "WorldServer.cfg"));
					FileUtil.ReplaceFile(Path.Combine(configurationPath, "SceneServer.cfg"), Path.Combine(buildPath, "SceneServer.cfg"));
					break;
				case CustomBuildType.Login:
					FileUtil.ReplaceFile(Path.Combine(configurationPath, "LoginServer.cfg"), Path.Combine(buildPath, "LoginServer.cfg"));
					break;
				case CustomBuildType.World:
					FileUtil.ReplaceFile(Path.Combine(configurationPath, "WorldServer.cfg"), Path.Combine(buildPath, "WorldServer.cfg"));
					break;
				case CustomBuildType.Scene:
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

		private static void CreateScript(string filePath, string scriptContent)
		{
			// Check if the file already exists and delete it if it does
			if (File.Exists(filePath))
			{
				File.Delete(filePath);
			}

			// Create the script file
			using (StreamWriter writer = File.CreateText(filePath))
			{
				// Write the script content to the file
				writer.Write(scriptContent);
			}
		}

		[MenuItem("FishMMO/Build/Installer/Windows x64", priority = -10)]
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

		[MenuItem("FishMMO/Build/Installer/Linux x64", priority = -9)]
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

			// Copy docker-compose to installer directory
			string dockerComposeTarget = Path.Combine(buildPath, "docker-compose.yml");
			FileUtil.DeleteFileOrDirectory(dockerComposeTarget);
			FileUtil.CopyFileOrDirectory(Path.Combine(setup, "docker-compose.yml"), dockerComposeTarget);

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
		[MenuItem("FishMMO/Build/Misc/Update Linker", priority = 12)]
		public static void UpdateLinker()
		{
			string current = Directory.GetCurrentDirectory();
			string assets = Path.Combine(current, "Assets");
			UpdateLinker(assets, Path.Combine(assets, "Dependencies"));
		}

		[MenuItem("FishMMO/Build/Build All Windows", priority = 10)]
		public static void BuildWindows64AllSeparate()
		{
			string selectedPath = EditorUtility.SaveFolderPanel("Pick a save directory", "", "");
			string rootPath = Path.Combine(selectedPath, Constants.Configuration.ProjectName);
			string serverRootPath = Path.Combine(selectedPath, Constants.Configuration.ProjectName + Path.DirectorySeparatorChar + "Server");
			//WorldSceneDetailsCacheBuilder.Rebuild();
			BuildExecutable(rootPath,
							Constants.Configuration.ProjectName,
							BOOTSTRAP_SCENES,
							CustomBuildType.Client,
							GetBuildOptions(),
							StandaloneBuildSubtarget.Player,
							BuildTarget.StandaloneWindows64);

			BuildExecutable(serverRootPath,
							ALL_IN_ONE_SERVER_BUILD_NAME,
							BOOTSTRAP_SCENES,
							CustomBuildType.AllInOne,
							GetBuildOptions(),
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneWindows64);
			BuildExecutable(serverRootPath,
							LOGIN_SERVER_BUILD_NAME,
							BOOTSTRAP_SCENES,
							CustomBuildType.Login,
							GetBuildOptions(),
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneWindows64);
			BuildExecutable(serverRootPath,
							WORLD_SERVER_BUILD_NAME,
							BOOTSTRAP_SCENES,
							CustomBuildType.World,
							GetBuildOptions(),
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneWindows64);
			BuildExecutable(serverRootPath,
							SCENE_SERVER_BUILD_NAME,
							BOOTSTRAP_SCENES,
							CustomBuildType.Scene,
							GetBuildOptions(),
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneWindows64);

#if UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
		Process.Start("xdg-open", rootPath);
#elif UNITY_STANDALONE_WIN
			Process.Start(rootPath);
#endif
		}

		[MenuItem("FishMMO/Build/Build All Linux", priority = 11)]
		public static void BuildAllLinux()
		{
			string selectedPath = EditorUtility.SaveFolderPanel("Pick a save directory", "", "");
			string rootPath = Path.Combine(selectedPath, Constants.Configuration.ProjectName);
			string serverRootPath = Path.Combine(selectedPath, Constants.Configuration.ProjectName + Path.DirectorySeparatorChar + "Server");
			//WorldSceneDetailsCacheBuilder.Rebuild();
			BuildExecutable(rootPath,
							Constants.Configuration.ProjectName,
							BOOTSTRAP_SCENES,
							CustomBuildType.Client,
							GetBuildOptions(),
							StandaloneBuildSubtarget.Player,
							BuildTarget.StandaloneLinux64);

			BuildExecutable(serverRootPath,
							ALL_IN_ONE_SERVER_BUILD_NAME,
							BOOTSTRAP_SCENES,
							CustomBuildType.AllInOne,
							GetBuildOptions(),
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneLinux64);
			BuildExecutable(serverRootPath,
							LOGIN_SERVER_BUILD_NAME,
							BOOTSTRAP_SCENES,
							CustomBuildType.Login,
							GetBuildOptions(),
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneLinux64);
			BuildExecutable(serverRootPath,
							WORLD_SERVER_BUILD_NAME,
							BOOTSTRAP_SCENES,
							CustomBuildType.World,
							GetBuildOptions(),
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneLinux64);
			BuildExecutable(serverRootPath,
							SCENE_SERVER_BUILD_NAME,
							BOOTSTRAP_SCENES,
							CustomBuildType.Scene,
							GetBuildOptions(),
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneLinux64);

#if UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
			Process.Start("xdg-open", rootPath);
#elif UNITY_STANDALONE_WIN
			Process.Start(rootPath);
#endif
		}

		[MenuItem("FishMMO/Build/Server/Windows x64", priority = 2)]
		public static void BuildWindows64AllInOneServer()
		{
			//WorldSceneDetailsCacheBuilder.Rebuild();
			BuildExecutable(ALL_IN_ONE_SERVER_BUILD_NAME,
							BOOTSTRAP_SCENES,
							CustomBuildType.AllInOne,
							GetBuildOptions(),
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneWindows64);
		}

		//[MenuItem("FishMMO/Build/Server/Windows x64 Login", priority = 3)]
		public static void BuildWindows64LoginServer()
		{
			//WorldSceneDetailsCacheBuilder.Rebuild();
			BuildExecutable(LOGIN_SERVER_BUILD_NAME,
							BOOTSTRAP_SCENES,
							CustomBuildType.Login,
							GetBuildOptions(),
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneWindows64);
		}

		//[MenuItem("FishMMO/Build/Server/Windows x64 World", priority = 4)]
		public static void BuildWindows64WorldServer()
		{
			//WorldSceneDetailsCacheBuilder.Rebuild();
			BuildExecutable(WORLD_SERVER_BUILD_NAME,
							BOOTSTRAP_SCENES,
							CustomBuildType.World,
							GetBuildOptions(),
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneWindows64);
		}

		//[MenuItem("FishMMO/Build/Server/Windows x64 Scene", priority = 5)]
		public static void BuildWindows64SceneServer()
		{
			//WorldSceneDetailsCacheBuilder.Rebuild();
			BuildExecutable(SCENE_SERVER_BUILD_NAME,
							BOOTSTRAP_SCENES,
							CustomBuildType.Scene,
							GetBuildOptions(),
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneWindows64);
		}

		[MenuItem("FishMMO/Build/Client/Windows x64", priority = 1)]
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

		[MenuItem("FishMMO/Build/Server/Linux x64", priority = 8)]
		public static void BuildLinux64AllInOneServer()
		{
			//WorldSceneDetailsCacheBuilder.Rebuild();
			BuildExecutable(ALL_IN_ONE_SERVER_BUILD_NAME,
							BOOTSTRAP_SCENES,
							CustomBuildType.AllInOne,
							GetBuildOptions(),
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneLinux64);
		}

		/*[MenuItem("FishMMO/Build/Server/Windows IPFetch Server", priority = 7)]
		public static void BuildWindowsIPFetchServer()
		{
			string rootPath = "";
			if (string.IsNullOrWhiteSpace(rootPath))
			{
				rootPath = EditorUtility.SaveFolderPanel("Pick a save directory", "", "");
				if (string.IsNullOrWhiteSpace(rootPath))
				{
					return;
				}
			}

			string root = Directory.GetParent(Directory.GetCurrentDirectory()).FullName;
			string configurationPath = WorkingEnvironmentOptions.AppendEnvironmentToPath(Constants.Configuration.SetupDirectory);
			string folderName = Constants.Configuration.ProjectName + " Windows IPFetch Server";
			string buildPath = Path.Combine(rootPath, folderName);

			CopyIPFetchFiles(BuildTarget.StandaloneWindows64,
							 Path.Combine(root, "FishMMO-WebServers", "IPFetch"),
							 Path.Combine(root, configurationPath),
							 buildPath,
							 Path.Combine(root, "FishMMO-WebServers", "CertificateGenerator"));

			OpenDirectory(buildPath);
		}

		[MenuItem("FishMMO/Build/Server/Linux IPFetch Server", priority = 9)]
		public static void BuildLinuxIPFetchServer()
		{
			string rootPath = "";
			if (string.IsNullOrWhiteSpace(rootPath))
			{
				rootPath = EditorUtility.SaveFolderPanel("Pick a save directory", "", "");
				if (string.IsNullOrWhiteSpace(rootPath))
				{
					return;
				}
			}

			string root = Directory.GetParent(Directory.GetCurrentDirectory()).FullName;
			string configurationPath = WorkingEnvironmentOptions.AppendEnvironmentToPath(Constants.Configuration.SetupDirectory);
			string folderName = Constants.Configuration.ProjectName + " Linux IPFetch Server";
			string buildPath = Path.Combine(rootPath, folderName);

			CopyIPFetchFiles(BuildTarget.StandaloneLinux64, Path.Combine(root, "FishMMO-WebServers", "IPFetch"), Path.Combine(root, configurationPath), buildPath, Path.Combine(root, "FishMMO-WebServers", "CertificateGenerator"));

			OpenDirectory(buildPath);
		}

		[MenuItem("FishMMO/Build/Server/Windows Patch Server", priority = 10)]
		public static void BuildWindowsPatchServer()
		{
			string rootPath = "";
			if (string.IsNullOrWhiteSpace(rootPath))
			{
				rootPath = EditorUtility.SaveFolderPanel("Pick a save directory", "", "");
				if (string.IsNullOrWhiteSpace(rootPath))
				{
					return;
				}
			}

			string root = Directory.GetParent(Directory.GetCurrentDirectory()).FullName;
			string configurationPath = WorkingEnvironmentOptions.AppendEnvironmentToPath(Constants.Configuration.SetupDirectory);
			string folderName = Constants.Configuration.ProjectName + " Patch Server";
			string buildPath = Path.Combine(rootPath, folderName);

			CopyPatcherFiles(BuildTarget.StandaloneWindows64, Path.Combine(root, "FishMMO-WebServers", "Patcher"), Path.Combine(root, configurationPath), buildPath, Path.Combine(root, "FishMMO-WebServers", "CertificateGenerator"));

			OpenDirectory(buildPath);
		}

		[MenuItem("FishMMO/Build/Server/Linux Patch Server", priority = 11)]
		public static void BuildLinuxPatchServer()
		{
			string rootPath = "";
			if (string.IsNullOrWhiteSpace(rootPath))
			{
				rootPath = EditorUtility.SaveFolderPanel("Pick a save directory", "", "");
				if (string.IsNullOrWhiteSpace(rootPath))
				{
					return;
				}
			}

			string root = Directory.GetParent(Directory.GetCurrentDirectory()).FullName;
			string configurationPath = WorkingEnvironmentOptions.AppendEnvironmentToPath(Constants.Configuration.SetupDirectory);
			string folderName = Constants.Configuration.ProjectName + " Patch Server";
			string buildPath = Path.Combine(rootPath, folderName);

			CopyPatcherFiles(BuildTarget.StandaloneLinux64, Path.Combine(root, "FishMMO-WebServers", "Patcher"), Path.Combine(root, configurationPath), buildPath, Path.Combine(root, "FishMMO-WebServers", "CertificateGenerator"));

			OpenDirectory(buildPath);
		}*/

		[MenuItem("FishMMO/Build/Client/Linux x64", priority = 2)]
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

		[MenuItem("FishMMO/Build/Client/WebGL", priority = 3)]
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

		[MenuItem("FishMMO/Build/Addressables/Build Windows Client Addressables")]
		public static void BuildWindowsClientAddressables()
		{
			PushSettings(BuildTarget.StandaloneWindows64);
			SetActiveBuildTarget(BuildTarget.StandaloneWindows64, StandaloneBuildSubtarget.Player);
			BuildAddressables(serverAddressableGroups);
			PopSettings(BuildTarget.StandaloneWindows64);
			RestoreActiveBuildTarget();
		}

		[MenuItem("FishMMO/Build/Addressables/Build Windows Server Addressables")]
		public static void BuildWindowsServerAddressables()
		{
			PushSettings(BuildTarget.StandaloneWindows64);
			SetActiveBuildTarget(BuildTarget.StandaloneWindows64, StandaloneBuildSubtarget.Server);
			BuildAddressables(clientAddressableGroups);
			PopSettings(BuildTarget.StandaloneWindows64);
			RestoreActiveBuildTarget();
		}

		[MenuItem("FishMMO/Build/Addressables/Build Linux Client Addressables")]
		public static void BuildLinuxClientAddressables()
		{
			PushSettings(BuildTarget.StandaloneLinux64);
			SetActiveBuildTarget(BuildTarget.StandaloneLinux64, StandaloneBuildSubtarget.Player);
			BuildAddressables(serverAddressableGroups);
			PopSettings(BuildTarget.StandaloneLinux64);
			RestoreActiveBuildTarget();
		}

		[MenuItem("FishMMO/Build/Addressables/Build Linux Server Addressables")]
		public static void BuildLinuxServerAddressables()
		{
			PushSettings(BuildTarget.StandaloneLinux64);
			SetActiveBuildTarget(BuildTarget.StandaloneLinux64, StandaloneBuildSubtarget.Server);
			BuildAddressables(clientAddressableGroups);
			PopSettings(BuildTarget.StandaloneLinux64);
			RestoreActiveBuildTarget();
		}

		[MenuItem("FishMMO/Build/Addressables/Build WebGL Addressables")]
		public static void BuildWebGLAddressables()
		{
			PushSettings(BuildTarget.WebGL);
			SetActiveBuildTarget(BuildTarget.WebGL, StandaloneBuildSubtarget.Player);
			BuildAddressables(serverAddressableGroups);
			PopSettings(BuildTarget.WebGL);
			RestoreActiveBuildTarget();
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
							Debug.Log($"Group {group.name} has been excluded from the build.");
						}
						else
						{
							schema.IncludeInBuild = true;
							Debug.LogWarning($"Group {group.name} has been included in the build.");
						}
					}
					else
					{
						Debug.LogWarning($"No schema found for group: {group.name}");
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
					Debug.Log($"Deleted previous Addressable build directory at {buildPath}");
				}
				catch (Exception ex)
				{
					Debug.LogError($"Failed to delete previous build directory: {ex.Message}");
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
					Debug.LogError(result.Error);
					Debug.LogError("Addressable content build failure (duration: " + TimeSpan.FromSeconds(result.Duration).ToString("g") + ")");
				}
				else
				{
					// Log information about the asset bundles that were built
					if (result.AssetBundleBuildResults != null && result.AssetBundleBuildResults.Count > 0)
					{
						Debug.Log("Built Asset Bundles:");
						foreach (var bundleResult in result.AssetBundleBuildResults)
						{
							Debug.Log($"Bundle: {bundleResult.SourceAssetGroup.Name} | {bundleResult.FilePath}");

							// Log each asset in the bundle
							foreach (var assetPath in bundleResult.SourceAssetGroup.entries)
							{
								Debug.Log($"  - Asset: {assetPath}");
							}
						}
					}
					else
					{
						Debug.Log("No asset bundles were built.");
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogError($"Error during Addressables build: {ex.Message}");
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