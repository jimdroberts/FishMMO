#if UNITY_EDITOR
namespace FishMMO.Shared.CustomBuildTool.Core
{
    /// <summary>
    /// Generates linker files for managed assemblies.
    /// </summary>
    public interface ILinkerGenerator
    {
        /// <summary>Generates a linker XML file for managed assemblies.</summary>
        /// <param name="rootPath">Root path for the linker file output.</param>
        /// <param name="directoryPath">Directory to scan for assemblies.</param>
        void GenerateLinker(string rootPath, string directoryPath);
    }
}
#endif