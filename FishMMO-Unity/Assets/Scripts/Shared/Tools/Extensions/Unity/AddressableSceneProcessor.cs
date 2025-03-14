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
		private AsyncOperation _currentBasicAsyncOperation;
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
					//Debug.LogWarning($"AddressableSceneProcessor Loaded scene: {op.Result.Scene.name}|{op.Result.Scene.handle}");
					_lastLoadedScene = op.Result.Scene;
				}
				else
				{
					Debug.LogError($"Failed to unload scene: {sceneName}");
				}
			};
		}

		public override void BeginUnloadAsync(Scene scene)
		{
			if (_loadedScenesByHandle.TryGetValue(scene.handle, out var loadHandle))
			{
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
			else
			{
				_currentBasicAsyncOperation = UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
			}
		}

		public override bool IsPercentComplete()
		{
			bool completed;

			if (_currentBasicAsyncOperation != null)
			{
				completed = GetPercentComplete() >= 1.0f;
				if (completed)
				{
					_currentBasicAsyncOperation = null;
				}
				return completed;
			}
			else if (_currentAsyncOperation.IsValid())
			{
				completed = _currentAsyncOperation.IsDone;
				if (completed)
				{
					AddLoadedScene(_currentAsyncOperation);
				}
				return completed;
			}

			//Debug.LogError("Something went wrong, no valid async operation found.", this);
			return false;
		}

		public override float GetPercentComplete()
		{
			float percent = 0f;

			if (_currentBasicAsyncOperation != null)
			{
				percent = _currentBasicAsyncOperation.progress;
			}
			else if (_currentAsyncOperation.IsValid())
			{
				percent = _currentAsyncOperation.PercentComplete;
			}

			return percent;
		}

		public override List<Scene> GetLoadedScenes() => _loadedScenes;

		public void AddLoadedScene(AsyncOperationHandle<SceneInstance> loadHandle)
		{
			Scene scene = loadHandle.Result.Scene;
			if (_loadedScenesByHandle.ContainsKey(scene.handle))
			{
				Debug.LogWarning("Already added scene with handle: " + scene.handle);
				return;
			}

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
						//Debug.Log($"Scene '{ao.Result.Scene.name}' is still loading...");
						break;
					}
				}

				yield return null;
			}

			//Debug.Log("All async operations are complete.");
		}
	}
}