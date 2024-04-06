using UnityEditor;
using UnityEngine;

public class GoogleAnalyticsMenu
{
    private const string SettingsAssetPath = "Assets/DreamTeamMobile/GoogleAnalytics/Resources/GoogleAnalyticsSettings.asset";

    [MenuItem("Window/GoogleAnalytics/Show GoogleAnalytics Settings")]
    public static void ShowGoogleAnalyticsSettings()
    {
        var asset = AssetDatabase.LoadAssetAtPath<GoogleAnalyticsSettings>(SettingsAssetPath);
        
        if(asset == null)
        {
            asset = ScriptableObject.CreateInstance<GoogleAnalyticsSettings>();
            AssetDatabase.CreateAsset(asset, SettingsAssetPath);
            AssetDatabase.SaveAssets();    
        } 
        EditorUtility.FocusProjectWindow();
        Selection.activeObject = asset;
    }
}
