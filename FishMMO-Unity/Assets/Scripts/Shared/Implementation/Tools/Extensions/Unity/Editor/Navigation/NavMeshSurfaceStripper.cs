#if UNITY_EDITOR
using Unity.AI.Navigation;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// Removes every <see cref="NavMeshSurface"/> from a scene in every build and in play mode, so no scene carries
	/// its NavMesh data: NavMeshes reach the scene server through <c>SceneNavMeshCatalogue</c> in the server-only
	/// addressables group, and nowhere else.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why every build.</b> A client never paths or samples the NavMesh (NPC AI, pets and spawners are
	/// server-only), and a generated scene's bake covers its whole playable area, around a hundred megabytes on a
	/// large open-world scene. A server keeping the surface would add the NavMesh twice, once from the surface and
	/// once from <c>NavMeshSystem</c>.
	/// </para>
	/// <para>
	/// <b>Why play mode too</b> (<c>report</c> is null there): so the editor plays the same path a scene server runs,
	/// and a scene missing from the catalogue shows up as NPCs with no NavMesh while authoring rather than after a
	/// build. Edit mode keeps the surfaces, for their bake settings and the editor's view of the mesh.
	/// </para>
	/// <para>
	/// The surface is the only thing that references its data and the data is not a scene-bundle addressable entry,
	/// so removing the component leaves the data out of the scene's bundle. The scene file on disk is never touched.
	/// </para>
	/// </remarks>
	public sealed class NavMeshSurfaceStripper : IProcessSceneWithReport
	{
		public int callbackOrder => 10;

		public void OnProcessScene(Scene scene, BuildReport report)
		{
			int removed = Strip(scene);
			if (removed > 0 && report != null)
			{
				Debug.Log($"[NavMesh surface stripper] Removed {removed} NavMesh surface(s) from '{scene.name}'; the scene server loads its NavMesh from the catalogue.");
			}
		}

		/// <summary>Removes every NavMesh surface in a scene and takes its data out of the world, leaving the objects they sat on. Returns how many.</summary>
		public static int Strip(Scene scene)
		{
			int removed = 0;
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (NavMeshSurface surface in root.GetComponentsInChildren<NavMeshSurface>(true))
				{
					surface.RemoveData();
					Object.DestroyImmediate(surface);
					removed++;
				}
			}
			return removed;
		}
	}
}
#endif
