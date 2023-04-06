#if UNITY_EDITOR
/*using UnityEditor;
using System.Diagnostics;
using System.Collections.Generic;

public class ServerBuildTool
{
	[MenuItem("FishMMO/Windows Server Build")]
	public static void BuildGame()
	{
		// Get filename.
		string path = EditorUtility.SaveFolderPanel("Choose a Location to build the Server", "", "FishMMOServer");

		string[] levels = new string[] { "Assets/Scenes/Bootstraps/ServerLauncher.unity",
										 "Assets/Scenes/Bootstraps/LoginServer.unity",
										 "Assets/Scenes/Bootstraps/WorldServer.unity",
										 "Assets/Scenes/Bootstraps/SceneServer.unity",};

		List<string> levelPaths = new List<string>();


		// add all of the WorldScenes
		foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
		{
			if (scene.path.Contains(WorldSceneDetailsCache.WORLD_SCENE_PATH))
			{
				levelPaths.Add(scene.path);
			}
		}

		// Build player.
		BuildPipeline.BuildPlayer(levelPaths.ToArray(), path + "/FishMMOServer.exe", BuildTarget.StandaloneWindows, BuildOptions.Development);

		// Copy a file from the project folder to the build folder, alongside the built game.
		//FileUtil.CopyFileOrDirectory("Assets/Templates/Readme.txt", path + "Readme.txt");

		// Run the game (Process class from System.Diagnostics).
		//Process proc = new Process();
		//proc.StartInfo.FileName = path + "/BuiltGame.exe";
		//proc.Start();
	}
}*/
#endif