#if UNITY_EDITOR
namespace FishMMO.Shared.CustomBuildTool
{
	/// <summary>
	/// Enum for custom build types.
	/// </summary>
	public enum CustomBuildType : byte
	{
		/// <summary>Server build type.</summary>
		Server = 0,
		/// <summary>Client build type.</summary>
		Client,
	}
}
#endif