#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using UnityEditor.Build.Reporting;
using Debug = UnityEngine.Debug;

public class CustomBuildTool
{
	public const string SERVER_BUILD_NAME = "FishMMOServer";
	public const string CLIENT_BUILD_NAME = "FishMMOClient";

	public static readonly string[] SERVER_BOOTSTRAP_SCENES = new string[]
	{
		"Assets/Scenes/Bootstraps/ServerLauncher.unity",
		"Assets/Scenes/Bootstraps/LoginServer.unity",
		"Assets/Scenes/Bootstraps/WorldServer.unity",
		"Assets/Scenes/Bootstraps/SceneServer.unity",
	};

	public static readonly string[] CLIENT_BOOTSTRAP_SCENES = new string[]
	{
		"Assets/Scenes/Bootstraps/ClientBootstrap.unity",
	};

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
		if (string.IsNullOrEmpty(rootPath))
		{
			return;
		}

		if (string.IsNullOrEmpty(executableName) ||
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
		string buildPath = rootPath + "/" + folderName;

		// build the project
		BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions()
		{
			locationPathName = buildPath + "/" + executableName + ".exe",
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
				string defaultFileDirectory = "";
#if UNITY_EDITOR
				defaultFileDirectory = Directory.GetParent(Application.dataPath).FullName;
#elif UNITY_ANDROID
				defaultFileDirectory = Application.persistentDataPath;
#elif UNITY_IOS
				defaultFileDirectory = Application.persistentDataPath;
#else
				defaultFileDirectory = Application.dataPath;
#endif

				if (buildTarget == BuildTarget.StandaloneWindows64)
				{
					FileUtil.ReplaceFile(defaultFileDirectory + "/START.bat", buildPath + "/START.bat");
				}
				else if (buildTarget == BuildTarget.StandaloneLinux64)
				{
					FileUtil.ReplaceFile(defaultFileDirectory + "/START.sh", buildPath + "/START.sh");
				}

				// append the data folder for configuration copy
				buildPath += "/" + executableName + "_Data";

				FileUtil.ReplaceFile(defaultFileDirectory + "/LoginServer.cfg", buildPath + "/LoginServer.cfg");
				FileUtil.ReplaceFile(defaultFileDirectory + "/WorldServer.cfg", buildPath + "/WorldServer.cfg");
				FileUtil.ReplaceFile(defaultFileDirectory + "/SceneServer.cfg", buildPath + "/SceneServer.cfg");
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

	[MenuItem("FishMMO/Setup Docker Database")]
	public static async void Database()
	{
		// load configuration first
		Configuration configuration = new Configuration();
		if (!configuration.Load("PostgresqlSetup.cfg"))
		{
			// if we failed to load the file.. save a new one
			configuration.Set("DbName", "fish_mmo");
			configuration.Set("DbUsername", "user");
			configuration.Set("DbPassword", "pass");
			configuration.Set("DbAddress", "127.0.0.1");
			configuration.Set("DbPort", "5432");
			configuration.Save();
		}

		if (configuration.TryGetString("DbName", out string dbName) &&
			configuration.TryGetString("DbUsername", out string dbUsername) &&
			configuration.TryGetString("DbPassword", out string dbPassword) &&
			configuration.TryGetString("DbAddress", out string dbAddress) &&
			configuration.TryGetString("DbPort", out string dbPort))
		{
			Debug.Log("Creating Docker container with postgresql 14");
			await Docker.RunAsync("run --name " + dbName +
								  " -e POSTGRES_USER=" + dbUsername +
								  " -e POSTGRES_PASSWORD=" + dbPassword +
								  " -p " + dbAddress + ":" + dbPort + ":" + dbPort +
								  " -d postgres:14");

			Debug.Log("Ensuring container exists... Please wait...");
			await Task.Delay(5000);

			Debug.Log("Creating base database.");
			await Docker.RunAsync("exec " + dbName +
								  " createdb " + dbName +
								  " -U " + dbUsername);

			Debug.Log("Database created successfully.");
		}
	}
}
#endif