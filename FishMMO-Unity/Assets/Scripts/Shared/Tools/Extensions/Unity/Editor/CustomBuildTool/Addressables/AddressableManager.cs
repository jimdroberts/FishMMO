#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using FishMMO.Logging;
using FishMMO.Shared.CustomBuildTool.Core;

namespace FishMMO.Shared.CustomBuildTool.Addressables
{
	/// <summary>
	/// Manages Addressable Asset Groups for builds.
	/// </summary>
	public class AddressableManager : IAddressableManager
	{
		/// <summary>
		/// Builds Addressable Asset Groups, excluding specified group names.
		/// </summary>
		/// <param name="excludeGroups">Array of group name substrings to exclude from the build.</param>
		/// <param name="enableCrcForRemoteLoading">If true, enables CRC checking for remote bundle loading (e.g., WebGL with CDN). If false, disables CRC for local StreamingAssets loading.</param>
		/// <param name="useUnityWebRequestForLocal">If true, uses UnityWebRequest for local bundles (WebGL requirement). If false, uses LoadFromFileAsync (better performance for Windows/Linux).</param>
		public void BuildAddressablesWithExclusions(string[] excludeGroups, bool enableCrcForRemoteLoading = false, bool useUnityWebRequestForLocal = false)
		{
			// Get the default AddressableAssetSettings
			var originalSettings = AddressableAssetSettingsDefaultObject.GetSettings(true);
			if (originalSettings == null || originalSettings.groups == null)
			{
				Log.Error("Addressables", "Addressable settings or groups are null.");
				return;
			}

			Log.Info("Addressables", $"Configuring bundles for {(useUnityWebRequestForLocal ? "WebGL (UnityWebRequest)" : "Windows/Linux (LoadFromFileAsync)")}");

			// Loop through each Addressable group and apply exclusion logic
			foreach (var group in originalSettings.groups)
			{
				if (group == null) continue;

				var schema = group.GetSchema<BundledAssetGroupSchema>();
				if (schema == null)
				{
					Log.Warning("Addressables", $"No schema found for group: {group.name}");
					continue;
				}

				bool exclude = excludeGroups.Any(exclusion =>
					group.name.IndexOf(exclusion, StringComparison.OrdinalIgnoreCase) >= 0);
				schema.IncludeInBuild = !exclude;

				// Configure CRC based on build type
				// - For local StreamingAssets loading (Windows/Linux clients/servers): Disable CRC
				//   CRC checking causes issues when bundles are loaded from file:// protocol
				//   because Unity may reprocess them during build, changing the CRC
				// - For remote loading (WebGL downloading from game servers): Enable CRC
				//   CRC ensures downloaded bundles haven't been corrupted during network transfer
				//
				// Configure loading method based on platform
				// - For WebGL: Must use UnityWebRequest (browser security requirement)
				// - For Windows/Linux: Use LoadFromFileAsync (better performance for local files)
				if (!exclude)
				{
					schema.UseAssetBundleCrc = enableCrcForRemoteLoading;
					schema.UseAssetBundleCrcForCachedBundles = enableCrcForRemoteLoading;
					schema.UseUnityWebRequestForLocalBundles = useUnityWebRequestForLocal;

					if (enableCrcForRemoteLoading)
					{
						Log.Info("Addressables", $"{group.name} - CRC enabled for remote loading");
					}
					else
					{
						Log.Info("Addressables", $"{group.name} - CRC disabled for local loading");
					}
				}

				Log.Info("Addressables", $"{group.name} has been {(exclude ? "excluded" : "included")} from the build.");
			}

			// Clean previous Addressables build safely
			try
			{
				AddressableAssetSettings.CleanPlayerContent();
				Log.Info("Addressables", "Cleaned previous Addressables player content.");
			}
			catch (Exception ex)
			{
				Log.Warning("Addressables", $"Error during cleanup (non-fatal): {ex.Message}");
			}

			// Start the Addressables build process
			try
			{
				AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);

				if (!string.IsNullOrEmpty(result.Error))
				{
					Log.Error("Addressables", result.Error);
					Log.Error("Addressables", $"Addressable content build failed (duration: {TimeSpan.FromSeconds(result.Duration):g})");
				}
				else
				{
					Log.Info("Addressables", $"Addressable content build succeeded (duration: {TimeSpan.FromSeconds(result.Duration):g})");

					if (result.AssetBundleBuildResults != null && result.AssetBundleBuildResults.Count > 0)
					{
						foreach (var bundleResult in result.AssetBundleBuildResults)
						{
							Log.Info("Addressables", $"Bundle: {bundleResult.SourceAssetGroup.Name} | File: {bundleResult.FilePath}");

							foreach (var assetEntry in bundleResult.SourceAssetGroup.entries)
							{
								Log.Info("Addressables", $" └─ Asset: {assetEntry.address} | Path: {assetEntry.AssetPath}");
							}
						}
					}
					else
					{
						Log.Info("Addressables", "No asset bundles were built.");
					}
				}

			}
			catch (Exception ex)
			{
				Log.Error("Addressables", $"Error during Addressables build: {ex.Message}");
			}

			// Refresh the AssetDatabase after the build
			AssetDatabase.Refresh();
		}
	}
}
#endif