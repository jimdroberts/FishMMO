using System.Collections.Generic;
using FishMMO.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// Says once per loaded scene how many of its objects carry a script that did not load, and why that usually happens.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A module's scene components (a banker, a plot foundation, an arena board) live in the module. A game built without
	/// that module still has those objects in its scenes, now with a missing script: harmless, since nothing runs on them,
	/// and the honest result of removing the module (Jim, 2026-11-21). Unity reports each one separately and without
	/// context; this adds one line per scene that names the likely cause, so a missing script is never mistaken for
	/// corruption.
	/// </para>
	/// <para>
	/// Runs at scene load only, in play mode and in builds. A missing script is a null entry in
	/// <see cref="GameObject.GetComponents(System.Type, List{Component})"/>; the walk reuses one list.
	/// </para>
	/// </remarks>
	public static class MissingScriptReport
	{
		private static readonly List<Component> components = new List<Component>(16);
		private static readonly List<Transform> transforms = new List<Transform>(1024);

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void Hook()
		{
			SceneManager.sceneLoaded -= OnSceneLoaded;
			SceneManager.sceneLoaded += OnSceneLoaded;
		}

		private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			int count = Count(scene, out string first);
			if (count > 0)
			{
				Log.Warning("MissingScriptReport",
					$"Scene '{scene.name}': {count} object(s) carry a script that did not load (first: {first}). " +
					"These usually belong to a module that is not installed in this build; nothing runs on them.");
			}
		}

		/// <summary>Counts the objects in <paramref name="scene"/> with at least one missing script; names the first.</summary>
		public static int Count(Scene scene, out string first)
		{
			first = null;
			int count = 0;
			if (!scene.IsValid() || !scene.isLoaded)
			{
				return 0;
			}
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				root.GetComponentsInChildren(true, transforms);
				foreach (Transform t in transforms)
				{
					t.GetComponents(components);
					for (int i = 0; i < components.Count; ++i)
					{
						if (components[i] == null)
						{
							if (first == null)
							{
								first = PathOf(t);
							}
							++count;
							break;
						}
					}
				}
			}
			transforms.Clear();
			components.Clear();
			return count;
		}

		private static string PathOf(Transform t)
		{
			string path = t.name;
			for (Transform p = t.parent; p != null; p = p.parent)
			{
				path = p.name + "/" + path;
			}
			return path;
		}
	}
}
