#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO;

namespace FishMMO.Shared
{
	public class VersionBuilder : IPreprocessBuildWithReport
	{
		public int callbackOrder => 0; // Defines the order in which callbacks are executed.

		private const string VersionConfigPath = "Assets/VersionConfig.asset";

		public void OnPreprocessBuild(BuildReport report)
		{
			//UpdateBuildVersion();
		}

		[MenuItem("FishMMO/Version/Increment Major")]
		public static void IncrementMajor()
		{
			VersionConfig config = GetVersionConfig();
			if (config != null)
			{
				config.Major++;
				config.Minor = 0;
				config.Patch = 0;
				config.PreRelease = ""; // Clear pre-release on major increment
				SaveVersionConfig(config);
				Debug.Log($"Major Version incremented... {config.FullVersion}");
			}
		}

		[MenuItem("FishMMO/Version/Increment Minor")]
		public static void IncrementMinor()
		{
			VersionConfig config = GetVersionConfig();
			if (config != null)
			{
				config.Minor++;
				config.Patch = 0;
				config.PreRelease = ""; // Clear pre-release on minor increment
				SaveVersionConfig(config);
				Debug.Log($"Minor Version incremented... {config.FullVersion}");
			}
		}

		[MenuItem("FishMMO/Version/Increment Patch")]
		public static void IncrementPatch()
		{
			VersionConfig config = GetVersionConfig();
			if (config != null)
			{
				config.Patch++;
				// Don't clear pre-release or metadata on patch, as it's a bug fix.
				SaveVersionConfig(config);
				Debug.Log($"Patch Version incremented... {config.FullVersion}");
			}
		}

		[MenuItem("FishMMO/Version/Reset")]
		public static void ResetVersion()
		{
			VersionConfig config = GetVersionConfig();
			if (config != null)
			{
				config.Major = 0;
				config.Minor = 0;
				config.Patch = 0;
				config.PreRelease = "";
				SaveVersionConfig(config);
				Debug.Log($"Version reset to {config.FullVersion}");
			}
		}

		// Automatically called before each build
		private static void UpdateBuildVersion()
		{
			VersionConfig config = GetVersionConfig();
			if (config != null)
			{
				// Increment patch version on build
				config.Patch++;

				SaveVersionConfig(config);
				Debug.Log($"Build Version: {config.FullVersion}");
			}
		}

		private static VersionConfig GetVersionConfig()
		{
			// Ensure the directory exists
			string directoryPath = Path.GetDirectoryName(VersionConfigPath);
			if (!Directory.Exists(directoryPath))
			{
				Directory.CreateDirectory(directoryPath);
			}

			VersionConfig config = AssetDatabase.LoadAssetAtPath<VersionConfig>(VersionConfigPath);

			if (config == null)
			{
				config = ScriptableObject.CreateInstance<VersionConfig>();
				AssetDatabase.CreateAsset(config, VersionConfigPath);
				AssetDatabase.SaveAssets();
				Debug.LogWarning($"No VersionConfig asset found at {VersionConfigPath}. Created a new one.");
			}
			return config;
		}

		private static void SaveVersionConfig(VersionConfig config)
		{
			PlayerSettings.bundleVersion = config.FullVersion;
			EditorUtility.SetDirty(config);
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
		}
	}
}
#endif