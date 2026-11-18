using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// Provides menu items for quickly jumping to key bootstrap scenes in the FishMMO project.
	/// </summary>
	public class JumpToBootstrapScene
	{
		/// <summary>
		/// Loads the specified scene in the Unity Editor if it exists at the given path.
		/// </summary>
		/// <param name="scenePath">The path to the scene asset.</param>
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
				Debug.Log("Scene not found at path: " + scenePath);
			}
		}

		/// <summary>
		/// Opens the MainBootstrap scene for quick access via the FishMMO menu.
		/// </summary>
		[MenuItem("FishMMO/QuickStart/Main Bootstrap", priority = -10)]
		public static void GoToMainBootstrapScene()
		{
			LoadScene(Constants.Configuration.BootstrapScenePath + "MainBootstrap.unity");
		}

		/// <summary>
		/// Opens the ClientPreboot scene for quick access via the FishMMO menu.
		/// </summary>
		[MenuItem("FishMMO/QuickStart/Client Preboot", priority = -10)]
		public static void GoToClientPrebootScene()
		{
			LoadScene(Constants.Configuration.ClientBootstrapScenePath + "ClientPreboot.unity");
		}

		/// <summary>
		/// Opens the ClientPostboot scene for quick access via the FishMMO menu.
		/// </summary>
		[MenuItem("FishMMO/QuickStart/Client Postboot", priority = -10)]
		public static void GoToClientPostbootScene()
		{
			LoadScene(Constants.Configuration.ClientBootstrapScenePath + "ClientPostboot.unity");
		}

		/// <summary>
		/// Opens the ClientLauncher scene for quick access via the FishMMO menu.
		/// </summary>
		[MenuItem("FishMMO/QuickStart/Client Launcher", priority = -10)]
		public static void GoToClientLauncherScene()
		{
			LoadScene(Constants.Configuration.ClientBootstrapScenePath + "ClientLauncher.unity");
		}

		/// <summary>
		/// Opens the LoginServer bootstrap scene for quick access via the FishMMO menu.
		/// </summary>
		[MenuItem("FishMMO/QuickStart/Login Server", priority = -9)]
		public static void GoToLoginBootstrapScene()
		{
			LoadScene(Constants.Configuration.ServerBootstrapScenePath + "LoginServer.unity");
		}

		/// <summary>
		/// Opens the WorldServer bootstrap scene for quick access via the FishMMO menu.
		/// </summary>
		[MenuItem("FishMMO/QuickStart/World Server", priority = -8)]
		public static void GoToWorldBootstrapScene()
		{
			LoadScene(Constants.Configuration.ServerBootstrapScenePath + "WorldServer.unity");
		}

		/// <summary>
		/// Opens the SceneServer bootstrap scene for quick access via the FishMMO menu.
		/// </summary>
		[MenuItem("FishMMO/QuickStart/Scene Server", priority = -7)]
		public static void GoToSceneBootstrapScene()
		{
			LoadScene(Constants.Configuration.ServerBootstrapScenePath + "SceneServer.unity");
		}
	}
}