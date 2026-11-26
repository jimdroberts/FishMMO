#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace FishMMO.Shared
{
	/// <summary>
	/// Whether a scene processor (<c>IProcessSceneWithReport</c>) is running for a build or for a scene loading in play mode.
	/// </summary>
	/// <remarks>
	/// The Addressables content build processes every scene it packs, as a player build does, but hands the processors no
	/// <see cref="BuildReport"/>: there is no player build for it to describe. A processor that took <c>report == null</c>
	/// to mean "play mode" therefore skipped every scene the game ships, since every world scene ships in a bundle, and
	/// said nothing about it: the terrain strippers never ran, and each scene carried Unity's full terrain data (splat maps
	/// and their layer textures) that the build was meant to leave out. Play mode is the only time a scene is processed
	/// with no report and with the editor entering or in play.
	/// </remarks>
	public static class SceneBuildProcessing
	{
		/// <summary>True when the scene is being processed for a player build or an Addressables content build.</summary>
		public static bool IsBuilding(BuildReport report)
		{
			return report != null || !(PlayingForTests ?? EditorApplication.isPlayingOrWillChangePlaymode);
		}

		/// <summary>Tests only: stands in for <see cref="EditorApplication.isPlayingOrWillChangePlaymode"/> while set.</summary>
		public static bool? PlayingForTests { get; set; }

		/// <summary>The platform being built for: the report's, or the active build target in a content build.</summary>
		public static BuildTarget Target(BuildReport report)
		{
			return report != null ? report.summary.platform : EditorUserBuildSettings.activeBuildTarget;
		}
	}
}
#endif
