#if UNITY_EDITOR
namespace FishMMO.Shared.CustomBuildTool
{
	/// <summary>
	/// Enum for custom build types.
	/// </summary>
	public enum CustomBuildType : byte
	{
		Server = 0,
		Client,
		Installer,
	}
}
#endif