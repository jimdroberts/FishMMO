using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents the data required to load a Unity scene using the Addressables system.
	/// Includes scene name, activation behavior, load mode, and post-load callback.
	/// </summary>
	[Serializable]
	public class AddressableSceneLoadData
	{
		/// <summary>
		/// The name of the scene to load via Addressables.
		/// </summary>
		public string SceneName;

		/// <summary>
		/// If true, sets the scene active when finished loading. If false, loading is blocked until activation.
		/// </summary>
		[Tooltip("Sets the scene active when it finishes loaded. Blocks AddressableAsync loading when false.")]
		public bool ActivateOnLoad;

		/// <summary>
		/// The mode used to load the scene (Single or Additive).
		/// </summary>
		public LoadSceneMode LoadSceneMode;

		/// <summary>
		/// Optional callback invoked after the scene is loaded.
		/// </summary>
		public Action<Scene> OnSceneLoaded;

		/// <summary>
		/// Constructs a new AddressableSceneLoadData instance.
		/// </summary>
		/// <param name="sceneName">The name of the scene to load.</param>
		/// <param name="onSceneLoaded">Callback invoked after scene is loaded (optional).</param>
		/// <param name="loadSceneMode">The mode to load the scene (default: Additive).</param>
		/// <param name="activateOnLoad">Whether to activate the scene on load (default: true).</param>
		public AddressableSceneLoadData(string sceneName, Action<Scene> onSceneLoaded = null, LoadSceneMode loadSceneMode = LoadSceneMode.Additive, bool activateOnLoad = true)
		{
			SceneName = sceneName;
			OnSceneLoaded = onSceneLoaded;
			LoadSceneMode = loadSceneMode;
			ActivateOnLoad = activateOnLoad;
		}

		/// <summary>
		/// Returns a hash code for this instance based on the scene name.
		/// </summary>
		/// <returns>Hash code for the scene name, or 0 if null.</returns>
		public override int GetHashCode()
		{
			return SceneName != null ? SceneName.GetHashCode() : 0;
		}

		/// <summary>
		/// Determines whether the specified object is equal to the current instance.
		/// Two AddressableSceneLoadData objects are considered equal if their scene names match (ordinal comparison).
		/// </summary>
		/// <param name="obj">The object to compare with the current instance.</param>
		/// <returns>True if the scene names are equal; otherwise, false.</returns>
		public override bool Equals(object obj)
		{
			if (obj is AddressableSceneLoadData other)
			{
				return string.Equals(SceneName, other.SceneName, StringComparison.Ordinal);
			}
			return false;
		}
	}
}