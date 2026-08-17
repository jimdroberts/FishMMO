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
		/// Frame rate the client is capped to during bootstrap, launcher, and the
		/// login/character-select screens.
		/// </summary>
		/// <remarks>
		/// Nothing sets <see cref="Application.targetFrameRate"/> before a network
		/// connection exists, and its default is -1 (unlimited). The default quality
		/// level also has vSync off, so the launcher and login menus render as fast as
		/// the GPU allows and peg a CPU core to draw a static screen.
		/// <para>This is only the pre-configuration default. The options UI persists a
		/// "Refresh Rate" preference, and <c>RefreshRateSettingOption</c> /
		/// <c>UITKOptions</c> call <c>Client.ApplyTargetFrameRate</c> when it loads, which
		/// replaces this value with the user's choice. Failing that, FishNet's
		/// <c>NetworkManager.UpdateFramerate</c> raises it from
		/// <c>ClientManager.FrameRate</c> when the client connects.</para>
		/// </remarks>
		private const int BootstrapTargetFrameRate = 60;

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
		/// <remarks>
		/// Overrides rather than hides the base. Declaring a second <c>void Awake()</c>
		/// shadowed the base implementation, so the base never ran for this system.
		/// </remarks>
		protected override void Awake()
		{
			base.Awake();

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
			// When exiting Play Mode, initiate shutdown.
			if (state == PlayModeStateChange.ExitingPlayMode)
			{
				Debug.Log("[MainBootstrapSystem] Editor exiting Play Mode. Initiating shutdown...");
				InitiateShutdown();
			}
		}
#endif

		/// <summary>
		/// Unity message called before the application quits. Allows delaying quit for async cleanup.
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

			// Perform Graphics Cleanup.
			Debug.Log("[MainBootstrapSystem] Starting graphics cleanup...");
			GraphicsCleanup().Wait();
			Debug.Log("[MainBootstrapSystem] Graphics cleanup completed.");

			// Detach UnityLoggerBridge before async shutdown.
			UnityLoggerBridge.Shutdown();

#if UNITY_EDITOR
			// Editor-specific shutdown logic
			if (Log.IsInitialized)
			{
				Debug.Log("[MainBootstrapSystem] Editor shutdown: Awaiting synchronous Log.Shutdown().");
				Log.Shutdown().Wait();
				Debug.Log("[MainBootstrapSystem] Editor shutdown: Log system synchronously shut down.");
			}
			else
			{
				Debug.Log("[MainBootstrapSystem] Editor shutdown: Log manager not initialized or already shut down. Skipping synchronous Log.Shutdown().");
			}
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

			/* A missing VersionConfig used to return here. That aborted the rest of
			 * OnPreload — including the EnqueueLoad of the first scene at the bottom of
			 * this method — which left the load queue empty. BeginProcessQueue then
			 * immediately reported 100%, postload found nothing, and OnCompleteProcessing
			 * had no bootstrap systems to start: the client sat on a black screen forever
			 * with nothing but a console error nobody could see.
			 *
			 * Carry on with a sentinel version instead. Boot completes, the launcher comes
			 * up, and its version check reports the bad version to the player in the UI. */
			if (versionConfig == null)
			{
				Debug.LogError("[MainBootstrapSystem] Failed to initialize Version Config. Continuing boot with an unknown version; the launcher will report this to the player.");
			}

			string workingDir = Constants.GetWorkingDirectory();

#if !UNITY_SERVER
			/* Cap the client before anything renders. See BootstrapTargetFrameRate:
			 * without this the launcher and login screens run uncapped.
			 * Headless servers are excluded — they do not render, and FishNet
			 * derives the server frame rate from the tick rate instead.
			 * vSyncCount is forced to 0 alongside it: Application.targetFrameRate is ignored
			 * entirely whenever the active QualitySettings level has vSync enabled, so the
			 * cap silently did nothing on quality levels that ship with it on. */
			QualitySettings.vSyncCount = 0;
			Application.targetFrameRate = BootstrapTargetFrameRate;
			Debug.Log($"[MainBootstrapSystem] Client frame rate capped to {BootstrapTargetFrameRate} for bootstrap and menus (FishNet raises it on connect).");
#endif

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
		protected override void OnDestroy()
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

			// Base last: it releases the internal-log hook, and the shutdown above still
			// wants that routing available while it runs.
			base.OnDestroy();
		}
	}
}