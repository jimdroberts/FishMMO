#if UNITY_EDITOR
namespace FishMMO.Shared.CustomBuildTool.Core
{
    /// <summary>
    /// Generates linker files for managed assemblies.
    /// </summary>
    public interface ILinkerGenerator
    {
        void GenerateLinker(string rootPath, string directoryPath);
    }
}
#endif