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
		/// connection exists, and its default is -1 (unlimited), so the launcher and login
		/// menus render as fast as the GPU allows and peg a CPU core to draw a static screen.
		/// <para>This is only the pre-configuration default. The options UI persists a
		/// "Frame Rate Limit" and a "VSync" preference and applies them when it loads —
		/// <c>UITKOptions.InitializeFrameRateLimit</c> calls
		/// <c>Client.ApplyTargetFrameRate</c>, and <c>UITKOptions.InitializeVSync</c> writes
		/// <c>QualitySettings.vSyncCount</c> — which replaces both values set here with the
		/// user's choice. The applied cap is bounded below by the network tick rate and above by
		/// the display's fastest mode.</para>
		/// <para>FishNet is deliberately <em>not</em> part of that chain any more. Its
		/// <c>NetworkManager.UpdateFramerate</c> overwrites <c>Application.targetFrameRate</c>
		/// from <c>ClientManager.FrameRate</c> on every connection-state change, so with
		/// <c>ChangeFrameRate</c> enabled the scene's value silently became a hard ceiling on
		/// what a player could render at. The client scene now ships with that flag off, leaving
		/// the render rate entirely to this default and the player's preference. Simulation is
		/// unaffected either way: gameplay runs on the fixed 30 Hz TimeManager tick.</para>
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
		/// <summary>
		/// Clears the shutdown latches at the start of every play session.
		/// </summary>
		/// <remarks>
		/// <see cref="isInitiatingShutdown"/> and <see cref="canQuitApplication"/> are static and
		/// are only ever set to true, so with Enter Play Mode Options disabling domain reload they
		/// survive into the next play session — and every teardown from the second session onward
		/// is skipped silently. <see cref="OnApplicationWantsToQuit"/> sees the latch already set
		/// and returns the stale <c>canQuitApplication</c>, so the editor quits immediately;
		/// <see cref="InitiateShutdown"/> early-returns, so <c>ReleaseAllAssets</c> never runs and
		/// the addressables stay held; and <see cref="OnDestroy"/>'s <c>!isInitiatingShutdown</c>
		/// fallback cannot fire either. Nothing logs an error, because from the code's point of
		/// view shutdown had already happened.
		/// <para>
		/// This project currently has <c>m_EnterPlayModeOptionsEnabled: 0</c>, so Unity reloads the
		/// domain and clears these itself — the bug is latent rather than live. It arms the moment
		/// anyone enables the option for faster iteration, which is exactly the setting a developer
		/// turns on without expecting teardown to change. <c>AddressableLoadProcessor</c> guards its
		/// own statics the same way and for the same reason.
		/// </para>
		/// </remarks>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStaticState()
		{
			isInitiatingShutdown = false;
			canQuitApplication = false;
		}

		/// <summary>
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

			// Perform Graphics Cleanup. Synchronous by design — see GraphicsCleanup.
			Debug.Log("[MainBootstrapSystem] Starting graphics cleanup...");
			GraphicsCleanup();
			Debug.Log("[MainBootstrapSystem] Graphics cleanup completed.");

			// Detach UnityLoggerBridge before async shutdown.
			UnityLoggerBridge.Shutdown();

#if UNITY_EDITOR
			// Editor-specific shutdown logic
			if (Log.IsInitialized)
			{
				Debug.Log("[MainBootstrapSystem] Editor shutdown: completing Log.Shutdown().");
				DrainOnTeardown(Log.Shutdown(), "Log.Shutdown");
				Debug.Log("[MainBootstrapSystem] Editor shutdown: Log system shut down.");
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
		/// Milliseconds a teardown task is given before the editor gives up waiting on it.
		/// </summary>
		private const int TeardownDrainTimeoutMs = 2000;

		/// <summary>
		/// Completes a teardown task from a callback that cannot yield, without risking a hang.
		/// </summary>
		/// <param name="task">The teardown task. May already be complete.</param>
		/// <param name="label">Name used in log output.</param>
		/// <remarks>
		/// Editor teardown runs from <c>playModeStateChanged/ExitingPlayMode</c>, which offers no
		/// way to defer the domain unload, so the work genuinely has to finish here — a
		/// fire-and-forget would let the loggers be torn down mid-flush. What it must NOT do is
		/// <c>Task.Wait()</c> with no bound, which is what it used to do:
		/// <list type="bullet">
		/// <item>the fast path is free — <see cref="Log.Shutdown"/> currently completes
		/// synchronously (its only <c>await</c> is <c>await Task.CompletedTask</c>), so the task
		/// arrives already completed and nothing blocks at all;</item>
		/// <item>if real asynchronous work is ever added behind that signature — the method's own
		/// comment invites it — an unbounded <c>Wait</c> on the main thread deadlocks outright the
		/// moment any continuation needs that same thread. A bounded wait cannot: it recovers,
		/// says so, and lets the editor exit.</item>
		/// </list>
		/// A timeout is a worse outcome than a clean flush and a better one than a frozen editor,
		/// which is the whole trade being made here. Exceptions are unwrapped and reported rather
		/// than surfacing later as an unobserved-task crash during domain reload.
		/// </remarks>
		private static void DrainOnTeardown(Task task, string label)
		{
			if (task == null)
			{
				return;
			}

			try
			{
				// Already finished: observe any fault and return without touching the scheduler.
				if (task.IsCompleted)
				{
					task.GetAwaiter().GetResult();
					return;
				}

				Debug.LogWarning($"[MainBootstrapSystem] {label} did not complete synchronously; " +
					$"waiting up to {TeardownDrainTimeoutMs}ms.");

				if (!task.Wait(TeardownDrainTimeoutMs))
				{
					Debug.LogError($"[MainBootstrapSystem] {label} did not finish within " +
						$"{TeardownDrainTimeoutMs}ms and was abandoned so the editor can exit. " +
						"Its remaining work — most likely a log flush — did not complete.");
				}
			}
			catch (Exception ex)
			{
				Debug.LogError($"[MainBootstrapSystem] {label} threw during teardown: {ex}");
			}
		}

		/// <summary>
		/// Releases every addressable handle the processor still holds.
		/// </summary>
		/// <remarks>
		/// Deliberately <c>void</c> and synchronous. <see cref="AddressableLoadProcessor.ReleaseAllAssets"/>
		/// is synchronous work, and the previous signature returned <see cref="Task.CompletedTask"/>
		/// only for the caller to immediately <c>.Wait()</c> on it — a main-thread block that did
		/// nothing today and would have become a deadlock the moment any real asynchronous work
		/// was added behind that signature. Returning a Task the caller must block on is the
		/// shape of the bug, so the shape is gone.
		/// </remarks>
		private void GraphicsCleanup()
		{
			AddressableLoadProcessor.ReleaseAllAssets();
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
			 *
			 * vSyncCount is forced to 0 first: Application.targetFrameRate is ignored entirely
			 * whenever the active QualitySettings level has vSync enabled, so the cap silently
			 * did nothing on any such level (the "Balanced" level ships with vSyncCount: 1).
			 * This only sets the boot-time default — the player's saved VSync preference is
			 * applied later by the options UI (UIOptions.OnStarting -> VSyncSettingOption.Load,
			 * UITKOptions.InitializeVSync), so nothing here overrides a user choice. */
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