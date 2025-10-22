#if UNITY_EDITOR
using UnityEditor;

namespace FishMMO.Shared.CustomBuildTool.Core
{
	/// <summary>
	/// Configures build settings for a Unity build process.
	/// </summary>
	public interface IBuildConfigurator
	{
		void Configure(StandaloneBuildSubtarget subTarget, BuildTarget targetBuildTarget);
		void Restore();
	}
}
#endif