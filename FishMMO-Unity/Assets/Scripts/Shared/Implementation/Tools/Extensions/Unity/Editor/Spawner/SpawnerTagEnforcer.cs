using FishMMO.Server.Implementation.World.SceneServer.Spawner;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// Tags every spawner <c>EditorOnly</c> as its scene is saved.
	/// </summary>
	/// <remarks>
	/// <see cref="ObjectSpawner"/>'s own <c>OnValidate</c> covers adding, loading, pasting and
	/// editing a spawner, but not a later change to its GameObject's tag. This hook runs before
	/// the scene is written, so no saved scene holds an untagged spawner, and the bake that
	/// follows the save never sees one. Builds still refuse to run if one gets through.
	/// </remarks>
	[InitializeOnLoad]
	internal static class SpawnerTagEnforcer
	{
		static SpawnerTagEnforcer()
		{
			EditorSceneManager.sceneSaving -= OnSceneSaving;
			EditorSceneManager.sceneSaving += OnSceneSaving;
		}

		private static void OnSceneSaving(Scene scene, string path)
		{
			foreach (ObjectSpawner spawner in SpawnTableBaker.CollectSpawners(scene))
			{
				ObjectSpawner.EnforceEditorOnlyTag(spawner);
			}
		}
	}
}
