using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FishMMO.Installer
{
	/// <summary>
	/// Handles Unity Hub installation and Unity Editor version installation with selectable modules.
	/// Supports Windows and Linux (Arch/CachyOS, Debian/Ubuntu).
	/// Unity Hub CLI is used to install specific Unity versions and modules.
	/// </summary>
	public static class UnityInstaller
	{
		/// <summary>
		/// Installs Unity Hub if not already installed. OS-dispatched.
		/// </summary>
		public static async Task InstallUnityHub()
		{
			Console.Clear();
			InstallerProcessHelper.Log("--- Install Unity Hub ---");

			if (await IsUnityHubInstalledAsync())
			{
				InstallerProcessHelper.Log("Unity Hub is already installed.");
				return;
			}

			if (!InstallerProcessHelper.PromptForYesNo("Unity Hub is not detected. Would you like to install it?"))
			{
				InstallerProcessHelper.Log("Unity Hub installation cancelled by user.");
				return;
			}

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				await InstallUnityHubWindows();
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				await InstallUnityHubLinux();
			}
			else
			{
				InstallerProcessHelper.Log("Unsupported operating system for Unity Hub installation.");
			}
		}

		/// <summary>
		/// Installs a specific Unity Editor version with user-selected modules via Unity Hub CLI.
		/// Requires Unity Hub to be installed first.
		/// </summary>
		public static async Task InstallUnityVersion()
		{
			Console.Clear();
			InstallerProcessHelper.Log("--- Install Unity Editor ---");

			if (!await IsUnityHubInstalledAsync())
			{
				InstallerProcessHelper.Log("Unity Hub is not installed. Please install Unity Hub first (menu option for Unity Hub).");
				return;
			}

			string version = InstallerProcessHelper.PromptForInput(
				$"Enter Unity version to install (default: {InstallationConstants.UnityDefaultVersion}): ")
				?? "";

			if (string.IsNullOrWhiteSpace(version))
			{
				version = InstallationConstants.UnityDefaultVersion;
			}

			InstallerProcessHelper.Log($"Unity version: {version}");

			// Check if this version is already installed
			if (await IsUnityEditorInstalledAsync(version))
			{
				InstallerProcessHelper.Log($"Unity {version} is already installed.");
				if (!InstallerProcessHelper.PromptForYesNo($"Would you like to reinstall or add modules to Unity {version}?"))
				{
					return;
				}
			}

			List<string> selectedModules = PromptForModules();

			if (!InstallerProcessHelper.PromptForYesNo($"Install Unity {version} with {selectedModules.Count} module(s)?"))
			{
				InstallerProcessHelper.Log("Unity Editor installation cancelled by user.");
				return;
			}

			await InstallUnityEditorAsync(version, selectedModules);
		}

		/// <summary>
		/// Checks if Unity Hub is installed.
		/// First checks if the binary exists on disk (fast), then falls back to running
		/// the CLI headless command to verify it's functional.
		/// </summary>
		/// <returns>True if Unity Hub is detected, otherwise false.</returns>
		private static async Task<bool> IsUnityHubInstalledAsync()
		{
			string hubPath = GetUnityHubCliPath();

			// Fast check: does the binary exist on disk?
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				// On Linux, also check via 'command -v' in case it's in a non-standard location
				(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();
				bool found = File.Exists(hubPath) ||
					await InstallerProcessHelper.RunProcessAsync(shell, $"{argPrefix} \"command -v unityhub\"", (e, o, err) => e == 0);

				if (found)
				{
					InstallerProcessHelper.Log("Unity Hub detected.");
				}
				return found;
			}
			else
			{
				// On Windows, check the expected install path
				if (File.Exists(hubPath))
				{
					InstallerProcessHelper.Log("Unity Hub detected.");
					return true;
				}
				return false;
			}
		}

		/// <summary>
		/// Checks if a specific Unity Editor version is already installed via Unity Hub CLI.
		/// Runs 'unityhub -- --headless editors --installed' and searches for the version string.
		/// </summary>
		/// <param name="version">The Unity version to check for (e.g., "6000.3.2f1").</param>
		/// <returns>True if the specified version is installed, otherwise false.</returns>
		private static async Task<bool> IsUnityEditorInstalledAsync(string version)
		{
			string hubPath = GetUnityHubCliPath();
			(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();
			string arguments = $"{argPrefix} \"\\\"{hubPath}\\\" -- --headless editors --installed\"";

			return await InstallerProcessHelper.RunProcessAsync(shell, arguments, (exitCode, output, error) =>
			{
				if (exitCode != 0) return false;

				// Each line of output looks like: "6000.3.2f1 installed at /path/to/Editor/Unity"
				using var reader = new StringReader(output);
				string? line;
				while ((line = reader.ReadLine()) != null)
				{
					if (line.TrimStart().StartsWith(version, StringComparison.OrdinalIgnoreCase))
					{
						return true;
					}
				}
				return false;
			});
		}

		/// <summary>
		/// Gets the OS-appropriate path to the Unity Hub CLI executable.
		/// </summary>
		/// <returns>Full path to the Unity Hub CLI.</returns>
		private static string GetUnityHubCliPath()
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				return Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
					"Unity Hub",
					"Unity Hub.exe");
			}
			else
			{
				return "/usr/bin/unityhub";
			}
		}

		/// <summary>
		/// Installs Unity Hub on Windows by downloading and running the official installer.
		/// </summary>
		private static async Task InstallUnityHubWindows()
		{
			InstallerProcessHelper.Log("Installing Unity Hub on Windows...");
			try
			{
				string installerPath = await InstallerProcessHelper.DownloadFileAsync(
					InstallationConstants.UnityHubWindowsDownloadUrl,
					InstallationConstants.UnityHubWindowsFileName);

				ProcessStartInfo startInfo = new ProcessStartInfo
				{
					FileName = installerPath,
					Arguments = "/S",
					UseShellExecute = true,
					Verb = "runas"
				};

				Process? process = Process.Start(startInfo);
				if (process == null)
				{
					InstallerProcessHelper.Log("Failed to start Unity Hub installer process.");
					return;
				}
				await process.WaitForExitAsync();

				if (process.ExitCode == 0)
				{
					InstallerProcessHelper.Log("Unity Hub installed successfully on Windows.");
				}
				else
				{
					InstallerProcessHelper.Log($"Unity Hub installer exited with code {process.ExitCode}.");
				}
			}
			catch (Exception ex)
			{
				InstallerProcessHelper.Log($"Error installing Unity Hub on Windows: {ex.Message}");
			}
		}

		/// <summary>
		/// Installs Unity Hub on Linux using the official Unity repository or AUR.
		/// For Debian/Ubuntu: adds the Unity GPG key and apt repository.
		/// For Arch/CachyOS: installs unityhub from AUR via yay or paru.
		/// </summary>
		private static async Task InstallUnityHubLinux()
		{
			InstallerProcessHelper.Log("Installing Unity Hub on Linux...");
			(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();

			// Detect package manager to determine distro family
			var packageNames = new Dictionary<string, string>
			{
				["pacman"] = "",
				["apt-get"] = ""
			};

			var detected = await InstallerProcessHelper.DetectLinuxPackageManagerAsync(packageNames);
			if (detected == null)
			{
				InstallerProcessHelper.Log("No supported package manager found. Please install Unity Hub manually from https://unity.com/download.");
				return;
			}

			var (_, _, managerName) = detected.Value;

			try
			{
				if (managerName.Contains("apt-get", StringComparison.OrdinalIgnoreCase))
				{
					await InstallUnityHubDebian(shell, argPrefix);
				}
				else if (managerName.Contains("pacman", StringComparison.OrdinalIgnoreCase))
				{
					await InstallUnityHubArch(shell, argPrefix);
				}
				else
				{
					InstallerProcessHelper.Log($"Automatic Unity Hub installation is not supported for {managerName}. Please install manually from https://unity.com/download.");
				}
			}
			catch (Exception ex)
			{
				InstallerProcessHelper.Log($"Error during Unity Hub installation on Linux: {ex.Message}");
			}
		}

		/// <summary>
		/// Installs Unity Hub on Debian/Ubuntu by adding the official Unity apt repository.
		/// </summary>
		/// <param name="shell">Shell executable.</param>
		/// <param name="argPrefix">Shell argument prefix.</param>
		private static async Task InstallUnityHubDebian(string shell, string argPrefix)
		{
			InstallerProcessHelper.Log("Adding Unity Hub repository for Debian/Ubuntu...");

			// Add Unity GPG key
			if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix,
				"wget -qO - https://hub.unity3d.com/linux/keys/public | sudo gpg --dearmor -o /usr/share/keyrings/Unity_Technologies_ApS.gpg",
				"Failed to add Unity GPG key."))
			{
				return;
			}

			// Add Unity repository
			if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix,
				"echo 'deb [signed-by=/usr/share/keyrings/Unity_Technologies_ApS.gpg] https://hub.unity3d.com/linux/repos/deb stable main' | sudo tee /etc/apt/sources.list.d/unityhub.list",
				"Failed to add Unity Hub repository."))
			{
				return;
			}

			// Update and install
			if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix,
				"sudo apt-get update",
				"Failed to update package lists."))
			{
				InstallerProcessHelper.Log("Continuing anyway...");
			}

			if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix,
				"sudo apt-get install -y unityhub",
				"Failed to install Unity Hub."))
			{
				return;
			}

			InstallerProcessHelper.Log("Unity Hub installed successfully on Debian/Ubuntu.");
		}

		/// <summary>
		/// Installs Unity Hub on Arch/CachyOS from the AUR using yay or paru.
		/// </summary>
		/// <param name="shell">Shell executable.</param>
		/// <param name="argPrefix">Shell argument prefix.</param>
		private static async Task InstallUnityHubArch(string shell, string argPrefix)
		{
			InstallerProcessHelper.Log("Installing Unity Hub from AUR for Arch/CachyOS...");

			// Detect AUR helper
			string? aurHelper = null;

			if (await InstallerProcessHelper.RunProcessAsync(shell, $"{argPrefix} \"command -v yay\"", (e, o, err) => e == 0))
			{
				aurHelper = "yay";
			}
			else if (await InstallerProcessHelper.RunProcessAsync(shell, $"{argPrefix} \"command -v paru\"", (e, o, err) => e == 0))
			{
				aurHelper = "paru";
			}

			if (aurHelper == null)
			{
				InstallerProcessHelper.Log("No AUR helper (yay or paru) found. Please install yay or paru first, then run: yay -S unityhub");
				return;
			}

			InstallerProcessHelper.Log($"Using {aurHelper} to install Unity Hub from AUR...");
			if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix,
				$"{aurHelper} -S --noconfirm unityhub",
				$"Failed to install Unity Hub via {aurHelper}."))
			{
				return;
			}

			InstallerProcessHelper.Log("Unity Hub installed successfully on Arch/CachyOS.");
		}

		/// <summary>
		/// Prompts the user to select which Unity modules to install alongside the editor.
		/// Displays a numbered list of available modules and allows comma-separated selection.
		/// </summary>
		/// <returns>List of selected module identifiers for the Unity Hub CLI.</returns>
		private static List<string> PromptForModules()
		{
			Console.WriteLine();
			Console.WriteLine("Available Unity Modules:");

			for (int i = 0; i < InstallationConstants.UnityAvailableModules.Length; i++)
			{
				var (moduleId, displayName) = InstallationConstants.UnityAvailableModules[i];
				bool isDefault = InstallationConstants.UnityDefaultModules.Contains(moduleId);
				string marker = isDefault ? " [default]" : "";
				Console.WriteLine($"  {i + 1}. {displayName}{marker}");
			}

			Console.WriteLine();
			string? input = InstallerProcessHelper.PromptForInput(
				"Enter module numbers (comma-separated) or press Enter for defaults: ");

			if (string.IsNullOrWhiteSpace(input))
			{
				InstallerProcessHelper.Log("Using default modules.");
				return new List<string>(InstallationConstants.UnityDefaultModules);
			}

			var selected = new List<string>();
			string[] parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

			foreach (string part in parts)
			{
				if (int.TryParse(part, out int index) &&
					index >= 1 &&
					index <= InstallationConstants.UnityAvailableModules.Length)
				{
					string moduleId = InstallationConstants.UnityAvailableModules[index - 1].moduleId;
					if (!selected.Contains(moduleId))
					{
						selected.Add(moduleId);
					}
				}
				else
				{
					InstallerProcessHelper.Log($"Skipping invalid selection: '{part}'");
				}
			}

			if (selected.Count == 0)
			{
				InstallerProcessHelper.Log("No valid modules selected. Using default modules.");
				return new List<string>(InstallationConstants.UnityDefaultModules);
			}

			Console.WriteLine("Selected modules:");
			foreach (string moduleId in selected)
			{
				var match = Array.Find(InstallationConstants.UnityAvailableModules, m => m.moduleId == moduleId);
				Console.WriteLine($"  - {match.displayName}");
			}

			return selected;
		}

		/// <summary>
		/// Installs a Unity Editor version with specified modules via Unity Hub CLI.
		/// </summary>
		/// <param name="version">Unity version string (e.g., "6000.3.2f1").</param>
		/// <param name="modules">List of module identifiers to install.</param>
		private static async Task InstallUnityEditorAsync(string version, List<string> modules)
		{
			string hubPath = GetUnityHubCliPath();
			(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();

			// Build per-module --module flags
			string moduleArgs = modules.Count > 0
				? string.Join(" ", modules.Select(m => $"--module {m}"))
				: "";

			string installCommand = $"\\\"{hubPath}\\\" -- --headless install --version {version} {moduleArgs}".Trim();

			InstallerProcessHelper.Log($"Installing Unity {version} with modules: {string.Join(", ", modules)}");
			InstallerProcessHelper.Log("This may take a while depending on your internet connection and selected modules...");

			bool success = await InstallerProcessHelper.RunProcessAsync(shell, $"{argPrefix} \"{installCommand}\"",
				(exitCode, output, error) =>
				{
					if (!string.IsNullOrWhiteSpace(output))
					{
						Console.WriteLine(output);
					}
					if (!string.IsNullOrWhiteSpace(error))
					{
						InstallerProcessHelper.Log($"Unity Hub Error: {error}");
					}
					return exitCode == 0;
				});

			if (success)
			{
				InstallerProcessHelper.Log($"Unity {version} installed successfully.");
			}
			else
			{
				InstallerProcessHelper.Log($"Unity {version} installation failed. Check the errors above.");
				InstallerProcessHelper.Log("You may also try installing manually via Unity Hub UI.");
			}
		}
	}
}