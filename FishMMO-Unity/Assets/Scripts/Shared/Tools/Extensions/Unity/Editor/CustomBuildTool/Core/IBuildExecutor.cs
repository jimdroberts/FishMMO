#if UNITY_EDITOR
using UnityEditor;

namespace FishMMO.Shared.CustomBuildTool.Core
{
    /// <summary>
    /// Executes the Unity build process.
    /// </summary>
    public interface IBuildExecutor
    {
        void ExecuteBuild(string rootPath, string executableName, string[] bootstrapScenes, CustomBuildType customBuildType, BuildOptions buildOptions, StandaloneBuildSubtarget subTarget, BuildTarget buildTarget);
    }
}
#endif