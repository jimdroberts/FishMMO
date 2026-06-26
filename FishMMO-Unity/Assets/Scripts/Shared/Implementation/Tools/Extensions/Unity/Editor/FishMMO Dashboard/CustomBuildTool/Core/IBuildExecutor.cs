#if UNITY_EDITOR
using UnityEditor;

namespace FishMMO.Shared.CustomBuildTool.Core
{
    /// <summary>
    /// Executes the Unity build process.
    /// </summary>
    public interface IBuildExecutor
    {
        /// <summary>Executes the Unity player build with the given parameters.</summary>
        /// <param name="rootPath">The root path for the build output.</param>
        /// <param name="executableName">The name of the executable.</param>
        /// <param name="bootstrapScenes">The bootstrap scenes to include.</param>
        /// <param name="customBuildType">The type of build (Client/Server).</param>
        /// <param name="buildOptions">Additional build options.</param>
        /// <param name="subTarget">The build subtarget.</param>
        /// <param name="buildTarget">The build target platform.</param>
        void ExecuteBuild(string rootPath, string executableName, string[] bootstrapScenes, CustomBuildType customBuildType, BuildOptions buildOptions, StandaloneBuildSubtarget subTarget, BuildTarget buildTarget);
    }
}
#endif