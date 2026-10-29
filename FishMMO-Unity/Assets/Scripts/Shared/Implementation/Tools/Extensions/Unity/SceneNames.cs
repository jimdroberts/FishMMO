using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// A loaded scene's name without the garbage: <see cref="Scene.name"/> builds a new string on every read, and the
	/// render hooks that seed their placement from it (ambient and sea life, sea floats, the glow sky's episodes) read it
	/// every frame.
	/// </summary>
	/// <remarks>
	/// Kept by the scene's handle, and forgotten whenever a scene is loaded, unloaded, opened, closed or saved (a save can
	/// rename it), so the answer is always the one <see cref="Scene.name"/> would give. Main thread only, as the
	/// SceneManager is.
	/// </remarks>
	public static class SceneNames
	{
		private static readonly Dictionary<int, string> byHandle = new Dictionary<int, string>();
		private static bool hooked;

		/// <summary>The scene's name, as <see cref="Scene.name"/>; null for an invalid scene, as it is.</summary>
		public static string Of(Scene scene)
		{
			if (!scene.IsValid())
			{
				return scene.name;
			}
			Hook();
			int handle = scene.handle;
			if (!byHandle.TryGetValue(handle, out string name))
			{
				name = scene.name;
				byHandle[handle] = name;
			}
			return name;
		}

		/// <summary>Forgets every name (the next read asks the scene again).</summary>
		public static void Forget() => byHandle.Clear();

		private static void Hook()
		{
			if (hooked)
			{
				return;
			}
			hooked = true;
			SceneManager.sceneLoaded += (scene, mode) => Forget();
			SceneManager.sceneUnloaded += scene => Forget();
#if UNITY_EDITOR
			UnityEditor.SceneManagement.EditorSceneManager.sceneOpened += (scene, mode) => Forget();
			UnityEditor.SceneManagement.EditorSceneManager.sceneClosed += scene => Forget();
			UnityEditor.SceneManagement.EditorSceneManager.sceneSaved += scene => Forget();
			UnityEditor.SceneManagement.EditorSceneManager.newSceneCreated += (scene, setup, mode) => Forget();
#endif
		}
	}
}
