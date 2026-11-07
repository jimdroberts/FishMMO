using FishMMO.Logging;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

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
					// An NGINX installed earlier, by hand or by the distribution, usually lacks the
					// separately packaged stream module the game's UDP ports need.
					(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();
					await EnsureStreamModuleInstalledAsync(shell, argPrefix, prompt: false);
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

			var packageNames = StreamModulePackages.ToDictionary(
				entry => entry.Key, entry => "nginx " + entry.Value);

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
			string nssmExe = await EnsureNssmInstalledAsync();
			if (string.IsNullOrWhiteSpace(nssmExe) || !File.Exists(nssmExe))
			{
				await Log.Error("FishMMOInstaller", "Failed to locate NSSM. Cannot configure Windows NGINX service reliably.");
				return;
			}

			if (!IsRunningAsAdministrator())
			{
				await Log.Error("FishMMOInstaller",
					"NGINX Windows service configuration requires Administrator privileges.\n" +
					"  -> Right-click the terminal and select 'Run as administrator', or\n" +
					"  -> Run from an elevated PowerShell: Start-Process -Verb RunAs FishMMO-Installer.exe");
				return;
			}

			bool serviceExists = await InstallerProcessHelper.RunProcessAsync("sc.exe", $"query \"{serviceName}\"", (exitCode, output, error) =>
				exitCode == 0 && output.Contains("SERVICE_NAME", StringComparison.OrdinalIgnoreCase));

			if (!serviceExists)
			{
				await Log.Info("FishMMOInstaller", $"Creating Windows service '{serviceName}' for NGINX...");
				bool created = await InstallerProcessHelper.RunProcessAsync(
					nssmExe,
					$"install \"{serviceName}\" \"{nginxExecutablePath}\"",
					(exitCode, output, error) => exitCode == 0);

				if (!created)
				{
					await Log.Error("FishMMOInstaller", "Failed to create NGINX Windows service.\n" +
				"  -> Ensure you are running as Administrator.\n" +
				"  -> Check that NSSM is not blocked by antivirus.");
					return;
				}
			}

			// Configure service logging and rotation.
			await SetNssmParamAsync(nssmExe, serviceName, "AppStdout",
				Path.Combine(nginxHomeDirectory, "logs", "service-out.log"));
			await SetNssmParamAsync(nssmExe, serviceName, "AppStderr",
				Path.Combine(nginxHomeDirectory, "logs", "service-err.log"));
			await SetNssmParamAsync(nssmExe, serviceName, "AppRotateFiles", "1");
			await InstallerProcessHelper.RunProcessAsync(nssmExe, $"set \"{serviceName}\" AppDirectory \"{nginxHomeDirectory}\"");
			await InstallerProcessHelper.RunProcessAsync(nssmExe, $"set \"{serviceName}\" AppParameters \"-p \\\"{nginxHomeDirectory}\\\" -c conf\\nginx.conf\"");
			await InstallerProcessHelper.RunProcessAsync(nssmExe, $"set \"{serviceName}\" Start SERVICE_AUTO_START");
			await InstallerProcessHelper.RunProcessAsync("sc.exe", $"description \"{serviceName}\" \"FishMMO NGINX reverse proxy service\"");

			await Log.Info("FishMMOInstaller", "Starting Windows NGINX service...");
			bool serviceStarted = await InstallerProcessHelper.RunProcessAsync(nssmExe, $"start \"{serviceName}\"", (exitCode, output, error) =>
				exitCode == 0 ||
				output.Contains("SERVICE_ALREADY_RUNNING", StringComparison.OrdinalIgnoreCase) ||
				error.Contains("SERVICE_ALREADY_RUNNING", StringComparison.OrdinalIgnoreCase));

			if (!serviceStarted)
			{
				await Log.Error("FishMMOInstaller", "Failed to start NGINX Windows service.\n" +
				"  -> Check Windows Event Viewer for service errors.\n" +
				$"  -> Run: {nssmExe} status \"{serviceName}\"\n" +
				"  -> Verify ports 80/443 are free: netstat -ano | findstr :80");
				return;
			}

			await Log.Info("FishMMOInstaller", $"Windows service '{serviceName}' is configured, enabled, and running.");
		}

		/// <summary>
		/// Ensures NSSM is available locally for reliable Windows service management.
		/// </summary>
		/// <returns>Absolute path to nssm.exe when available; otherwise an empty string.</returns>
		public static async Task<string> EnsureNssmInstalledAsync()
		{
			string nssmDirectory = Path.Combine(InstallerProcessHelper.GetWorkingDirectory(), "nssm");
			string nssmExe = Path.Combine(nssmDirectory, "nssm.exe");

			if (File.Exists(nssmExe))
			{
				return nssmExe;
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
					File.Copy(extractedExecutablePath, nssmExe, true);
					return nssmExe;
				}
			}
			catch (Exception ex)
			{
				await Log.Error("FishMMOInstaller", "Failed to prepare NSSM", ex);
			}

			return string.Empty;
		}

		/// <summary>The placeholder domain the repository's nginx.conf is written for.</summary>
		public const string TemplateDomain = "fishmmo.com";

		/// <summary>The UDP stream generator in <see cref="InstallationConstants.FishMMOSetupPath"/>.</summary>
		public const string StreamGeneratorName = "gen-fishmmo-stream-config.sh";

		/// <summary>Where the stream generator is installed, matching nginx.conf's comments and the README.</summary>
		public const string StreamGeneratorInstallPath = "/usr/local/bin/gen-fishmmo-stream-config.sh";

		internal static readonly Regex RootDomainPattern = new Regex(
			@"^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$", RegexOptions.Compiled);

		// Directories the distributions' module packages drop load_module snippets into; nginx.conf
		// includes all three.
		private static readonly string[] ModuleSnippetDirectories =
		{
			"/etc/nginx/modules-enabled", // Debian, Ubuntu
			"/etc/nginx/modules.d",       // Arch, CachyOS
			"/usr/share/nginx/modules",   // Fedora, RHEL
		};

		/// <summary>
		/// Rewrites the template's placeholder domain (host names and the certificate lineage path)
		/// to <paramref name="domain"/>.
		/// </summary>
		public static string ForDomain(string template, string domain) =>
			Regex.Replace(template, @"(?<![a-z0-9-])fishmmo\.com\b", domain);

		/// <summary>
		/// Makes sure the stream module is installed and loaded. A distribution that builds it as a
		/// dynamic module needs a load_module line; Debian and Fedora packages ship one, Arch's ships
		/// only the .so. Returns false, with the reason logged, when the module is missing and was
		/// not installed.
		/// </summary>
		private static async Task<bool> EnsureStreamModuleLoadedAsync(string shell, string argPrefix)
		{
			string? module = await EnsureStreamModuleInstalledAsync(shell, argPrefix, prompt: true);
			if (module == null)
			{
				return false;
			}
			if (module.Length == 0)
			{
				// Built in (or absent, which nginx -t reports as 'unknown directive "stream"').
				return true;
			}

			foreach (string directory in ModuleSnippetDirectories.Where(Directory.Exists))
			{
				foreach (string snippet in Directory.EnumerateFiles(directory, "*.conf"))
				{
					try
					{
						if (File.ReadAllText(snippet).Contains("ngx_stream_module", StringComparison.Ordinal))
							return true;
					}
					catch (IOException) { }
					catch (UnauthorizedAccessException) { }
				}
			}

			await Log.Info("FishMMOInstaller", "Loading the NGINX stream module through /etc/nginx/modules.d/fishmmo-stream.conf.");
			return await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix,
					"sudo install -d -o root -g root -m 0755 /etc/nginx/modules.d", "Failed to create /etc/nginx/modules.d.") &&
				await LinuxConfigHardeningHelper.SudoInstallAsync($"load_module {module};\n",
					"/etc/nginx/modules.d/fishmmo-stream.conf", "root", "root", "0644");
		}

		/// <summary>
		/// The stream module forwards the game's UDP (QUIC) ports. Arch, Debian and Fedora all ship
		/// it as a separate dynamic-module package, keyed here by package manager.
		/// </summary>
		/// <summary>
		/// Asks for the LAN address NGINX listens on for the UDP game ports, defaulting to this
		/// host's source address on the default route. Returns null (reason logged) when the answer
		/// cannot work.
		/// </summary>
		/// <remarks>
		/// NGINX must not listen on 0.0.0.0: the game servers bind the same port numbers on
		/// 127.0.0.1, which 0.0.0.0 includes, so they could not bind and NGINX would forward every
		/// datagram back to itself. A specific LAN address and 127.0.0.1 coexist on one port.
		/// </remarks>
		private static async Task<string?> PromptForGameListenAddressAsync(string shell, string argPrefix)
		{
			string detected = string.Empty;
			await InstallerProcessHelper.RunProcessAsync(shell, $"{argPrefix} \"ip -4 route get 1.1.1.1\"", (code, output, _) =>
			{
				Match source = Regex.Match(output, @"\bsrc\s+(\d+\.\d+\.\d+\.\d+)");
				if (source.Success) detected = source.Groups[1].Value;
				return code == 0;
			});

			Console.WriteLine();
			Console.WriteLine("LAN address for the UDP game ports (7770-7999).");
			Console.WriteLine("  NGINX listens for game traffic on this address and forwards it to the game servers on 127.0.0.1.");
			Console.WriteLine("  It must be this machine's own address on the local network: the one your router forwards");
			Console.WriteLine("  UDP 7770-7999 to (usually 192.168.x.x or 10.x.x.x). Not 0.0.0.0: the game servers use the");
			Console.WriteLine("  same port numbers on 127.0.0.1, which 0.0.0.0 includes, so they could not start. Not 127.0.0.1:");
			Console.WriteLine("  the router cannot reach it.");
			Console.WriteLine("  Keep it fixed (a static address or a DHCP reservation in the router): if it changes, NGINX");
			Console.WriteLine("  cannot start until this step is run again with the new address.");
			string answer = (InstallerProcessHelper.PromptForInput(
				detected.Length > 0 ? $"LAN address [{detected}]: " : "LAN address (e.g. 192.168.1.10): ") ?? "").Trim();
			if (answer.Length == 0) answer = detected;

			if (!System.Net.IPAddress.TryParse(answer, out System.Net.IPAddress? parsed) ||
				parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || answer.Split('.').Length != 4)
			{
				await Log.Warning("FishMMOInstaller", $"'{answer}' is not an IPv4 address. Nothing was changed.");
				return null;
			}
			if (answer == "0.0.0.0" || answer.StartsWith("127.", StringComparison.Ordinal))
			{
				await Log.Warning("FishMMOInstaller", $"{answer} cannot be used (see above). Enter this machine's LAN address. Nothing was changed.");
				return null;
			}

			// NGINX cannot bind an address this machine does not have.
			bool assigned = await InstallerProcessHelper.RunProcessAsync(shell, $"{argPrefix} \"ip -o -4 addr show\"",
				(code, output, _) => code == 0 && Regex.IsMatch(output, $@"\binet\s+{Regex.Escape(answer)}/"));
			if (!assigned)
			{
				await Log.Warning("FishMMOInstaller",
					$"{answer} is not an address of this machine ('ip -4 addr' does not list it), so NGINX could not listen on it. Nothing was changed.");
				return null;
			}
			return answer;
		}

		/// <summary>systemd drop-in that raises NGINX's open-file limit for the UDP stream sockets.</summary>
		public const string LimitsDropInPath = "/etc/systemd/system/nginx.service.d/fishmmo-limits.conf";

		private static readonly Dictionary<string, string> StreamModulePackages = new()
		{
			["pacman"] = "nginx-mod-stream",
			["apt-get"] = "libnginx-mod-stream",
			["dnf"] = "nginx-mod-stream",
			["yum"] = "nginx-mod-stream"
		};

		/// <summary>
		/// Returns the path of the stream module shared object when NGINX builds it as a dynamic
		/// module, installing its package first if the file is missing; returns the empty string
		/// when NGINX has the module built in, and null (with the reason logged) when it is missing
		/// and could not be installed.
		/// </summary>
		private static async Task<string?> EnsureStreamModuleInstalledAsync(string shell, string argPrefix, bool prompt)
		{
			string configure = string.Empty;
			await InstallerProcessHelper.RunProcessAsync("nginx", "-V", (code, output, error) =>
			{
				configure = output + error; // nginx -V prints to stderr
				return code == 0;
			});
			if (!configure.Contains("--with-stream=dynamic", StringComparison.Ordinal))
			{
				return string.Empty;
			}

			Match modulesPath = Regex.Match(configure, @"--modules-path=(\S+)");
			string module = Path.Combine(modulesPath.Success ? modulesPath.Groups[1].Value : "/usr/lib/nginx/modules",
				"ngx_stream_module.so");
			if (File.Exists(module))
			{
				return module;
			}

			const string manualHint = "Install 'nginx-mod-stream' (Arch, Fedora) or 'libnginx-mod-stream' (Debian, Ubuntu), then try again.";
			var detected = await LinuxPackageManagerHelper.DetectAsync(StreamModulePackages);
			if (detected == null)
			{
				await Log.Error("FishMMOInstaller", $"The NGINX stream module is not installed ({module} is missing). {manualHint}");
				return null;
			}
			if (prompt && !InstallerProcessHelper.PromptForYesNo(
					$"The NGINX stream module (needed for the game's UDP ports) is not installed. Install it with {detected.ManagerName}?"))
			{
				await Log.Warning("FishMMOInstaller", $"The NGINX stream module is required. {manualHint}");
				return null;
			}

			await Log.Info("FishMMOInstaller", "Installing the NGINX stream module...");
			// pacman installs from the existing package database: refreshing it alone (-Sy) and then
			// installing one package is a partial upgrade, which Arch does not support. The module is
			// built alongside the installed NGINX, so the database that installed NGINX has it.
			if (!detected.ManagerName.StartsWith("pacman", StringComparison.Ordinal))
			{
				await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, detected.UpdateCommand,
					"Failed to update package lists.");
			}
			if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, detected.InstallCommand,
					"Failed to install the NGINX stream module.") || !File.Exists(module))
			{
				await Log.Error("FishMMOInstaller", $"The NGINX stream module is still missing ({module}). {manualHint}");
				return null;
			}
			await Log.Info("FishMMOInstaller", $"NGINX stream module installed ({module}).");
			return module;
		}

		/// <summary>
		/// Deploys the canonical FishMMO <c>nginx.conf</c> from
		/// <see cref="InstallationConstants.FishMMOSetupPath"/> to the system NGINX config
		/// location and reloads the service. Existing files are backed up once with
		/// <c>.pre-fishmmo.bak</c>. Runs <c>nginx -t</c> before reload to validate syntax.
		/// </summary>
		/// <remarks>
		/// On Linux it also substitutes the operator's root domain for <see cref="TemplateDomain"/>,
		/// refuses to run before that domain's certificate exists (every server block names it),
		/// loads the stream module, and installs and runs the UDP stream generator.
		/// </remarks>
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
				(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();

				string domain = (InstallerProcessHelper.PromptForInput(
					$"Enter your root domain (the template's '{TemplateDomain}' is replaced with it): ") ?? string.Empty)
					.Trim().TrimEnd('.').ToLowerInvariant();
				if (!RootDomainPattern.IsMatch(domain))
				{
					await Log.Warning("FishMMOInstaller", "A valid root domain such as example.com is required. Nothing was changed.");
					return;
				}

				string? listenAddress = await PromptForGameListenAddressAsync(shell, argPrefix);
				if (listenAddress == null)
				{
					return;
				}

				// Every server block names this certificate, so nginx -t cannot pass without it.
				string certificate = $"/etc/letsencrypt/live/{domain}/fullchain.pem";
				if (!await InstallerProcessHelper.RunProcessAsync(shell, $"{argPrefix} \"sudo test -s '{certificate}'\""))
				{
					await Log.Warning("FishMMOInstaller",
						$"No certificate at {certificate}. Run Web Server > Install/Renew Let's Encrypt Certificate first " +
						"(its DNS challenge needs no nginx), then deploy again. Nothing was changed.");
					return;
				}

				if (!await EnsureStreamModuleLoadedAsync(shell, argPrefix))
				{
					return;
				}

				await LinuxConfigHardeningHelper.EnsureBackupAsync(destPath);
				if (!await LinuxConfigHardeningHelper.SudoInstallAsync(ForDomain(sourceContent, domain), destPath, "root", "root", "0644"))
				{
					return;
				}

				bool valid = await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix,
					"sudo nginx -t",
					"nginx -t reported configuration errors. The previous config is preserved as '/etc/nginx/nginx.conf.pre-fishmmo.bak'.");

				if (!valid)
				{
					await Log.Warning("FishMMOInstaller",
						"Validation failed. To revert: sudo install -o root -g root -m 0644 /etc/nginx/nginx.conf.pre-fishmmo.bak /etc/nginx/nginx.conf");
					return;
				}

				// One UDP server block per game port; without them nothing reaches the game servers.
				string generator = Path.Combine(InstallationConstants.FishMMOSetupPath, StreamGeneratorName);
				if (!File.Exists(generator))
				{
					await Log.Error("FishMMOInstaller", $"Stream generator not found at '{generator}'.");
					return;
				}
				if (!await LinuxConfigHardeningHelper.SudoInstallAsync(await File.ReadAllTextAsync(generator),
						StreamGeneratorInstallPath, "root", "root", "0755") ||
					!await InstallerProcessHelper.RunShellCommandWithLiveOutputAsync(shell, argPrefix,
						$"sudo env NO_RELOAD=1 LISTEN_IP={listenAddress} {StreamGeneratorInstallPath}",
						"Generating the UDP stream configuration failed. Review the output above."))
				{
					return;
				}

				// reuseport gives every worker its own socket per UDP port, so the master opens
				// workers x ports sockets; the systemd default of 1024-2048 open files is too few.
				// The limit is read when the master starts, hence a restart rather than a reload.
				if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix,
						$"sudo install -d -o root -g root -m 0755 {Path.GetDirectoryName(LimitsDropInPath)}",
						"Failed to create the NGINX systemd drop-in directory.") ||
					!await LinuxConfigHardeningHelper.SudoInstallAsync(
						"# Installed by FishMMO-Installer: NGINX opens one socket per worker for every UDP game port.\n" +
						"[Service]\nLimitNOFILE=65535\n",
						LimitsDropInPath, "root", "root", "0644") ||
					!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix,
						"sudo systemctl daemon-reload", "Failed to reload systemd."))
				{
					return;
				}

				if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix,
					"sudo systemctl enable nginx && sudo systemctl restart nginx",
					"NGINX restart failed. Check 'sudo journalctl -u nginx -n 50' and /var/log/nginx/error.log."))
				{
					return;
				}

				// systemctl can succeed while the master fails to open a listening socket, so
				// confirm NGINX really holds the first game port.
				bool listening = false;
				for (int attempt = 0; attempt < 10 && !listening; attempt++)
				{
					listening = await InstallerProcessHelper.RunProcessAsync(shell,
						$"{argPrefix} \"sudo ss -Hulnp 'sport = :7770'\"",
						(code, output, _) => code == 0 && output.Contains("nginx", StringComparison.Ordinal));
					if (!listening) await Task.Delay(500);
				}
				if (!listening)
				{
					await Log.Error("FishMMOInstaller",
						"NGINX is running but not listening on UDP 7770. Check /var/log/nginx/error.log.");
					return;
				}

				await Log.Info("FishMMOInstaller",
					$"FishMMO nginx.conf deployed for {domain} with UDP game ports 7770-7999 on {listenAddress}, and NGINX restarted. " +
					$"After changing game port ranges or the LAN address, run 'sudo env LISTEN_IP={listenAddress} {StreamGeneratorInstallPath}' " +
					"(with the new address if it changed).");
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

		// ──────────────────────────────────────────────────────────────────────────
		//  NSSM Windows Service Helpers (shared across all Windows service setup)
		// ──────────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Checks whether the current process is running with Administrator privileges.
		/// Returns true on non-Windows (Linux uses sudo for individual commands).
		/// </summary>
		public static bool IsRunningAsAdministrator()
		{
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				return true;

			try
			{
				using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
				var principal = new System.Security.Principal.WindowsPrincipal(identity);
				return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
			}
			catch
			{
				return false;
			}
		}

		/// <summary>Sets an NSSM parameter for a service (best-effort).</summary>
		public static async Task SetNssmParamAsync(string nssmExe, string serviceName,
			string parameter, string value)
		{
			await InstallerProcessHelper.RunProcessAsync(nssmExe,
				$"set \"{serviceName}\" {parameter} \"{value}\"",
				(exitCode, _, _) => exitCode == 0);
		}

		/// <summary>Starts an NSSM-managed service.</summary>
		public static async Task<bool> NssmStartServiceAsync(string nssmExe, string serviceName)
		{
			return await InstallerProcessHelper.RunProcessAsync(nssmExe,
				$"start \"{serviceName}\"",
				(exitCode, output, error) =>
					exitCode == 0 ||
					output.Contains("SERVICE_ALREADY_RUNNING") ||
					error.Contains("SERVICE_ALREADY_RUNNING"));
		}

		/// <summary>Stops an NSSM-managed service.</summary>
		/// <summary>Checks if an NSSM-managed service exists.</summary>
		public static async Task<bool> NssmServiceExistsAsync(string nssmExe, string serviceName)
		{
			return await InstallerProcessHelper.RunProcessAsync(nssmExe,
				$"status \"{serviceName}\"",
				(exitCode, output, _) => exitCode == 0 && !output.Contains("Can't open service"));
		}

		/// <summary>NSSM install with AppDirectory and Start set automatically.</summary>
		public static async Task<bool> NssmInstallAsync(string nssmExe, string serviceName,
			string exePath, string appDirectory, string appParameters)
		{
			bool created = await InstallerProcessHelper.RunProcessAsync(nssmExe,
				$"install \"{serviceName}\" \"{exePath}\"",
				(exitCode, _, _) => exitCode == 0);
			if (!created) return false;
			await SetNssmParamAsync(nssmExe, serviceName, "AppDirectory", appDirectory);
			await SetNssmParamAsync(nssmExe, serviceName, "AppParameters", appParameters);
			await SetNssmParamAsync(nssmExe, serviceName, "Start", "SERVICE_AUTO_START");
			return true;
		}

		/// <summary>
		/// Sets NSSM AppEnvironmentExtra entries for a service.
		/// Each string should be in KEY=VALUE format.
		/// </summary>
		public static async Task NssmSetEnvironmentAsync(string nssmExe, string serviceName,
			IReadOnlyList<string> environmentVariables)
		{
			foreach (string env in environmentVariables)
			{
				if (!string.IsNullOrWhiteSpace(env))
					await SetNssmParamAsync(nssmExe, serviceName, "AppEnvironmentExtra", env);
			}
		}


		public static async Task<bool> NssmStopServiceAsync(string nssmExe, string serviceName)
		{
			return await InstallerProcessHelper.RunProcessAsync(nssmExe,
				$"stop \"{serviceName}\"",
				(exitCode, output, error) =>
					exitCode == 0 ||
					output.Contains("SERVICE_NOT_RUNNING") ||
					error.Contains("SERVICE_NOT_RUNNING"));
		}

	}
}

