using UnityEngine;
using UnityEditor;
using System.IO;

namespace FishMMO.Shared
{
	[CustomEditor(typeof(DotNetBuildSettings))]
	public class DotNetBuildSettingsEditor : Editor
	{
		public override void OnInspectorGUI()
		{
			DotNetBuildSettings settings = (DotNetBuildSettings)target;
			SerializedObject serializedSettings = new SerializedObject(settings);
			serializedSettings.Update();

			// Draw common fields
			EditorGUILayout.PropertyField(serializedSettings.FindProperty(nameof(DotNetBuildSettings.BuildConfiguration)));
			EditorGUILayout.PropertyField(serializedSettings.FindProperty(nameof(DotNetBuildSettings.TargetFramework)));
			EditorGUILayout.PropertyField(serializedSettings.FindProperty(nameof(DotNetBuildSettings.DotnetExecutablePath)));
			EditorGUILayout.PropertyField(serializedSettings.FindProperty(nameof(DotNetBuildSettings.SkipDotNetRestore)));
			EditorGUILayout.PropertyField(serializedSettings.FindProperty(nameof(DotNetBuildSettings.PerformCleanBeforeBuild)));

			EditorGUILayout.Space();

			// Control for UseDefaultOutputPath
			EditorGUILayout.PropertyField(serializedSettings.FindProperty(nameof(DotNetBuildSettings.UseDefaultOutputPath))); // RE-ADDED FIELD IN INSPECTOR

			// Conditional GUI for OutputDirectory based on UseDefaultOutputPath
			EditorGUI.BeginDisabledGroup(settings.UseDefaultOutputPath); // Disable if using default path
			EditorGUILayout.LabelField("Output Directory (relative to Assets)");
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.PropertyField(serializedSettings.FindProperty(nameof(DotNetBuildSettings.OutputDirectory)), GUIContent.none); // RE-ADDED FIELD
			if (GUILayout.Button("Select Output Dir", GUILayout.Width(110)))
			{
				string currentPath = settings.GetAbsoluteOutputDirectory();
				// Fallback to Assets folder if current path is null or doesn't exist
				if (string.IsNullOrEmpty(currentPath) || !Directory.Exists(currentPath))
				{
					currentPath = Application.dataPath;
				}

				string selectedPath = EditorUtility.OpenFolderPanel("Select Output Directory (in Assets)", currentPath, "");
				if (!string.IsNullOrEmpty(selectedPath))
				{
					if (selectedPath.StartsWith(Application.dataPath))
					{
						string relativePath = Path.GetRelativePath(Application.dataPath, selectedPath);
						serializedSettings.FindProperty(nameof(DotNetBuildSettings.OutputDirectory)).stringValue = relativePath.Replace('\\', '/');
						serializedSettings.ApplyModifiedProperties();
					}
					else
					{
						Debug.LogError("Output directory must be within your Unity project's Assets folder.");
					}
				}
			}
			EditorGUILayout.EndHorizontal();
			EditorGUI.EndDisabledGroup(); // End disabled group

			EditorGUILayout.Space();

			// Custom GUI for PathToClassLibraryCsproj with a select button (always enabled)
			EditorGUILayout.LabelField("Project File Path (relative to Unity root)");
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.PropertyField(serializedSettings.FindProperty(nameof(DotNetBuildSettings.PathToClassLibraryCsproj)), GUIContent.none);
			if (GUILayout.Button("Select .csproj", GUILayout.Width(100)))
			{
				string currentPath = settings.GetAbsoluteCsprojPath();
				string directory = Path.GetDirectoryName(currentPath);

				if (!Directory.Exists(directory))
				{
					directory = Application.dataPath;
				}

				string selectedPath = EditorUtility.OpenFilePanel("Select .NET Project File (.csproj)", directory, "csproj");
				if (!string.IsNullOrEmpty(selectedPath))
				{
					string unityProjectRoot = Path.GetFullPath(Application.dataPath + "/../");
					string relativePath = Path.GetRelativePath(unityProjectRoot, selectedPath);
					serializedSettings.FindProperty(nameof(DotNetBuildSettings.PathToClassLibraryCsproj)).stringValue = relativePath.Replace('\\', '/');
					serializedSettings.ApplyModifiedProperties();
				}
			}
			EditorGUILayout.EndHorizontal();

			serializedSettings.ApplyModifiedProperties();
		}
	}
}