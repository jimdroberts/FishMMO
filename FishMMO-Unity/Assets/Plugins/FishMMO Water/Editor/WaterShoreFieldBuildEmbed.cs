using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Water.Editor
{
	/// <summary>
	/// Writes each scene's shore field into the scene as a build processes it, so no player or server
	/// ever builds one at load.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Scene processors run on the build's copy of each scene, after it is loaded and before it is
	/// written; the scene file on disk is never touched, so the field never lands in the project as
	/// megabytes of hex. The field is the one the editor has cached for that terrain when there is one,
	/// so a build normally costs a file read per scene.
	/// </para>
	/// <para>
	/// Not for a dedicated server, which draws no shore and so builds no field at all.
	/// </para>
	/// <para>
	/// Only in a build: <c>report</c> is null when entering play mode, where the editor's cache serves.
	/// Ordered after <c>ClientOnlySceneStripper</c> (0), so a field it removes is not built first.
	/// </para>
	/// </remarks>
	public sealed class WaterShoreFieldBuildEmbed : IProcessSceneWithReport
	{
		public int callbackOrder => 100;

		public void OnProcessScene(Scene scene, BuildReport report)
		{
			// A dedicated server builds no field (WaterShoreField.Request): tens of megabytes it would only carry.
			if (report == null || IsServerBuild())
			{
				return;
			}
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (WaterShoreField field in root.GetComponentsInChildren<WaterShoreField>(false))
				{
					if (field.enabled && field.EmbedForBuild())
					{
						Debug.Log($"[Water] Embedded the shore field ({field.Resolution}² at {field.TexelMetres:0.##} m) in '{scene.name}'.");
					}
				}
			}
		}

		/// <summary>True while a dedicated server is being built (the same test as ClientOnlySceneStripper's).</summary>
		private static bool IsServerBuild()
		{
#if UNITY_SERVER
			return true;
#else
			return UnityEditor.EditorUserBuildSettings.standaloneBuildSubtarget == UnityEditor.StandaloneBuildSubtarget.Server;
#endif
		}
	}
}
