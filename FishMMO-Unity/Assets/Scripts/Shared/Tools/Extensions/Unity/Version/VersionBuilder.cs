#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	public class VersionBuilder : IPostprocessBuildWithReport
	{
		public int callbackOrder => 0; // Defines the order in which callbacks are executed.

		private const string VersionConfigPath = "Assets/VersionConfig.asset";
		private const string VersionFileName = "version.txt";

		public void OnPostprocessBuild(BuildReport report)
		{
			//UpdateBuildVersion(); // Auto increment build version

			// Get the current version configuration
			VersionConfig config = GetVersionConfig();
			if (config != null)
			{
				// Determine the path where the build will be created
				string buildPath = report.summary.outputPath;
				// Get the directory of the build (e.g., where the .exe or app bundle will be)
				string buildDirectory = Path.GetDirectoryName(buildPath);

				// Construct the full path for the version.txt file
				string versionFilePath = Path.Combine(buildDirectory, VersionFileName);

				try
				{
					// Write the full version string to the file
					File.WriteAllText(versionFilePath, config.FullVersion);
					Log.Debug("VersionBuilder", $"Version file written to: {versionFilePath} with content: {config.FullVersion}");
				}
				catch (System.Exception e)
				{
					Log.Error("VersionBuilder", $"Failed to write version file to {versionFilePath}: {e.Message}");
				}
			}
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
				Log.Debug("VersionBuilder", $"Major Version incremented... {config.FullVersion}");
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
				Log.Debug("VersionBuilder", $"Minor Version incremented... {config.FullVersion}");
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
				Log.Debug("VersionBuilder", $"Patch Version incremented... {config.FullVersion}");
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
				Log.Debug("VersionBuilder", $"Version reset to {config.FullVersion}");
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
				Log.Debug("VersionBuilder", $"Build Version: {config.FullVersion}");
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
				Log.Warning("VersionBuilder", $"No VersionConfig asset found at {VersionConfigPath}. Created a new one.");
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