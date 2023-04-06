#if UNITY_EDITOR
using UnityEditor;
using System.Diagnostics;

public class CustomBuildTool
{
	[MenuItem("FishMMO/Windows Server Build")]
	public static void BuildGame()
	{
		// Get filename.
		string path = EditorUtility.SaveFolderPanel("Choose Location of Built Game", "", "");
		string[] levels = new string[] { "Assets/Scene1.unity", "Assets/Scene2.unity" };

		foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
		{
		}

			// Build player.
			BuildPipeline.BuildPlayer(levels, path + "/BuiltGame.exe", BuildTarget.StandaloneWindows, BuildOptions.None);

		// Copy a file from the project folder to the build folder, alongside the built game.
		//FileUtil.CopyFileOrDirectory("Assets/Templates/Readme.txt", path + "Readme.txt");

		// Run the game (Process class from System.Diagnostics).
		//Process proc = new Process();
		//proc.StartInfo.FileName = path + "/BuiltGame.exe";
		//proc.Start();
	}
}
#endif