using System.Collections.Generic;
using UnityEngine;
using FishMMO.Logging;
using System;
using System.IO;
using System.Threading.Tasks;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishMMO.Shared
{
	public class MainBootstrapSystem : BootstrapSystem
	{
		public static string GameVersion = "UNKNOWN_VERSION";

		[Tooltip("The name of the logging configuration JSON file (e.g., logging.json).")]
		public string configFileName = "logging.json";

		[SerializeField]
		private VersionConfig versionConfig;
		private static bool isInitiatingShutdown = false;
		private static bool canQuitApplication = false; // Controls if Application.wantsToQuit should allow quitting

		void Awake()
		{
			// Starts the Bootstrap initialization chain.
			StartBootstrap();
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

#if UNITY_EDITOR
		/// <summary>
		/// Handles changes in Unity Editor's Play Mode state.
		/// </summary>
		private void OnEditorPlayModeStateChanged(PlayModeStateChange state)
		{
			// When exiting Play Mode, initiate shutdown.
			if (state == PlayModeStateChange.ExitingPlayMode)
			{
				Debug.Log("[MainBootstrapSystem] Editor exiting Play Mode. Initiating shutdown...");
				InitiateShutdown();
			}
		}
#endif

		/// <summary>
		/// This Unity message is called before the application quits.
		/// It allows us to delay quitting to perform asynchronous cleanup.
		/// </summary>
		/// <returns>True if the application should quit, false to defer quitting.</returns>
		private bool OnApplicationWantsToQuit()
		{
			Debug.Log("[MainBootstrapSystem] OnApplicationWantsToQuit called. (isInitiatingShutdown: " + isInitiatingShutdown + ", canQuitApplication: " + canQuitApplication + ")");
			if (isInitiatingShutdown)
			{
				// If shutdown is already initiated, allow quit if we've completed our async tasks.
				return canQuitApplication;
			}

			// Initiate shutdown and defer quitting until async tasks are complete.
			Debug.Log("[MainBootstrapSystem] Application wants to quit. Delaying for asynchronous cleanup...");
			InitiateShutdown();
			return false; // Defer quitting
		}

		/// <summary>
		/// Initiates the shutdown process.
		/// </summary>
		public void InitiateShutdown()
		{
			Debug.Log("[MainBootstrapSystem] InitiateShutdown called.");
			if (isInitiatingShutdown)
			{
				// Already initiating shutdown, prevent redundant calls.
				Debug.Log("[MainBootstrapSystem] InitiateShutdown already in progress. Returning.");
				return;
			}
			isInitiatingShutdown = true;

			// Before performing asynchronous shutdown, detach our UnityLoggerBridge
			// so that any Debug.Log calls during the async shutdown (e.g., from
			// Log.SaveConfig or Log.Shutdown's internal messages) go directly to Unity's console
			// and do not try to re-route through a potentially shutting-down Log manager.
			UnityLoggerBridge.Shutdown();

#if UNITY_EDITOR
			// Editor-specific shutdown logic
			if (Log.IsInitialized)
			{
				// Synchronously wait for Log shutdown in editor to prevent race conditions.
				// Note: Log.Shutdown() internally prints its own 'shutdown complete' message
				// which might show up as 'CRITICAL' because it logs after clearing 'loggers'.
				// This is an internal behavior of the Log class.
				Debug.Log("[MainBootstrapSystem] Editor shutdown: Awaiting synchronous Log.Shutdown().");
				Log.Shutdown().Wait();
				Debug.Log("[MainBootstrapSystem] Editor shutdown: Log system synchronously shut down.");
			}
			else
			{
				Debug.Log("[MainBootstrapSystem] Editor shutdown: Log manager not initialized or already shut down. Skipping synchronous Log.Shutdown().");
			}
			// Set canQuitApplication immediately for editor to allow quick exit
			canQuitApplication = true;
			Debug.Log("[MainBootstrapSystem] Editor shutdown: Setting canQuitApplication = true.");
			return;
#else
			// For standalone builds or runtime quits, perform asynchronous shutdown.
			Debug.Log("[MainBootstrapSystem] Standalone: Performing async shutdown.");
			_ = PerformAsyncShutdown();
#endif
		}

		/// <summary>
		/// Performs asynchronous cleanup tasks before the application quits.
		/// </summary>
		private async Task PerformAsyncShutdown()
		{
			Debug.Log("[MainBootstrapSystem] PerformAsyncShutdown started.");

			try
			{
				// Step 1: Perform Graphics Cleanup.
				// This is critical for preventing "Releasing render texture" errors.
				// For a dedicated server build, there typically should be no active cameras
				// or render textures. If you still see "Releasing render texture" warnings,
				// it suggests some unexpected graphics context or resource is being created.
				// Implement specific logic here if you identify such resources.
				Debug.Log("[MainBootstrapSystem] Starting graphics cleanup...");
				await GraphicsCleanup();
				Debug.Log("[MainBootstrapSystem] Graphics cleanup completed.");

				// Step 2: Save logging configuration.
				// Only save config for standalone builds during shutdown
				Debug.Log("[MainBootstrapSystem] Attempting to save logging configuration...");
				// IMPORTANT: Log.CurrentLoggingConfig might be null if Log.Initialize failed
				// or if the Log system's internal state was reset prematurely by Log.Shutdown().
				// We add a null check here to prevent "Attempted to save a null LoggingConfig" error.
				LoggingConfig currentConfig = Log.CurrentLoggingConfig;
				if (currentConfig != null)
				{
					string configFilePath = Path.Combine(Constants.GetWorkingDirectory(), configFileName);
					Debug.Log($"[MainBootstrapSystem] Saving logging configuration to {configFilePath}...");
					await Log.SaveConfig(currentConfig, configFilePath);
					Debug.Log("[MainBootstrapSystem] Logging configuration saved.");
				}
				else
				{
					Debug.LogWarning("[MainBootstrapSystem] Skipping logging configuration save because Log.CurrentLoggingConfig is null.");
				}
				Debug.Log("[MainBootstrapSystem] Finished attempting to save logging configuration.");

				// Step 3: Shut down the logging system.
				if (Log.IsInitialized)
				{
					Debug.Log("[MainBootstrapSystem] Awaiting Log.Shutdown()...");
					// Calling Log.Shutdown() here will internally cause Log.cs to print
					// "CRITICAL: Log manager not initialized or shut down" messages
					// due to its internal logging of "shutdown complete" after clearing loggers.
					// This is a known behavior of Log.cs and does not prevent application quit.
					await Log.Shutdown();
					Debug.Log("[MainBootstrapSystem] Log.Shutdown() completed.");
				}
				else
				{
					Debug.Log("[MainBootstrapSystem] Log manager was not initialized or already shut down. Skipping Log.Shutdown().");
				}

				Debug.Log("[MainBootstrapSystem] All asynchronous shutdown tasks completed.");
			}
			catch (Exception ex)
			{
				// Log any unexpected errors during shutdown.
				// Since UnityLoggerBridge is shut down, this Debug.LogError goes directly to Unity's console.
				Debug.LogError($"[MainBootstrapSystem] An error occurred during async shutdown: {ex.Message}\n{ex.StackTrace}");
			}
			finally
			{
				// Always ensure the application can quit after cleanup attempts.
				canQuitApplication = true;
				Debug.Log("[MainBootstrapSystem] Finalizing shutdown.");
				Application.Quit(); // Re-call Application.Quit() to finalize the deferred quit process.
			}
		}

		/// <summary>
		/// Placeholder for actual graphics cleanup logic.
		/// For a dedicated server build, this method typically does nothing as there are no cameras or visual rendering.
		/// However, if "Releasing render texture" warnings persist, you might need to investigate
		/// if any code is creating RenderTextures (e.g., for compute shaders, image processing)
		/// and explicitly release them here.
		/// </summary>
		private async Task GraphicsCleanup()
		{
			Debug.Log("[MainBootstrapSystem] GraphicsCleanup method executed for server build.");
			await Task.Yield(); // Simulate asynchronous work if needed.
		}

		/// <summary>
		/// Initializes the logging system and other bootstrap components.
		/// </summary>
		public override void OnPreload()
		{
			Debug.Log("[MainBootstrapSystem] Initializing...");

			if (versionConfig == null)
			{
				Debug.LogError("[MainBootstrapSystem] FATAL ERROR: Failed to initialize Version Config.");
				return;
			}

			string workingDir = Constants.GetWorkingDirectory();

#if !UNITY_EDITOR
			string versionFilePath = Path.Combine(workingDir, "version.txt");

			if (File.Exists(versionFilePath))
			{
				try
				{
					string versionText = File.ReadAllText(versionFilePath).Trim();
					VersionConfig loadedVersionConfig = VersionConfig.Parse(versionText);

					if (loadedVersionConfig != null)
					{
						Debug.Log($"[MainBootstrapSystem] Loaded VersionConfig from version.txt: {versionConfig.FullVersion}");

						if (versionConfig != null && versionConfig != loadedVersionConfig)
						{
							Debug.LogError($"Version mismatch between asset and version.txt: {versionConfig.FullVersion} vs {loadedVersionConfig.FullVersion}");
						}
					}
					else
					{
						Debug.LogError("[MainBootstrapSystem] Failed to parse version.txt content into VersionConfig.");
					}
				}
				catch (System.Exception ex)
				{
					Debug.LogError($"[MainBootstrapSystem] Exception reading or parsing version.txt: {ex}");
				}
			}
			else
			{
				Debug.LogError($"[MainBootstrapSystem] version.txt not found in working directory: {workingDir}");
			}
#endif
			GameVersion = versionConfig?.FullVersion ?? "UNKNOWN";

			Debug.Log($"[MainBootstrapSystem] Loaded GameVersion: {GameVersion}");

#if UNITY_EDITOR
			EditorApplication.playModeStateChanged += OnEditorPlayModeStateChanged;
#endif
			Application.wantsToQuit += OnApplicationWantsToQuit;

			Log.OnInternalLogMessage += OnInternalLogCallback;

			string configFilePath = Path.Combine(workingDir, configFileName);

			try
			{
				Log.RegisterLoggerFactory(nameof(UnityConsoleLoggerConfig), (cfg, logCallback) => new UnityConsoleLogger((UnityConsoleLoggerConfig)cfg, logCallback));

				var defaultUnityConsoleLoggerConfig = new UnityConsoleLoggerConfig();

#if UNITY_EDITOR
				var unityConsoleFormatter = new UnityConsoleFormatter(defaultUnityConsoleLoggerConfig.LogLevelColors, true);

				var manualLoggers = new List<FishMMO.Logging.ILogger>
				{
					new UnityConsoleLogger(new UnityConsoleLoggerConfig
					{
						Enabled = true,
						AllowedLevels = new HashSet<LogLevel>
						{
							LogLevel.Info, LogLevel.Debug, LogLevel.Warning, LogLevel.Error, LogLevel.Critical, LogLevel.Verbose
						}
					},
					OnInternalLogCallback),
				};
#else
				// For standalone builds, UnityConsoleFormatter and manual loggers are typically managed by the config file
				IConsoleFormatter unityConsoleFormatter = null;
				List<FishMMO.Logging.ILogger> manualLoggers = null;
#endif

				// Initialize the Log manager. It will attempt to load config from file first,
				// then use manual loggers if provided.
				Log.Initialize(configFilePath, unityConsoleFormatter, manualLoggers, Log.OnInternalLogMessage, new List<Type>() { typeof(UnityConsoleLoggerConfig) });


				Debug.Log("[MainBootstrapSystem] Logging system initialized successfully.");
			}
			catch (Exception ex)
			{
				Debug.LogError($"[MainBootstrapSystem] FATAL ERROR: Failed to initialize logging system: {ex.Message}\n{ex.StackTrace}");
			}

			Log.Info("MainBootstrapSystem", $"Logging system initialized. Config path: {configFilePath}");

#if UNITY_SERVER
#region Server
			List<AddressableSceneLoadData> initialScenes = new List<AddressableSceneLoadData>()
			{
				new AddressableSceneLoadData("ServerLauncher", OnBootstrapPostProcess),
			};
#endregion
#else
			#region Client
			List<AddressableSceneLoadData> initialScenes = new List<AddressableSceneLoadData>()
			{
				new AddressableSceneLoadData("ClientPreboot", OnBootstrapPostProcess),
			};
			#endregion
#endif
			AddressableLoadProcessor.EnqueueLoad(initialScenes);
		}

		void OnDestroy()
		{
#if UNITY_EDITOR
			EditorApplication.playModeStateChanged -= OnEditorPlayModeStateChanged;
#endif
			Application.wantsToQuit -= OnApplicationWantsToQuit;
			Log.OnInternalLogMessage -= OnInternalLogCallback;

			if (!isInitiatingShutdown && Application.isPlaying)
			{
				Debug.Log("[MainBootstrapSystem] OnDestroy called outside of normal shutdown. Initiating graceful shutdown...");
				InitiateShutdown();
			}
			else
			{
				Debug.Log("[MainBootstrapSystem] OnDestroy called. (isInitiatingShutdown: " + isInitiatingShutdown + ", canQuitApplication: " + canQuitApplication + ")");
			}
		}
	}
}