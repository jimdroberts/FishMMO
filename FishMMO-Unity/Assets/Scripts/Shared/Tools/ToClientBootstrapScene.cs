using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public class ToClientBootstrapScene : MonoBehaviour
{
    [MenuItem("FishMMO_QuickStart/ Start")]
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
