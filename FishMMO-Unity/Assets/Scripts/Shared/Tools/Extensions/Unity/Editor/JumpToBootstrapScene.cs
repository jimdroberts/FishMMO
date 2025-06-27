using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace FishMMO.Shared
{
	public class JumpToBootstrapScene
	{
		private static void LoadScene(string scenePath)
		{
			// Check if the scene exists at the specified path
			if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null)
			{
				// Open the scene in the editor
				EditorSceneManager.OpenScene(scenePath);
			}
			else
			{
				Log.Error("Scene not found at path: " + scenePath);
			}
		}

		[MenuItem("FishMMO/QuickStart/Main Bootstrap", priority = -10)]
		public static void GoToMainBootstrapScene()
		{
			LoadScene(Constants.Configuration.BootstrapScenePath + "MainBootstrap.unity");
		}

		[MenuItem("FishMMO/QuickStart/Client Preboot", priority = -10)]
		public static void GoToClientPrebootScene()
		{
			LoadScene(Constants.Configuration.ClientBootstrapScenePath + "ClientPreboot.unity");
		}

		[MenuItem("FishMMO/QuickStart/Client Postboot", priority = -10)]
		public static void GoToClientPostbootScene()
		{
			LoadScene(Constants.Configuration.ClientBootstrapScenePath + "ClientPostboot.unity");
		}

		[MenuItem("FishMMO/QuickStart/Client Launcher", priority = -10)]
		public static void GoToClientLauncherScene()
		{
			LoadScene(Constants.Configuration.ClientBootstrapScenePath + "ClientLauncher.unity");
		}

		[MenuItem("FishMMO/QuickStart/Login Server", priority = -9)]
		public static void GoToLoginBootstrapScene()
		{
			LoadScene(Constants.Configuration.ServerBootstrapScenePath + "LoginServer.unity");
		}

		[MenuItem("FishMMO/QuickStart/World Server", priority = -8)]
		public static void GoToWorldBootstrapScene()
		{
			LoadScene(Constants.Configuration.ServerBootstrapScenePath + "WorldServer.unity");
		}

		[MenuItem("FishMMO/QuickStart/Scene Server", priority = -7)]
		public static void GoToSceneBootstrapScene()
		{
			LoadScene(Constants.Configuration.ServerBootstrapScenePath + "SceneServer.unity");
		}
	}
}