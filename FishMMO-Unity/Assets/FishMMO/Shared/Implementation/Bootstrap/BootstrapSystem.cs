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
		/// Guards <see cref="OnCompletePreload"/> against running more than once.
		/// </summary>
		private bool hasCompletedPreload = false;
		/// <summary>
		/// Guards <see cref="OnCompleteProcessing"/> against running more than once.
		/// </summary>
		private bool hasCompletedPostload = false;
		/// <summary>
		/// Bootstrap systems discovered in the scenes this system loaded.
		/// </summary>
		/// <remarks>
		/// Accumulated across every scene this system loads. It used to be reassigned per
		/// scene, so a system that loaded more than one scene containing bootstraps started
		/// only the last one's, and a postload scene's bootstraps silently displaced the
		/// preload scene's before either had been started.
		/// </remarks>
		private readonly List<BootstrapSystem> preloadedBootstrapSystems = new List<BootstrapSystem>();

		/// <summary>
		/// Unity Awake message. Initializes logging callback.
		/// </summary>
		/// <remarks>
		/// Virtual so derived systems extend rather than hide it. A derived
		/// <c>void Awake()</c> that merely shadows this one silently suppresses it —
		/// Unity dispatches only the most-derived member — which is how
		/// <see cref="OnDestroying"/> came to never run for <c>MainBootstrapSystem</c>.
		/// </remarks>
		protected virtual void Awake()
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
			// Iterate a copy: StartBootstrap can load further scenes whose post-process
			// appends to this list while we are walking it.
			BootstrapSystem[] discovered = preloadedBootstrapSystems.ToArray();
			foreach (BootstrapSystem bootstrapSystem in discovered)
			{
				if (bootstrapSystem != null)
				{
					bootstrapSystem.StartBootstrap();
				}
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

				/* Completion comes from this system's own batch, not from the processor's
				 * global progress event. That event is shared by every bootstrap system,
				 * both loading screens and the world-scene handshake, so it reported "done"
				 * to all of them whenever any queue drained — and a handler that
				 * resubscribed during dispatch could make another subscriber run twice,
				 * double-invoking OnCompletePreload. A batch tracks only what this call
				 * enqueued and fires exactly once. */
				AddressableLoadBatch batch = AddressableLoadProcessor.BeginProcessQueue();
				batch.Completed += OnPreloadBatchCompleted;
			}
			catch (UnityException ex)
			{
				Log.Error("BootstrapSystem", "Failed to load preload scenes...", ex);
			}
		}

		/// <summary>
		/// Called when this system's preload batch finishes. Triggers postload.
		/// </summary>
		/// <param name="batch">The completed preload batch.</param>
		private void OnPreloadBatchCompleted(AddressableLoadBatch batch)
		{
			if (hasCompletedPreload)
			{
				return;
			}
			hasCompletedPreload = true;

			if (batch != null && batch.HasFailures)
			{
				// Continue regardless: refusing to advance would strand the client on a
				// black screen with no UI. The failures are already logged by name.
				Log.Error("BootstrapSystem", $"{gameObject.scene.name} preload finished with {batch.FailedItems.Count} failed item(s). Continuing bootstrap.");
			}

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

				AddressableLoadBatch batch = AddressableLoadProcessor.BeginProcessQueue();
				batch.Completed += OnPostloadBatchCompleted;
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
		/// Called when this system's postload batch finishes. Triggers completion processing.
		/// </summary>
		/// <param name="batch">The completed postload batch.</param>
		private void OnPostloadBatchCompleted(AddressableLoadBatch batch)
		{
			if (hasCompletedPostload)
			{
				return;
			}
			hasCompletedPostload = true;

			if (batch != null && batch.HasFailures)
			{
				Log.Error("BootstrapSystem", $"{gameObject.scene.name} postload finished with {batch.FailedItems.Count} failed item(s). Continuing bootstrap.");
			}

			Log.Debug("BootstrapSystem", $"{gameObject.scene.name} Postload Complete");
			OnCompleteProcessing();
		}

		/// <summary>
		/// Unity OnDestroy message. Handles cleanup when the object is destroyed.
		/// </summary>
		/// <remarks>
		/// Only clears <see cref="Log.OnInternalLogMessage"/> if this instance is the
		/// current owner. That hook is a single-cast static assigned by every bootstrap
		/// system's <see cref="Awake"/>, so several are alive at once and the last to wake
		/// owns it. Clearing unconditionally meant the first system destroyed during
		/// teardown silenced internal logging for every system still shutting down —
		/// exactly when those diagnostics matter most.
		/// </remarks>
		protected virtual void OnDestroy()
		{
			OnDestroying();

			if (Log.OnInternalLogMessage == OnInternalLogCallback)
			{
				Log.OnInternalLogMessage = null;
			}
		}

		/// <summary>
		/// Called when the object is being destroyed. Override for custom cleanup logic.
		/// </summary>
		public virtual void OnDestroying() { }

		/// <summary>
		/// Called after a scene is loaded to collect bootstrap systems in the new scene.
		/// </summary>
		/// <param name="scene">The loaded scene.</param>
		public void OnBootstrapPostProcess(Scene scene)
		{
			CollectBootstrapSystems(scene);
		}

		/// <summary>
		/// Appends every BootstrapSystem found in the given scene to
		/// <see cref="preloadedBootstrapSystems"/>, skipping duplicates and self.
		/// </summary>
		/// <param name="scene">The scene to search.</param>
		private void CollectBootstrapSystems(Scene scene)
		{
			if (!scene.IsValid() || !scene.isLoaded)
			{
				Log.Warning("BootstrapSystem", $"Post-process skipped for an invalid or unloaded scene ('{scene.name}').");
				return;
			}

			foreach (GameObject rootObject in scene.GetRootGameObjects())
			{
				// Includes inactive children: a bootstrap system parented under a disabled
				// container would otherwise never be started.
				BootstrapSystem[] bootstraps = rootObject.GetComponentsInChildren<BootstrapSystem>(true);
				foreach (BootstrapSystem bootstrap in bootstraps)
				{
					if (bootstrap == null || bootstrap == this || preloadedBootstrapSystems.Contains(bootstrap))
					{
						continue;
					}
					preloadedBootstrapSystems.Add(bootstrap);
				}
			}
		}
	}
}