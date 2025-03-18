using FishNet.Managing.Scened;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace Scenes
{
	public sealed class AddressableSceneProcessor : SceneProcessorBase
	{
		private readonly Dictionary<int, AsyncOperationHandle<SceneInstance>> _loadedScenesByHandle = new(4);
		private readonly List<Scene> _loadedScenes = new(4);
		private readonly List<AsyncOperationHandle<SceneInstance>> _loadingAsyncOperations = new(4);
		private AsyncOperationHandle<SceneInstance> _currentAsyncOperation;
		private Scene _lastLoadedScene;

		public override void LoadStart(LoadQueueData queueData)
		{
			ResetProcessor();
		}

		public override void LoadEnd(LoadQueueData queueData)
		{
			ResetProcessor();
		}

		private void ResetProcessor()
		{
			_currentAsyncOperation = default;
			_lastLoadedScene = default;
			_loadingAsyncOperations.Clear();
		}

		public override void BeginLoadAsync(string sceneName, LoadSceneParameters parameters)
		{
			if (string.IsNullOrEmpty(sceneName))
			{
				Debug.LogError("SceneName is null or empty!");
				return;
			}
			//Debug.LogWarning($"AddressableSceneProcessor Loading Scene: {sceneName}");
			AsyncOperationHandle<SceneInstance> loadHandle = Addressables.LoadSceneAsync(sceneName, parameters, false);
			_loadingAsyncOperations.Add(loadHandle);
			_currentAsyncOperation = loadHandle;

			loadHandle.Completed += (op) =>
			{
				if (op.Status == AsyncOperationStatus.Succeeded)
				{
					//Debug.LogWarning($"AddressableSceneProcessor Loaded scene: {_currentAsyncOperation.Result.Scene.name}|{_currentAsyncOperation.Result.Scene.handle}");
					AddLoadedScene(_currentAsyncOperation);
				}
				else
				{
					Debug.LogError($"Failed to load scene: {sceneName}");
				}
			};
		}

		public override void BeginUnloadAsync(Scene scene)
		{
			if (!_loadedScenesByHandle.TryGetValue(scene.handle, out var loadHandle))
			{
				Debug.LogError("Trying to unload a non addressable scene.");
				return;
			}

			//Debug.LogWarning($"AddressableSceneProcessor Unloading Scene: {scene.name}|{scene.handle}");
			AsyncOperationHandle<SceneInstance> unloadHandle = Addressables.UnloadSceneAsync(loadHandle, false);
			_currentAsyncOperation = unloadHandle;

			unloadHandle.Completed += (op) =>
			{
				if (op.Status == AsyncOperationStatus.Succeeded)
				{
					Scene unloadedScene = op.Result.Scene;

					//Debug.LogWarning($"AddressableSceneProcessor Unloaded Scene: {unloadedScene.name}|{unloadedScene.handle}");

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
					Debug.LogError($"Failed to unload scene: {scene.name}");
				}
			};
		}

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

		public override float GetPercentComplete()
		{
			return _currentAsyncOperation.IsValid() ? _currentAsyncOperation.PercentComplete : 1.0f;
		}

		public override Scene GetLastLoadedScene() => _lastLoadedScene;

		public override List<Scene> GetLoadedScenes() => _loadedScenes;

		public void AddLoadedScene(AsyncOperationHandle<SceneInstance> loadHandle)
		{
			Scene scene = loadHandle.Result.Scene;
			if (_loadedScenesByHandle.ContainsKey(scene.handle))
			{
				Debug.LogWarning("Already added scene with handle: " + scene.handle);
				return;
			}
			_lastLoadedScene = scene;
			_loadedScenes.Add(scene);
			_loadedScenesByHandle.Add(scene.handle, loadHandle);
		}

		public override void ActivateLoadedScenes()
		{
			foreach (var loadingAsyncOp in _loadingAsyncOperations)
			{
				loadingAsyncOp.Result.ActivateAsync();
			}
		}

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