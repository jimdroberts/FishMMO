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

	[MenuItem("FishMMO/Windows Server Build")]
	public static void BuildWindowsServer()
	{
		// Get filename.
		string path = EditorUtility.SaveFolderPanel("Choose a location to build the Server", "", SERVER_BUILD_NAME);

		if (string.IsNullOrEmpty(path))
		{
			return;
		}
		// add bootstraps
		string[] scenes = GetBuildScenePaths(new string[] { "Assets/Scenes/Bootstraps/ServerLauncher.unity",
														    "Assets/Scenes/Bootstraps/LoginServer.unity",
														    "Assets/Scenes/Bootstraps/WorldServer.unity",
														    "Assets/Scenes/Bootstraps/SceneServer.unity"});

		// Build player
		BuildPlayerOptions options = new BuildPlayerOptions()
		{
			locationPathName = path + "/" + SERVER_BUILD_NAME + ".exe",
			options = BuildOptions.Development,
			scenes = scenes,
			subtarget = (int)StandaloneBuildSubtarget.Server,
			target = BuildTarget.StandaloneWindows,
		};
		BuildReport report = BuildPipeline.BuildPlayer(options);

		BuildSummary summary = report.summary;

		if (summary.result == BuildResult.Succeeded)
		{
			Debug.Log("Build succeeded: " + summary.totalSize + " bytes " + DateTime.UtcNow);

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
			path += "/" + SERVER_BUILD_NAME + "_Data";

			FileUtil.CopyFileOrDirectory(defaultFileDirectory + "/LoginServer.cfg", path + "/LoginServer.cfg");
			FileUtil.CopyFileOrDirectory(defaultFileDirectory + "/WorldServer.cfg", path + "/WorldServer.cfg");
			FileUtil.CopyFileOrDirectory(defaultFileDirectory + "/SceneServer.cfg", path + "/SceneServer.cfg");
		}
		else if (summary.result == BuildResult.Failed)
		{
			Debug.Log("Build failed: " + report.summary.result + " " + report);
		}
	}

	[MenuItem("FishMMO/Windows Client Build")]
	public static void BuildWindowsClient()
	{
		// Get filename.
		string path = EditorUtility.SaveFolderPanel("Choose a location to build the Client", "", CLIENT_BUILD_NAME);

		if (string.IsNullOrEmpty(path))
		{
			return;
		}

		string[] scenes = GetBuildScenePaths(new string[] { "Assets/Scenes/Bootstraps/ClientBootstrap.unity", });

		// Build player
		BuildPlayerOptions options = new BuildPlayerOptions()
		{
			locationPathName = path + "/" + CLIENT_BUILD_NAME + ".exe",
			options = BuildOptions.Development,
			scenes = scenes,
			subtarget = (int)StandaloneBuildSubtarget.Player,
			target = BuildTarget.StandaloneWindows,
		};
		BuildReport report = BuildPipeline.BuildPlayer(options);

		BuildSummary summary = report.summary;

		if (summary.result == BuildResult.Succeeded)
		{
			Debug.Log("Build succeeded: " + summary.totalSize + " bytes " + DateTime.UtcNow);
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
}
#endif