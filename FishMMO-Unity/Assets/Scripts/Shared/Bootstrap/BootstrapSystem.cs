using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Base class for bootstrap systems in FishMMO. Handles asset and scene loading, progress tracking, and initialization flow.
	/// </summary>
	public class BootstrapSystem : MonoBehaviour
	{
		/// <summary>
		/// Assets to preload in the editor before scene loading.
		/// </summary>
		public List<AddressableAssetKey> EditorPreloadAssets = new List<AddressableAssetKey>();
		/// <summary>
		/// Assets to load after scene loading in the editor.
		/// </summary>
		public List<AddressableAssetKey> EditorPostloadAssets = new List<AddressableAssetKey>();
		/// <summary>
		/// Scenes to preload in the editor.
		/// </summary>
		public List<AddressableSceneLoadData> EditorPreloadScenes = new List<AddressableSceneLoadData>();
		/// <summary>
		/// Scenes to load after preloading in the editor.
		/// </summary>
		public List<AddressableSceneLoadData> EditorPostloadScenes = new List<AddressableSceneLoadData>();

		/// <summary>
		/// Assets to preload in standalone builds.
		/// </summary>
		public List<AddressableAssetKey> PreloadAssets = new List<AddressableAssetKey>();
		/// <summary>
		/// Assets to load after scene loading in standalone builds.
		/// </summary>
		public List<AddressableAssetKey> PostloadAssets = new List<AddressableAssetKey>();
		/// <summary>
		/// Scenes to preload in standalone builds.
		/// </summary>
		public List<AddressableSceneLoadData> PreloadScenes = new List<AddressableSceneLoadData>();
		/// <summary>
		/// Scenes to load after preloading in standalone builds.
		/// </summary>
		public List<AddressableSceneLoadData> PostloadScenes = new List<AddressableSceneLoadData>();

		/// <summary>
		/// Assets to preload in WebGL builds.
		/// </summary>
		public List<AddressableAssetKey> WebGLPreloadAssets = new List<AddressableAssetKey>();
		/// <summary>
		/// Assets to load after scene loading in WebGL builds.
		/// </summary>
		public List<AddressableAssetKey> WebGLPostloadAssets = new List<AddressableAssetKey>();
		/// <summary>
		/// Scenes to preload in WebGL builds.
		/// </summary>
		public List<AddressableSceneLoadData> WebGLPreloadScenes = new List<AddressableSceneLoadData>();
		/// <summary>
		/// Scenes to load after preloading in WebGL builds.
		/// </summary>
		public List<AddressableSceneLoadData> WebGLPostloadScenes = new List<AddressableSceneLoadData>();

		/// <summary>
		/// Indicates whether the bootstrap process has started for this system.
		/// </summary>
		private bool hasStartedBootstrap = false;
		/// <summary>
		/// List of bootstrap systems that were preloaded by this system.
		/// </summary>
		private List<BootstrapSystem> preloadedBootstrapSystems = new List<BootstrapSystem>();

		/// <summary>
		/// Unity Awake message. Initializes logging callback.
		/// </summary>
		void Awake()
		{
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

		/// <summary>
		/// Callback for internal logging messages from FishMMO.Logging.Log.
		/// Ensures UnityLoggerBridge does not re-capture internal log calls.
		/// </summary>
		/// <param name="message">The log message.</param>
		private void OnInternalLogCallback(string message)
		{
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

		/// <summary>
		/// Called immediately after Preload Scenes are enqueued to the Load Processor.
		/// </summary>
		public virtual void OnPreload() { }

		/// <summary>
		/// Initializes the preload asset and scene loading process.
		/// </summary>
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
		/// Called after Preload is completed. Handles progress update and triggers postload.
		/// </summary>
		/// <param name="progress">The progress value (0.0 to 1.0).</param>
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

		/// <summary>
		/// Initializes the postload asset and scene loading process.
		/// </summary>
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
		/// Called after Postload is completed. Handles progress update and triggers completion processing.
		/// </summary>
		/// <param name="progress">The progress value (0.0 to 1.0).</param>
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

		/// <summary>
		/// Unity OnDestroy message. Handles cleanup when the object is destroyed.
		/// </summary>
		void OnDestroy()
		{
			OnDestroying();

			Log.OnInternalLogMessage = null;
		}

		/// <summary>
		/// Called when the object is being destroyed. Override for custom cleanup logic.
		/// </summary>
		public virtual void OnDestroying() { }

		/// <summary>
		/// Called after a scene is loaded to process bootstrap systems in the new scene.
		/// </summary>
		/// <param name="scene">The loaded scene.</param>
		public void OnBootstrapPostProcess(Scene scene)
		{
			preloadedBootstrapSystems = OnScenePostProcess(scene);
		}

		/// <summary>
		/// Finds all BootstrapSystem components in the root objects of the given scene.
		/// </summary>
		/// <param name="scene">The scene to search.</param>
		/// <returns>List of found BootstrapSystem components.</returns>
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