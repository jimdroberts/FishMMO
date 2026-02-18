using System.IO.Compression;
using System.Runtime.InteropServices;

namespace FishMMO.Installer
{
	/// <summary>
	/// Handles NGINX installation and detection.
	/// Supports Windows (zip download + Windows service) and Linux (pacman, apt-get, dnf, yum + systemd).
	/// </summary>
	public static class NGINXInstaller
	{
		/// <summary>
		/// Computes the expected NGINX home directory from configured zip filename.
		/// </summary>
		/// <returns>Absolute NGINX home directory path.</returns>
		public static string GetExpectedWindowsNginxHomePath()
		{
			string extractedFolderName = Path.GetFileNameWithoutExtension(InstallationConstants.NGINXWindowsFileName);
			return Path.Combine(InstallationConstants.NGINXWindowsExtractPath, extractedFolderName);
		}

		/// <summary>
		/// Installs NGINX based on the operating system.
		/// </summary>
		public static async Task InstallNGINX()
		{
			Console.Clear();
			InstallerProcessHelper.Log("--- Install NGINX ---");

			if (await IsNGINXInstalledAsync())
			{
				InstallerProcessHelper.Log("NGINX appears to be already installed. Ensuring service is configured and enabled.");

				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				{
					await EnsureWindowsServiceConfiguredAsync(GetExpectedWindowsNginxHomePath());
				}
				else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
				{
					await EnsureLinuxServiceConfiguredAsync();
				}

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
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				string nginxExecutablePath = Path.Combine(GetExpectedWindowsNginxHomePath(), "nginx.exe");
				if (File.Exists(nginxExecutablePath))
				{
					bool installedFromKnownPath = await InstallerProcessHelper.RunProcessAsync(nginxExecutablePath, "-v", (exitCode, output, error) =>
					{
						return (exitCode == 0 || exitCode == 1) &&
							(output.Contains("nginx version") || error.Contains("nginx version"));
					});

					if (installedFromKnownPath)
					{
						InstallerProcessHelper.Log("NGINX detected from managed Windows installation path.");
						return true;
					}
				}
			}

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
				string nginxHomeDirectory = GetExpectedWindowsNginxHomePath();

				Directory.CreateDirectory(extractDirectory);

				if (Directory.Exists(nginxHomeDirectory))
				{
					InstallerProcessHelper.Log($"Detected existing NGINX directory at '{nginxHomeDirectory}'.");
					if (InstallerProcessHelper.PromptForYesNo("Delete the existing NGINX directory for a clean reinstall?"))
					{
						Directory.Delete(nginxHomeDirectory, true);
					}
					else if (!InstallerProcessHelper.PromptForYesNo("Continue and overwrite existing files where possible?"))
					{
						InstallerProcessHelper.Log("NGINX installation cancelled.");
						return;
					}
				}

				ZipFile.ExtractToDirectory(downloadPath, extractDirectory, true);
				InstallerProcessHelper.Log($"NGINX successfully extracted to '{nginxHomeDirectory}'.");

				await EnsureWindowsServiceConfiguredAsync(nginxHomeDirectory);
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

				await EnsureLinuxServiceConfiguredAsync();

				InstallerProcessHelper.Log("NGINX installed and configured on Linux. Check its status with 'sudo systemctl status nginx'.");
			}
			catch (Exception ex)
			{
				InstallerProcessHelper.Log($"Error during NGINX installation on Linux: {ex.Message}");
			}
		}

		/// <summary>
		/// Ensures the Linux systemd service is enabled and running for NGINX.
		/// </summary>
		private static async Task EnsureLinuxServiceConfiguredAsync()
		{
			(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();

			InstallerProcessHelper.Log("Enabling and starting NGINX service via systemd...");
			if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, "sudo systemctl enable --now nginx", "Failed to enable and start NGINX service."))
			{
				InstallerProcessHelper.Log("You may need to run: 'sudo systemctl enable --now nginx'");
				return;
			}

			bool isEnabled = await InstallerProcessHelper.RunProcessAsync(shell, $"{argPrefix} \"systemctl is-enabled nginx\"", (exitCode, output, error) => exitCode == 0 && output.Trim().Equals("enabled", StringComparison.OrdinalIgnoreCase));
			bool isActive = await InstallerProcessHelper.RunProcessAsync(shell, $"{argPrefix} \"systemctl is-active nginx\"", (exitCode, output, error) => exitCode == 0 && output.Trim().Equals("active", StringComparison.OrdinalIgnoreCase));

