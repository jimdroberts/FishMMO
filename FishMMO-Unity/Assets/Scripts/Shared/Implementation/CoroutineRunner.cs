using UnityEngine;
using System.Collections;

namespace FishMMO.Shared
{
	/// <summary>Minimal MonoBehaviour for running coroutines from non-MonoBehaviour classes.</summary>
	public class CoroutineRunner : MonoBehaviour
	{
		private static CoroutineRunner instance;
		/// <summary>Starts a coroutine on a persistent hidden GameObject (created on first call).</summary>
		/// <param name="routine">The coroutine to start.</param>
		public static void Start(IEnumerator routine)
		{
			if (routine == null) return;
			if (instance == null)
			{
				var go = new GameObject("CoroutineRunner") { hideFlags = HideFlags.HideAndDontSave };
				DontDestroyOnLoad(go);
				instance = go.AddComponent<CoroutineRunner>();
			}
			instance.StartCoroutine(routine);
		}

		/// <summary>Stops a coroutine previously started via <see cref="Start"/>.</summary>
		/// <param name="routine">The coroutine to stop. No-op if null or if the CoroutineRunner instance does not exist.</param>
		public static void Stop(IEnumerator routine)
		{
			if (routine == null) return;
			if (instance != null)
				instance.StopCoroutine(routine);
		}
	}
}
