using FishMMO.Logging;
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
			await Log.Info("FishMMOInstaller", "--- Install NGINX ---");

			if (await IsNGINXInstalledAsync())
			{
				await Log.Info("FishMMOInstaller", "NGINX appears to be already installed. Ensuring service is configured and enabled.");

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
				await Log.Info("FishMMOInstaller", "NGINX installation cancelled by user.");
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
				await Log.Warning("FishMMOInstaller", "Unsupported operating system for NGINX installation.");
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
						await Log.Info("FishMMOInstaller", "NGINX detected from managed Windows installation path.");
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
				await Log.Info("FishMMOInstaller", "NGINX detected. (Run 'nginx -v' to confirm version)");
			}
			return installed;
		}

		/// <summary>
		/// Installs NGINX on Windows by downloading and extracting the zip file.
		/// </summary>
		private static async Task InstallNGINXWindows()
		{
			await Log.Info("FishMMOInstaller", "Installing NGINX on Windows...");
			try
			{
				string? downloadPath = await DownloadHelper.DownloadFileWithProgressAsync(
					InstallationConstants.NGINXWindowsDownloadUrl,
					InstallationConstants.NGINXWindowsFileName,
					new DownloadHelper.ConsoleProgress());

				if (downloadPath == null)
				{
					await Log.Error("FishMMOInstaller", "Failed to download NGINX.");
					return;
				}
				string extractDirectory = InstallationConstants.NGINXWindowsExtractPath;
				string nginxHomeDirectory = GetExpectedWindowsNginxHomePath();

				Directory.CreateDirectory(extractDirectory);

				if (Directory.Exists(nginxHomeDirectory))
				{
					await Log.Info("FishMMOInstaller", $"Detected existing NGINX directory at '{nginxHomeDirectory}'.");
					if (InstallerProcessHelper.PromptForYesNo("Delete the existing NGINX directory for a clean reinstall?"))
					{
						Directory.Delete(nginxHomeDirectory, true);
					}
					else if (!InstallerProcessHelper.PromptForYesNo("Continue and overwrite existing files where possible?"))
					{
						await Log.Info("FishMMOInstaller", "NGINX installation cancelled.");
						return;
					}
				}

				ZipFile.ExtractToDirectory(downloadPath, extractDirectory, true);
				await Log.Info("FishMMOInstaller", $"NGINX successfully extracted to '{nginxHomeDirectory}'.");

				await EnsureWindowsServiceConfiguredAsync(nginxHomeDirectory);
			}
			catch (Exception ex)
			{
				await Log.Error("FishMMOInstaller", "Error installing NGINX on Windows", ex);
			}
		}

		/// <summary>
		/// Installs NGINX on Linux using the appropriate package manager.
		/// Detects pacman (Arch/CachyOS), apt-get (Debian/Ubuntu), dnf, and yum.
		/// </summary>
		private static async Task InstallNGINXLinux()
		{
			await Log.Info("FishMMOInstaller", "Installing NGINX on Linux...");
			(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();

			var packageNames = new Dictionary<string, string>
			{
				["pacman"] = "nginx",
				["apt-get"] = "nginx",
				["dnf"] = "nginx",
				["yum"] = "nginx"
			};

			var detected = await LinuxPackageManagerHelper.DetectAsync(packageNames);
			if (detected == null)
			{
				await Log.Warning("FishMMOInstaller", "No supported package manager (pacman, apt-get, yum, dnf) found. Please install NGINX manually.");
				return;
			}

			await Log.Info("FishMMOInstaller", $"Using {detected.ManagerName} for NGINX installation.");

			try
			{
				await Log.Info("FishMMOInstaller", "Updating package lists...");
				if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, detected.UpdateCommand, "Failed to update package lists."))
				{
					await Log.Warning("FishMMOInstaller", "Continuing anyway, but installation might fail.");
				}

				await Log.Info("FishMMOInstaller", "Installing NGINX...");
				if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, detected.InstallCommand, "Failed to install NGINX."))
				{
					await Log.Warning("FishMMOInstaller", "Check for errors above.");
					return;
				}

				await EnsureLinuxServiceConfiguredAsync();

				await Log.Info("FishMMOInstaller", "NGINX installed and configured on Linux. Check its status with 'sudo systemctl status nginx'.");
			}
			catch (Exception ex)
			{
				await Log.Error("FishMMOInstaller", "Error during NGINX installation on Linux", ex);
			}
		}

		/// <summary>
		/// Ensures the Linux systemd service is enabled and running for NGINX.
		/// </summary>
		private static async Task EnsureLinuxServiceConfiguredAsync()
		{
			(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();

			await Log.Info("FishMMOInstaller", "Enabling and starting NGINX service via systemd...");
			if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, "sudo systemctl enable --now nginx", "Failed to enable and start NGINX service."))
			{
				await Log.Warning("FishMMOInstaller", "You may need to run: 'sudo systemctl enable --now nginx'");
				return;
			}

			bool isEnabled = await InstallerProcessHelper.RunProcessAsync(shell, $"{argPrefix} \"systemctl is-enabled nginx\"", (exitCode, output, error) => exitCode == 0 && output.Trim().Equals("enabled", StringComparison.OrdinalIgnoreCase));
			bool isActive = await InstallerProcessHelper.RunProcessAsync(shell, $"{argPrefix} \"systemctl is-active nginx\"", (exitCode, output, error) => exitCode == 0 && output.Trim().Equals("active", StringComparison.OrdinalIgnoreCase));

			if (isEnabled && isActive)
			{
				await Log.Info("FishMMOInstaller", "NGINX service is enabled and active.");
			}
			else
			{
				await Log.Warning("FishMMOInstaller", "NGINX service verification failed. Check with 'systemctl status nginx'.");
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
				await Log.Error("FishMMOInstaller", $"Cannot configure Windows service because '{nginxExecutablePath}' does not exist.");
				return;
			}

			if (!File.Exists(nginxConfigurationPath))
			{
				await Log.Error("FishMMOInstaller", $"Cannot configure Windows service because '{nginxConfigurationPath}' does not exist.");
				return;
			}

			string serviceName = InstallationConstants.NGINXWindowsServiceName;
			string nssmExecutablePath = await EnsureNssmInstalledAsync();
			if (string.IsNullOrWhiteSpace(nssmExecutablePath) || !File.Exists(nssmExecutablePath))
			{
				await Log.Error("FishMMOInstaller", "Failed to locate NSSM. Cannot configure Windows NGINX service reliably.");
				return;
			}

			bool serviceExists = await InstallerProcessHelper.RunProcessAsync("sc.exe", $"query \"{serviceName}\"", (exitCode, output, error) =>
				exitCode == 0 && output.Contains("SERVICE_NAME", StringComparison.OrdinalIgnoreCase));

			if (!serviceExists)
			{
				await Log.Info("FishMMOInstaller", $"Creating Windows service '{serviceName}' for NGINX...");
				bool created = await InstallerProcessHelper.RunProcessAsync(
					nssmExecutablePath,
					$"install \"{serviceName}\" \"{nginxExecutablePath}\"",
					(exitCode, output, error) => exitCode == 0);

				if (!created)
				{
					await Log.Error("FishMMOInstaller", "Failed to create NGINX Windows service. Run installer as administrator.");
					return;
				}
			}

			await InstallerProcessHelper.RunProcessAsync(nssmExecutablePath, $"set \"{serviceName}\" AppDirectory \"{nginxHomeDirectory}\"");
			await InstallerProcessHelper.RunProcessAsync(nssmExecutablePath, $"set \"{serviceName}\" AppParameters \"-p \\\"{nginxHomeDirectory}\\\" -c conf\\nginx.conf\"");
			await InstallerProcessHelper.RunProcessAsync(nssmExecutablePath, $"set \"{serviceName}\" Start SERVICE_AUTO_START");
			await InstallerProcessHelper.RunProcessAsync("sc.exe", $"description \"{serviceName}\" \"FishMMO NGINX reverse proxy service\"");

			await Log.Info("FishMMOInstaller", "Starting Windows NGINX service...");
			bool serviceStarted = await InstallerProcessHelper.RunProcessAsync(nssmExecutablePath, $"start \"{serviceName}\"", (exitCode, output, error) =>
				exitCode == 0 ||
				output.Contains("SERVICE_ALREADY_RUNNING", StringComparison.OrdinalIgnoreCase) ||
				error.Contains("SERVICE_ALREADY_RUNNING", StringComparison.OrdinalIgnoreCase));

			if (!serviceStarted)
			{
				await Log.Error("FishMMOInstaller", "Failed to start NGINX Windows service. Verify service account permissions and run as administrator.");
				return;
			}

			await Log.Info("FishMMOInstaller", $"Windows service '{serviceName}' is configured, enabled, and running.");
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
				string? nssmArchivePath = await DownloadHelper.DownloadFileWithProgressAsync(
					InstallationConstants.NssmDownloadUrl,
					InstallationConstants.NssmFileName,
					new DownloadHelper.ConsoleProgress());

				if (nssmArchivePath == null)
				{
					await Log.Error("FishMMOInstaller", "Failed to download NSSM.");
					return string.Empty;
				}

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
				await Log.Error("FishMMOInstaller", "Failed to prepare NSSM", ex);
			}

			return string.Empty;
		}

		/// <summary>
		/// Deploys the canonical FishMMO <c>nginx.conf</c> from
		/// <see cref="InstallationConstants.FishMMOSetupPath"/> to the system NGINX config
		/// location and reloads the service. Existing files are backed up once with
		/// <c>.pre-fishmmo.bak</c>. Runs <c>nginx -t</c> before reload to validate syntax.
		/// </summary>
		public static async Task DeployNginxConfigAsync()
		{
			Console.Clear();
			await Log.Info("FishMMOInstaller", "--- Deploy FishMMO nginx.conf ---");

			string sourcePath = Path.Combine(InstallationConstants.FishMMOSetupPath, "nginx.conf");
			if (!File.Exists(sourcePath))
			{
				await Log.Error("FishMMOInstaller", $"Source config not found at '{sourcePath}'.");
				return;
			}

			if (!await IsNGINXInstalledAsync())
			{
				await Log.Warning("FishMMOInstaller", "NGINX is not installed. Install NGINX first, then deploy the config.");
				return;
			}

			string sourceContent;
			try
			{
				sourceContent = await File.ReadAllTextAsync(sourcePath);
			}
			catch (Exception ex)
			{
				await Log.Error("FishMMOInstaller", $"Failed to read '{sourcePath}'", ex);
				return;
			}

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				const string destPath = "/etc/nginx/nginx.conf";
				await LinuxConfigHardeningHelper.EnsureBackupAsync(destPath);
				if (!await LinuxConfigHardeningHelper.SudoInstallAsync(sourceContent, destPath, "root", "root", "0644"))
				{
					return;
				}

				(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();
				bool valid = await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix,
					"sudo nginx -t",
					"nginx -t reported configuration errors. The previous config is preserved as '/etc/nginx/nginx.conf.pre-fishmmo.bak'.");

				if (!valid)
				{
					await Log.Warning("FishMMOInstaller",
						"Validation failed. To revert: sudo install -o root -g root -m 0644 /etc/nginx/nginx.conf.pre-fishmmo.bak /etc/nginx/nginx.conf");
					return;
				}

				if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix,
					"sudo systemctl reload nginx",
					"NGINX reload failed. Check 'sudo journalctl -u nginx -n 50'."))
				{
					return;
				}

				await Log.Info("FishMMOInstaller", "FishMMO nginx.conf deployed to /etc/nginx/nginx.conf and NGINX reloaded.");
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				string nginxHome = GetExpectedWindowsNginxHomePath();
				string destPath = Path.Combine(nginxHome, "conf", "nginx.conf");
				if (!File.Exists(destPath))
				{
					await Log.Error("FishMMOInstaller", $"Destination not found at '{destPath}'. Install NGINX first.");
					return;
				}

				string backupPath = destPath + LinuxConfigHardeningHelper.BackupSuffix;
				try
				{
					if (!File.Exists(backupPath))
					{
						File.Copy(destPath, backupPath);
					}
					await File.WriteAllTextAsync(destPath, sourceContent);
				}
				catch (Exception ex)
				{
					await Log.Error("FishMMOInstaller", $"Failed to deploy '{destPath}'", ex);
					return;
				}

				string nginxExe = Path.Combine(nginxHome, "nginx.exe");
				bool valid = await InstallerProcessHelper.RunProcessAsync(nginxExe, "-t",
					(exitCode, _, _) => exitCode == 0);
				if (!valid)
				{
					await Log.Warning("FishMMOInstaller",
						$"nginx -t reported configuration errors. Previous config preserved as '{backupPath}'.");
					return;
				}

				await InstallerProcessHelper.RunProcessAsync("sc.exe",
					$"stop \"{InstallationConstants.NGINXWindowsServiceName}\"");
				await InstallerProcessHelper.RunProcessAsync("sc.exe",
					$"start \"{InstallationConstants.NGINXWindowsServiceName}\"");

				await Log.Info("FishMMOInstaller", $"FishMMO nginx.conf deployed to '{destPath}' and service restarted.");
			}
			else
			{
				await Log.Warning("FishMMOInstaller", "Unsupported operating system for NGINX config deployment.");
			}
		}
	}
}