#if UNITY_EDITOR
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Shared.CustomBuildTool.Core
{
	/// <summary>
	/// Facade for the custom build process, coordinating configuration, execution, addressables, and logging.
	/// </summary>
	public class CustomBuildTool
	{
		private readonly IBuildConfigurator configurator;
		private readonly IBuildExecutor executor;
		private readonly ILinkerGenerator linkerGenerator;
		private readonly IAddressableManager addressableManager;

		private static bool isBuildInProgress = false;
		private static readonly object buildLock = new object();

		public CustomBuildTool(
			IBuildConfigurator configurator,
			IBuildExecutor executor,
			ILinkerGenerator linkerGenerator,
			IAddressableManager addressableManager)
		{
			this.configurator = configurator ?? throw new System.ArgumentNullException(nameof(configurator));
			this.executor = executor ?? throw new System.ArgumentNullException(nameof(executor));
			this.linkerGenerator = linkerGenerator ?? throw new System.ArgumentNullException(nameof(linkerGenerator));
			this.addressableManager = addressableManager ?? throw new System.ArgumentNullException(nameof(addressableManager));
		}

		/// <summary>
		/// Runs the full custom build process with proper error handling and cleanup.
		/// </summary>
		public void RunBuild(
			string linkerRootPath,
			string linkerDirectoryPath,
			string rootPath,
			string executableName,
			string[] bootstrapScenes,
			string[] excludedAddressableGroups,
			CustomBuildType customBuildType,
			BuildOptions buildOptions,
			StandaloneBuildSubtarget subTarget,
			BuildTarget buildTarget)
		{
			// Prevent concurrent builds
			lock (buildLock)
			{
				if (isBuildInProgress)
				{
					Log.Error("BuildTool", "A build is already in progress. Please wait for it to complete.");
					return;
				}
				isBuildInProgress = true;
			}

			try
			{
				Log.Debug("BuildLogger", "=== Build Process Started ===");

				Log.Debug("BuildLogger", "Configuring build...");
				configurator.Configure(subTarget, buildTarget);

				try
				{
					Log.Debug("BuildLogger", "Configuring addressables...");
					addressableManager.BuildAddressablesWithExclusions(excludedAddressableGroups);

					//Log.Debug("BuildLogger", "Generating linker file...");
					//linkerGenerator.GenerateLinker(linkerRootPath, linkerDirectoryPath);

					Log.Debug("BuildLogger", "Executing build...");
					executor.ExecuteBuild(rootPath, executableName, bootstrapScenes, customBuildType, buildOptions, subTarget, buildTarget);

					Log.Debug("BuildLogger", "=== Build Process Complete ===");
				}
				finally
				{
					// CRITICAL: Always restore settings, even if build fails
					Log.Debug("BuildLogger", "Restoring build configuration...");
					configurator.Restore();
				}
			}
			catch (System.Exception ex)
			{
				Log.Error("BuildTool", $"Build process failed with exception: {ex.Message}");
				Log.Error("BuildTool", $"Stack trace: {ex.StackTrace}");
				throw;
			}
			finally
			{
				lock (buildLock)
				{
					isBuildInProgress = false;
				}
			}
		}

		[MenuItem("FishMMO/Update Linker")]
		public static void UpdateLinker()
		{
			try
			{
				string current = Directory.GetCurrentDirectory();
				string assets = Path.Combine(current, "Assets");
				var linker = CustomBuildToolFactory.CreateLinkerGenerator();
				linker.GenerateLinker(assets, Path.Combine(assets, "Dependencies"));
			}
			catch (System.Exception ex)
			{
				Log.Error("BuildTool", $"Failed to update linker: {ex.Message}");
			}
		}

		[MenuItem("FishMMO/Build/Windows x64/Game Server")]
		public static void BuildWindows64GameServer()
		{
			BuildExecutable(
				GAMESERVER_BUILD_NAME,
				BOOTSTRAP_SCENES,
				CustomBuildType.Server,
				GetBuildOptions(),
				StandaloneBuildSubtarget.Server,
				BuildTarget.StandaloneWindows64);
		}

		[MenuItem("FishMMO/Build/Windows x64/Game Client")]
		public static void BuildWindows64Client()
		{
			BuildExecutable(
				Constants.Configuration.ProjectName,
				BOOTSTRAP_SCENES,
				CustomBuildType.Client,
				GetBuildOptions(),
				StandaloneBuildSubtarget.Player,
				BuildTarget.StandaloneWindows64);
		}

		[MenuItem("FishMMO/Build/Linux x64/Game Server")]
		public static void BuildLinux64GameServer()
		{
			BuildExecutable(
				GAMESERVER_BUILD_NAME,
				BOOTSTRAP_SCENES,
				CustomBuildType.Server,
				GetBuildOptions(),
				StandaloneBuildSubtarget.Server,
				BuildTarget.StandaloneLinux64);
		}

		[MenuItem("FishMMO/Build/Linux x64/Game Client")]
		public static void BuildLinux64Client()
		{
			BuildExecutable(
				Constants.Configuration.ProjectName,
				BOOTSTRAP_SCENES,
				CustomBuildType.Client,
				GetBuildOptions(),
				StandaloneBuildSubtarget.Player,
				BuildTarget.StandaloneLinux64);
		}

		[MenuItem("FishMMO/Build/WebGL/Game Client")]
		public static void BuildWebGLClient()
		{
			BuildExecutable(
				Constants.Configuration.ProjectName,
				BOOTSTRAP_SCENES,
				CustomBuildType.Client,
				GetBuildOptions(BuildTarget.WebGL),
				StandaloneBuildSubtarget.Player,
				BuildTarget.WebGL);
		}

		[MenuItem("FishMMO/Build/Windows x64/Addressables/Client Addressables")]
		public static void BuildWindowsClientAddressables()
		{
			BuildAddressablesWithExclusionsWrapper(serverAddressableGroups, enableCrcForRemoteLoading: false, useUnityWebRequestForLocal: false);
		}

		[MenuItem("FishMMO/Build/Windows x64/Addressables/Server Addressables")]
		public static void BuildWindowsServerAddressables()
		{
			BuildAddressablesWithExclusionsWrapper(clientAddressableGroups, enableCrcForRemoteLoading: false, useUnityWebRequestForLocal: false);
		}

		[MenuItem("FishMMO/Build/Linux x64/Addressables/Client Addressables")]
		public static void BuildLinuxClientAddressables()
		{
			BuildAddressablesWithExclusionsWrapper(serverAddressableGroups, enableCrcForRemoteLoading: false, useUnityWebRequestForLocal: false);
		}

		[MenuItem("FishMMO/Build/Linux x64/Addressables/Server Addressables")]
		public static void BuildLinuxServerAddressables()
		{
			BuildAddressablesWithExclusionsWrapper(clientAddressableGroups, enableCrcForRemoteLoading: false, useUnityWebRequestForLocal: false);
		}

		[MenuItem("FishMMO/Build/WebGL/Addressables/Client Addressables")]
		public static void BuildWebGLAddressables()
		{
			// WebGL downloads addressables from game servers over HTTP
			// Enable CRC to validate network transfer integrity
			// Use UnityWebRequest (required for browser security)
			BuildAddressablesWithExclusionsWrapper(serverAddressableGroups, enableCrcForRemoteLoading: true, useUnityWebRequestForLocal: true);
		}

		/// <summary>
		/// Helper method to build addressables with proper error handling and cleanup.
		/// </summary>
		/// <param name="excludeGroups">Array of group name substrings to exclude from the build.</param>
		/// <param name="enableCrcForRemoteLoading">If true, enables CRC checking for remote bundle loading (WebGL/CDN). If false, disables CRC for local StreamingAssets loading.</param>
		/// <param name="useUnityWebRequestForLocal">If true, uses UnityWebRequest for local bundles (WebGL requirement). If false, uses LoadFromFileAsync (better performance for Windows/Linux).</param>
		private static void BuildAddressablesWithExclusionsWrapper(string[] excludeGroups, bool enableCrcForRemoteLoading = false, bool useUnityWebRequestForLocal = false)
		{
			InitializeLogger();

			var configurator = CustomBuildToolFactory.CreateConfigurator();
			var addressableManager = CustomBuildToolFactory.CreateAddressableManager();

			try
			{
				// Get the current build target - addressables build for current platform
				StandaloneBuildSubtarget currentSubTarget = EditorUserBuildSettings.standaloneBuildSubtarget;
				BuildTarget currentTarget = EditorUserBuildSettings.activeBuildTarget;

				configurator.Configure(currentSubTarget, currentTarget);
				addressableManager.BuildAddressablesWithExclusions(excludeGroups, enableCrcForRemoteLoading, useUnityWebRequestForLocal);
			}
			catch (System.Exception ex)
			{
				Log.Error("BuildTool", $"Addressables build failed: {ex.Message}");
			}
			finally
			{
				configurator.Restore();
			}

			Log.Shutdown();
		}

		/// <summary>
		/// Builds the Windows x64 Database Installer executable for FishMMO.
		/// </summary>
		[MenuItem("FishMMO/Build/Windows x64/Database Installer")]
		public static void BuildWindows64Setup()
		{
			BuildExecutable("Installer",
							new string[]
							{
								Constants.Configuration.InstallerPath,
							},
							CustomBuildType.Installer,
							GetBuildOptions(),
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneWindows64);
		}

		/// <summary>
		/// Builds the Linux x64 Database Installer executable for FishMMO.
		/// </summary>
		[MenuItem("FishMMO/Build/Linux x64/Database Installer")]
		public static void BuildLinuxSetup()
		{
			BuildExecutable("Installer",
							new string[]
							{
								Constants.Configuration.InstallerPath,
							},
							CustomBuildType.Installer,
							GetBuildOptions(),
							StandaloneBuildSubtarget.Server,
							BuildTarget.StandaloneLinux64);
		}

		// --- Helper methods and fields (stubs, to be implemented or replaced as needed) ---

		/// <summary>
		/// The build name for the game server executable.
		/// </summary>
		private const string GAMESERVER_BUILD_NAME = "GameServer";
		/// <summary>
		/// Bootstrap scenes required for initial game startup.
		/// </summary>
		private static readonly string[] BOOTSTRAP_SCENES = new string[]
		{
			Constants.Configuration.BootstrapScenePath + "MainBootstrap.unity",
		};
		private static readonly string[] serverAddressableGroups = new string[] { "ServerOnly" };
		private static readonly string[] clientAddressableGroups = new string[] { "ClientOnly" };

		/// <summary>
		/// Initializes the FishMMO Logger for Editor build tools with all log levels enabled.
		/// </summary>
		private static void InitializeLogger()
		{
			try
			{
				// Initialize custom logging for the Editor build tool
				Log.RegisterLoggerFactory(nameof(UnityConsoleLoggerConfig), (cfg, logCallback) => new UnityConsoleLogger((UnityConsoleLoggerConfig)cfg, logCallback));

				var defaultUnityConsoleLoggerConfig = new UnityConsoleLoggerConfig();
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
					(message) => Debug.Log($"{message}")) // Direct Debug.Log for internal callback
				};

				Log.Initialize(null, unityConsoleFormatter, manualLoggers, Log.OnInternalLogMessage, new List<Type>() { typeof(UnityConsoleLoggerConfig) });

				Debug.Log("[BuildTool] Logger initialized successfully with all log levels enabled.");
			}
			catch (Exception ex)
			{
				Debug.LogError($"[BuildTool] Failed to initialize logger: {ex.Message}");
			}
		}

		private static void BuildExecutable(string executableName, string[] bootstrapScenes, CustomBuildType customBuildType, BuildOptions buildOptions, StandaloneBuildSubtarget subTarget, BuildTarget buildTarget)
		{
			InitializeLogger();

			var buildTool = CustomBuildToolFactory.Create();

			try
			{
				string current = Directory.GetCurrentDirectory();
				string assets = Path.Combine(current, "Assets");

				buildTool.RunBuild(
					linkerRootPath: assets,
					linkerDirectoryPath: Path.Combine(assets, "Dependencies"),
					rootPath: string.Empty,
					executableName: executableName,
					bootstrapScenes: bootstrapScenes,
					excludedAddressableGroups: customBuildType == CustomBuildType.Server ? clientAddressableGroups : serverAddressableGroups,
					customBuildType: customBuildType,
					buildOptions: buildOptions,
					subTarget: subTarget,
					buildTarget: buildTarget);
			}
			catch (System.Exception ex)
			{
				Log.Error("BuildTool", $"Build executable failed: {ex.Message}");
				EditorUtility.DisplayDialog("Build Failed", $"Build process failed:\n{ex.Message}", "OK");
			}
			finally
			{
				Log.Shutdown();
			}
		}

		/// <summary>
		/// Returns build options based on the current working environment and build target.
		/// </summary>
		/// <param name="buildTarget">Optional build target for environment-specific options.</param>
		/// <returns>BuildOptions flags for the build.</returns>
		private static BuildOptions GetBuildOptions(BuildTarget? buildTarget = null)
		{
			BuildOptions buildOptions = BuildOptions.CleanBuildCache | BuildOptions.ShowBuiltPlayer;

			WorkingEnvironmentState workingEnvironmentState = WorkingEnvironmentOptions.GetWorkingEnvironmentState();
			switch (workingEnvironmentState)
			{
				case WorkingEnvironmentState.Release:
					break;
				case WorkingEnvironmentState.Development:
					if (buildTarget != null && buildTarget == BuildTarget.WebGL)
					{
						break;
					}
					buildOptions |= BuildOptions.Development;
					break;
				default: break;
			}

			return buildOptions;
		}
	}
}
#endif