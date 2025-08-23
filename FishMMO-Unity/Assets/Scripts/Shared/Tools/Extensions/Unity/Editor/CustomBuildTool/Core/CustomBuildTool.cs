#if UNITY_EDITOR
using UnityEditor;
using System.IO;
using FishMMO.Logging;
using FishMMO.Shared.CustomBuildTool.Config;
using FishMMO.Shared.CustomBuildTool.Execution;
using FishMMO.Shared.CustomBuildTool.Addressables;
using FishMMO.Shared.CustomBuildTool.Linker;

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

		public CustomBuildTool(
			IBuildConfigurator configurator,
			IBuildExecutor executor,
			ILinkerGenerator linkerGenerator,
			IAddressableManager addressableManager)
		{
			this.configurator = configurator;
			this.executor = executor;
			this.linkerGenerator = linkerGenerator;
			this.addressableManager = addressableManager;
		}

		/// <summary>
		/// Runs the full custom build process.
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
			Log.Debug("BuildLogger", "Configuring build...");
			configurator.Configure();
			Log.Debug("BuildLogger", "Configuring addressables...");
			addressableManager.BuildAddressablesWithExclusions(excludedAddressableGroups);
			Log.Debug("BuildLogger", "Generating linker file...");
			linkerGenerator.GenerateLinker(linkerRootPath, linkerDirectoryPath);
			Log.Debug("BuildLogger", "Executing build...");
			executor.ExecuteBuild(rootPath, executableName, bootstrapScenes, customBuildType, buildOptions, subTarget, buildTarget);
			Log.Debug("BuildLogger", "Restoring build configuration...");
			configurator.Restore();
			Log.Debug("BuildLogger", "Build process complete.");
		}

		[MenuItem("FishMMO/Update Linker")]
		public static void UpdateLinker()
		{
			string current = Directory.GetCurrentDirectory();
			string assets = Path.Combine(current, "Assets");
			var linker = new LinkerGenerator();
			linker.GenerateLinker(assets, Path.Combine(assets, "Dependencies"));
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
			var configurator = new BuildConfigurator();
			configurator.Configure();
			new AddressableManager().BuildAddressablesWithExclusions(serverAddressableGroups);
			configurator.Restore();
		}


		[MenuItem("FishMMO/Build/Windows x64/Addressables/Server Addressables")]
		public static void BuildWindowsServerAddressables()
		{
			var configurator = new BuildConfigurator();
			configurator.Configure();
			new AddressableManager().BuildAddressablesWithExclusions(clientAddressableGroups);
			configurator.Restore();
		}


		[MenuItem("FishMMO/Build/Linux x64/Addressables/Client Addressables")]
		public static void BuildLinuxClientAddressables()
		{
			var configurator = new BuildConfigurator();
			configurator.Configure();
			new AddressableManager().BuildAddressablesWithExclusions(serverAddressableGroups);
			configurator.Restore();
		}


		[MenuItem("FishMMO/Build/Linux x64/Addressables/Server Addressables")]
		public static void BuildLinuxServerAddressables()
		{
			var configurator = new BuildConfigurator();
			configurator.Configure();
			new AddressableManager().BuildAddressablesWithExclusions(clientAddressableGroups);
			configurator.Restore();
		}


		[MenuItem("FishMMO/Build/WebGL/Addressables/Client Addressables")]
		public static void BuildWebGLAddressables()
		{
			var configurator = new BuildConfigurator();
			configurator.Configure();
			new AddressableManager().BuildAddressablesWithExclusions(serverAddressableGroups);
			configurator.Restore();
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

		private static void BuildExecutable(string executableName, string[] bootstrapScenes, CustomBuildType customBuildType, BuildOptions buildOptions, StandaloneBuildSubtarget subTarget, BuildTarget buildTarget)
		{
			var configurator = new BuildConfigurator();
			configurator.Configure();
			new BuildExecutor().ExecuteBuild(
				rootPath: string.Empty,
				executableName: executableName,
				bootstrapScenes: bootstrapScenes,
				customBuildType: customBuildType,
				buildOptions: buildOptions,
				subTarget: subTarget,
				buildTarget: buildTarget);
			configurator.Restore();
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