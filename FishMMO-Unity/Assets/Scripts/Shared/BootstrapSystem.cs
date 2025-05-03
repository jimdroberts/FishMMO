using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	public class BootstrapSystem : MonoBehaviour
	{
		public List<AddressableAssetKey> EditorPreloadAssets = new List<AddressableAssetKey>();
		public List<AddressableAssetKey> EditorPostloadAssets = new List<AddressableAssetKey>();
		public List<AddressableSceneLoadData> EditorPreloadScenes = new List<AddressableSceneLoadData>();
		public List<AddressableSceneLoadData> EditorPostloadScenes = new List<AddressableSceneLoadData>();

		public List<AddressableAssetKey> PreloadAssets = new List<AddressableAssetKey>();
		public List<AddressableAssetKey> PostloadAssets = new List<AddressableAssetKey>();
		public List<AddressableSceneLoadData> PreloadScenes = new List<AddressableSceneLoadData>();
		public List<AddressableSceneLoadData> PostloadScenes = new List<AddressableSceneLoadData>();

		public List<AddressableAssetKey> WebGLPreloadAssets = new List<AddressableAssetKey>();
		public List<AddressableAssetKey> WebGLPostloadAssets = new List<AddressableAssetKey>();
		public List<AddressableSceneLoadData> WebGLPreloadScenes = new List<AddressableSceneLoadData>();
		public List<AddressableSceneLoadData> WebGLPostloadScenes = new List<AddressableSceneLoadData>();

		void Awake()
		{
#if UNITY_EDITOR
			foreach (AddressableAssetKey assetKey in EditorPreloadAssets)
			{
				AddressableLoadProcessor.EnqueueLoad(assetKey.Keys, assetKey.MergeMode);
			}
			AddressableLoadProcessor.EnqueueLoad(EditorPreloadScenes);
#elif UNITY_WEBGL
			foreach (AddressableAssetKey assetKey in WebGLPreloadAssets)
			{
				AddressableLoadProcessor.EnqueueLoad(assetKey.Keys, assetKey.MergeMode);
			}
			AddressableLoadProcessor.EnqueueLoad(WebGLPreloadScenes);
#else
			foreach (AddressableAssetKey assetKey in PreloadAssets)
			{
				AddressableLoadProcessor.EnqueueLoad(assetKey.Keys, assetKey.MergeMode);
			}
			AddressableLoadProcessor.EnqueueLoad(PreloadScenes);
#endif
			OnPreload();
			try
			{
				//Debug.Log($"{gameObject.scene.name} Preload Start");
				AddressableLoadProcessor.OnProgressUpdate += AddressableLoadProcessor_OnPreloadProgressUpdate;
				AddressableLoadProcessor.BeginProcessQueue();
			}
			catch (UnityException ex)
			{
				Debug.LogError($"Failed to load preload scenes: {ex.Message}");
			}
		}

		/// <summary>
		/// Called immediately after PreloadScenes are enqueued to the Load Processor.
		/// </summary>
		public virtual void OnPreload() { }

		/// <summary>
		/// Called after Preload is completed.
		/// </summary>
		private void AddressableLoadProcessor_OnPreloadProgressUpdate(float progress)
		{
			// Wait for the Addressable Load Processor queue to complete loading.
			if (progress < 1.0f)
			{
				return;
			}

			//Debug.Log($"{gameObject.scene.name} Preload Completed");

			// Preload has completed.
			AddressableLoadProcessor.OnProgressUpdate -= AddressableLoadProcessor_OnPreloadProgressUpdate;

#if UNITY_EDITOR
			foreach (AddressableAssetKey assetKey in EditorPostloadAssets)
			{
				AddressableLoadProcessor.EnqueueLoad(assetKey.Keys, assetKey.MergeMode);
			}
			AddressableLoadProcessor.EnqueueLoad(EditorPostloadScenes);
#elif UNITY_WEBGL
			foreach (AddressableAssetKey assetKey in WebGLPostloadAssets)
			{
				AddressableLoadProcessor.EnqueueLoad(assetKey.Keys, assetKey.MergeMode);
			}
			AddressableLoadProcessor.EnqueueLoad(WebGLPostloadScenes);
#else
			foreach (AddressableAssetKey assetKey in PostloadAssets)
			{
				AddressableLoadProcessor.EnqueueLoad(assetKey.Keys, assetKey.MergeMode);
			}
			AddressableLoadProcessor.EnqueueLoad(PostloadScenes);
#endif
			OnPostLoad();
			try
			{
				//Debug.Log($"{gameObject.scene.name} Postload Start");

				AddressableLoadProcessor.OnProgressUpdate += AddressableLoadProcessor_OnPostloadProgressUpdate;
				AddressableLoadProcessor.BeginProcessQueue();
			}
			catch (UnityException ex)
			{
				Debug.LogError($"Failed to load postload scenes: {ex.Message}");
			}
		}

		/// <summary>
		/// Called immediately after Postload Scenes are enqueued to the Load Processor.
		/// </summary>
		public virtual void OnPostLoad() { }

		/// <summary>
		/// Called after Postload is completed.
		/// </summary>
		public void AddressableLoadProcessor_OnPostloadProgressUpdate(float progress)
		{
			if (progress < 1.0f)
			{
				return;
			}
			// Postload has completed.
			AddressableLoadProcessor.OnProgressUpdate -= AddressableLoadProcessor_OnPostloadProgressUpdate;

			//Debug.Log($"{gameObject.scene.name} Postload Complete");
			OnCompleteProcessing();
		}

		public virtual void OnCompleteProcessing() { }

		void OnDestroy()
		{
			OnDestroying();
		}

		/// <summary>
		/// Called immediately before the Load Processor releases all assets.
		/// </summary>
		public virtual void OnDestroying() { }
	}
}