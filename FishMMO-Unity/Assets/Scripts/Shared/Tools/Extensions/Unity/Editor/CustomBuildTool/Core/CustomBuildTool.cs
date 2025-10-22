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

		/// <summary>
		/// Builds the game executable using the current Build Environment Options (Build Type and OS Target).
		/// </summary>
		[MenuItem("FishMMO/Build/Build Game")]
		public static void BuildGameWithEnvironmentOptions()
		{
			// Check if scripts are currently compiling
			if (BuildEnvironmentOptions.IsCompiling())
			{
				UnityEngine.Debug.LogWarning("[CustomBuildTool] Cannot start build while scripts are compiling. Please wait for compilation to finish.");
				EditorUtility.DisplayDialog("Build Blocked", "Scripts are currently compiling.\nPlease wait for compilation to finish before building.", "OK");
				return;
			}

			// Get build settings from environment options
			BuildTypeEnvironment buildType = BuildEnvironmentOptions.GetBuildType();
			OSTargetEnvironment osTarget = BuildEnvironmentOptions.GetOSTarget();
			BuildTarget buildTarget = BuildEnvironmentOptions.GetBuildTarget(osTarget);
			StandaloneBuildSubtarget buildSubtarget = BuildEnvironmentOptions.GetBuildSubtarget(buildType);
			CustomBuildType customBuildType = BuildEnvironmentOptions.GetCustomBuildType();

			// Determine executable name based on build type
			string executableName = (buildType == BuildTypeEnvironment.Server)
				? GAMESERVER_BUILD_NAME
				: Constants.Configuration.ProjectName;

			// Build with environment settings
			BuildExecutable(
				executableName,
				BOOTSTRAP_SCENES,
				customBuildType,
				GetBuildOptions(buildTarget),
				buildSubtarget,
				buildTarget);
		}

		/// <summary>
		/// Builds addressables using the current Build Environment Options (Build Type and OS Target).
		/// </summary>
		[MenuItem("FishMMO/Build/Build Addressables")]
		public static void BuildAddressablesWithEnvironmentOptions()
		{
			// Check if scripts are currently compiling
			if (BuildEnvironmentOptions.IsCompiling())
			{
				UnityEngine.Debug.LogWarning("[CustomBuildTool] Cannot start addressables build while scripts are compiling. Please wait for compilation to finish.");
				EditorUtility.DisplayDialog("Build Blocked", "Scripts are currently compiling.\nPlease wait for compilation to finish before building addressables.", "OK");
				return;
			}

			// Get build settings from environment options
			BuildTypeEnvironment buildType = BuildEnvironmentOptions.GetBuildType();
			OSTargetEnvironment osTarget = BuildEnvironmentOptions.GetOSTarget();

			// Determine which groups to exclude based on build type
			string[] excludedGroups = (buildType == BuildTypeEnvironment.Server)
				? clientAddressableGroups
				: serverAddressableGroups;

			// Determine if we need special settings for WebGL
			bool enableCrc = (osTarget == OSTargetEnvironment.WebGL);
			bool useUnityWebRequest = (osTarget == OSTargetEnvironment.WebGL);

			BuildAddressablesWithExclusionsWrapper(excludedGroups, enableCrc, useUnityWebRequest);
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
		/// Builds the Database Installer executable using the current OS Target environment option.
		/// </summary>
		[MenuItem("FishMMO/Build/Build Database Installer")]
		public static void BuildInstallerWithEnvironmentOptions()
		{
			// Check if scripts are currently compiling
			if (BuildEnvironmentOptions.IsCompiling())
			{
				UnityEngine.Debug.LogWarning("[CustomBuildTool] Cannot start installer build while scripts are compiling. Please wait for compilation to finish.");
				EditorUtility.DisplayDialog("Build Blocked", "Scripts are currently compiling.\nPlease wait for compilation to finish before building installer.", "OK");
				return;
			}

			// Get OS target from environment options
			OSTargetEnvironment osTarget = BuildEnvironmentOptions.GetOSTarget();
			BuildTarget buildTarget = BuildEnvironmentOptions.GetBuildTarget(osTarget);

			// WebGL doesn't support installers
			if (osTarget == OSTargetEnvironment.WebGL)
			{
				UnityEngine.Debug.LogWarning("[CustomBuildTool] Database Installer cannot be built for WebGL. Please select Windows or Linux.");
				EditorUtility.DisplayDialog("Invalid Target", "Database Installer cannot be built for WebGL.\nPlease select Windows or Linux as the OS Target.", "OK");
				return;
			}

			BuildExecutable("Installer",
							new string[]
							{
								Constants.Configuration.InstallerPath,
							},
							CustomBuildType.Installer,
							GetBuildOptions(),
							StandaloneBuildSubtarget.Server,
							buildTarget);
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