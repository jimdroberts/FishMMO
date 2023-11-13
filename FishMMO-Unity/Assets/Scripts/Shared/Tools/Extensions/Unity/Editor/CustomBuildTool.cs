// --------- DO NOT FORMAT DOCUMENT ---------

#if UNITY_EDITOR
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using UnityEditor.Build.Reporting;
using Debug = UnityEngine.Debug;
using System.Diagnostics;
using UnityEngine;

namespace FishMMO.Shared
{
	public class CustomBuildTool
	{
		public enum CustomBuildType : byte
		{
			AllInOne = 0,
			Login,
			World,
			Scene,
			Client,
			Installer,
		}

		public const BuildOptions BASE_BUILD_OPTIONS = BuildOptions.CleanBuildCache | BuildOptions.Development;

		public const string ALL_IN_ONE_SERVER_BUILD_NAME = "All-In-One";
		public const string LOGIN_SERVER_BUILD_NAME = "Login";
		public const string WORLD_SERVER_BUILD_NAME = "World";
		public const string SCENE_SERVER_BUILD_NAME = "Scene";
		public const string CLIENT_BUILD_NAME = "Client";

		public static readonly string[] ALL_IN_ONE_SERVER_BOOTSTRAP_SCENES = new string[]
		{
			"Assets\\Scenes\\Bootstraps\\ServerLauncher.unity",
			"Assets\\Scenes\\Bootstraps\\LoginServer.unity",
			"Assets\\Scenes\\Bootstraps\\WorldServer.unity",
			"Assets\\Scenes\\Bootstraps\\SceneServer.unity",
		};

		public static readonly string ALL_IN_ONE_SERVER_BAT_SCRIPT = @"@echo off
start All-In-One.exe LOGIN
start All-In-One.exe WORLD
start All-In-One.exe SCENE";

		public static readonly string LINUX_ALL_IN_ONE_SERVER_BAT_SCRIPT = @"./All-In-One.exe LOGIN &
./All-In-One.exe WORLD &
./All-In-One.exe SCENE";

		public static readonly string[] LOGIN_SERVER_BOOTSTRAP_SCENES = new string[]
		{
		"Assets\\Scenes\\Bootstraps\\ServerLauncher.unity",
		"Assets\\Scenes\\Bootstraps\\LoginServer.unity",
		};

		public static readonly string LOGIN_SERVER_BAT_SCRIPT = @"@echo off
start Login.exe LOGIN";
		public static readonly string LINUX_LOGIN_SERVER_BAT_SCRIPT = @"./Login.exe LOGIN";

		public static readonly string[] WORLD_SERVER_BOOTSTRAP_SCENES = new string[]
		{
			"Assets\\Scenes\\Bootstraps\\ServerLauncher.unity",
			"Assets\\Scenes\\Bootstraps\\WorldServer.unity",
		};

		public static readonly string WORLD_SERVER_BAT_SCRIPT = @"@echo off
start World.exe WORLD";
		public static readonly string LINUX_WORLD_SERVER_BAT_SCRIPT = @"./World.exe WORLD";

		public static readonly string[] SCENE_SERVER_BOOTSTRAP_SCENES = new string[]
		{
			"Assets\\Scenes\\Bootstraps\\ServerLauncher.unity",
			"Assets\\Scenes\\Bootstraps\\SceneServer.unity",
		};

		public static readonly string SCENE_SERVER_BAT_SCRIPT = @"@echo off
start Scene.exe SCENE";
		public static readonly string LINUX_SCENE_SERVER_BAT_SCRIPT = @"./Scene.exe SCENE";

