using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	[Serializable]
	public class AddressableSceneLoadData
	{
		public string SceneName;
		[Tooltip("Sets the scene active when it finishes loaded. Blocks AddressableAsync loading when false.")]
		public bool ActivateOnLoad;
		public LoadSceneMode LoadSceneMode;
		public Action<Scene> OnSceneLoaded;

		public AddressableSceneLoadData(string sceneName, Action<Scene> onSceneLoaded = null, LoadSceneMode loadSceneMode = LoadSceneMode.Additive, bool activateOnLoad = true)
		{
			SceneName = sceneName;
			OnSceneLoaded = onSceneLoaded;
			LoadSceneMode = loadSceneMode;
			ActivateOnLoad = activateOnLoad;
		}

		public override int GetHashCode()
		{
			return SceneName != null ? SceneName.GetHashCode() : 0;
		}

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