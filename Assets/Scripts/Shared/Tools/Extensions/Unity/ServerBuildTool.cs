#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System;
using UnityEditor.Build.Reporting;

public class ServerBuildTool
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

	private static void BuildExecutable(string buildName, string[] bootstrapScenes, BuildOptions buildOptions, StandaloneBuildSubtarget subTarget, BuildTarget target)
	{
		if (string.IsNullOrEmpty(buildName))
		{
			return;
		}

		// Get filename.
		string path = EditorUtility.SaveFolderPanel("Choose a build location.", "", buildName);

		// validate
		if (string.IsNullOrEmpty(path) ||
			bootstrapScenes == null ||
			bootstrapScenes.Length < 1)
		{
			return;
		}

		// compile scenes list with bootstraps
		string[] scenes = GetBuildScenePaths(bootstrapScenes);

		BuildPlayerOptions options = new BuildPlayerOptions()
		{
			locationPathName = path + "/" + buildName + ".exe",
			options = buildOptions,
			scenes = scenes,
			subtarget = (int)subTarget,
			target = target,
		};
		
		// build the project
		BuildReport report = BuildPipeline.BuildPlayer(options);

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
				FileUtil.CopyFileOrDirectory(defaultFileDirectory + "/START.bat", path + "/START.bat");

				// append the data folder for configuration copy
				path += "/" + buildName + "_Data";

				FileUtil.CopyFileOrDirectory(defaultFileDirectory + "/LoginServer.cfg", path + "/LoginServer.cfg");
				FileUtil.CopyFileOrDirectory(defaultFileDirectory + "/WorldServer.cfg", path + "/WorldServer.cfg");
				FileUtil.CopyFileOrDirectory(defaultFileDirectory + "/SceneServer.cfg", path + "/SceneServer.cfg");
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
		BuildExecutable(SERVER_BUILD_NAME, SERVER_BOOTSTRAP_SCENES, BuildOptions.Development, StandaloneBuildSubtarget.Server, BuildTarget.StandaloneWindows);
	}

	[MenuItem("FishMMO/Windows 32 Client Build")]
	public static void BuildWindows32Client()
	{
		BuildExecutable(CLIENT_BUILD_NAME, CLIENT_BOOTSTRAP_SCENES, BuildOptions.Development, StandaloneBuildSubtarget.Player, BuildTarget.StandaloneWindows);
	}

	[MenuItem("FishMMO/Windows 64 Server Build")]
	public static void BuildWindows64Server()
	{
		BuildExecutable(SERVER_BUILD_NAME, SERVER_BOOTSTRAP_SCENES, BuildOptions.Development, StandaloneBuildSubtarget.Server, BuildTarget.StandaloneWindows64);
	}

	[MenuItem("FishMMO/Windows 64 Client Build")]
	public static void BuildWindows64Client()
	{
		BuildExecutable(CLIENT_BUILD_NAME, CLIENT_BOOTSTRAP_SCENES, BuildOptions.Development, StandaloneBuildSubtarget.Player, BuildTarget.StandaloneWindows64);
	}
}
#endif