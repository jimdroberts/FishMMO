using FishMMO.Logging;
using System.Runtime.InteropServices;

namespace FishMMO.Installer
{
	/// <summary>
	/// Drives FishMMO-Unity headless builds (Client / Server / Addressables) for
	/// Windows, Linux, and WebGL targets by invoking the Unity Editor in
	/// -batchmode -nographics and dispatching to <c>CustomBuildTool</c> CLI methods.
	/// </summary>
	public static class UnityBuildInstaller
	{
		/// <summary>Identifies a build flavor for the Unity CLI dispatcher.</summary>
		public enum UnityBuildKind
		{
			/// <summary>Player build only.</summary>
			Player,
			/// <summary>Addressables only.</summary>
			Addressables,
			/// <summary>Player + Addressables in a single Unity session.</summary>
			PlayerWithAddressables,
		}

		/// <summary>Identifies the build type passed to the Unity build tool.</summary>
		public enum UnityBuildType
		{
			Client,
			Server,
		}

		/// <summary>Identifies the OS target passed to the Unity build tool.</summary>
		public enum UnityOSTarget
		{
			Windows,
			Linux,
			WebGL,
		}

		/// <summary>
		/// Top-level entry: interactively prompt for build type, OS, and kind, then run.
		/// </summary>
		public static async Task RunInteractiveBuild()
		{
			Console.Clear();
			await Log.Info("FishMMOInstaller", "--- Build FishMMO-Unity ---");

			if (!Directory.Exists(InstallationConstants.FishMMOUnityProjectPath))
			{
				await Log.Warning("FishMMOInstaller",
					$"FishMMO-Unity project not found at '{InstallationConstants.FishMMOUnityProjectPath}'. " +
					"Ensure the monorepo is checked out at the expected location.");
				return;
			}

			UnityBuildType buildType = PromptForBuildType();
			UnityOSTarget osTarget = PromptForOSTarget(buildType);
			UnityBuildKind kind = PromptForBuildKind();

			await RunBuildAsync(buildType, osTarget, kind);
		}

		/// <summary>
		/// Runs a single Unity headless build for the given configuration.
		/// </summary>
		public static async Task RunBuildAsync(UnityBuildType buildType, UnityOSTarget osTarget, UnityBuildKind kind)
		{
			if (buildType == UnityBuildType.Server && osTarget == UnityOSTarget.WebGL)
			{
				await Log.Warning("FishMMOInstaller", "Server + WebGL is not a supported combination. Aborting.");
				return;
			}

			string? unityExe = await ResolveUnityExecutableAsync();
			if (string.IsNullOrWhiteSpace(unityExe))
			{
				await Log.Warning("FishMMOInstaller",
					$"Could not locate a Unity Editor install for version {InstallationConstants.UnityDefaultVersion}. " +
					"Install Unity Hub + the target Editor version first (menu 4 -> 1 / 4 -> 2).");
				return;
			}

			string executeMethod = ResolveExecuteMethod(buildType, kind);
			string projectPath = InstallationConstants.FishMMOUnityProjectPath;
			string logPath = Path.Combine(
				InstallerProcessHelper.GetWorkingDirectory(),
				$"unity-build-{buildType}-{osTarget}-{kind}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");

			await Log.Info("FishMMOInstaller", $"Unity executable : {unityExe}");
			await Log.Info("FishMMOInstaller", $"Project path     : {projectPath}");
			await Log.Info("FishMMOInstaller", $"Execute method   : {executeMethod}");
			await Log.Info("FishMMOInstaller", $"Build type       : {buildType}");
			await Log.Info("FishMMOInstaller", $"OS target        : {osTarget}");
			await Log.Info("FishMMOInstaller", $"Build kind       : {kind}");
			await Log.Info("FishMMOInstaller", $"Log file         : {logPath}");
			await Log.Info("FishMMOInstaller", "Starting Unity in batch mode. This may take several minutes...");

			string args = string.Join(' ',
				"-batchmode",
				"-nographics",
				"-quit",
				$"-projectPath \"{projectPath}\"",
				$"-executeMethod {executeMethod}",
				$"-fishmmoBuildType {buildType}",
				$"-fishmmoOSTarget {osTarget}",
				$"-logFile \"{logPath}\"");

			bool success = await InstallerProcessHelper.RunProcessAsync(unityExe, args,
				(exitCode, stdout, stderr) =>
				{
					if (exitCode == 0)
					{
						_ = Log.Info("FishMMOInstaller", "Unity build completed successfully.");
						return true;
					}

					_ = Log.Warning("FishMMOInstaller",
						$"Unity exited with code {exitCode}. See log file for details: {logPath}");
					if (!string.IsNullOrWhiteSpace(stderr))
					{
						_ = Log.Warning("FishMMOInstaller", $"stderr: {stderr.Trim()}");
					}
					return false;
				});

			if (success)
			{
				await Log.Info("FishMMOInstaller", $"Build artifacts can be found in the FishMMO-Unity build output directory.");
			}
		}

