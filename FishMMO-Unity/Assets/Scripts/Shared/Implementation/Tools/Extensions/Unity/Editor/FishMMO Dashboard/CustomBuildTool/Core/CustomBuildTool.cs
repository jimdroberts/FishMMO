#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
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

		/// <summary>CustomBuildTool method.</summary>
		/// <returns>The result of the operation.</returns>
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
					// SBP cannot build asset bundles under the Server subtarget.
					// Temporarily switch to Player for the addressable build, then restore
					// the requested subtarget for the player build.
					if (subTarget == StandaloneBuildSubtarget.Server)
					{
						EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
					}

					Log.Debug("BuildLogger", "Configuring addressables...");
					bool isWebGL = buildTarget == BuildTarget.WebGL;
					addressableManager.BuildAddressablesWithExclusions(excludedAddressableGroups, isWebGL, isWebGL);

					// Restore the actual subtarget for the player build
					EditorUserBuildSettings.standaloneBuildSubtarget = subTarget;

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

		/// <summary>Updates the linker XML file by scanning project assemblies.</summary>
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
		/// <param name="rootPath">
		/// Output directory to build into. When null/empty, BuildExecutor falls back to an
		/// interactive folder picker (EditorUtility.SaveFolderPanel), which does not work in
		/// -batchmode. CLI callers must supply this explicitly.
		/// </param>
		public static void BuildGameWithEnvironmentOptions(string rootPath = null)
		{
			// Check if scripts are currently compiling
			if (BuildEnvironmentOptions.IsCompiling())
			{
				UnityEngine.Debug.LogWarning("[CustomBuildTool] Cannot start build while scripts are compiling. Please wait for compilation to finish.");
				if (CanShowDialog())
				{
					EditorUtility.DisplayDialog("Build Blocked", "Scripts are currently compiling.\nPlease wait for compilation to finish before building.", "OK");
				}
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
				buildTarget,
				rootPath);
		}

		/// <summary>
		/// Builds addressables using the current Build Environment Options (Build Type and OS Target).
		/// </summary>
		public static void BuildAddressablesWithEnvironmentOptions()
		{
			// Check if scripts are currently compiling
			if (BuildEnvironmentOptions.IsCompiling())
			{
				UnityEngine.Debug.LogWarning("[CustomBuildTool] Cannot start addressables build while scripts are compiling. Please wait for compilation to finish.");
				if (CanShowDialog())
				{
					EditorUtility.DisplayDialog("Build Blocked", "Scripts are currently compiling.\nPlease wait for compilation to finish before building addressables.", "OK");
				}
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

			BuildTarget buildTarget = BuildEnvironmentOptions.GetBuildTarget(osTarget);
			BuildAddressablesWithExclusionsWrapper(excludedGroups, buildTarget, enableCrc, useUnityWebRequest);
		}

		/// <summary>
		/// Helper method to build addressables with proper error handling and cleanup.
		/// Asset bundles are always built for the Player subtarget — bundles are platform-specific
		/// but not subtarget-specific, so Server vs Player distinction is irrelevant for content.
		/// </summary>
		/// <param name="excludeGroups">Array of group name substrings to exclude from the build.</param>
		/// <param name="buildTarget">The build target platform (e.g. StandaloneLinux64, WebGL).</param>
		/// <param name="enableCrcForRemoteLoading">If true, enables CRC checking for remote bundle loading (WebGL/CDN). If false, disables CRC for local StreamingAssets loading.</param>
		/// <param name="useUnityWebRequestForLocal">If true, uses UnityWebRequest for local bundles (WebGL requirement). If false, uses LoadFromFileAsync (better performance for Windows/Linux).</param>
		private static void BuildAddressablesWithExclusionsWrapper(string[] excludeGroups, BuildTarget buildTarget, bool enableCrcForRemoteLoading = false, bool useUnityWebRequestForLocal = false)
		{
			InitializeLogger();

			var configurator = CustomBuildToolFactory.CreateConfigurator();
			var addressableManager = CustomBuildToolFactory.CreateAddressableManager();

			try
			{
				// Always use Player subtarget for addressable builds. Asset bundles contain
				// assets, not code — the Server/Player distinction doesn't affect bundle content.
				// Building with Server subtarget causes SBP failures.
				configurator.Configure(StandaloneBuildSubtarget.Player, buildTarget);
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
		private static readonly string[] serverAddressableGroups = new string[] { "Server" };
		private static readonly string[] clientAddressableGroups = new string[] { "Client" };

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

		private static void BuildExecutable(string executableName, string[] bootstrapScenes, CustomBuildType customBuildType, BuildOptions buildOptions, StandaloneBuildSubtarget subTarget, BuildTarget buildTarget, string rootPath = null)
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
					rootPath: rootPath ?? string.Empty,
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
				if (CanShowDialog())
				{
					EditorUtility.DisplayDialog("Build Failed", $"Build process failed:\n{ex.Message}", "OK");
				}
				// In batch mode, surface non-zero exit so CI / installer detects the failure.
				if (Application.isBatchMode)
				{
					EditorApplication.Exit(1);
				}
			}
			finally
			{
				Log.Shutdown();
			}
		}

		/// <summary>
		/// Returns true when it is safe to open a modal EditorUtility dialog.
		/// Suppresses dialogs in -batchmode / CI / headless invocations to avoid
		/// HeadlessException or indefinite hangs waiting for user input.
		/// </summary>
		private static bool CanShowDialog()
		{
			if (Application.isBatchMode) return false;
			if (!InternalEditorUtility.isHumanControllingUs) return false;
			return true;
		}

		// =====================================================================
		// CLI entry points invoked by the FishMMO-Installer via:
		//   Unity -batchmode -nographics -projectPath <FishMMO-Unity>
		//         -executeMethod FishMMO.Shared.CustomBuildTool.Core.CustomBuildTool.<Method>
		//         -fishmmoOSTarget <Windows|Linux|WebGL>
		//         -quit -logFile -
		// Each method parses CLI args, sets EditorPrefs, and dispatches to the
		// existing pref-driven build entry points. Build type is implied by the
		// CLI entry name (Client / Server). Addressables CLI honours the
		// -fishmmoBuildType arg if supplied so server-only / client-only bundles
		// can be generated from CI.
		// =====================================================================

		/// <summary>CLI entry: build the Client player. OS target read from -fishmmoOSTarget arg.</summary>
		public static void BuildClientCLI()
		{
			RunCliBuild(BuildTypeEnvironment.Client, includeAddressables: false, addressablesOnly: false);
		}

		/// <summary>CLI entry: build the Server player. OS target read from -fishmmoOSTarget arg.</summary>
		public static void BuildServerCLI()
		{
			RunCliBuild(BuildTypeEnvironment.Server, includeAddressables: false, addressablesOnly: false);
		}

		/// <summary>CLI entry: build Addressables only. Defaults to Client build type unless -fishmmoBuildType supplied.</summary>
		public static void BuildAddressablesCLI()
		{
			BuildTypeEnvironment buildType = BuildEnvironmentOptions.GetBuildType();
			if (TryGetArg("-fishmmoBuildType", out string buildTypeArg)
				&& Enum.TryParse(buildTypeArg, true, out BuildTypeEnvironment parsed))
			{
				buildType = parsed;
			}
			RunCliBuild(buildType, includeAddressables: true, addressablesOnly: true);
		}

		/// <summary>CLI entry: build the Client player AND addressables together.</summary>
		public static void BuildClientWithAddressablesCLI()
		{
			RunCliBuild(BuildTypeEnvironment.Client, includeAddressables: true, addressablesOnly: false);
		}

		/// <summary>CLI entry: build the Server player AND addressables together.</summary>
		public static void BuildServerWithAddressablesCLI()
		{
			RunCliBuild(BuildTypeEnvironment.Server, includeAddressables: true, addressablesOnly: false);
		}

		/// <summary>
		/// Shared CLI dispatcher. Parses -fishmmoOSTarget, applies prefs, invokes the
		/// pref-driven build entry points, and forces a non-zero exit on failure when
		/// running in batch mode so the caller (installer / CI) can detect the result.
		/// </summary>
		private static void RunCliBuild(BuildTypeEnvironment buildType, bool includeAddressables, bool addressablesOnly)
		{
			try
			{
				OSTargetEnvironment osTarget = OSTargetEnvironment.Windows;
				if (TryGetArg("-fishmmoOSTarget", out string osArg)
					&& Enum.TryParse(osArg, true, out OSTargetEnvironment parsedOs))
				{
					osTarget = parsedOs;
				}

				if (buildType == BuildTypeEnvironment.Server && osTarget == OSTargetEnvironment.WebGL)
				{
					UnityEngine.Debug.LogError("[CustomBuildTool] WebGL Server builds are not supported. Aborting.");
					if (Application.isBatchMode) EditorApplication.Exit(2);
					return;
				}

				BuildEnvironmentOptions.SetBuildType(buildType);
				BuildEnvironmentOptions.SetOSTarget(osTarget);
				BuildEnvironmentOptions.SwitchToEnvironmentBuildTarget();

				/* Switching build target (e.g. Client -> Server) triggers a script recompile,
				 * and the Addressables/player builds below reject requests made while scripts
				 * are compiling. SwitchToEnvironmentBuildTarget drives that compile to
				 * completion synchronously when running in batch mode, so by this point it is
				 * already done.
				 *
				 * This used to be a Thread.Sleep poll loop, which could never succeed: Unity
				 * runs compilation on the main thread's editor tick, so sleeping that thread
				 * is what stopped the very work it was waiting for. It burned its full 120s
				 * budget and failed on every single target switch. Do not reintroduce a sleep
				 * here — if compilation is genuinely still pending there is nothing this
				 * thread can do to advance it, so fail immediately rather than hang. */
				if (Application.isBatchMode && BuildEnvironmentOptions.IsCompiling())
				{
					UnityEngine.Debug.LogError("[CustomBuildTool] Scripts are still compiling after the build target switch; cannot start a CLI build. This should not happen — SwitchToEnvironmentBuildTarget forces a synchronous import in batch mode.");
					EditorApplication.Exit(1);
					return;
				}

				if (includeAddressables)
				{
					BuildAddressablesWithEnvironmentOptions();
				}

				if (!addressablesOnly)
				{
					string outputPath;
					if (!TryGetArg("-fishmmoOutputPath", out outputPath) || string.IsNullOrWhiteSpace(outputPath))
					{
						string projectRoot = Directory.GetParent(Directory.GetCurrentDirectory()).FullName;
						string subFolder = buildType == BuildTypeEnvironment.Server ? "Server" : "Client";
						outputPath = Path.Combine(projectRoot, "Builds", subFolder);
					}
					BuildGameWithEnvironmentOptions(outputPath);
				}

				if (Application.isBatchMode)
				{
					EditorApplication.Exit(0);
				}
			}
			catch (Exception ex)
			{
				UnityEngine.Debug.LogError($"[CustomBuildTool] CLI build failed: {ex.Message}\n{ex.StackTrace}");
				if (Application.isBatchMode) EditorApplication.Exit(1);
			}
		}

		/// <summary>Returns the value of a CLI flag (e.g. -fishmmoOSTarget Linux).</summary>
		private static bool TryGetArg(string name, out string value)
		{
			string[] args = Environment.GetCommandLineArgs();
			for (int i = 0; i < args.Length - 1; i++)
			{
				if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
				{
					value = args[i + 1];
					return true;
				}
			}
			value = string.Empty;
			return false;
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
				case WorkingEnvironmentState.Production:
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