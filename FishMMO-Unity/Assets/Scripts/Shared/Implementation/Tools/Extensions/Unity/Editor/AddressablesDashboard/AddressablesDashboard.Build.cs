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

			// Pre-build validation: WebGL builds must use Local load paths.
			// Remote paths cause catalog 404 and bundle load failures.
			var remoteGroups = GetGroupsWithRemoteLoadPath();
			if (remoteGroups.Count > 0)
			{
				string groupList = string.Join("\n", remoteGroups.ConvertAll(g => $"  • {g.Name}"));
				if (osTarget == OSTargetEnvironment.WebGL)
				{
					if (EditorUtility.DisplayDialog("WebGL Path Warning",
						$"{remoteGroups.Count} group(s) use Remote load paths:\n{groupList}\n\n" +
						"WebGL builds must use Local paths so content is packaged into StreamingAssets " +
						"and uploaded with the player. Remote paths cause catalog 404 errors.\n\n" +
						"Convert these groups to Local load paths?",
						"Convert to Local", "Build Anyway"))
					{
						SetGroupLoadPathsToLocal(remoteGroups);
						SetStatus($"Converted {remoteGroups.Count} group(s) to Local load paths…");
					}
				}
				else if (buildType == BuildTypeEnvironment.Server)
				{
					Debug.Log($"[AddressablesDashboard] {remoteGroups.Count} group(s) use Remote load paths (expected for server).");
				}
			}

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
		/// Sets the LoadPath on each group to the Local.LoadPath profile variable
		/// so content is packaged into StreamingAssets (required for WebGL).
		/// </summary>
		private void SetGroupLoadPathsToLocal(List<AddressableAssetGroup> groups)
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