#if UNITY_EDITOR
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.Build.Reporting;
using Debug = UnityEngine.Debug;
using System.Diagnostics;
using System.Net;

public class CustomBuildTool
{
	public const string SERVER_BUILD_NAME = "FishMMOServer";
	public const string CLIENT_BUILD_NAME = "FishMMOClient";

	public static readonly string[] SERVER_BOOTSTRAP_SCENES = new string[]
	{
		"Assets\\Scenes\\Bootstraps\\ServerLauncher.unity",
		"Assets\\Scenes\\Bootstraps\\LoginServer.unity",
		"Assets\\Scenes\\Bootstraps\\WorldServer.unity",
		"Assets\\Scenes\\Bootstraps\\SceneServer.unity",
	};

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

	[MenuItem("FishMMO/Install Tools")]
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
	}

	public static string GetBuildTargetShortName(BuildTarget target)
	{
		switch (target)
		{
			case BuildTarget.StandaloneWindows:
				return "_win_32";
			case BuildTarget.StandaloneWindows64:
				return "_win_64";
			case BuildTarget.StandaloneLinux64:
				return "_linux_64";
			default:
				return "";
		}
	}

	private static void BuildExecutable(string executableName, string[] bootstrapScenes, BuildOptions buildOptions, StandaloneBuildSubtarget subTarget, BuildTarget buildTarget)
	{
		string rootPath = EditorUtility.SaveFolderPanel("Pick a save directory", "", "");
		if (string.IsNullOrWhiteSpace(rootPath))
		{
			return;
		}

		if (string.IsNullOrWhiteSpace(executableName) ||
			bootstrapScenes == null ||
			bootstrapScenes.Length < 1)
		{
			return;
		}

		// just incase buildpipeline bug is still present
		BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);
		EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, buildTarget);
		EditorUserBuildSettings.standaloneBuildSubtarget = subTarget;

		// compile scenes list with bootstraps
		string[] scenes = GetBuildScenePaths(bootstrapScenes);

		string folderName = executableName + GetBuildTargetShortName(buildTarget);
		string buildPath = rootPath + "\\" + folderName;

		// build the project
		BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions()
		{
			locationPathName = buildPath + "\\" + executableName + ".exe",
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
					FileUtil.ReplaceFile(root + "\\START.bat", buildPath + "\\START.bat");
				}
				else if (buildTarget == BuildTarget.StandaloneLinux64)
				{
					FileUtil.ReplaceFile(root + "\\START.sh", buildPath + "\\START.sh");
				}

				
				if (buildTarget == BuildTarget.StandaloneWindows64)
				{
					FileUtil.ReplaceFile(root + "\\WindowsSetup.bat", buildPath + "\\WindowsSetup.bat");
				}
				else if (buildTarget == BuildTarget.StandaloneLinux64)
				{
					FileUtil.ReplaceFile(root + "\\LinuxSetup.sh", buildPath + "\\LinuxSetup.sh");
				}

				// append the data folder for configuration copy
				//buildPath += "/" + executableName + "_Data";

				FileUtil.ReplaceFile(root + "\\LoginServer.cfg", buildPath + "\\LoginServer.cfg");
				FileUtil.ReplaceFile(root + "\\WorldServer.cfg", buildPath + "\\WorldServer.cfg");
				FileUtil.ReplaceFile(root + "\\SceneServer.cfg", buildPath + "\\SceneServer.cfg");
				FileUtil.ReplaceFile(root + "\\Database.cfg", buildPath + "\\Database.cfg");
			}
		}
		else if (summary.result == BuildResult.Failed)
		{
			Debug.Log("Build failed: " + report.summary.result + " " + report);
		}
	}

	private static string[] GetBuildScenePaths(string[] requiredPaths)
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

	[MenuItem("FishMMO/Windows 32 Server Build")]
	public static void BuildWindows32Server()
	{
		BuildExecutable(SERVER_BUILD_NAME,
						SERVER_BOOTSTRAP_SCENES,
						BuildOptions.CleanBuildCache | BuildOptions.Development | BuildOptions.ShowBuiltPlayer,
						StandaloneBuildSubtarget.Server,
						BuildTarget.StandaloneWindows);
	}

	[MenuItem("FishMMO/Windows 32 Client Build")]
	public static void BuildWindows32Client()
	{
		BuildExecutable(CLIENT_BUILD_NAME,
						CLIENT_BOOTSTRAP_SCENES,
						BuildOptions.CleanBuildCache | BuildOptions.Development | BuildOptions.ShowBuiltPlayer,
						StandaloneBuildSubtarget.Player,
						BuildTarget.StandaloneWindows);
	}

	[MenuItem("FishMMO/Windows 64 Server Build")]
	public static void BuildWindows64Server()
	{
		BuildExecutable(SERVER_BUILD_NAME,
						SERVER_BOOTSTRAP_SCENES,
						BuildOptions.CleanBuildCache | BuildOptions.Development | BuildOptions.ShowBuiltPlayer,
						StandaloneBuildSubtarget.Server,
						BuildTarget.StandaloneWindows64);
	}

	[MenuItem("FishMMO/Windows 64 Client Build")]
	public static void BuildWindows64Client()
	{
		BuildExecutable(CLIENT_BUILD_NAME,
						CLIENT_BOOTSTRAP_SCENES,
						BuildOptions.CleanBuildCache | BuildOptions.Development | BuildOptions.ShowBuiltPlayer,
						StandaloneBuildSubtarget.Player,
						BuildTarget.StandaloneWindows64);
	}

	[MenuItem("FishMMO/Linux 64 Server Build")]
	public static void BuildLinux64Server()
	{
		BuildExecutable(SERVER_BUILD_NAME,
						SERVER_BOOTSTRAP_SCENES,
						BuildOptions.CleanBuildCache | BuildOptions.Development | BuildOptions.ShowBuiltPlayer,
						StandaloneBuildSubtarget.Server,
						BuildTarget.StandaloneLinux64);
	}

	[MenuItem("FishMMO/Linux 64 Client Build")]
	public static void BuildLinux64Client()
	{
		BuildExecutable(CLIENT_BUILD_NAME,
						CLIENT_BOOTSTRAP_SCENES,
						BuildOptions.CleanBuildCache | BuildOptions.Development | BuildOptions.ShowBuiltPlayer,
						StandaloneBuildSubtarget.Player,
						BuildTarget.StandaloneLinux64);
	}
}
#endif