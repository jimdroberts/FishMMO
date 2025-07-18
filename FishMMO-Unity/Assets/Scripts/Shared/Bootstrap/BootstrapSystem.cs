using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Logging;

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

		private bool hasStartedBootstrap = false;
		private List<BootstrapSystem> preloadedBootstrapSystems = new List<BootstrapSystem>();

		void Awake()
		{
			// Logging is still safe to initialize here, as it's typically a global setup
			Log.OnInternalLogMessage = OnInternalLogCallback;
		}

		/// <summary>
		/// Initiates the asset and scene loading process for this bootstrap system.
		/// This should be called explicitly, typically by a previous BootstrapSystem
		/// after a scene has been loaded.
		/// </summary>
		public virtual void StartBootstrap()
		{
			if (hasStartedBootstrap)
			{
				Log.Warning("BootstrapSystem", $"{gameObject.scene.name} BootstrapSystem tried to start multiple times. Ignoring.");
				return;
			}
			hasStartedBootstrap = true;

			Log.Debug("BootstrapSystem", $"{gameObject.scene.name} BootstrapSystem Start");

			InitializePreload();
		}

		private void OnInternalLogCallback(string message)
		{
			// This callback is used by FishMMO.Logging.Log for its internal messages.
			// We set IsLoggingInternally to true to prevent UnityLoggerBridge from re-capturing
			// Debug.Log calls made by our own logging system or internal processes.
			// This ensures that these messages go directly to Unity's console without re-routing.
			UnityLoggerBridge.IsLoggingInternally = true;
			Debug.Log($"{message}");
			UnityLoggerBridge.IsLoggingInternally = false;
		}

		/// <summary>
		/// Called immediately after the BootstrapSystem has completed its full loading process (Preload & Postload).
		/// This is the ideal place to trigger the loading of the next scene in a sequential flow.
		/// </summary>
		public virtual void OnCompleteProcessing()
		{
			Log.Debug("BootstrapSystem", $"{gameObject.scene.name} OnCompleteProcessing");

			// Start any Bootstrap systems in the scenes that were loaded by this BootstrapSystem.
			foreach (BootstrapSystem bootstrapSystem in preloadedBootstrapSystems)
			{
				bootstrapSystem.StartBootstrap();
			}
		}

		/// <summary>
		/// Called when Preload is completed.
		/// </summary>
		public virtual void OnCompletePreload()
		{
			Log.Debug("BootstrapSystem", $"{gameObject.scene.name} OnCompletePreload");
			InitializePostload();
		}

		// Called immediately after Preload Scenes are enqueued to the Load Processor.
		public virtual void OnPreload() { }

		public void InitializePreload()
		{
#if UNITY_EDITOR
			foreach (AddressableAssetKey assetKey in EditorPreloadAssets)
			{
				AddressableLoadProcessor.EnqueueLoad(assetKey.Keys, assetKey.MergeMode);
			}
			AddressableLoadProcessor.EnqueueLoad(EditorPreloadScenes, OnBootstrapPostProcess);
#elif UNITY_WEBGL
            foreach (AddressableAssetKey assetKey in WebGLPreloadAssets)
            {
                AddressableLoadProcessor.EnqueueLoad(assetKey.Keys, assetKey.MergeMode);
            }
            AddressableLoadProcessor.EnqueueLoad(WebGLPreloadScenes, OnBootstrapPostProcess);
#else
            foreach (AddressableAssetKey assetKey in PreloadAssets)
            {
                AddressableLoadProcessor.EnqueueLoad(assetKey.Keys, assetKey.MergeMode);
            }
            AddressableLoadProcessor.EnqueueLoad(PreloadScenes, OnBootstrapPostProcess);
#endif
			OnPreload();
			try
			{
				Log.Debug("BootstrapSystem", $"{gameObject.scene.name} Preload Start");

				AddressableLoadProcessor.OnProgressUpdate += AddressableLoadProcessor_OnPreloadProgressUpdate;
				AddressableLoadProcessor.BeginProcessQueue(); // This will start processing the global queue
			}
			catch (UnityException ex)
			{
				Log.Error("BootstrapSystem", "Failed to load preload scenes...", ex);
			}
		}

		/// <summary>
		/// Called after Preload is completed.
		/// </summary>
		public void AddressableLoadProcessor_OnPreloadProgressUpdate(float progress)
		{
			if (progress < 1.0f)
			{
				return;
			}
			// Preload has completed.
			AddressableLoadProcessor.OnProgressUpdate -= AddressableLoadProcessor_OnPreloadProgressUpdate;

			Log.Debug("BootstrapSystem", $"{gameObject.scene.name} Preload Complete");
			OnCompletePreload();
		}

		public void InitializePostload()
		{
#if UNITY_EDITOR
			foreach (AddressableAssetKey assetKey in EditorPostloadAssets)
			{
				AddressableLoadProcessor.EnqueueLoad(assetKey.Keys, assetKey.MergeMode);
			}
			AddressableLoadProcessor.EnqueueLoad(EditorPostloadScenes, OnBootstrapPostProcess);
#elif UNITY_WEBGL
            foreach (AddressableAssetKey assetKey in WebGLPostloadAssets)
            {
                AddressableLoadProcessor.EnqueueLoad(assetKey.Keys, assetKey.MergeMode);
            }
            AddressableLoadProcessor.EnqueueLoad(WebGLPostloadScenes, OnBootstrapPostProcess);
#else
            foreach (AddressableAssetKey assetKey in PostloadAssets)
            {
                AddressableLoadProcessor.EnqueueLoad(assetKey.Keys, assetKey.MergeMode);
            }
            AddressableLoadProcessor.EnqueueLoad(PostloadScenes, OnBootstrapPostProcess);
#endif
			OnPostLoad();
			try
			{
				Log.Debug("BootstrapSystem", $"{gameObject.scene.name} Postload Start");

				AddressableLoadProcessor.OnProgressUpdate += AddressableLoadProcessor_OnPostloadProgressUpdate;
				AddressableLoadProcessor.BeginProcessQueue();
			}
			catch (UnityException ex)
			{
				Log.Error("BootstrapSystem", "Failed to load postload scenes...", ex);
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

			Log.Debug("BootstrapSystem", $"{gameObject.scene.name} Postload Complete");
			OnCompleteProcessing();
		}

		void OnDestroy()
		{
			OnDestroying();

			Log.OnInternalLogMessage = null;
		}

		public virtual void OnDestroying() { }

		public void OnBootstrapPostProcess(Scene scene)
		{
			preloadedBootstrapSystems = OnScenePostProcess(scene);
		}

		private List<BootstrapSystem> OnScenePostProcess(Scene scene)
		{
			List<BootstrapSystem> preloadedBootstrapSystems = new List<BootstrapSystem>();
			foreach (GameObject rootObject in scene.GetRootGameObjects())
			{
				BootstrapSystem[] bootstraps = rootObject.GetComponentsInChildren<BootstrapSystem>();
				foreach (BootstrapSystem bootstrap in bootstraps)
				{
					preloadedBootstrapSystems.Add(bootstrap);
				}
			}
			return preloadedBootstrapSystems;
		}
	}
}