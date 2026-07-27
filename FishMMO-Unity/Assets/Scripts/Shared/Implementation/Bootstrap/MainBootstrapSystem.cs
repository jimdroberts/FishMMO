using System.Collections.Generic;
using UnityEngine;
using FishMMO.Logging;
using System;
using System.IO;
using System.Threading.Tasks;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Transporting.Multipass;
using FishNet.Transporting.WebTransport;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishMMO.Shared
{
	/// <summary>
	/// Main bootstrap system for FishMMO. Handles initialization, logging, version management, and graceful shutdown.
	/// </summary>
	public class MainBootstrapSystem : BootstrapSystem
	{
		/// <summary>
		/// The current game version string. Set during initialization.
		/// </summary>
		public static string GameVersion = "UNKNOWN_VERSION";

		/// <summary>
		/// The name of the logging configuration JSON file (e.g., logging.json).
		/// </summary>
		[Tooltip("The name of the logging configuration JSON file (e.g., logging.json).")]
		public string configFileName = "logging.json";

		/// <summary>
		/// Reference to the VersionConfig asset.
		/// </summary>
		[SerializeField]
		private VersionConfig versionConfig;

		/// <summary>
		/// Indicates if shutdown is currently being initiated.
		/// </summary>
		private static bool isInitiatingShutdown = false;

		/// <summary>
		/// Controls if Application.wantsToQuit should allow quitting.
		/// </summary>
		private static bool canQuitApplication = false;

		/// <summary>
		/// Unity Awake message. Starts the bootstrap initialization chain.
		/// </summary>
		void Awake()
		{
			StartBootstrap();
		}

		/// <summary>
		/// Callback for internal logging messages from FishMMO.Logging.Log.
		/// Ensures UnityLoggerBridge does not re-capture internal log calls.
		/// This is a local copy required because the base class method is private
		/// and this instance is passed as a delegate to Log.Initialize.
		/// </summary>
		/// <param name="message">The log message.</param>
		private void OnInternalLogCallback(string message)
		{
			UnityLoggerBridge.IsLoggingInternally = true;
			Debug.Log($"{message}");
			UnityLoggerBridge.IsLoggingInternally = false;
		}

#if UNITY_EDITOR
		/// <summary>
		/// Handles changes in Unity Editor's Play Mode state.
		/// </summary>
		/// <param name="state">The play mode state change event.</param>
		private void OnEditorPlayModeStateChanged(PlayModeStateChange state)
		{
			if (state == PlayModeStateChange.ExitingPlayMode)
			{
				Debug.Log("[MainBootstrapSystem] Editor exiting Play Mode — force network stop (no blocking Wait).");
				// Critical: kill WebTransport/FishNet BEFORE Unity tears down native plugins.
				// Blocking Log.Shutdown().Wait() / Addressables.Release during exit caused
				// wantsToQuit loops and native mono/msquic crashes while still connected.
				// Do NOT Release Addressables handles here — Addressables.PlayModeStateChangedCleanup
				// owns that and double-release throws "invalid operation handle".
				ForceStopNetworkingForEditorExit();
				try { UnityLoggerBridge.Shutdown(); } catch { /* ignore */ }
				isInitiatingShutdown = true;
				canQuitApplication = true;
			}
			else if (state == PlayModeStateChange.EnteredPlayMode)
			{
				// Fresh play session — reset static quit flags from last exit.
				isInitiatingShutdown = false;
				canQuitApplication = false;
			}
		}

		/// <summary>
		/// Stops all FishNet client/server sockets and WebTransport clients without deinit'ing
		/// the native library (library stays loaded for the next Play session).
		/// </summary>
		private static void ForceStopNetworkingForEditorExit()
		{
			try
			{
				foreach (var nm in UnityEngine.Object.FindObjectsByType<NetworkManager>(FindObjectsSortMode.None))
				{
					if (nm == null) continue;
					try
					{
						if (nm.ClientManager != null && nm.ClientManager.Started)
							nm.ClientManager.StopConnection();
					}
					catch { /* best effort */ }
					try
					{
						if (nm.ServerManager != null && nm.ServerManager.Started)
							nm.ServerManager.StopConnection(true);
					}
					catch { /* best effort */ }

					try
					{
						Transport t = nm.TransportManager != null ? nm.TransportManager.Transport : null;
						if (t is Multipass mp)
						{
							foreach (Transport nested in mp.Transports)
							{
								if (nested is WebTransport nestedWt)
									nestedWt.ForceStopClient();
							}
						}
						else if (t is WebTransport wt)
						{
							wt.ForceStopClient();
						}
					}
					catch { /* best effort */ }
				}

				// Any WebTransport components not reached via NetworkManager.
				foreach (var wt in UnityEngine.Object.FindObjectsByType<WebTransport>(FindObjectsSortMode.None))
				{
					try { wt.ForceStopClient(); } catch { /* best effort */ }
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[MainBootstrapSystem] ForceStopNetworkingForEditorExit: {ex.Message}");
			}
		}
#endif

		/// <summary>
		/// Unity message called before the application quits. Allows delaying quit for async cleanup.
		/// </summary>
		/// <returns>True if the application should quit, false to defer quitting.</returns>
		private bool OnApplicationWantsToQuit()
		{
#if UNITY_EDITOR
			// Play Mode Stop uses wantsToQuit in some Unity versions. Never block/defer here —
			// ExitingPlayMode already force-stopped networking. Returning false re-entered a
			// quit loop and crashed native code.
			if (!canQuitApplication)
			{
				ForceStopNetworkingForEditorExit();
				canQuitApplication = true;
				isInitiatingShutdown = true;
			}
			return true;
#else
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
#endif
		}

		/// <summary>
		/// Initiates the shutdown process, including graphics cleanup and logging system shutdown.
		/// </summary>
		public void InitiateShutdown()
		{
			Debug.Log("[MainBootstrapSystem] InitiateShutdown called.");
			if (isInitiatingShutdown)
			{
				Debug.Log("[MainBootstrapSystem] InitiateShutdown already in progress. Returning.");
				return;
			}
			isInitiatingShutdown = true;

#if UNITY_EDITOR
			// Editor: never .Wait() on the main thread during Play Mode exit.
			ForceStopNetworkingForEditorExit();
			try { UnityLoggerBridge.Shutdown(); } catch { /* ignore */ }
			canQuitApplication = true;
			return;
#else
			// Perform Graphics Cleanup.
			Debug.Log("[MainBootstrapSystem] Starting graphics cleanup...");
			GraphicsCleanup().Wait();
			Debug.Log("[MainBootstrapSystem] Graphics cleanup completed.");

			// Detach UnityLoggerBridge before async shutdown.
			UnityLoggerBridge.Shutdown();

			// For standalone builds or runtime quits, perform asynchronous shutdown.
			Debug.Log("[MainBootstrapSystem] Standalone: Performing async shutdown.");
			_ = PerformAsyncShutdown();
#endif
		}

		/// <summary>
		/// Performs asynchronous cleanup tasks before the application quits.
		/// </summary>
		/// <returns>A Task representing the async shutdown process.</returns>
		private async Task PerformAsyncShutdown()
		{
			Debug.Log("[MainBootstrapSystem] PerformAsyncShutdown started.");

			try
			{
				// Step 1: Save logging configuration.
				Debug.Log("[MainBootstrapSystem] Attempting to save logging configuration...");
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

				// Step 2: Shut down the logging system.
				if (Log.IsInitialized)
				{
					Debug.Log("[MainBootstrapSystem] Awaiting Log.Shutdown()...");
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
				Debug.LogError($"[MainBootstrapSystem] An error occurred during async shutdown: {ex.Message}\n{ex.StackTrace}");
			}
			finally
			{
				canQuitApplication = true;
				Debug.Log("[MainBootstrapSystem] Finalizing shutdown.");
				Application.Quit();
			}
		}

		/// <summary>
		/// Placeholder for actual graphics cleanup logic. Typically does nothing for dedicated server builds.
		/// </summary>
		/// <returns>A completed Task.</returns>
		private Task GraphicsCleanup()
		{
			AddressableLoadProcessor.ReleaseAllAssets();
			return Task.CompletedTask;
		}

		/// <summary>
		/// Initializes the logging system and other bootstrap components.
		/// Loads version info and configures initial scene loading.
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

			GameVersion = versionConfig?.FullVersion ?? "UNKNOWN";

			Debug.Log($"[MainBootstrapSystem] Loaded GameVersion: {GameVersion}");

#if UNITY_EDITOR
			EditorApplication.playModeStateChanged += OnEditorPlayModeStateChanged;
#endif
			Application.wantsToQuit += OnApplicationWantsToQuit;

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
				IConsoleFormatter unityConsoleFormatter = null;
				List<FishMMO.Logging.ILogger> manualLoggers = null;
#endif

				Log.Initialize(configFilePath, unityConsoleFormatter, manualLoggers, OnInternalLogCallback, new List<Type>() { typeof(UnityConsoleLoggerConfig) });

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

		/// <summary>
		/// Unity OnDestroy message. Handles shutdown and cleanup when the object is destroyed.
		/// </summary>
		void OnDestroy()
		{
#if UNITY_EDITOR
			EditorApplication.playModeStateChanged -= OnEditorPlayModeStateChanged;
#endif
			Application.wantsToQuit -= OnApplicationWantsToQuit;

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