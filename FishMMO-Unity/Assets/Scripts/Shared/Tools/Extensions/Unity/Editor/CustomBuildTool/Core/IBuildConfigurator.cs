#if UNITY_EDITOR
namespace FishMMO.Shared.CustomBuildTool.Core
{
    /// <summary>
    /// Configures build settings for a Unity build process.
    /// </summary>
    public interface IBuildConfigurator
    {
        void Configure();
        void Restore();
    }
}
#endif