using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace FishMMO.Shared
{
	public class JumpToBootstrapScene
	{
		[MenuItem("FishMMO/QuickStart/Client Bootstrap", priority = -10)]
		public static void GoToClientBootstrapScene()
		{
			// Specify the path to the scene asset
			string scenePath = Constants.Configuration.BootstrapScenePath + "ClientBootstrap.unity";

			// Check if the scene exists at the specified path
			if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null)
			{
				// Open the scene in the editor
				EditorSceneManager.OpenScene(scenePath);
			}
			else
			{
				Debug.LogError("Scene asset 'Client Bootstrap' not found at path: " + scenePath);
			}
		}

		[MenuItem("FishMMO/QuickStart/Login Server Bootstrap", priority = -9)]
		public static void GoToLoginBootstrapScene()
		{
			// Specify the path to the scene asset
			string scenePath = Constants.Configuration.BootstrapScenePath + "LoginServer.unity";

			// Check if the scene exists at the specified path
			if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null)
			{
				// Open the scene in the editor
				EditorSceneManager.OpenScene(scenePath);
			}
			else
			{
				Debug.LogError("Scene asset 'Login Server Bootstrap' not found at path: " + scenePath);
			}
		}

		[MenuItem("FishMMO/QuickStart/World Server Bootstrap", priority = -8)]
		public static void GoToWorldBootstrapScene()
		{
			// Specify the path to the scene asset
			string scenePath = Constants.Configuration.BootstrapScenePath + "WorldServer.unity";

			// Check if the scene exists at the specified path
			if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null)
			{
				// Open the scene in the editor
				EditorSceneManager.OpenScene(scenePath);
			}
			else
			{
				Debug.LogError("Scene asset 'World Server Bootstrap' not found at path: " + scenePath);
			}
		}

		[MenuItem("FishMMO/QuickStart/Scene Server Bootstrap", priority = -7)]
		public static void GoToSceneBootstrapScene()
		{
			// Specify the path to the scene asset
			string scenePath = Constants.Configuration.BootstrapScenePath + "SceneServer.unity";

			// Check if the scene exists at the specified path
			if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null)
			{
				// Open the scene in the editor
				EditorSceneManager.OpenScene(scenePath);
			}
			else
			{
				Debug.LogError("Scene asset 'Scene Server Bootstrap' not found at path: " + scenePath);
			}
		}
	}
}