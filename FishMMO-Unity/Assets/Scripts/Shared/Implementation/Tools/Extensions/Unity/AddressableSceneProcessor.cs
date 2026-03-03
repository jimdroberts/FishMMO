using FishNet.Managing.Scened;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Scene processor for loading and unloading Unity scenes using the Addressables system.
	/// Manages async operations and loaded scene tracking.
	/// </summary>
	public sealed class AddressableSceneProcessor : SceneProcessorBase
	{
		/// <summary>
		/// Maps scene handles to their async load operation handles.
		/// </summary>
		private readonly Dictionary<int, AsyncOperationHandle<SceneInstance>> _loadedScenesByHandle = new(4);

		/// <summary>
		/// List of currently loaded scenes.
		/// </summary>
		private readonly List<Scene> _loadedScenes = new(4);

		/// <summary>
		/// List of async operations for scenes currently loading.
		/// </summary>
		private readonly List<AsyncOperationHandle<SceneInstance>> _loadingAsyncOperations = new(4);

		/// <summary>
		/// The current async operation being processed (load or unload).
		/// </summary>
		private AsyncOperationHandle<SceneInstance> _currentAsyncOperation;

		/// <summary>
		/// The most recently loaded scene.
		/// </summary>
		private Scene _lastLoadedScene;

		/// <summary>
		/// Called at the start of a scene load queue. Resets processor state.
		/// </summary>
		/// <param name="queueData">The load queue data.</param>
		public override void LoadStart(LoadQueueData queueData)
		{
			ResetProcessor();
		}

		/// <summary>
		/// Called at the end of a scene load queue. Resets processor state.
		/// </summary>
		/// <param name="queueData">The load queue data.</param>
		public override void LoadEnd(LoadQueueData queueData)
		{
			ResetProcessor();
		}

		/// <summary>
		/// Resets async operation and loaded scene tracking for this processor.
		/// </summary>
		private void ResetProcessor()
		{
			_currentAsyncOperation = default;
			_lastLoadedScene = default;
			_loadingAsyncOperations.Clear();
		}

		/// <summary>
		/// Begins loading a scene asynchronously using Addressables.
		/// </summary>
		/// <param name="sceneName">The name of the scene to load.</param>
		/// <param name="parameters">Scene loading parameters.</param>
		public override void BeginLoadAsync(string sceneName, LoadSceneParameters parameters)
		{
			if (string.IsNullOrEmpty(sceneName))
			{
				Log.Error("AddressableSceneProcessor", "SceneName is null or empty!");
				return;
			}
			//Log.Warning($"AddressableSceneProcessor Loading Scene: {sceneName}");
			AsyncOperationHandle<SceneInstance> loadHandle = Addressables.LoadSceneAsync(sceneName, parameters, false);
			_loadingAsyncOperations.Add(loadHandle);
			_currentAsyncOperation = loadHandle;

			loadHandle.Completed += (op) =>
			{
				if (op.Status == AsyncOperationStatus.Succeeded)
				{
					//Log.Warning($"AddressableSceneProcessor Loaded scene: {_currentAsyncOperation.Result.Scene.name}|{_currentAsyncOperation.Result.Scene.handle}");
					AddLoadedScene(_currentAsyncOperation);
				}
				else
				{
					Log.Error("AddressableSceneProcessor", $"Failed to load scene: {sceneName}");
				}
			};
		}

		/// <summary>
		/// Begins unloading a scene asynchronously using Addressables.
		/// </summary>
		/// <param name="scene">The scene to unload.</param>
		public override void BeginUnloadAsync(Scene scene)
		{
			if (!_loadedScenesByHandle.TryGetValue(scene.handle, out var loadHandle))
			{
				Log.Error("AddressableSceneProcessor", "Trying to unload a non addressable scene.");
				return;
			}

			//Log.Warning($"AddressableSceneProcessor Unloading Scene: {scene.name}|{scene.handle}");
			AsyncOperationHandle<SceneInstance> unloadHandle = Addressables.UnloadSceneAsync(loadHandle, false);
			_currentAsyncOperation = unloadHandle;

			unloadHandle.Completed += (op) =>
			{
				if (op.Status == AsyncOperationStatus.Succeeded)
				{
					Scene unloadedScene = op.Result.Scene;

					//Log.Warning($"AddressableSceneProcessor Unloaded Scene: {unloadedScene.name}|{unloadedScene.handle}");

					_loadedScenes.Remove(unloadedScene);
					_loadedScenesByHandle.Remove(unloadedScene.handle);

					// Try to release the load handle if it's still valid for some reason.
					if (loadHandle.IsValid())
					{
						Addressables.Release(loadHandle);
					}
				}
				else
				{
					Log.Error("AddressableSceneProcessor", $"Failed to unload scene: {scene.name}");
				}
			};
		}

		/// <summary>
		/// Returns true if the current async operation is complete (percent >= 1.0).
		/// </summary>
		public override bool IsPercentComplete()
		{
			if (_currentAsyncOperation.IsValid())
			{
				if (GetPercentComplete() < 1.0f)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Gets the percent complete of the current async operation, or 1.0 if not valid.
		/// </summary>
		public override float GetPercentComplete()
		{
			return _currentAsyncOperation.IsValid() ? _currentAsyncOperation.PercentComplete : 1.0f;
		}

		/// <summary>
		/// Gets the most recently loaded scene.
		/// </summary>
		public override Scene GetLastLoadedScene() => _lastLoadedScene;

		/// <summary>
		/// Gets the list of currently loaded scenes.
		/// </summary>
		public override List<Scene> GetLoadedScenes() => _loadedScenes;

		/// <summary>
		/// Adds a loaded scene to tracking collections after a successful load.
		/// </summary>
		/// <param name="loadHandle">The async operation handle for the loaded scene.</param>
		public void AddLoadedScene(AsyncOperationHandle<SceneInstance> loadHandle)
		{
			Scene scene = loadHandle.Result.Scene;
			if (_loadedScenesByHandle.ContainsKey(scene.handle))
			{
				Log.Warning("AddressableSceneProcessor", "Already added scene with handle: " + scene.handle);
				return;
			}
			_lastLoadedScene = scene;
			_loadedScenes.Add(scene);
			_loadedScenesByHandle.Add(scene.handle, loadHandle);
		}

		/// <summary>
		/// Activates all loaded scenes asynchronously.
		/// </summary>
		public override void ActivateLoadedScenes()
		{
			foreach (var loadingAsyncOp in _loadingAsyncOperations)
			{
				loadingAsyncOp.Result.ActivateAsync();
			}
		}

		/// <summary>
		/// Coroutine that yields until all async scene load operations are done.
		/// </summary>
		public override IEnumerator AsyncsIsDone()
		{
			if (_loadingAsyncOperations == null || _loadingAsyncOperations.Count == 0)
			{
				yield break;
			}

			bool notDone = true;

			while (notDone)
			{
				notDone = false;

				foreach (AsyncOperationHandle<SceneInstance> ao in _loadingAsyncOperations)
				{
					if (!ao.IsDone)
					{
						notDone = true;
						break;
					}
				}

				yield return null;
			}
		}
	}
}