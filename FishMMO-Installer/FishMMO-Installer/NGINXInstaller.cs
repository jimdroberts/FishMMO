using System.IO.Compression;
using System.Runtime.InteropServices;

namespace FishMMO.Installer
{
	/// <summary>
	/// Handles NGINX installation and detection.
	/// Supports Windows (zip download) and Linux (pacman, apt-get, dnf, yum).
	/// </summary>
	public static class NGINXInstaller
	{
		/// <summary>
		/// Installs NGINX based on the operating system.
		/// </summary>
		public static async Task InstallNGINX()
		{
			Console.Clear();
			InstallerProcessHelper.Log("--- Install NGINX ---");

			if (await IsNGINXInstalledAsync())
			{
				InstallerProcessHelper.Log("NGINX appears to be already installed. Skipping installation.");
				return;
			}

			if (!InstallerProcessHelper.PromptForYesNo("NGINX is not detected. Would you like to install it?"))
			{
				InstallerProcessHelper.Log("NGINX installation cancelled by user.");
				return;
			}

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				await InstallNGINXWindows();
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				await InstallNGINXLinux();
			}
			else
			{
				InstallerProcessHelper.Log("Unsupported operating system for NGINX installation.");
			}
		}

		/// <summary>
		/// Checks if NGINX is installed by running 'nginx -v'.
		/// </summary>
		/// <returns>True if NGINX is installed, otherwise false.</returns>
		public static async Task<bool> IsNGINXInstalledAsync()
		{
			(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();
			string arguments = $"{argPrefix} \"nginx -v\"";

			bool installed = await InstallerProcessHelper.RunProcessAsync(shell, arguments, (exitCode, output, error) =>
			{
				return (exitCode == 0 || exitCode == 1) &&
					   (output.Contains("nginx version") || error.Contains("nginx version"));
			});

			if (installed)
			{
				InstallerProcessHelper.Log("NGINX detected. (Run 'nginx -v' to confirm version)");
			}
			return installed;
		}

		/// <summary>
		/// Installs NGINX on Windows by downloading and extracting the zip file.
		/// </summary>
		private static async Task InstallNGINXWindows()
		{
			InstallerProcessHelper.Log("Installing NGINX on Windows...");
			try
			{
				string downloadPath = await InstallerProcessHelper.DownloadFileAsync(
					InstallationConstants.NGINXWindowsDownloadUrl,
					InstallationConstants.NGINXWindowsFileName);
				string extractDirectory = InstallationConstants.NGINXWindowsExtractPath;

				if (Directory.Exists(extractDirectory))
				{
					InstallerProcessHelper.Log($"NGINX extraction directory '{extractDirectory}' already exists. Please manually delete it or choose a different path if you want a clean install.");
					if (!InstallerProcessHelper.PromptForYesNo("Attempt to extract anyway (may overwrite files)?"))
					{
						InstallerProcessHelper.Log("NGINX installation cancelled.");
						return;
					}
				}
				else
				{
					Directory.CreateDirectory(extractDirectory);
				}

				ZipFile.ExtractToDirectory(downloadPath, extractDirectory);
				InstallerProcessHelper.Log($"NGINX successfully extracted to '{extractDirectory}'.");
				InstallerProcessHelper.Log("To start NGINX, navigate to the extracted directory (e.g., C:\\nginx-1.24.0) and run 'nginx.exe'.");
				InstallerProcessHelper.Log("For production, consider running NGINX as a Windows service.");
			}
			catch (Exception ex)
			{
				InstallerProcessHelper.Log($"Error installing NGINX on Windows: {ex.Message}");
			}
		}

		/// <summary>
		/// Installs NGINX on Linux using the appropriate package manager.
		/// Detects pacman (Arch/CachyOS), apt-get (Debian/Ubuntu), dnf, and yum.
		/// </summary>
		private static async Task InstallNGINXLinux()
		{
			InstallerProcessHelper.Log("Installing NGINX on Linux...");
			(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();

			var packageNames = new Dictionary<string, string>
			{
				["pacman"] = "nginx",
				["apt-get"] = "nginx",
				["dnf"] = "nginx",
				["yum"] = "nginx"
			};

			var detected = await InstallerProcessHelper.DetectLinuxPackageManagerAsync(packageNames);
			if (detected == null)
			{
				InstallerProcessHelper.Log("No supported package manager (pacman, apt-get, yum, dnf) found. Please install NGINX manually.");
				return;
			}

			var (updateCommand, installCommand, managerName) = detected.Value;
			InstallerProcessHelper.Log($"Using {managerName} for NGINX installation.");

			try
			{
				InstallerProcessHelper.Log("Updating package lists...");
				if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, updateCommand, "Failed to update package lists."))
				{
					InstallerProcessHelper.Log("Continuing anyway, but installation might fail.");
				}

				InstallerProcessHelper.Log("Installing NGINX...");
				if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, installCommand, "Failed to install NGINX."))
				{
					InstallerProcessHelper.Log("Check for errors above.");
					return;
				}

				InstallerProcessHelper.Log("Starting NGINX service...");
				if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, "sudo systemctl start nginx", "Failed to start NGINX service."))
				{
					InstallerProcessHelper.Log("You may need to start it manually: 'sudo systemctl start nginx'");
				}
				else
				{
					InstallerProcessHelper.Log("NGINX service started successfully.");
				}

				InstallerProcessHelper.Log("Enabling NGINX to start on boot...");
				if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, "sudo systemctl enable nginx", "Failed to enable NGINX to start on boot."))
				{
					InstallerProcessHelper.Log("You may need to enable it manually: 'sudo systemctl enable nginx'");
				}
				else
				{
					InstallerProcessHelper.Log("NGINX enabled to start on boot.");
				}

				InstallerProcessHelper.Log("NGINX installed and configured on Linux. Check its status with 'sudo systemctl status nginx'.");
			}
			catch (Exception ex)
			{
				InstallerProcessHelper.Log($"Error during NGINX installation on Linux: {ex.Message}");
			}
		}
	}
}