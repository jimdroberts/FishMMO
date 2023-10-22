// --------- DO NOT FORMAT DOCUMENT ---------

#if UNITY_EDITOR
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.Build.Reporting;
using Debug = UnityEngine.Debug;
using System.Diagnostics;
using System.Net;
using UnityEngine;

public class CustomBuildTool
{
	public enum CustomBuildType : byte
	{
		AllInOne = 0,
		Login,
		World,
		Scene,
		Client,
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

#if UNITY_EDITOR_WIN
	public static readonly string VirtualizationFileName = "powershell.exe";
	public static readonly string VirtualizationArguments = "-Command \"(Get-WmiObject -Namespace 'root\\cimv2' -Class Win32_Processor).VirtualizationFirmwareEnabled\"";
	public static readonly string DockerInstalledInfoA = "docker";
	public static readonly string DockerInstalledInfoB = "--version";
#elif UNITY_EDITOR_OSX
	public static readonly string DockerInstallA = "brew";
	public static readonly string DockerInstallB = "install --cask docker";
#else
	public static readonly string VirtualizationFileName = "grep";
	public static readonly string VirtualizationArguments = "-E --color 'svm|vmx'";
	public static readonly string DockerInstalledInfoA = "which";
	public static readonly string DockerInstalledInfoB = "docker";
	public static readonly string DockerInstallA = "apt-get";
	public static readonly string DockerInstallB = "install -y docker.io";
#endif

	public static bool IsVirtualizationEnabled()
	{
		using (Process process = new Process())
		{
			process.StartInfo.FileName = VirtualizationFileName;
			process.StartInfo.Arguments = VirtualizationArguments;
			process.StartInfo.UseShellExecute = false;
			process.StartInfo.RedirectStandardOutput = true;
			process.StartInfo.CreateNoWindow = true;
			process.Start();

			string output = process.StandardOutput.ReadToEnd();
			process.WaitForExit();

			return bool.Parse(output.Trim());
		}
	}

	public static bool IsWSLInstalled()
	{
#if UNITY_EDITOR_WIN
		using (Process process = new Process())
		{
			process.StartInfo.FileName = "wsl";
			process.StartInfo.Arguments = "-l -v";
			process.StartInfo.RedirectStandardOutput = true;
			process.StartInfo.UseShellExecute = false;
			process.StartInfo.CreateNoWindow = true;
			process.Start();

			string output = process.StandardOutput.ReadToEnd();
			process.WaitForExit();

			return output.Contains("2");
		}
#else
		return true;
#endif
	}

	public static void InstallWSL2()
	{
		using (Process process = new Process())
		{
			process.StartInfo.FileName = "powershell.exe";
			process.StartInfo.Arguments = "Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Windows-Subsystem-Linux; Enable-WindowsOptionalFeature -Online -FeatureName VirtualMachinePlatform";
			process.StartInfo.Verb = "runas";  // Run PowerShell with administrator privileges

			process.Start();
			process.WaitForExit();
		}
	}

	public static bool IsDockerInstalled()
	{
		using (Process process = new Process())
		{
			process.StartInfo.FileName = DockerInstalledInfoA;
			process.StartInfo.Arguments = DockerInstalledInfoB;
			process.StartInfo.RedirectStandardOutput = true;
			process.StartInfo.UseShellExecute = false;
			process.StartInfo.CreateNoWindow = true;
			process.Start();
			process.WaitForExit();

			return process.ExitCode == 0;
		}
	}

#if UNITY_EDITOR_WIN
	public static void InstallDocker()
	{
		string downloadUrl = "https://desktop.docker.com/win/main/amd64/Docker%20Desktop%20Installer.exe";
		string installerPath = Path.Combine(Path.GetTempPath(), "DockerInstaller.exe");

		using (WebClient client = new WebClient())
		{
			try
			{
				client.DownloadFile(downloadUrl, installerPath);
				Debug.Log("Docker installer downloaded successfully.");
			}
			catch (Exception ex)
			{
				Debug.Log("Failed to download Docker installer: " + ex.Message);
				return;
			}
		}

		using (Process process = new Process())
		{
			process.StartInfo.FileName = installerPath;
			process.StartInfo.Arguments = "--quiet";
			process.StartInfo.RedirectStandardOutput = true;
			process.StartInfo.UseShellExecute = false;
			process.StartInfo.CreateNoWindow = true;
			process.Start();
			process.WaitForExit();

			if (process.ExitCode == 0)
			{
				Debug.Log("Docker installed successfully.");
			}
			else
			{
				Debug.Log("Docker installation failed.");
			}
		}
	}
#else
	public static void InstallDocker()
	{
		using (Process process = new Process())
		{
			process.StartInfo.FileName = DockerInstallA;
			process.StartInfo.Arguments = DockerInstallB;
			process.StartInfo.RedirectStandardOutput = true;
			process.StartInfo.UseShellExecute = false;
			process.StartInfo.CreateNoWindow = true;
			process.Start();
			process.WaitForExit();

			if (process.ExitCode == 0)
			{
				Debug.Log("Docker installed successfully.");
			}
			else
			{
				Debug.Log("Docker installation failed.");
			}
		}
	}
#endif

