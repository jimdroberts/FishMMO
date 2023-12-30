using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace FishMMO.Shared
{
	public class JumpToClientBootstrapScene
	{
		[MenuItem("FishMMO/QuickStart/Load Client Bootstrap", priority = -10)]
		public static void GoToClientBootstrapScene()
		{
			// Specify the path to the scene asset
			string scenePath = "Assets/Scenes/Bootstraps/ClientBootstrap.unity";

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
	}
}