#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// Deletes every <see cref="ClientOnlyObject"/> from a scene while a server build processes it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Scene processors run on the build's copy of each scene, after it is loaded and before it is
	/// written, and the build only collects the assets the written scene still references. So an
	/// object removed here takes its meshes, textures and materials out of the server with it;
	/// the scene file on disk is never touched.
	/// </para>
	/// <para>
	/// Only in a build (<c>report</c> is null when entering play mode) and only for the server:
	/// the subtarget the build tool switches to (<c>BuildConfigurator</c> sets
	/// <c>standaloneBuildSubtarget</c> before every server build), or a dedicated-server compile.
	/// </para>
	/// </remarks>
	public sealed class ClientOnlySceneStripper : IProcessSceneWithReport
	{
		public int callbackOrder => 0;

		public void OnProcessScene(Scene scene, BuildReport report)
		{
			if (report == null || !IsServerBuild())
			{
				return;
			}
			int removed = Strip(scene);
			if (removed > 0)
			{
				Debug.Log($"[Client-only stripper] Removed {removed} client-only object(s) from '{scene.name}' for the server build.");
			}
		}

		/// <summary>Deletes every client-only object in a scene, children included. Returns how many were marked.</summary>
		public static int Strip(Scene scene)
		{
			int removed = 0;
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (ClientOnlyObject marked in root.GetComponentsInChildren<ClientOnlyObject>(true))
				{
					// Destroyed already as part of a marked parent.
					if (marked == null)
					{
						continue;
					}
					Object.DestroyImmediate(marked.gameObject);
					removed++;
				}
			}
			return removed;
		}

		/// <summary>True while building the dedicated server.</summary>
		public static bool IsServerBuild()
		{
#if UNITY_SERVER
			return true;
#else
			return EditorUserBuildSettings.standaloneBuildSubtarget == StandaloneBuildSubtarget.Server;
#endif
		}
	}
}
#endif
