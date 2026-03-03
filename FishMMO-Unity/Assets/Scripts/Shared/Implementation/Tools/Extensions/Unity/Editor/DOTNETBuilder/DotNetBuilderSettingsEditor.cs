using UnityEngine;
using UnityEditor;
using System.IO;

namespace FishMMO.Shared.DotNetBuilder
{
	/// <summary>
	/// Custom Unity Editor for DotNetBuildSettings ScriptableObject.
	/// Provides a user-friendly inspector for configuring .NET build settings, including custom path selectors and conditional GUI logic.
	/// </summary>
	[CustomEditor(typeof(DotNetBuildSettings))]
	public class DotNetBuildSettingsEditor : Editor
	{
		/// <summary>
		/// Draws the custom inspector GUI for DotNetBuildSettings.
		/// Includes controls for build configuration, target framework, output directory, and project file selection.
		/// </summary>
		public override void OnInspectorGUI()
		{
			// Get the target settings object and wrap it in a SerializedObject for property management
			DotNetBuildSettings settings = (DotNetBuildSettings)target;
			SerializedObject serializedSettings = new SerializedObject(settings);
			serializedSettings.Update();

			// Draw common build settings fields
			EditorGUILayout.PropertyField(serializedSettings.FindProperty(nameof(DotNetBuildSettings.BuildConfiguration)));
			EditorGUILayout.PropertyField(serializedSettings.FindProperty(nameof(DotNetBuildSettings.TargetFramework)));
			EditorGUILayout.PropertyField(serializedSettings.FindProperty(nameof(DotNetBuildSettings.DotnetExecutablePath)));
			EditorGUILayout.PropertyField(serializedSettings.FindProperty(nameof(DotNetBuildSettings.SkipDotNetRestore)));
			EditorGUILayout.PropertyField(serializedSettings.FindProperty(nameof(DotNetBuildSettings.PerformCleanBeforeBuild)));

			EditorGUILayout.Space();

			// Draw toggle for using the default output path
			EditorGUILayout.PropertyField(serializedSettings.FindProperty(nameof(DotNetBuildSettings.UseDefaultOutputPath)));

			// Conditionally enable/disable OutputDirectory field based on UseDefaultOutputPath
			EditorGUI.BeginDisabledGroup(settings.UseDefaultOutputPath); // Disable if using default path
			EditorGUILayout.LabelField("Output Directory (relative to Assets)");
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.PropertyField(serializedSettings.FindProperty(nameof(DotNetBuildSettings.OutputDirectory)), GUIContent.none);
			// Button to select output directory using a folder panel
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
					// Convert selected absolute path to a path relative to Assets
					string relativePath = Path.GetRelativePath(Application.dataPath, selectedPath);
					serializedSettings.FindProperty(nameof(DotNetBuildSettings.OutputDirectory)).stringValue = relativePath.Replace("\\", "/");
					serializedSettings.ApplyModifiedProperties();
				}
			}
			EditorGUILayout.EndHorizontal();
			EditorGUI.EndDisabledGroup(); // End disabled group

			EditorGUILayout.Space();

			// Custom GUI for selecting the .csproj file path
			EditorGUILayout.LabelField("Project File Path (relative to Unity root)");
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.PropertyField(serializedSettings.FindProperty(nameof(DotNetBuildSettings.PathToClassLibraryCsproj)), GUIContent.none);
			// Button to select .csproj file using a file panel
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
					// Convert selected absolute path to a path relative to the Unity project root
					string unityProjectRoot = Path.GetFullPath(Application.dataPath + "/../");
					string relativePath = Path.GetRelativePath(unityProjectRoot, selectedPath);
					serializedSettings.FindProperty(nameof(DotNetBuildSettings.PathToClassLibraryCsproj)).stringValue = relativePath.Replace("\\", "/");
					serializedSettings.ApplyModifiedProperties();
				}
			}
			EditorGUILayout.EndHorizontal();

			// Apply any property changes made in the inspector
			serializedSettings.ApplyModifiedProperties();
		}
	}
}