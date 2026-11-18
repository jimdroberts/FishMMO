#if UNITY_EDITOR
using UnityEditor;

namespace FishMMO.Shared.CustomBuildTool.Core
{
	/// <summary>
	/// Configures build settings for a Unity build process.
	/// </summary>
	public interface IBuildConfigurator
	{
		/// <summary>Configures the Unity Editor and Player settings for the build process.</summary>
		/// <param name="subTarget">The build subtarget to switch to.</param>
		/// <param name="targetBuildTarget">The build target to switch to.</param>
		void Configure(StandaloneBuildSubtarget subTarget, BuildTarget targetBuildTarget);
		/// <summary>Restores the Unity Editor and Player settings after a build.</summary>
		void Restore();
	}
}
#endif