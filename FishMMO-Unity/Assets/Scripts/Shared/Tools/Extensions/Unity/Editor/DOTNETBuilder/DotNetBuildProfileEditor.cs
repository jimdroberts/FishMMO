using UnityEngine;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace FishMMO.Shared
{
	[CustomEditor(typeof(DotNetBuildProfile))]
	public class DotNetBuildProfileEditor : Editor
	{
		private SerializedProperty settingsListProperty;
		private Vector2 scrollPosition;

		private void OnEnable()
		{
			settingsListProperty = serializedObject.FindProperty(nameof(DotNetBuildProfile.SettingsList));
		}

		public override void OnInspectorGUI()
		{
			serializedObject.Update();

			EditorGUILayout.LabelField("DotNet Build Profiles", EditorStyles.boldLabel);
			EditorGUILayout.Space(10);

			EditorGUILayout.LabelField("Build Settings Assets:", EditorStyles.boldLabel);

			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(settingsListProperty, true);

			if (GUILayout.Button("Add New Setting Slot"))
			{
				settingsListProperty.arraySize++;
			}
			if (GUILayout.Button("Create & Add New Settings Asset"))
			{
				DotNetBuildSettings newSettings = CreateNewSettingsAssetInstance();
				if (newSettings != null)
				{
					settingsListProperty.arraySize++;
					settingsListProperty.GetArrayElementAtIndex(settingsListProperty.arraySize - 1).objectReferenceValue = newSettings;
				}
			}

			if (EditorGUI.EndChangeCheck())
			{
				DotNetBuildProfile profile = (DotNetBuildProfile)target;
				profile.LogOutput = "";
				EditorUtility.SetDirty(profile);
			}

			EditorGUILayout.Space(20);

			if (GUILayout.Button("Build All Selected Libraries"))
			{
				// Call the static utility method from DotNetBuilderUtility
				DotNetBuildProfile profile = (DotNetBuildProfile)target;
				_ = DotNetBuilderUtility.BuildAllAndLog(profile);
			}

			EditorGUILayout.Space(10);

			// Display Log Output
			EditorGUILayout.LabelField("Build Output:", EditorStyles.boldLabel);
			DotNetBuildProfile currentProfile = (DotNetBuildProfile)target;
			scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(800));
			EditorGUILayout.TextArea(currentProfile.LogOutput, GUI.skin.textArea);
			EditorGUILayout.EndScrollView();

			serializedObject.ApplyModifiedProperties();
		}

		private DotNetBuildSettings CreateNewSettingsAssetInstance()
		{
			DotNetBuildSettings newSettings = CreateInstance<DotNetBuildSettings>();
			string path = "Assets/NewDotNetBuildSettings.asset";
			path = AssetDatabase.GenerateUniqueAssetPath(path);
			AssetDatabase.CreateAsset(newSettings, path);
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			Log.Debug($"Created new DotNetBuildSettings asset at: {path}");
			return newSettings;
		}
	}
}