		public static readonly string[] CLIENT_BOOTSTRAP_SCENES = new string[]
		{
			"Assets\\Scenes\\Bootstraps\\ClientBootstrap.unity",
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

			// get the original active build info
			BuildTargetGroup originalGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
			BuildTarget originalBuildTarget = EditorUserBuildSettings.activeBuildTarget;
			StandaloneBuildSubtarget originalBuildSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget;
			ScriptingImplementation originalScriptingImp = PlayerSettings.GetScriptingBackend(originalGroup);
			Il2CppCompilerConfiguration originalCompilerConf = PlayerSettings.GetIl2CppCompilerConfiguration(originalGroup);
			UnityEditor.Build.NamedBuildTarget originalNamedBuildTargetGroup = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(originalGroup);
			UnityEditor.Build.Il2CppCodeGeneration originalOptimization = PlayerSettings.GetIl2CppCodeGeneration(originalNamedBuildTargetGroup);

			// enable IL2CPP for webgl
			if (buildTarget == BuildTarget.WebGL)
			{
				PlayerSettings.SetScriptingBackend(EditorUserBuildSettings.selectedBuildTargetGroup, ScriptingImplementation.IL2CPP);
				PlayerSettings.SetIl2CppCompilerConfiguration(EditorUserBuildSettings.selectedBuildTargetGroup, Il2CppCompilerConfiguration.Release);
				PlayerSettings.SetIl2CppCodeGeneration(UnityEditor.Build.NamedBuildTarget.WebGL, UnityEditor.Build.Il2CppCodeGeneration.OptimizeSize);
			}

			// switch active build target so #defines work properly
			BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);
			EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, buildTarget);
			EditorUserBuildSettings.standaloneBuildSubtarget = subTarget;

			// append world scene paths to bootstrap scene array
			string[] scenes = customBuildType == CustomBuildType.AllInOne ||
							  customBuildType == CustomBuildType.Scene ||
							  customBuildType == CustomBuildType.Client ? AppendWorldScenePaths(bootstrapScenes) : bootstrapScenes;

			string folderName = executableName;
			if (string.IsNullOrEmpty(tmpPath))
			{
				folderName += GetBuildTargetShortName(buildTarget);
			}
			string buildPath = Path.Combine(rootPath, folderName);

