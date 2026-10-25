#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using FishMMO.Logging;
using FishMMO.Shared.CustomBuildTool.Core;
using RuntimeAddressables = UnityEngine.AddressableAssets.Addressables;

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
			var originalSettings = GetSettings();
			if (originalSettings == null || originalSettings.groups == null)
				throw new BuildFailedException("Addressable settings or groups are missing; player build cancelled.");

			var saved = new Dictionary<BundledAssetGroupSchema, (bool include, bool crc, bool cachedCrc, bool webRequest)>();
			foreach (var group in originalSettings.groups)
			{
				var schema = group != null ? group.GetSchema<BundledAssetGroupSchema>() : null;
				if (schema != null)
					saved[schema] = (schema.IncludeInBuild, schema.UseAssetBundleCrc,
						schema.UseAssetBundleCrcForCachedBundles, schema.UseUnityWebRequestForLocalBundles);
			}
			try
			{
				BuildContent(originalSettings, excludeGroups, enableCrcForRemoteLoading, useUnityWebRequestForLocal);
			}
			finally
			{
				foreach (var pair in saved)
				{
					if (pair.Key == null) continue;
					pair.Key.IncludeInBuild = pair.Value.include;
					pair.Key.UseAssetBundleCrc = pair.Value.crc;
					pair.Key.UseAssetBundleCrcForCachedBundles = pair.Value.cachedCrc;
					pair.Key.UseUnityWebRequestForLocalBundles = pair.Value.webRequest;
				}
				AssetDatabase.SaveAssets();
			}
		}

		protected virtual AddressableAssetSettings GetSettings() => AddressableAssetSettingsDefaultObject.GetSettings(true);

		protected virtual void BuildContent(AddressableAssetSettings originalSettings, string[] excludeGroups,
			bool enableCrcForRemoteLoading, bool useUnityWebRequestForLocal)
		{
			Log.Info("Addressables", $"Configuring bundles for {(useUnityWebRequestForLocal ? "WebGL (UnityWebRequest)" : "Windows/Linux (LoadFromFileAsync)")}");

			// All targets load content locally from StreamingAssets — content delivery
			// is handled by the Updater, not by Addressables remote catalog.  Disable the
			// remote catalog so no catalog_VERSION.hash is built, and skip the startup
			// remote-catalog probe so browsers never request a non-existent remote catalog.
			originalSettings.BuildRemoteCatalog = false;
			originalSettings.DisableCatalogUpdateOnStartup = true;
			Log.Info("Addressables",
				"BuildRemoteCatalog=false, DisableCatalogUpdateOnStartup=true " +
				"(local StreamingAssets only; content delivered by Updater).");

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

				bool exclude = !ContentBuildFilter.IncludesGroup(group.name, excludeGroups);
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

			// Flush schema changes to disk so BuildPlayerContent reads the correct
			// per-target values. In-memory ScriptableObject modifications may not be
			// visible if the Addressables pipeline internally reloads settings.
			AssetDatabase.SaveAssets();

			// A clean player build does not require clearing Addressables output.
			// Keep existing content and caches available while the builder calculates changes.
			Log.Info("Addressables", "Preserving previous Addressables output and shader caches; obsolete bundles are pruned only after a successful content build.");

			// Start the Addressables build process
			try
			{
				Log.Info("Addressables", "Before BuildPlayerContent");

				Log.Debug("Addressables", $"Active Target: {EditorUserBuildSettings.activeBuildTarget}");
				Log.Debug("Addressables", $"Selected Build Target Group: {EditorUserBuildSettings.selectedBuildTargetGroup}");

				var settings = AddressableAssetSettingsDefaultObject.Settings;
				Log.Debug("Addressables", $"Active Player Data Builder Index: {settings.ActivePlayerDataBuilderIndex}");

				if (settings.ActivePlayerDataBuilderIndex >= 0 &&
					settings.ActivePlayerDataBuilderIndex < settings.DataBuilders.Count)
				{
					var builder = settings.DataBuilders[settings.ActivePlayerDataBuilderIndex];
					Log.Debug("Addressables", $"Builder Type: {builder?.GetType().FullName}");
					Log.Debug("Addressables", $"Builder Name: {builder?.name}");
				}
				else
				{
					Log.Warning("Addressables", $"Builder index out of range! DataBuilders count: {settings.DataBuilders.Count}");
				}

				// Dump Build Settings scenes before the build
				Log.Debug("Addressables", $"Scenes In Build: {EditorBuildSettings.scenes.Length}");

				foreach (var scene in EditorBuildSettings.scenes)
				{
					Log.Debug("Addressables", $"Scene: {scene.path} Enabled:{scene.enabled}");
				}

				// Diagnose why CanBuildPlayer might return false
				var targetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
				var target = EditorUserBuildSettings.activeBuildTarget;
				Log.Debug("Addressables", $"IsBuildTargetSupported({targetGroup}, {target}): {BuildPipeline.IsBuildTargetSupported(targetGroup, target)}");
				Log.Debug("Addressables", $"EditorApplication.isCompiling: {EditorApplication.isCompiling}");
				var namedTarget = NamedBuildTarget.FromBuildTargetGroup(targetGroup);
				Log.Debug("Addressables", $"ScriptingBackend for {target}: {PlayerSettings.GetScriptingBackend(namedTarget)}");

				AddressablesPlayerBuildResult result;
				try
				{
					AddressableAssetSettings.BuildPlayerContent(out result);
				}
				catch (Exception ex)
				{
					Log.Error("Addressables", $"EXCEPTION TYPE: {ex.GetType()}", ex);
					Log.Error("Addressables", $"EXCEPTION: {ex}");

					Exception inner = ex.InnerException;
					while (inner != null)
					{
						Log.Error("Addressables", $"INNER: {inner}");
						inner = inner.InnerException;
					}

					throw;
				}
				Log.Info("Addressables", "After BuildPlayerContent");

				RequireSuccessfulContentBuild(result);
				var outputPaths = new HashSet<string> { RuntimeAddressables.BuildPath };
				foreach (var group in originalSettings.groups)
				{
					var schema = group != null ? group.GetSchema<BundledAssetGroupSchema>() : null;
					if (schema != null) outputPaths.Add(schema.BuildPath.GetValue(originalSettings));
				}
				// Include Remote.BuildPath even when every group has switched to Local.
				string remoteBuildPath = originalSettings.profileSettings.GetValueByName(originalSettings.activeProfileId, "Remote.BuildPath");
				if (!string.IsNullOrEmpty(remoteBuildPath))
					outputPaths.Add(originalSettings.profileSettings.EvaluateString(originalSettings.activeProfileId, remoteBuildPath));
				int removed = PruneStaleBundleOutputs(result, outputPaths);
				Log.Info("Addressables", $"Removed {removed} obsolete output bundle(s); retained current bundles and build caches.");
				Log.Info("Addressables", $"Result Error: {result.Error}");

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
			catch (Exception ex)
			{
				Log.Error("Addressables", $"Error during Addressables build: {ex.Message}", ex);

				if (ex.InnerException != null)
				{
					Log.Error("Addressables", $"Inner exception: {ex.InnerException.Message}", ex.InnerException);
				}
				throw;
			}

		}

		/// <summary>Addressables reports many failures through Error instead of throwing.</summary>
		public static void RequireSuccessfulContentBuild(AddressablesPlayerBuildResult result)
		{
			if (result == null)
				throw new BuildFailedException("Addressables returned no build result; player build cancelled.");
			if (!string.IsNullOrEmpty(result.Error))
				throw new BuildFailedException($"Addressable content build failed; player build cancelled: {result.Error}");
		}

		/// <summary>
		/// Preserve reusable content until a build succeeds, then remove output bundles
		/// absent from its file registry. In particular, excluded server/client bundles
		/// must not be copied into the next player's StreamingAssets. Never clear caches.
		/// </summary>
		public static int PruneStaleBundleOutputs(AddressablesPlayerBuildResult result, IEnumerable<string> outputPaths)
		{
			RequireSuccessfulContentBuild(result);
			if (result.FileRegistry == null)
				throw new BuildFailedException("Addressables did not provide its output file registry; cannot safely prepare player content.");
			var comparer = Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
			var current = new HashSet<string>(comparer);
			foreach (string path in result.FileRegistry.GetFilePaths()) current.Add(Path.GetFullPath(path));
			var visited = new HashSet<string>(comparer);
			int removed = 0;
			foreach (string path in outputPaths)
			{
				if (string.IsNullOrEmpty(path)) continue;
				string root = Path.GetFullPath(path);
				if (!visited.Add(root) || !Directory.Exists(root)) continue;
				foreach (string bundle in Directory.GetFiles(root, "*.bundle", SearchOption.AllDirectories))
				{
					if (current.Contains(Path.GetFullPath(bundle))) continue;
					File.Delete(bundle);
					removed++;
				}
			}
			return removed;
		}
	}
}
#endif
