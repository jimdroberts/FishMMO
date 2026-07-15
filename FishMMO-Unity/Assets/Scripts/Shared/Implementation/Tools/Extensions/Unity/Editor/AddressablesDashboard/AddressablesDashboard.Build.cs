#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using BuildTool = FishMMO.Shared.CustomBuildTool.Core.CustomBuildTool;

namespace FishMMO.Shared
{
	public partial class AddressablesDashboard
	{
		// ──────────────────────────────────────────────
		// Build Addressables
		// ──────────────────────────────────────────────

		/// <summary>
		/// Builds addressable asset bundles using the current environment options.
		/// Reads BuildType and OSTarget from <see cref="BuildEnvironmentOptions"/>,
		/// shows a confirmation dialog, and delegates to <see cref="CustomBuildTool.BuildAddressablesWithEnvironmentOptions"/>.
		/// </summary>
		private void BuildAddressables()
		{
			if (BuildEnvironmentOptions.IsCompiling())
			{
				EditorUtility.DisplayDialog("Build Blocked",
					"Scripts are currently compiling.\nPlease wait for compilation to finish before building addressables.",
					"OK");
				return;
			}

			// Gate: require Analyze to have been run and zero critical issues
			if (cachedDuplicateCount < 0 && cachedNonAddressableRefCount < 0)
			{
				EditorUtility.DisplayDialog("Build Blocked",
					"Run Analyze first to verify the project has no duplicate dependencies or non-addressable references.",
					"OK");
				return;
			}

			bool hasBlockers = cachedDuplicateCount > 0 || cachedNonAddressableRefCount > 0 || cachedAddressCollisionCount > 0;
			if (hasBlockers)
			{
				string issues = "";
				if (cachedDuplicateCount > 0)
					issues += $"  • {cachedDuplicateCount} duplicate dependencies\n";
				if (cachedNonAddressableRefCount > 0)
					issues += $"  • {cachedNonAddressableRefCount} non-addressable references\n";
				if (cachedAddressCollisionCount > 0)
					issues += $"  • {cachedAddressCollisionCount} address collisions\n";

				EditorUtility.DisplayDialog("Build Blocked",
					"Cannot build addressables with unresolved issues:\n\n" + issues +
					"\nUse Analyze → Dependency Viewer to identify and fix these.\n" +
					"Use Fix All or Smart Group to resolve bulk issues.",
					"OK");
				return;
			}

			BuildTypeEnvironment buildType = BuildEnvironmentOptions.GetBuildType();
			OSTargetEnvironment osTarget = BuildEnvironmentOptions.GetOSTarget();

			string buildTypeStr = buildType == BuildTypeEnvironment.Server ? "Server" : "Client";
			string osStr;
			switch (osTarget)
			{
				case OSTargetEnvironment.Windows: osStr = "Windows x64"; break;
				case OSTargetEnvironment.Linux: osStr = "Linux x64"; break;
				case OSTargetEnvironment.WebGL: osStr = "WebGL"; break;
				default: osStr = osTarget.ToString(); break;
			}

			if (!EditorUtility.DisplayDialog("Build Addressables",
				$"Build addressable bundles with current environment settings?\n\n" +
				$"  Build Type: {buildTypeStr}\n" +
				$"  OS Target:  {osStr}\n\n" +
				"You can change these under FishMMO > Build > Build Type / OS Target.",
				"Build", "Cancel"))
			{
				return;
			}

			// Auto-align load paths to the build target so content loads correctly
			// without user intervention. Eliminates catalog 404 (2.1) and bundle load
			// failures (2.2) caused by mismatched profile paths.
			ValidateLoadPaths();

			SetStatus($"Building addressables ({buildTypeStr} / {osStr})…");

			EditorApplication.delayCall += () =>
			{
				BuildTool.BuildAddressablesWithEnvironmentOptions();
				SetStatus($"Addressables build complete ({buildTypeStr} / {osStr}).");
			};
		}

		/// <summary>
		/// Returns all Addressable groups whose LoadPath references a Remote path.
		/// WebGL builds must use Local paths to avoid catalog 404 errors.
		/// </summary>
		private List<AddressableAssetGroup> GetGroupsWithRemoteLoadPath()
		{
			var settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null) return new List<AddressableAssetGroup>();