	public static void RunDockerCommand(string commandArgs)
	{
		// Create a new process instance
		using (Process process = new Process())
		{
			// Set the start info for the process
			process.StartInfo.FileName = "docker"; // Command to execute (e.g., docker)
			process.StartInfo.Arguments = commandArgs; // Arguments for the command
			process.StartInfo.RedirectStandardOutput = true; // Redirect the output to be able to read it
			process.StartInfo.RedirectStandardError = true; // Redirect the error output to be able to read it
			process.StartInfo.UseShellExecute = false; // Do not use the operating system shell
			process.StartInfo.CreateNoWindow = true; // Do not create a window for the process

			// Start the process
			process.Start();

			// Read the output
			string output = process.StandardOutput.ReadToEnd();
			string errorOutput = process.StandardError.ReadToEnd();

			// Wait for the process to exit
			process.WaitForExit();

			// Display the output
			Debug.Log(output +
					  "\r\n" + errorOutput);

			// Display the output
			Debug.Log(output);

			// Check the exit code
			int exitCode = process.ExitCode;
			Debug.Log("Exit Code: " + exitCode);
		}
	}

	/*[MenuItem("FishMMO/Install Tools")]
	public static void EnsureToolkitInstallation()
	{
		bool virtualization = IsVirtualizationEnabled();
		bool wsl = IsWSLInstalled();
		bool docker = IsDockerInstalled();
		Debug.Log("Setup Output: " +
				  "\r\nVirtualization: " + virtualization +
				  "\r\nWSL2: " + wsl +
				  "\r\nDocker: " + docker);

		if (!virtualization)
		{
			return;
		}
		if (!wsl)
		{
			InstallWSL2();
		}
		if (!docker)
		{
			InstallDocker();
		}

		string root = Directory.GetParent(Directory.GetCurrentDirectory()).FullName;
		//string dotNet = "mcr.microsoft.com/dotnet/sdk:7.0";
		//string projectPath = "/app/fishdb/FishMMO-DB/FishMMO-DB.csproj";
		//string startupProject = "/app/fishdb/FishMMO-DB-Migrator/FishMMO-DB-Migrator.csproj";

		// do we need to make an image with dotnet 7 and dotnet-ef for global use with migration?
		//RunDockerCommand($"build -t dotnet-ef-image -f \"{root}\" .");

		// run initial migration
		//RunDockerCommand($"run -d -n fishdb-tmp -v \"{root}\":/app/fishdb -w /app/fishdb {dotNet} /bin/bash -c \"dotnet tool install --global dotnet-ef --version 5.0.10 && dotnet ef migrations add InitialMigration -p {projectPath} -s {startupProject}\"");
	}*/

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

			// copy the configuration files if it's a server build
			if (subTarget == StandaloneBuildSubtarget.Server)
			{
				string root = Directory.GetParent(Directory.GetCurrentDirectory()).FullName;
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
				CopyConfigurationFiles(customBuildType, root, buildPath);
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

	private static void CopyConfigurationFiles(CustomBuildType customBuildType, string root, string buildPath)
	{
		switch (customBuildType)
		{
			case CustomBuildType.AllInOne:
				FileUtil.ReplaceFile(Path.Combine(root, "LoginServer.cfg"), Path.Combine(buildPath, "LoginServer.cfg"));
				FileUtil.ReplaceFile(Path.Combine(root, "WorldServer.cfg"), Path.Combine(buildPath, "WorldServer.cfg"));
				FileUtil.ReplaceFile(Path.Combine(root, "SceneServer.cfg"), Path.Combine(buildPath, "SceneServer.cfg"));
				break;
			case CustomBuildType.Login:
				FileUtil.ReplaceFile(Path.Combine(root, "LoginServer.cfg"), Path.Combine(buildPath, "LoginServer.cfg"));
				break;
			case CustomBuildType.World:
				FileUtil.ReplaceFile(Path.Combine(root, "WorldServer.cfg"), Path.Combine(buildPath, "WorldServer.cfg"));
				break;
			case CustomBuildType.Scene:
				FileUtil.ReplaceFile(Path.Combine(root, "SceneServer.cfg"), Path.Combine(buildPath, "SceneServer.cfg"));
				break;
			case CustomBuildType.Client:
			default:
				break;
		}
		FileUtil.ReplaceFile(Path.Combine(root, "Database.cfg"), Path.Combine(buildPath, "Database.cfg"));
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
		FileUtil.ReplaceFile(Path.Combine(root, setupScriptFileName), Path.Combine(buildPath, setupScriptFileName));
		FileUtil.ReplaceFile(Path.Combine(root, "Database.cfg"), Path.Combine(buildPath, "Database.cfg"));
		FileUtil.ReplaceDirectory(Path.Combine(root, "FishMMO-DB"), Path.Combine(buildPath, "FishMMO-DB"));

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

	[MenuItem("FishMMO Build/Build All Windows")]
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

	[MenuItem("FishMMO Build/Build All Linux")]
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

	[MenuItem("FishMMO Build/Server/Windows Setup")]
	public static void BuildWindowsSetup()
	{
		BuildSetupFolder("FishMMO Windows Setup", "Windows Setup.bat");
	}

	[MenuItem("FishMMO Build/Server/Windows x64 All-In-One")]
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

	[MenuItem("FishMMO Build/Server/Windows x64 Login")]
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

	[MenuItem("FishMMO Build/Server/Windows x64 World")]
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

	[MenuItem("FishMMO Build/Server/Windows x64 Scene")]
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

	[MenuItem("FishMMO Build/Client/Windows x64")]
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

	[MenuItem("FishMMO Build/Server/Linux Setup")]
	public static void BuildLinuxSetup()
	{
		BuildSetupFolder("FishMMO Linux Setup", "Linux Setup.sh");
	}

	[MenuItem("FishMMO Build/Server/Linux x64 All-In-One")]
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

	[MenuItem("FishMMO Build/Client/Linux x64")]
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

	[MenuItem("FishMMO Build/Client/WebGL")]
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
#endif