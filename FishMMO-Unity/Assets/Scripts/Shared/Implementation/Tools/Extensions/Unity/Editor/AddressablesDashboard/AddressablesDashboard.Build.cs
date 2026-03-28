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