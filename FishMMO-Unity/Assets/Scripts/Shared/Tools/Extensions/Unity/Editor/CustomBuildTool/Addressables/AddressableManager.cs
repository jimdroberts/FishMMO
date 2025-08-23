#if UNITY_EDITOR
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
		public void BuildAddressablesWithExclusions(string[] excludeGroups)
		{
			// Get the original AddressableAssetSettings (default settings)
			var originalSettings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.GetSettings(true);

			// Loop through each Addressable group and exclude based on the provided group names
			foreach (var group in originalSettings.groups)
			{
				foreach (var exclusion in excludeGroups)
				{
					var schema = group.GetSchema<UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema>();
					if (schema != null)
					{
						if (group.name.Contains(exclusion))
						{
							schema.IncludeInBuild = false;
							Log.Info("Addressables", $"Group {group.name} has been excluded from the build.");
						}
						else
						{
							schema.IncludeInBuild = true;
							Log.Warning("Addressables", $"Group {group.name} has been included in the build.");
						}
					}
					else
					{
						Log.Warning("Addressables", $"No schema found for group: {group.name}");
					}
				}
			}

			// Clean up old Addressable builds if the build path exists
			string buildPath = UnityEngine.AddressableAssets.Addressables.BuildPath;
			if (System.IO.Directory.Exists(buildPath))
			{
				try
				{
					System.IO.Directory.Delete(buildPath, recursive: true);
					Log.Info("Addressables", $"Deleted previous Addressable build directory at {buildPath}");
				}
				catch (System.Exception ex)
				{
					Log.Error("Addressables", $"Failed to delete previous build directory: {ex.Message}");
				}
			}

			// Start the Addressables build process
			try
			{
				// Perform the actual Addressables build
				UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.BuildPlayerContent(out UnityEditor.AddressableAssets.Build.AddressablesPlayerBuildResult result);

				// Log the overall build result
				if (!string.IsNullOrEmpty(result.Error))
				{
					Log.Error("Addressables", result.Error);
					Log.Error("Addressables", $"Addressable content build failure (duration: {System.TimeSpan.FromSeconds(result.Duration):g})");
				}
				else
				{
					// Log information about the asset bundles that were built
					if (result.AssetBundleBuildResults != null && result.AssetBundleBuildResults.Count > 0)
					{
						Log.Info("Addressables", "Built Asset Bundles:");
						foreach (var bundleResult in result.AssetBundleBuildResults)
						{
							Log.Info("Addressables", $"Bundle: {bundleResult.SourceAssetGroup.Name} | {bundleResult.FilePath}");

							// Log each asset in the bundle
							foreach (var assetPath in bundleResult.SourceAssetGroup.entries)
							{
								Log.Info("Addressables", $"Asset: {assetPath}");
							}
						}
					}
					else
					{
						Log.Info("Addressables", "No asset bundles were built.");
					}
				}
			}
			catch (System.Exception ex)
			{
				Log.Error("Addressables", $"Error during Addressables build: {ex.Message}");
			}

			// Optionally, refresh the asset database after the build
			UnityEditor.AssetDatabase.Refresh();
		}
	}
}
#endif