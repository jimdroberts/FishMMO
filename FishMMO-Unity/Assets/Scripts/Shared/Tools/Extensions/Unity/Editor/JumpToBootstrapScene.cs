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
				Debug.LogError("Scene asset 'ClientBootstrap' not found at path: " + scenePath);
			}
		}

		[MenuItem("FishMMO/QuickStart/Login Bootstrap", priority = -9)]
		public static void GoToLoginBootstrapScene()
		{
			// Specify the path to the scene asset
			string scenePath = Constants.Configuration.BootstrapScenePath + "LoginBootstrap.unity";

			// Check if the scene exists at the specified path
			if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null)
			{
				// Open the scene in the editor
				EditorSceneManager.OpenScene(scenePath);
			}
			else
			{
				Debug.LogError("Scene asset 'LoginBootstrap' not found at path: " + scenePath);
			}
		}

		[MenuItem("FishMMO/QuickStart/World Bootstrap", priority = -8)]
		public static void GoToWorldBootstrapScene()
		{
			// Specify the path to the scene asset
			string scenePath = Constants.Configuration.BootstrapScenePath + "WorldBootstrap.unity";

			// Check if the scene exists at the specified path
			if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null)
			{
				// Open the scene in the editor
				EditorSceneManager.OpenScene(scenePath);
			}
			else
			{
				Debug.LogError("Scene asset 'WorldBootstrap' not found at path: " + scenePath);
			}
		}

		[MenuItem("FishMMO/QuickStart/Scene Bootstrap", priority = -7)]
		public static void GoToSceneBootstrapScene()
		{
			// Specify the path to the scene asset
			string scenePath = Constants.Configuration.BootstrapScenePath + "SceneBootstrap.unity";

			// Check if the scene exists at the specified path
			if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null)
			{
				// Open the scene in the editor
				EditorSceneManager.OpenScene(scenePath);
			}
			else
			{
				Debug.LogError("Scene asset 'SceneBootstrap' not found at path: " + scenePath);
			}
		}
	}
}