using UnityEngine;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace FishMMO.Shared.DotNetBuilder
{
	/// <summary>
	/// Custom Unity Editor for DotNetBuildProfile ScriptableObject.
	/// Provides a user-friendly inspector for managing build settings, triggering builds, and viewing build logs.
	/// </summary>
	[CustomEditor(typeof(DotNetBuildProfile))]
	public class DotNetBuildProfileEditor : Editor
	{
		/// <summary>
		/// Serialized property for the SettingsList field in DotNetBuildProfile.
		/// </summary>
		private SerializedProperty settingsListProperty;

		/// <summary>
		/// Scroll position for the build log output area.
		/// </summary>
		private Vector2 scrollPosition;

		/// <summary>
		/// Called when the editor is enabled. Initializes the serialized property reference for SettingsList.
		/// </summary>
		private void OnEnable()
		{
			settingsListProperty = serializedObject.FindProperty(nameof(DotNetBuildProfile.SettingsList));
		}

		/// <summary>
		/// Draws the custom inspector GUI for DotNetBuildProfile.
		/// Includes controls for managing build settings, triggering builds, and displaying build logs.
		/// </summary>
		public override void OnInspectorGUI()
		{
			serializedObject.Update();

			// Section header for build profiles
			EditorGUILayout.LabelField("DotNet Build Profiles", EditorStyles.boldLabel);
			EditorGUILayout.Space(10);

			// Section header for build settings assets
			EditorGUILayout.LabelField("Build Settings Assets:", EditorStyles.boldLabel);

			// Editable list of build settings assets
			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(settingsListProperty, true);

			// Button to add a new empty slot to the settings list
			if (GUILayout.Button("Add New Setting Slot"))
			{
				settingsListProperty.arraySize++;
			}
			// Button to create a new DotNetBuildSettings asset and add it to the list
			if (GUILayout.Button("Create & Add New Settings Asset"))
			{
				DotNetBuildSettings newSettings = CreateNewSettingsAssetInstance();
				if (newSettings != null)
				{
					settingsListProperty.arraySize++;
					settingsListProperty.GetArrayElementAtIndex(settingsListProperty.arraySize - 1).objectReferenceValue = newSettings;
				}
			}

			// If the settings list was changed, clear the log and mark the profile dirty
			if (EditorGUI.EndChangeCheck())
			{
				DotNetBuildProfile profile = (DotNetBuildProfile)target;
				profile.LogOutput = "";
				EditorUtility.SetDirty(profile);
			}

			EditorGUILayout.Space(20);

			// Button to trigger build for all selected libraries
			if (GUILayout.Button("Build All Selected Libraries"))
			{
				// Call the static utility method from DotNetBuilderUtility
				DotNetBuildProfile profile = (DotNetBuildProfile)target;
				_ = DotNetBuilderUtility.BuildAllAndLog(profile);
			}

			EditorGUILayout.Space(10);

			// Display the build log output in a scrollable text area
			EditorGUILayout.LabelField("Build Output:", EditorStyles.boldLabel);
			DotNetBuildProfile currentProfile = (DotNetBuildProfile)target;
			scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(800));
			EditorGUILayout.TextArea(currentProfile.LogOutput, GUI.skin.textArea);
			EditorGUILayout.EndScrollView();

			serializedObject.ApplyModifiedProperties();
		}

		/// <summary>
		/// Creates a new DotNetBuildSettings asset instance, saves it to disk, and returns the reference.
		/// </summary>
		/// <returns>The newly created DotNetBuildSettings asset.</returns>
		private DotNetBuildSettings CreateNewSettingsAssetInstance()
		{
			DotNetBuildSettings newSettings = CreateInstance<DotNetBuildSettings>();
			string path = "Assets/NewDotNetBuildSettings.asset";
			path = AssetDatabase.GenerateUniqueAssetPath(path);
			AssetDatabase.CreateAsset(newSettings, path);
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			Debug.Log($"Created new DotNetBuildSettings asset at: {path}");
			return newSettings;
		}
	}
}