			var result = new List<AddressableAssetGroup>();
			foreach (var group in settings.groups)
			{
				if (group == null || group == settings.DefaultGroup) continue;
				var schema = group.GetSchema<BundledAssetGroupSchema>();
				if (schema == null) continue;

				string loadPathName = schema.LoadPath.GetName(settings);
				if (loadPathName != null && loadPathName.IndexOf("Remote", StringComparison.OrdinalIgnoreCase) >= 0)
					result.Add(group);
			}
			return result;
		}

		/// <summary>
		/// Validates every group's resolved LoadPath for common configuration errors
		/// that cause catalog 404 (2.1) and bundle load failures (2.2):
		///   • Double slashes after the domain (trailing base + leading path)
		///   • Empty host between scheme and path (truncated URL)
		///   • Remote paths without a configured CDN / webserver base
		/// Issues are logged as errors so they appear in the build report.
		/// </summary>
		private void ValidateLoadPaths()
		{
			var settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null) return;

			int errors = 0;
			foreach (var group in settings.groups)
			{
				if (group == null || group == settings.DefaultGroup) continue;
				var schema = group.GetSchema<BundledAssetGroupSchema>();
				if (schema == null) continue;

				string loadPathName = schema.LoadPath.GetName(settings);
				string resolved = settings.profileSettings.GetValueByName(settings.activeProfileId, loadPathName);
				if (string.IsNullOrEmpty(resolved)) continue;

				// Check for double slashes in the path portion (e.g. "http://host//path")
				int schemeEnd = resolved.IndexOf("://", StringComparison.Ordinal);
				if (schemeEnd >= 0)
				{
					string afterScheme = resolved.Substring(schemeEnd + 3);
					if (afterScheme.Contains("//"))
					{
						Debug.LogError($"[AddressablesDashboard] Group '{group.Name}' has double-slash in resolved path: {resolved}");
						errors++;
					}

					// Check for empty host (e.g. "https:///path" or "http:///path")
					int firstSlash = afterScheme.IndexOf('/');
					if (firstSlash == 0)
					{
						Debug.LogError($"[AddressablesDashboard] Group '{group.Name}' has empty host in resolved path: {resolved}");
						errors++;
					}
				}

				// Check that http/https paths have a meaningful host
				if ((resolved.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
					 resolved.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
					!resolved.Contains("."))
				{
					Debug.LogWarning($"[AddressablesDashboard] Group '{group.Name}' Remote path has no public host: {resolved}. Verify CDN/webserver serves bundles at this URL.");
				}
			}

			if (errors > 0)
				SetStatus($"⚠ {errors} path error(s) detected — check Console.");
			else
				SetStatus("Load paths validated — no issues.");
		}

		/// <summary>
		/// Switches each group's LoadPath to the Local.LoadPath profile variable.
		/// </summary>
		private void SetGroupLoadPathsToLocal(List<AddressableAssetGroup> groups, bool suppressLog = false)
		{
			var settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null) return;

			foreach (var group in groups)
			{
				var schema = group.GetSchema<BundledAssetGroupSchema>();
				if (schema == null) continue;

				// Find the Local.LoadPath profile variable by scanning profile entries
				string localLoadPathId = null;
				foreach (var name in settings.profileSettings.GetVariableNames())
				{
					string lower = name.ToLowerInvariant();
					if (lower.Contains("local") && lower.Contains("load") &&
						lower.Contains("path") && !lower.Contains("build"))
					{
						localLoadPathId = name;
						break;
					}
				}

				if (!string.IsNullOrEmpty(localLoadPathId))
				{
					schema.LoadPath.SetVariableByName(settings, localLoadPathId);
					Debug.Log($"[AddressablesDashboard] Set {group.Name} LoadPath -> Local ({localLoadPathId})");
				}
				else
				{
					Debug.LogWarning($"[AddressablesDashboard] Could not find Local load path profile variable for {group.Name}");
				}
			}

			EditorUtility.SetDirty(settings);
			RebuildTree();
		}
	}
}
#endif