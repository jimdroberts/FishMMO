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
					"Install Unity Hub + the target Editor version first (menu 4 -> 1 / 4 -> 2), " +
					"or set the FISHMMO_UNITY_EXE environment variable to the Unity executable path.");
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

			// Add -standaloneBuildSubtarget "Server" for server builds
			if (buildType == UnityBuildType.Server)
			{
				args += " -standaloneBuildSubtarget \"Server\"";
			}

			// Add -overrideMonoSearchPath for Linux, dynamically resolved
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				// Try to resolve MonoBleedingEdge path relative to the Unity executable
				string? monoPath = TryResolveMonoBleedingEdgePath(unityExe);
				if (!string.IsNullOrWhiteSpace(monoPath))
				{
					args += $" -overrideMonoSearchPath \"{monoPath}\"";
				}
				else
				{
					await Log.Warning("FishMMOInstaller", "Could not resolve MonoBleedingEdge path for -overrideMonoSearchPath. Unity may fail to start.");
				}
			}

			// Helper to resolve MonoBleedingEdge path
			static string? TryResolveMonoBleedingEdgePath(string unityExePath)
			{
				try
				{
					// Handles both .../Editor/Unity and .../Editor/Unity.exe
					var editorDir = Path.GetDirectoryName(unityExePath); // .../Editor
					if (editorDir == null) return null;
					var monoPath = Path.Combine(editorDir, "Data", "MonoBleedingEdge", "lib", "mono", "unityjit-linux");
					return Directory.Exists(monoPath) ? monoPath : null;
				}
				catch
				{
					return null;
				}
			}

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
		/// Cached Unity executable path, set after the first successful resolve so subsequent
		/// builds in the same session do not re-prompt or re-query Unity Hub.
		/// </summary>
		private static string? _cachedUnityExe;

		/// <summary>
		/// Locates the Unity Editor executable for <see cref="InstallationConstants.UnityDefaultVersion"/>.
		/// Resolution order:
		/// <list type="number">
		///   <item>Cached value from a previous successful resolve in this session.</item>
		///   <item><c>FISHMMO_UNITY_EXE</c> environment variable.</item>
		///   <item>Default Unity Hub install path for the configured version.</item>
		///   <item>Unity Hub CLI (<c>unityhub -- --headless editors --installed</c>) output.</item>
		///   <item>Common alternate install locations (e.g. <c>/opt</c>).</item>
		///   <item>Interactive prompt for a user-supplied path.</item>
		/// </list>
		/// </summary>
		private static async Task<string?> ResolveUnityExecutableAsync()
		{
			if (!string.IsNullOrWhiteSpace(_cachedUnityExe) && File.Exists(_cachedUnityExe))
			{
				return _cachedUnityExe;
			}

			string version = InstallationConstants.UnityDefaultVersion;

			// 1. Environment variable override.
			string? envOverride = Environment.GetEnvironmentVariable("FISHMMO_UNITY_EXE");
			if (!string.IsNullOrWhiteSpace(envOverride))
			{
				if (File.Exists(envOverride))
				{
					await Log.Info("FishMMOInstaller", $"Using Unity executable from FISHMMO_UNITY_EXE: {envOverride}");
					return _cachedUnityExe = envOverride;
				}
				await Log.Warning("FishMMOInstaller", $"FISHMMO_UNITY_EXE is set to '{envOverride}' but the file does not exist. Falling back to auto-detection.");
			}

			// 2. Default Unity Hub install path for the configured version.
			string defaultPath = GetDefaultUnityExecutablePath(version);
			if (File.Exists(defaultPath))
			{
				await Log.Info("FishMMOInstaller", $"Found Unity editor at default location: {defaultPath}");
				return _cachedUnityExe = defaultPath;
			}

			// 3. Ask Unity Hub CLI which editors it knows about.
			string? hubReported = await TryResolveFromUnityHubAsync(version);
			if (!string.IsNullOrWhiteSpace(hubReported))
			{
				await Log.Info("FishMMOInstaller", $"Found Unity editor via Unity Hub CLI: {hubReported}");
				return _cachedUnityExe = hubReported;
			}

			// 4. Probe common alternate install locations.
			string? probed = TryProbeAlternateLocations(version);
			if (!string.IsNullOrWhiteSpace(probed))
			{
				await Log.Info("FishMMOInstaller", $"Found Unity editor via filesystem probe: {probed}");
				return _cachedUnityExe = probed;
			}

			// 5. Prompt the user for a manual path.
			string? manual = await PromptForUnityExecutablePathAsync(version, defaultPath);
			if (!string.IsNullOrWhiteSpace(manual))
			{
				return _cachedUnityExe = manual;
			}

			return null;
		}

		/// <summary>
		/// Returns the OS-appropriate default Unity Hub editor executable path for the given version.
		/// </summary>
		private static string GetDefaultUnityExecutablePath(string version)
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				return Path.Combine(InstallationConstants.UnityWindowsEditorRoot, version, "Editor", "Unity.exe");
			}
			return Path.Combine(InstallationConstants.UnityLinuxEditorRoot, version, "Editor", "Unity");
		}

		/// <summary>
		/// Queries the Unity Hub CLI for installed editors and returns the path that matches
		/// the requested version, or <c>null</c> if Hub is not available or the version is not installed.
		/// Each output line looks like: <c>6000.3.2f1 , installed at /path/to/Editor/Unity</c>.
		/// </summary>
		private static async Task<string?> TryResolveFromUnityHubAsync(string version)
		{
			string hubPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
				? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity Hub", "Unity Hub.exe")
				: "/usr/bin/unityhub";

			if (!File.Exists(hubPath) && !RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				return null;
			}

			(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();
			string arguments = $"{argPrefix} \"\\\"{hubPath}\\\" -- --headless editors --installed\"";

			string? resolved = null;
			await InstallerProcessHelper.RunProcessAsync(shell, arguments, (exitCode, output, error) =>
			{
				if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
				{
					return false;
				}

				using var reader = new StringReader(output);
				string? line;
				while ((line = reader.ReadLine()) != null)
				{
					string trimmed = line.TrimStart();
					if (!trimmed.StartsWith(version, StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					// Match "installed at <path>" — the path is the remainder of the line.
					const string marker = "installed at";
					int markerIndex = trimmed.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
					if (markerIndex < 0)
					{
						continue;
					}

					string candidate = trimmed[(markerIndex + marker.Length)..].Trim().Trim('"');
					if (File.Exists(candidate))
					{
						resolved = candidate;
						return true;
					}

					// Some Hub builds report the editor directory; append the executable name.
					string exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Unity.exe" : "Unity";
					string withExe = Path.Combine(candidate, exeName);
					if (File.Exists(withExe))
					{
						resolved = withExe;
						return true;
					}
				}
				return false;
			});

			return resolved;
		}

		/// <summary>
		/// Probes a small set of well-known alternate install locations for the editor executable.
		/// </summary>
		private static string? TryProbeAlternateLocations(string version)
		{
			var candidates = new List<string>();

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				string? programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
				string? localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

				if (!string.IsNullOrEmpty(programFilesX86))
				{
					candidates.Add(Path.Combine(programFilesX86, "Unity", "Hub", "Editor", version, "Editor", "Unity.exe"));
				}
				if (!string.IsNullOrEmpty(localAppData))
				{
					candidates.Add(Path.Combine(localAppData, "Unity", "Hub", "Editor", version, "Editor", "Unity.exe"));
				}
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
				candidates.Add(Path.Combine(home, ".local", "share", "unityhub", "Editor", version, "Editor", "Unity"));
				candidates.Add(Path.Combine(home, "UnityHub", "Editor", version, "Editor", "Unity"));
				candidates.Add(Path.Combine("/opt", "Unity", "Hub", "Editor", version, "Editor", "Unity"));
				candidates.Add(Path.Combine("/opt", "unityhub", "Editor", version, "Editor", "Unity"));
				candidates.Add(Path.Combine("/opt", "Unity", "Editor", "Unity"));
			}

			foreach (string candidate in candidates)
			{
				if (File.Exists(candidate))
				{
					return candidate;
				}
			}
			return null;
		}

		/// <summary>
		/// Interactively asks the user for a Unity executable path. Accepts either the full
		/// path to the editor binary or the parent directory containing it.
		/// </summary>
		private static async Task<string?> PromptForUnityExecutablePathAsync(string version, string defaultPath)
		{
			await Log.Warning("FishMMOInstaller",
				$"Could not auto-detect Unity Editor {version}. Expected default location: {defaultPath}");

			Console.WriteLine();
			Console.WriteLine($"Please enter the full path to the Unity {version} executable.");
			Console.WriteLine("  - Linux example:   /home/<user>/Unity/Hub/Editor/" + version + "/Editor/Unity");
			Console.WriteLine("  - Windows example: C:\\Program Files\\Unity\\Hub\\Editor\\" + version + "\\Editor\\Unity.exe");
			Console.WriteLine("You can also enter the directory that contains the Unity executable.");
			Console.WriteLine("Tip: set the FISHMMO_UNITY_EXE environment variable to skip this prompt next time.");
			Console.WriteLine("Leave blank to cancel.");

			string exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Unity.exe" : "Unity";

			for (int attempt = 0; attempt < 3; attempt++)
			{
				string? input = InstallerProcessHelper.PromptForInput("Unity executable path: ");
				if (string.IsNullOrWhiteSpace(input))
				{
					await Log.Info("FishMMOInstaller", "Unity path entry cancelled by user.");
					return null;
				}

				string candidate = input.Trim().Trim('"');

				if (File.Exists(candidate))
				{
					return candidate;
				}

				if (Directory.Exists(candidate))
				{
					string withExe = Path.Combine(candidate, exeName);
					if (File.Exists(withExe))
					{
						return withExe;
					}

					string nestedExe = Path.Combine(candidate, "Editor", exeName);
					if (File.Exists(nestedExe))
					{
						return nestedExe;
					}
				}

				await Log.Warning("FishMMOInstaller", $"No Unity executable found at '{candidate}'. Please try again.");
			}

			await Log.Warning("FishMMOInstaller", "Unity path entry failed after 3 attempts.");
			return null;
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