			if (isEnabled && isActive)
			{
				InstallerProcessHelper.Log("NGINX service is enabled and active.");
			}
			else
			{
				InstallerProcessHelper.Log("NGINX service verification failed. Check with 'systemctl status nginx'.");
			}
		}

		/// <summary>
		/// Ensures a Windows Service exists for NGINX, is set to automatic startup, and is running.
		/// </summary>
		/// <param name="nginxHomeDirectory">NGINX home directory containing nginx.exe and conf/nginx.conf.</param>
		private static async Task EnsureWindowsServiceConfiguredAsync(string nginxHomeDirectory)
		{
			string nginxExecutablePath = Path.Combine(nginxHomeDirectory, "nginx.exe");
			string nginxConfigurationPath = Path.Combine(nginxHomeDirectory, "conf", "nginx.conf");

			if (!File.Exists(nginxExecutablePath))
			{
				InstallerProcessHelper.Log($"Cannot configure Windows service because '{nginxExecutablePath}' does not exist.");
				return;
			}

			if (!File.Exists(nginxConfigurationPath))
			{
				InstallerProcessHelper.Log($"Cannot configure Windows service because '{nginxConfigurationPath}' does not exist.");
				return;
			}

			string serviceName = InstallationConstants.NGINXWindowsServiceName;
			string nssmExecutablePath = await EnsureNssmInstalledAsync();
			if (string.IsNullOrWhiteSpace(nssmExecutablePath) || !File.Exists(nssmExecutablePath))
			{
				InstallerProcessHelper.Log("Failed to locate NSSM. Cannot configure Windows NGINX service reliably.");
				return;
			}

			bool serviceExists = await InstallerProcessHelper.RunProcessAsync("sc.exe", $"query \"{serviceName}\"", (exitCode, output, error) =>
				exitCode == 0 && output.Contains("SERVICE_NAME", StringComparison.OrdinalIgnoreCase));

			if (!serviceExists)
			{
				InstallerProcessHelper.Log($"Creating Windows service '{serviceName}' for NGINX...");
				bool created = await InstallerProcessHelper.RunProcessAsync(
					nssmExecutablePath,
					$"install \"{serviceName}\" \"{nginxExecutablePath}\"",
					(exitCode, output, error) => exitCode == 0);

				if (!created)
				{
					InstallerProcessHelper.Log("Failed to create NGINX Windows service. Run installer as administrator.");
					return;
				}
			}

			await InstallerProcessHelper.RunProcessAsync(nssmExecutablePath, $"set \"{serviceName}\" AppDirectory \"{nginxHomeDirectory}\"");
			await InstallerProcessHelper.RunProcessAsync(nssmExecutablePath, $"set \"{serviceName}\" AppParameters \"-p \\\"{nginxHomeDirectory}\\\" -c conf\\nginx.conf\"");
			await InstallerProcessHelper.RunProcessAsync(nssmExecutablePath, $"set \"{serviceName}\" Start SERVICE_AUTO_START");
			await InstallerProcessHelper.RunProcessAsync("sc.exe", $"description \"{serviceName}\" \"FishMMO NGINX reverse proxy service\"");

			InstallerProcessHelper.Log("Starting Windows NGINX service...");
			bool serviceStarted = await InstallerProcessHelper.RunProcessAsync(nssmExecutablePath, $"start \"{serviceName}\"", (exitCode, output, error) =>
				exitCode == 0 ||
				output.Contains("SERVICE_ALREADY_RUNNING", StringComparison.OrdinalIgnoreCase) ||
				error.Contains("SERVICE_ALREADY_RUNNING", StringComparison.OrdinalIgnoreCase));

			if (!serviceStarted)
			{
				InstallerProcessHelper.Log("Failed to start NGINX Windows service. Verify service account permissions and run as administrator.");
				return;
			}

			InstallerProcessHelper.Log($"Windows service '{serviceName}' is configured, enabled, and running.");
		}

		/// <summary>
		/// Ensures NSSM is available locally for reliable Windows service management.
		/// </summary>
		/// <returns>Absolute path to nssm.exe when available; otherwise an empty string.</returns>
		private static async Task<string> EnsureNssmInstalledAsync()
		{
			string nssmDirectory = Path.Combine(InstallerProcessHelper.GetWorkingDirectory(), "nssm");
			string nssmExecutablePath = Path.Combine(nssmDirectory, "nssm.exe");

			if (File.Exists(nssmExecutablePath))
			{
				return nssmExecutablePath;
			}

			try
			{
				string nssmArchivePath = await InstallerProcessHelper.DownloadFileAsync(InstallationConstants.NssmDownloadUrl, InstallationConstants.NssmFileName);

				if (Directory.Exists(nssmDirectory))
				{
					Directory.Delete(nssmDirectory, true);
				}
				Directory.CreateDirectory(nssmDirectory);

				ZipFile.ExtractToDirectory(nssmArchivePath, nssmDirectory, true);

				string extractedExecutablePath = Path.Combine(nssmDirectory, "nssm-2.24", "win64", "nssm.exe");
				if (File.Exists(extractedExecutablePath))
				{
					File.Copy(extractedExecutablePath, nssmExecutablePath, true);
					return nssmExecutablePath;
				}
			}
			catch (Exception ex)
			{
				InstallerProcessHelper.Log($"Failed to prepare NSSM: {ex.Message}");
			}

			return string.Empty;
		}
	}
}