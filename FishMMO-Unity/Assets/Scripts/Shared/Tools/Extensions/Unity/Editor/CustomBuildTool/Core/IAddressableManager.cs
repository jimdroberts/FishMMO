#if UNITY_EDITOR
namespace FishMMO.Shared.CustomBuildTool.Core
{
    /// <summary>
    /// Manages Addressable Asset Groups for builds.
    /// </summary>
    public interface IAddressableManager
    {
		void BuildAddressablesWithExclusions(string[] excludeGroups);
    }
}
#endif