		/// <summary>
		/// Maps (buildType, kind) to the fully-qualified -executeMethod string.
		/// </summary>
		private static string ResolveExecuteMethod(UnityBuildType buildType, UnityBuildKind kind)
		{
			return (buildType, kind) switch
			{
				(UnityBuildType.Client, UnityBuildKind.Player) => InstallationConstants.UnityBuildClientMethod,
				(UnityBuildType.Server, UnityBuildKind.Player) => InstallationConstants.UnityBuildServerMethod,
				(_, UnityBuildKind.Addressables) => InstallationConstants.UnityBuildAddressablesMethod,
				(UnityBuildType.Client, UnityBuildKind.PlayerWithAddressables) => InstallationConstants.UnityBuildClientWithAddressablesMethod,
				(UnityBuildType.Server, UnityBuildKind.PlayerWithAddressables) => InstallationConstants.UnityBuildServerWithAddressablesMethod,
				_ => InstallationConstants.UnityBuildClientMethod,
			};
		}

		/// <summary>
		/// Locates the Unity Editor executable for <see cref="InstallationConstants.UnityDefaultVersion"/>.
		/// On Linux: <c>~/Unity/Hub/Editor/&lt;version&gt;/Editor/Unity</c>.
		/// On Windows: <c>%ProgramFiles%\Unity\Hub\Editor\&lt;version&gt;\Editor\Unity.exe</c>.
		/// Allows override via the <c>FISHMMO_UNITY_EXE</c> environment variable.
		/// </summary>
		private static Task<string?> ResolveUnityExecutableAsync()
		{
			string? envOverride = Environment.GetEnvironmentVariable("FISHMMO_UNITY_EXE");
			if (!string.IsNullOrWhiteSpace(envOverride) && File.Exists(envOverride))
			{
				return Task.FromResult<string?>(envOverride);
			}

			string version = InstallationConstants.UnityDefaultVersion;

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				string winPath = Path.Combine(InstallationConstants.UnityWindowsEditorRoot, version, "Editor", "Unity.exe");
				return Task.FromResult<string?>(File.Exists(winPath) ? winPath : null);
			}

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				string linuxPath = Path.Combine(InstallationConstants.UnityLinuxEditorRoot, version, "Editor", "Unity");
				return Task.FromResult<string?>(File.Exists(linuxPath) ? linuxPath : null);
			}

			return Task.FromResult<string?>(null);
		}

		private static UnityBuildType PromptForBuildType()
		{
			Console.WriteLine();
			Console.WriteLine("Build type:");
			Console.WriteLine("  1) Client");
			Console.WriteLine("  2) Server");
			while (true)
			{
				Console.Write("Select (1-2): ");
				string? input = Console.ReadLine();
				if (input == "1") return UnityBuildType.Client;
				if (input == "2") return UnityBuildType.Server;
				Console.WriteLine("Invalid selection.");
			}
		}

		private static UnityOSTarget PromptForOSTarget(UnityBuildType buildType)
		{
			Console.WriteLine();
			Console.WriteLine("OS target:");
			Console.WriteLine("  1) Windows x64");
			Console.WriteLine("  2) Linux x64");
			if (buildType == UnityBuildType.Client)
			{
				Console.WriteLine("  3) WebGL");
			}
			while (true)
			{
				Console.Write("Select: ");
				string? input = Console.ReadLine();
				if (input == "1") return UnityOSTarget.Windows;
				if (input == "2") return UnityOSTarget.Linux;
				if (input == "3" && buildType == UnityBuildType.Client) return UnityOSTarget.WebGL;
				Console.WriteLine("Invalid selection.");
			}
		}

		private static UnityBuildKind PromptForBuildKind()
		{
			Console.WriteLine();
			Console.WriteLine("Build kind:");
			Console.WriteLine("  1) Player only");
			Console.WriteLine("  2) Addressables only");
			Console.WriteLine("  3) Player + Addressables");
			while (true)
			{
				Console.Write("Select (1-3): ");
				string? input = Console.ReadLine();
				if (input == "1") return UnityBuildKind.Player;
				if (input == "2") return UnityBuildKind.Addressables;
				if (input == "3") return UnityBuildKind.PlayerWithAddressables;
				Console.WriteLine("Invalid selection.");
			}
		}
	}
}
