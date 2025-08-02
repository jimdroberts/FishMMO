#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Handles versioning for FishMMO builds. Provides build post-processing to write version info,
	/// and menu items for incrementing and resetting version numbers.
	/// </summary>
	public class VersionBuilder : IPostprocessBuildWithReport
	{
		/// <summary>
		/// The order in which build post-process callbacks are executed. 0 means first.
		/// </summary>
		public int callbackOrder => 0;

		/// <summary>
		/// Path to the VersionConfig asset in the project.
		/// </summary>
		private const string VersionConfigPath = "Assets/VersionConfig.asset";

		/// <summary>
		/// Name of the version file to be written in the build output directory.
		/// </summary>
		private const string VersionFileName = "version.txt";

		/// <summary>
		/// Called after the build is completed. Writes the current version to a file in the build output directory.
		/// </summary>
		/// <param name="report">The build report containing build details.</param>
		public void OnPostprocessBuild(BuildReport report)
		{
			//UpdateBuildVersion(); // Auto increment build version

			// Retrieve the current version configuration asset.
			VersionConfig config = GetVersionConfig();
			if (config != null)
			{
				// Path to the built executable or app bundle.
				string buildPath = report.summary.outputPath;
				// Directory containing the build output.
				string buildDirectory = Path.GetDirectoryName(buildPath);
				// Full path for the version.txt file.
				string versionFilePath = Path.Combine(buildDirectory, VersionFileName);

				try
				{
					// Write the full version string to the version.txt file.
					File.WriteAllText(versionFilePath, config.FullVersion);
					Log.Debug("VersionBuilder", $"Version file written to: {versionFilePath} with content: {config.FullVersion}");
				}
				catch (System.Exception e)
				{
					Log.Error("VersionBuilder", $"Failed to write version file to {versionFilePath}: {e.Message}");
				}
			}
		}

		/// <summary>
		/// Increments the major version, resets minor and patch, and clears pre-release.
		/// Accessible via FishMMO/Version/Increment Major menu.
		/// </summary>
		[MenuItem("FishMMO/Version/Increment Major")]
		public static void IncrementMajor()
		{
			VersionConfig config = GetVersionConfig();
			if (config != null)
			{
				// Major version increment resets minor and patch, and clears pre-release.
				config.Major++;
				config.Minor = 0;
				config.Patch = 0;
				config.PreRelease = ""; // Clear pre-release on major increment
				SaveVersionConfig(config);
				Log.Debug("VersionBuilder", $"Major Version incremented... {config.FullVersion}");
			}
		}

		/// <summary>
		/// Increments the minor version, resets patch, and clears pre-release.
		/// Accessible via FishMMO/Version/Increment Minor menu.
		/// </summary>
		[MenuItem("FishMMO/Version/Increment Minor")]
		public static void IncrementMinor()
		{
			VersionConfig config = GetVersionConfig();
			if (config != null)
			{
				// Minor version increment resets patch and clears pre-release.
				config.Minor++;
				config.Patch = 0;
				config.PreRelease = ""; // Clear pre-release on minor increment
				SaveVersionConfig(config);
				Log.Debug("VersionBuilder", $"Minor Version incremented... {config.FullVersion}");
			}
		}

		/// <summary>
		/// Increments the patch version. Does not clear pre-release or metadata.
		/// Accessible via FishMMO/Version/Increment Patch menu.
		/// </summary>
		[MenuItem("FishMMO/Version/Increment Patch")]
		public static void IncrementPatch()
		{
			VersionConfig config = GetVersionConfig();
			if (config != null)
			{
				// Patch version increment for bug fixes; pre-release and metadata are not cleared.
				config.Patch++;
				SaveVersionConfig(config);
				Log.Debug("VersionBuilder", $"Patch Version incremented... {config.FullVersion}");
			}
		}

		/// <summary>
		/// Resets the version to 0.0.0 and clears pre-release.
		/// Accessible via FishMMO/Version/Reset menu.
		/// </summary>
		[MenuItem("FishMMO/Version/Reset")]
		public static void ResetVersion()
		{
			VersionConfig config = GetVersionConfig();
			if (config != null)
			{
				// Reset all version fields to initial state.
				config.Major = 0;
				config.Minor = 0;
				config.Patch = 0;
				config.PreRelease = "";
				SaveVersionConfig(config);
				Log.Debug("VersionBuilder", $"Version reset to {config.FullVersion}");
			}
		}

		/// <summary>
		/// Automatically increments the patch version before each build.
		/// </summary>
		private static void UpdateBuildVersion()
		{
			VersionConfig config = GetVersionConfig();
			if (config != null)
			{
				// Patch version is incremented for every build.
				config.Patch++;
				SaveVersionConfig(config);
				Log.Debug("VersionBuilder", $"Build Version: {config.FullVersion}");
			}
		}

		/// <summary>
		/// Loads the VersionConfig asset from disk, creating it if it does not exist.
		/// </summary>
		/// <returns>The loaded or newly created VersionConfig asset.</returns>
		private static VersionConfig GetVersionConfig()
		{
			// Ensure the directory for the asset exists.
			string directoryPath = Path.GetDirectoryName(VersionConfigPath);
			if (!Directory.Exists(directoryPath))
			{
				Directory.CreateDirectory(directoryPath);
			}

			VersionConfig config = AssetDatabase.LoadAssetAtPath<VersionConfig>(VersionConfigPath);

			if (config == null)
			{
				// Create a new VersionConfig asset if none exists.
				config = ScriptableObject.CreateInstance<VersionConfig>();
				AssetDatabase.CreateAsset(config, VersionConfigPath);
				AssetDatabase.SaveAssets();
				Log.Warning("VersionBuilder", $"No VersionConfig asset found at {VersionConfigPath}. Created a new one.");
			}
			return config;
		}

		/// <summary>
		/// Saves the VersionConfig asset and updates the PlayerSettings bundle version.
		/// </summary>
		/// <param name="config">The VersionConfig asset to save.</param>
		private static void SaveVersionConfig(VersionConfig config)
		{
			// Update Unity's bundle version to match the config.
			PlayerSettings.bundleVersion = config.FullVersion;
			EditorUtility.SetDirty(config);
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
		}
	}
}
#endif