#if UNITY_EDITOR
namespace FishMMO.Shared.CustomBuildTool.Core
{
    /// <summary>
    /// Manages Addressable Asset Groups for builds.
    /// </summary>
    public interface IAddressableManager
    {
		/// <summary>
		/// Builds Addressable Asset Groups, excluding specified group names.
		/// </summary>
		/// <param name="excludeGroups">Array of group name substrings to exclude from the build.</param>
		/// <param name="enableCrcForRemoteLoading">If true, enables CRC checking for remote bundle loading. If false, disables CRC for local loading.</param>
		/// <param name="useUnityWebRequestForLocal">If true, uses UnityWebRequest for local bundles (WebGL requirement). If false, uses LoadFromFileAsync (better performance for Windows/Linux).</param>
		void BuildAddressablesWithExclusions(string[] excludeGroups, bool enableCrcForRemoteLoading = false, bool useUnityWebRequestForLocal = false);
    }
}
#endif