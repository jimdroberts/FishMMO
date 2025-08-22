#if UNITY_EDITOR
namespace FishMMO.Shared.CustomBuildTool
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