			// build the project
			BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions()
			{
				locationPathName = Path.Combine(buildPath, executableName + ".exe"),
				options = buildOptions,
				scenes = scenes,
				subtarget = (int)subTarget,
				target = buildTarget,
				targetGroup = targetGroup,
			});

			// check the results of the build
			BuildSummary summary = report.summary;
			if (summary.result == BuildResult.Succeeded)
			{
				Debug.Log("Build succeeded: " + summary.totalSize + " bytes " + DateTime.UtcNow);

				string root = Directory.GetParent(Directory.GetCurrentDirectory()).FullName;

				// copy the configuration files if it's a server build
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
								//CreateScript(Path.Combine(buildPath, "Start.bat"), LOGIN_SERVER_BAT_SCRIPT);
								break;
							case CustomBuildType.World:
								//CreateScript(Path.Combine(buildPath, "Start.bat"), WORLD_SERVER_BAT_SCRIPT);
								break;
							case CustomBuildType.Scene:
								//CreateScript(Path.Combine(buildPath, "Start.bat"), SCENE_SERVER_BAT_SCRIPT);
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

					string configurationPath = "FishMMO-Setup";
					configurationPath = WorkingEnvironmentOptions.AppendEnvironmentToPath(configurationPath);

					CopyConfigurationFiles(customBuildType, Path.Combine(root, configurationPath), buildPath);
				}
				if (customBuildType == CustomBuildType.Installer)
				{
					NewBuildSetupFolder(root, buildPath);
				}
			}
			else if (summary.result == BuildResult.Failed)
			{
				Debug.Log("Build failed: " + report.summary.result + " " + report);
			}

			// return IL2CPP settings to original
			if (buildTarget == BuildTarget.WebGL)
			{
				PlayerSettings.SetScriptingBackend(originalGroup, originalScriptingImp);
				PlayerSettings.SetIl2CppCompilerConfiguration(originalGroup, originalCompilerConf);
				PlayerSettings.SetIl2CppCodeGeneration(originalNamedBuildTargetGroup, originalOptimization);
			}

			EditorUserBuildSettings.SwitchActiveBuildTarget(originalGroup, originalBuildTarget);
			EditorUserBuildSettings.standaloneBuildSubtarget = originalBuildSubtarget;
		}

		private static void RebuildWorldSceneDetailsCache()
		{
			// rebuild world details cache, this includes teleporters, teleporter destinations, spawn points, and other constant scene data
			WorldSceneDetailsCache worldDetailsCache = AssetDatabase.LoadAssetAtPath<WorldSceneDetailsCache>(WorldSceneDetailsCache.CACHE_FULL_PATH);
			if (worldDetailsCache != null)
			{
				worldDetailsCache.Rebuild();
			}
			else
			{
				worldDetailsCache = ScriptableObject.CreateInstance<WorldSceneDetailsCache>();
				worldDetailsCache.Rebuild();
				AssetDatabase.CreateAsset(worldDetailsCache, WorldSceneDetailsCache.CACHE_FULL_PATH);
			}
			AssetDatabase.SaveAssets();
		}

		private static string[] AppendWorldScenePaths(string[] requiredPaths)
		{
			List<string> allPaths = new List<string>(requiredPaths);

			// add all of the WorldScenes
			foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
			{
				if (scene.path.Contains(WorldSceneDetailsCache.WORLD_SCENE_PATH))
				{
					allPaths.Add(scene.path);
				}
			}
			return allPaths.ToArray();
		}

		private static void CopyConfigurationFiles(CustomBuildType customBuildType, string configurationPath, string buildPath)
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
				default:
					break;
			}
			FileUtil.ReplaceFile(Path.Combine(configurationPath, "appsettings.json"), Path.Combine(buildPath, "appsettings.json"));
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

		[MenuItem("FishMMO/Build/Installer", priority = -1)]
		public static void BuildWindows64Setup()
		{
			BuildExecutable("Installer",
							new string[]
							{
								"Assets\\Scenes\\Installer.unity",
							},
							CustomBuildType.Installer,
							BASE_BUILD_OPTIONS | BuildOptions.ShowBuiltPlayer,
							StandaloneBuildSubtarget.Player,
							BuildTarget.StandaloneWindows64);
		}

		private static void NewBuildSetupFolder(string rootPath, string buildPath)
		{
			string setup = Path.Combine(rootPath, "FishMMO-Setup");
			FileUtil.ReplaceFile(Path.Combine(setup, "docker-compose.yml"), Path.Combine(buildPath, "docker-compose.yml"));

			string envConfigurationPath = WorkingEnvironmentOptions.AppendEnvironmentToPath(setup);
			FileUtil.ReplaceFile(Path.Combine(envConfigurationPath, "appsettings.json"), Path.Combine(buildPath, "appsettings.json"));

			string dbBuildDirectory = Path.Combine(buildPath, "FishMMO-Database");
			FileUtil.ReplaceDirectory(Path.Combine(rootPath, "FishMMO-Database"), dbBuildDirectory);

			string dbBinDirectory = Path.Combine(Path.Combine(dbBuildDirectory, "FishMMO-DB"), "bin");
			FileUtil.DeleteFileOrDirectory(dbBinDirectory);

			string dbMigratorBinDirectory = Path.Combine(Path.Combine(dbBuildDirectory, "FishMMO-DB-Migrator"), "bin");
			FileUtil.DeleteFileOrDirectory(dbMigratorBinDirectory);
		}

		private static void BuildSetupFolder(string buildDirectoryName, string setupScriptFileName)
		{
			BuildSetupFolder(null, buildDirectoryName, setupScriptFileName, true);
		}
		private static void BuildSetupFolder(string rootPath, string buildDirectoryName, string setupScriptFileName, bool openExplorer)
		{
			if (string.IsNullOrWhiteSpace(rootPath))
			{
				rootPath = EditorUtility.SaveFolderPanel("Pick a save directory", "", "");
				if (string.IsNullOrWhiteSpace(rootPath))
				{
					return;
				}
			}

			string buildPath = Path.Combine(rootPath, buildDirectoryName);
			Directory.CreateDirectory(buildPath);

			string root = Directory.GetParent(Directory.GetCurrentDirectory()).FullName;
			string setup = Path.Combine(root, "FishMMO-Setup");
			FileUtil.ReplaceFile(Path.Combine(setup, setupScriptFileName), Path.Combine(buildPath, setupScriptFileName));
			FileUtil.ReplaceFile(Path.Combine(setup, "docker-compose.yml"), Path.Combine(buildPath, "docker-compose.yml"));

			string envConfigurationPath = WorkingEnvironmentOptions.AppendEnvironmentToPath(setup);
			FileUtil.ReplaceFile(Path.Combine(envConfigurationPath, "appsettings.json"), Path.Combine(buildPath, "appsettings.json"));
			FileUtil.ReplaceDirectory(Path.Combine(root, "FishMMO-Database"), Path.Combine(buildPath, "FishMMO-Database"));

			if (!openExplorer)
			{
				return;
			}

#if UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
			Process.Start("xdg-open", buildPath);
#elif UNITY_STANDALONE_WIN
			Process.Start(buildPath);
#endif
		}

		[MenuItem("FishMMO/Build/Update Linker", priority = 12)]
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
			string rootPath = Path.Combine(selectedPath, "FishMMO");
			string serverRootPath = Path.Combine(selectedPath, "FishMMO" + Path.DirectorySeparatorChar + "Server");
			RebuildWorldSceneDetailsCache();
			BuildSetupFolder(serverRootPath, "Server Setup", "Windows Setup.bat", false);
			BuildExecutable(rootPath,
							CLIENT_BUILD_NAME,
							CLIENT_BOOTSTRAP_SCENES,
							CustomBuildType.Client,
							BASE_BUILD_OPTIONS,
							StandaloneBuildSubtarget.Player,
							BuildTarget.StandaloneWindows64);
			BuildExecutable(serverRootPath,
							ALL_IN_ONE_SERVER_BUILD_NAME,
							ALL_IN_ONE_SERVER_BOOTSTRAP_SCENES,
							CustomBuildType.AllInOne,
							BASE_BUILD_OPTIONS,
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneWindows64);
			BuildExecutable(serverRootPath,
							LOGIN_SERVER_BUILD_NAME,
							LOGIN_SERVER_BOOTSTRAP_SCENES,
							CustomBuildType.Login,
							BASE_BUILD_OPTIONS,
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneWindows64);
			BuildExecutable(serverRootPath,
							WORLD_SERVER_BUILD_NAME,
							WORLD_SERVER_BOOTSTRAP_SCENES,
							CustomBuildType.World,
							BASE_BUILD_OPTIONS,
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneWindows64);
			BuildExecutable(serverRootPath,
							SCENE_SERVER_BUILD_NAME,
							SCENE_SERVER_BOOTSTRAP_SCENES,
							CustomBuildType.Scene,
							BASE_BUILD_OPTIONS,
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
			string rootPath = Path.Combine(selectedPath, "FishMMO");
			string serverRootPath = Path.Combine(selectedPath, "FishMMO" + Path.DirectorySeparatorChar + "Server");
			RebuildWorldSceneDetailsCache();
			BuildSetupFolder(serverRootPath, "Server Setup", "Linux Setup.sh", false);
			BuildExecutable(rootPath,
							CLIENT_BUILD_NAME,
							CLIENT_BOOTSTRAP_SCENES,
							CustomBuildType.Client,
							BASE_BUILD_OPTIONS,
							StandaloneBuildSubtarget.Player,
							BuildTarget.StandaloneLinux64);
			BuildExecutable(serverRootPath,
							ALL_IN_ONE_SERVER_BUILD_NAME,
							ALL_IN_ONE_SERVER_BOOTSTRAP_SCENES,
							CustomBuildType.AllInOne,
							BASE_BUILD_OPTIONS,
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneLinux64);
			BuildExecutable(serverRootPath,
							LOGIN_SERVER_BUILD_NAME,
							LOGIN_SERVER_BOOTSTRAP_SCENES,
							CustomBuildType.Login,
							BASE_BUILD_OPTIONS,
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneLinux64);
			BuildExecutable(serverRootPath,
							WORLD_SERVER_BUILD_NAME,
							WORLD_SERVER_BOOTSTRAP_SCENES,
							CustomBuildType.World,
							BASE_BUILD_OPTIONS,
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneLinux64);
			BuildExecutable(serverRootPath,
							SCENE_SERVER_BUILD_NAME,
							SCENE_SERVER_BOOTSTRAP_SCENES,
							CustomBuildType.Scene,
							BASE_BUILD_OPTIONS,
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneLinux64);

#if UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
			Process.Start("xdg-open", rootPath);
#elif UNITY_STANDALONE_WIN
			Process.Start(rootPath);
#endif
		}

		[MenuItem("FishMMO/Build/Server/Windows Setup", priority = 1)]
		public static void BuildWindowsSetup()
		{
			BuildSetupFolder("FishMMO Windows Setup", "Windows Setup.bat");
		}

		[MenuItem("FishMMO/Build/Server/Windows x64 All-In-One", priority = 2)]
		public static void BuildWindows64AllInOneServer()
		{
			RebuildWorldSceneDetailsCache();
			BuildExecutable(ALL_IN_ONE_SERVER_BUILD_NAME,
							ALL_IN_ONE_SERVER_BOOTSTRAP_SCENES,
							CustomBuildType.AllInOne,
							BASE_BUILD_OPTIONS | BuildOptions.ShowBuiltPlayer,
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneWindows64);
		}

		[MenuItem("FishMMO/Build/Server/Windows x64 Login", priority = 3)]
		public static void BuildWindows64LoginServer()
		{
			RebuildWorldSceneDetailsCache();
			BuildExecutable(LOGIN_SERVER_BUILD_NAME,
							LOGIN_SERVER_BOOTSTRAP_SCENES,
							CustomBuildType.Login,
							BASE_BUILD_OPTIONS | BuildOptions.ShowBuiltPlayer,
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneWindows64);
		}

		[MenuItem("FishMMO/Build/Server/Windows x64 World", priority = 4)]
		public static void BuildWindows64WorldServer()
		{
			RebuildWorldSceneDetailsCache();
			BuildExecutable(WORLD_SERVER_BUILD_NAME,
							WORLD_SERVER_BOOTSTRAP_SCENES,
							CustomBuildType.World,
							BASE_BUILD_OPTIONS | BuildOptions.ShowBuiltPlayer,
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneWindows64);
		}

		[MenuItem("FishMMO/Build/Server/Windows x64 Scene", priority = 5)]
		public static void BuildWindows64SceneServer()
		{
			RebuildWorldSceneDetailsCache();
			BuildExecutable(SCENE_SERVER_BUILD_NAME,
							SCENE_SERVER_BOOTSTRAP_SCENES,
							CustomBuildType.Scene,
							BASE_BUILD_OPTIONS | BuildOptions.ShowBuiltPlayer,
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneWindows64);
		}

		[MenuItem("FishMMO/Build/Client/Windows x64", priority = 1)]
		public static void BuildWindows64Client()
		{
			RebuildWorldSceneDetailsCache();
			BuildExecutable(CLIENT_BUILD_NAME,
							CLIENT_BOOTSTRAP_SCENES,
							CustomBuildType.Client,
							BASE_BUILD_OPTIONS | BuildOptions.ShowBuiltPlayer,
							StandaloneBuildSubtarget.Player,
							BuildTarget.StandaloneWindows64);
		}

		[MenuItem("FishMMO/Build/Server/Linux Setup", priority = 6)]
		public static void BuildLinuxSetup()
		{
			BuildSetupFolder("FishMMO Linux Setup", "Linux Setup.sh");
		}

		[MenuItem("FishMMO/Build/Server/Linux x64 All-In-One", priority = 7)]
		public static void BuildLinux64AllInOneServer()
		{
			RebuildWorldSceneDetailsCache();
			BuildExecutable(ALL_IN_ONE_SERVER_BUILD_NAME,
							ALL_IN_ONE_SERVER_BOOTSTRAP_SCENES,
							CustomBuildType.AllInOne,
							BASE_BUILD_OPTIONS | BuildOptions.ShowBuiltPlayer,
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneLinux64);
		}

		[MenuItem("FishMMO/Build/Client/Linux x64", priority = 2)]
		public static void BuildLinux64Client()
		{
			RebuildWorldSceneDetailsCache();
			BuildExecutable(CLIENT_BUILD_NAME,
							CLIENT_BOOTSTRAP_SCENES,
							CustomBuildType.Client,
							BASE_BUILD_OPTIONS | BuildOptions.ShowBuiltPlayer,
							StandaloneBuildSubtarget.Player,
							BuildTarget.StandaloneLinux64);
		}

		[MenuItem("FishMMO/Build/Client/WebGL", priority = 3)]
		public static void BuildWebGLClient()
		{
			RebuildWorldSceneDetailsCache();
			BuildExecutable(CLIENT_BUILD_NAME,
							CLIENT_BOOTSTRAP_SCENES,
							CustomBuildType.Client,
							BASE_BUILD_OPTIONS | BuildOptions.ShowBuiltPlayer,
							StandaloneBuildSubtarget.Player,
							BuildTarget.WebGL);
		}
	}
}
#endif