#if UNITY_EDITOR
using UnityEditor;
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

			SetStatus($"Building addressables ({buildTypeStr} / {osStr})…");

			BuildTool.BuildAddressablesWithEnvironmentOptions();

			SetStatus($"Addressables build complete ({buildTypeStr} / {osStr}).");
		}
	}
}
#endif