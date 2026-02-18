using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace FishMMO.Installer
{
	/// <summary>
	/// Handles Let's Encrypt certificate installation and renewal bootstrap for NGINX
	/// on Windows and Linux distributions supported by this installer.
	/// </summary>
	public static class LetsEncryptInstaller
	{
		/// <summary>
		/// Prompts for certificate configuration and installs/renews certificates for the current OS.
		/// </summary>
		public static async Task InstallLetsEncryptCertificate()
		{
			Console.Clear();
			InstallerProcessHelper.Log("--- Install / Renew Let's Encrypt Certificate ---");

			if (!await NGINXInstaller.IsNGINXInstalledAsync())
			{
				InstallerProcessHelper.Log("NGINX was not detected. Install and configure NGINX before requesting certificates.");
				return;
			}

			string domainInput = (InstallerProcessHelper.PromptForInput("Enter domains (comma-separated, e.g. fishmmo.com,play.fishmmo.com): ") ?? string.Empty).Trim();
			string[] domains = ParseDomains(domainInput);
			if (domains.Length == 0)
			{
				InstallerProcessHelper.Log("No valid domains were provided. Certificate operation cancelled.");
				return;
			}

			string email = (InstallerProcessHelper.PromptForInput("Enter email for Let's Encrypt registration: ") ?? string.Empty).Trim();
			if (string.IsNullOrWhiteSpace(email))
			{
				InstallerProcessHelper.Log("Email is required. Certificate operation cancelled.");
				return;
			}

			bool useStaging = InstallerProcessHelper.PromptForYesNo("Use Let's Encrypt staging environment (recommended for first run)?");

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				string defaultNginxConfPath = Path.Combine(NGINXInstaller.GetExpectedWindowsNginxHomePath(), "conf", "nginx.conf");
				string defaultWebRoot = Path.Combine(NGINXInstaller.GetExpectedWindowsNginxHomePath(), "html");
				string nginxConfPath = PromptWithDefault("Enter nginx.conf path", defaultNginxConfPath);
				string webRootPath = PromptWithDefault("Enter ACME challenge web root path", defaultWebRoot);

				await InstallLetsEncryptWindows(domains, email, nginxConfPath, webRootPath, useStaging);
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				string nginxConfPath = PromptWithDefault("Enter nginx.conf path", InstallationConstants.LinuxNginxConfigurationPath);
				string webRootPath = PromptWithDefault("Enter ACME challenge web root path", InstallationConstants.LinuxCertbotWebRoot);
				string webServersPath = PromptWithDefault("Enter FishMMO web servers path", InstallationConstants.LinuxFishMMOWebServersPath);

				await InstallLetsEncryptLinux(domains, email, nginxConfPath, webRootPath, webServersPath, useStaging);
			}
			else
			{
				InstallerProcessHelper.Log("Unsupported operating system for Let's Encrypt certificate automation.");
			}
		}

		/// <summary>
		/// Installs and runs certbot to request/renew certificates on Linux.
		/// </summary>
		/// <param name="domains">Domain list included in the certificate.</param>
		/// <param name="email">Let's Encrypt registration email.</param>
		/// <param name="nginxConfPath">Path to nginx.conf for post-update path synchronization.</param>
		/// <param name="webRootPath">Web root used for ACME HTTP-01 challenges.</param>
		/// <param name="webServersPath">FishMMO webservers path used for environment validation.</param>
		/// <param name="useStaging">Whether to use Let's Encrypt staging endpoint.</param>
		private static async Task InstallLetsEncryptLinux(
			IReadOnlyList<string> domains,
			string email,
			string nginxConfPath,
			string webRootPath,
			string webServersPath,
			bool useStaging)
		{
			InstallerProcessHelper.Log("Configuring Let's Encrypt on Linux...");
			(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();

			var packageNames = new Dictionary<string, string>
			{
				["pacman"] = "certbot certbot-nginx",
				["apt-get"] = "certbot python3-certbot-nginx",
				["dnf"] = "certbot python3-certbot-nginx",
				["yum"] = "certbot python3-certbot-nginx"
			};

			var detected = await InstallerProcessHelper.DetectLinuxPackageManagerAsync(packageNames);
			if (detected == null)
			{
				InstallerProcessHelper.Log("No supported package manager found for certbot installation.");
				return;
			}

			var (updateCommand, installCommand, managerName) = detected.Value;
			InstallerProcessHelper.Log($"Using {managerName} to install certbot dependencies.");

			if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, updateCommand, "Failed to update package metadata for certbot."))
			{
				return;
			}

			if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, installCommand, "Failed to install certbot packages."))
			{
				return;
			}

			if (!Directory.Exists(webServersPath))
			{
				InstallerProcessHelper.Log($"Warning: Web servers path '{webServersPath}' was not found. Verify deployment layout before proceeding.");
			}

			if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, $"sudo mkdir -p \"{webRootPath}\"", "Failed to create certbot web root directory."))
			{
				return;
			}

			string domainArguments = string.Join(" ", domains.Select(domain => $"-d \"{domain}\""));
			string stagingFlag = useStaging ? " --staging" : string.Empty;
			string certbotCommand = $"sudo certbot certonly --webroot -w \"{webRootPath}\" {domainArguments} --agree-tos -m \"{email}\" --non-interactive --keep-until-expiring{stagingFlag}";

			if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, certbotCommand, "Failed to request/renew Let's Encrypt certificate."))
			{
				return;
			}

			ValidateLinuxNginxAcmeConfiguration(nginxConfPath, webRootPath);
			ApplyCertificatePathsToNginxConfig(nginxConfPath, domains[0], isLinux: true);

			if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, "sudo nginx -t", "NGINX config test failed after certificate updates."))
			{
				return;
			}

			if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, "sudo systemctl reload nginx", "Failed to reload NGINX after certificate update."))
			{
				return;
			}

			InstallerProcessHelper.Log("Let's Encrypt certificate was installed/renewed and NGINX was reloaded.");
		}

		/// <summary>
		/// Installs and runs win-acme to request/renew certificates on Windows.
		/// </summary>
		/// <param name="domains">Domain list included in the certificate.</param>
		/// <param name="email">Let's Encrypt registration email.</param>
		/// <param name="nginxConfPath">Path to nginx.conf for certificate path synchronization.</param>
		/// <param name="webRootPath">Web root used for ACME HTTP-01 challenges.</param>
		/// <param name="useStaging">Whether to use Let's Encrypt staging endpoint.</param>
		private static async Task InstallLetsEncryptWindows(
			IReadOnlyList<string> domains,
			string email,
			string nginxConfPath,
			string webRootPath,
			bool useStaging)
		{
			InstallerProcessHelper.Log("Configuring Let's Encrypt on Windows using win-acme...");

			string downloadPath = await InstallerProcessHelper.DownloadFileAsync(InstallationConstants.WinAcmeDownloadUrl, InstallationConstants.WinAcmeFileName);
			string winAcmeExtractDirectory = Path.Combine(InstallerProcessHelper.GetWorkingDirectory(), "win-acme");
			string winAcmeExecutablePath = Path.Combine(winAcmeExtractDirectory, "wacs.exe");
			string certificateOutputDirectory = Path.Combine(NGINXInstaller.GetExpectedWindowsNginxHomePath(), "certificates", domains[0]);

			if (Directory.Exists(winAcmeExtractDirectory))
			{
				Directory.Delete(winAcmeExtractDirectory, true);
			}
			Directory.CreateDirectory(winAcmeExtractDirectory);
			Directory.CreateDirectory(certificateOutputDirectory);

			ZipFile.ExtractToDirectory(downloadPath, winAcmeExtractDirectory, true);

			if (!File.Exists(winAcmeExecutablePath))
			{
				InstallerProcessHelper.Log($"win-acme executable was not found at '{winAcmeExecutablePath}'.");
				return;
			}

			string domainList = string.Join(",", domains);
			string stagingFlag = useStaging ? "--test " : string.Empty;
			string arguments =
				$"{stagingFlag}" +
				"--target manual " +
				$"--host \"{domainList}\" " +
				"--validation filesystem " +
				$"--webroot \"{webRootPath}\" " +
				"--store pemfiles " +
				$"--pemfilespath \"{certificateOutputDirectory}\" " +
				"--installation none " +
				"--accepttos " +
				$"--emailaddress \"{email}\" " +
				"--usedefaulttaskuser";

			bool certificateInstalled = await InstallerProcessHelper.RunProcessAsync(
				winAcmeExecutablePath,
				arguments,
				(exitCode, output, error) => exitCode == 0);

			if (!certificateInstalled)
			{
				InstallerProcessHelper.Log("win-acme failed to request/renew certificate. Verify DNS and port 80 reachability.");
				return;
			}

			(string certificatePath, string privateKeyPath) = ResolveWindowsCertificatePaths(certificateOutputDirectory);
			if (string.IsNullOrWhiteSpace(certificatePath) || string.IsNullOrWhiteSpace(privateKeyPath))
			{
				InstallerProcessHelper.Log("Could not locate generated PEM certificate files from win-acme output.");
				return;
			}

			ApplyCertificatePathsToNginxConfig(nginxConfPath, certificatePath, privateKeyPath);

			string nginxExecutablePath = Path.Combine(NGINXInstaller.GetExpectedWindowsNginxHomePath(), "nginx.exe");
			if (!File.Exists(nginxExecutablePath))
			{
				InstallerProcessHelper.Log($"NGINX executable was not found at '{nginxExecutablePath}'. Install NGINX first.");
				return;
			}

			if (!await InstallerProcessHelper.RunProcessAsync(nginxExecutablePath, "-t", (exitCode, output, error) => exitCode == 0))
			{
				InstallerProcessHelper.Log("NGINX config test failed after certificate updates.");
				return;
			}

			await InstallerProcessHelper.RunProcessAsync(nginxExecutablePath, "-s reload");
			await InstallerProcessHelper.RunProcessAsync("sc.exe", $"start \"{InstallationConstants.NGINXWindowsServiceName}\"");

			InstallerProcessHelper.Log("Let's Encrypt certificate was installed/renewed and NGINX was reloaded.");
		}

		/// <summary>
		/// Applies certificate and key path replacements to nginx.conf for all SSL server blocks.
		/// </summary>
		/// <param name="nginxConfPath">Target nginx.conf path.</param>
		/// <param name="certificateDomain">Primary certificate domain used in path construction.</param>
		/// <param name="isLinux">True for Linux path layout, false for Windows path resolution from generated certificate files.</param>
		private static void ApplyCertificatePathsToNginxConfig(string nginxConfPath, string certificateDomain, bool isLinux)
		{
			if (!File.Exists(nginxConfPath))
			{
				InstallerProcessHelper.Log($"Skipping nginx.conf update because file '{nginxConfPath}' does not exist.");
				return;
			}

			string certificatePath;
			string privateKeyPath;
			if (isLinux)
			{
				certificatePath = $"/etc/letsencrypt/live/{certificateDomain}/fullchain.pem";
				privateKeyPath = $"/etc/letsencrypt/live/{certificateDomain}/privkey.pem";
			}
			else
			{
				string certificateDirectory = Path.Combine(NGINXInstaller.GetExpectedWindowsNginxHomePath(), "certificates", certificateDomain);
				(string resolvedCertificatePath, string resolvedPrivateKeyPath) = ResolveWindowsCertificatePaths(certificateDirectory);
				certificatePath = resolvedCertificatePath;
				privateKeyPath = resolvedPrivateKeyPath;

				if (string.IsNullOrWhiteSpace(certificatePath) || string.IsNullOrWhiteSpace(privateKeyPath))
				{
					InstallerProcessHelper.Log("Skipping nginx.conf update because generated Windows PEM files were not found.");
					return;
				}
			}

			ApplyCertificatePathsToNginxConfig(nginxConfPath, certificatePath, privateKeyPath);
		}

		/// <summary>
		/// Applies certificate and key path replacements to nginx.conf for all SSL server blocks.
		/// </summary>
		/// <param name="nginxConfPath">Target nginx.conf path.</param>
		/// <param name="certificatePath">Certificate file path for ssl_certificate.</param>
		/// <param name="privateKeyPath">Private key file path for ssl_certificate_key.</param>
		private static void ApplyCertificatePathsToNginxConfig(string nginxConfPath, string certificatePath, string privateKeyPath)
		{
			if (!File.Exists(nginxConfPath))
			{
				InstallerProcessHelper.Log($"Skipping nginx.conf update because file '{nginxConfPath}' does not exist.");
				return;
			}

			string normalizedCertificatePath = certificatePath.Replace("\\", "/");
			string normalizedPrivateKeyPath = privateKeyPath.Replace("\\", "/");

			string nginxConfigContent = File.ReadAllText(nginxConfPath);
			string updatedContent = Regex.Replace(
				nginxConfigContent,
				@"ssl_certificate\s+[^;]+;",
				$"ssl_certificate     {normalizedCertificatePath};",
				RegexOptions.IgnoreCase);

			updatedContent = Regex.Replace(
				updatedContent,
				@"ssl_certificate_key\s+[^;]+;",
				$"ssl_certificate_key {normalizedPrivateKeyPath};",
				RegexOptions.IgnoreCase);

			if (updatedContent != nginxConfigContent)
			{
				File.WriteAllText(nginxConfPath, updatedContent);
				InstallerProcessHelper.Log($"Updated certificate paths in '{nginxConfPath}'.");
			}
			else
			{
				InstallerProcessHelper.Log("No SSL directives were updated in nginx.conf (directives not found or already up to date).");
			}
		}

		/// <summary>
		/// Locates generated Windows PEM certificate files in the target win-acme output folder.
		/// </summary>
		/// <param name="certificateDirectory">Directory where win-acme writes PEM files.</param>
		/// <returns>Tuple of (certificatePath, privateKeyPath). Empty values indicate unresolved paths.</returns>
		private static (string certificatePath, string privateKeyPath) ResolveWindowsCertificatePaths(string certificateDirectory)
		{
			if (!Directory.Exists(certificateDirectory))
			{
				return (string.Empty, string.Empty);
			}

			string[] candidateCertificateFiles =
			[
				"fullchain.pem",
				"chain.pem",
				"cert.pem"
			];

			string[] candidateKeyFiles =
			[
				"privkey.pem",
				"key.pem"
			];

			string? certificatePath = candidateCertificateFiles
				.Select(fileName => Path.Combine(certificateDirectory, fileName))
				.FirstOrDefault(File.Exists);

			string? privateKeyPath = candidateKeyFiles
				.Select(fileName => Path.Combine(certificateDirectory, fileName))
				.FirstOrDefault(File.Exists);

			if (string.IsNullOrWhiteSpace(certificatePath))
			{
				certificatePath = Directory.GetFiles(certificateDirectory, "*.pem", SearchOption.TopDirectoryOnly)
					.FirstOrDefault(path => !Path.GetFileName(path).Contains("key", StringComparison.OrdinalIgnoreCase));
			}

			if (string.IsNullOrWhiteSpace(privateKeyPath))
			{
				privateKeyPath = Directory.GetFiles(certificateDirectory, "*.pem", SearchOption.TopDirectoryOnly)
					.FirstOrDefault(path => Path.GetFileName(path).Contains("key", StringComparison.OrdinalIgnoreCase));
			}

			return (certificatePath ?? string.Empty, privateKeyPath ?? string.Empty);
		}

		/// <summary>
		/// Verifies that nginx.conf contains an ACME challenge location and expected web root.
		/// </summary>
		/// <param name="nginxConfPath">Path to nginx.conf.</param>
		/// <param name="webRootPath">Configured certbot web root path.</param>
		private static void ValidateLinuxNginxAcmeConfiguration(string nginxConfPath, string webRootPath)
		{
			if (!File.Exists(nginxConfPath))
			{
				InstallerProcessHelper.Log($"Warning: nginx.conf was not found at '{nginxConfPath}'. Skipping ACME location validation.");
				return;
			}

			string configContent = File.ReadAllText(nginxConfPath);
			bool hasAcmeLocation = configContent.Contains("/.well-known/acme-challenge/", StringComparison.OrdinalIgnoreCase);
			bool hasExpectedRoot = configContent.Contains($"root {webRootPath};", StringComparison.OrdinalIgnoreCase);

			if (!hasAcmeLocation || !hasExpectedRoot)
			{
				InstallerProcessHelper.Log("Warning: nginx.conf ACME challenge location/root does not match provided web root. Cert issuance/renewal may fail.");
			}
		}

		/// <summary>
		/// Prompts for text input with a default value fallback.
		/// </summary>
		/// <param name="prompt">Prompt message prefix.</param>
		/// <param name="defaultValue">Default value when user enters no text.</param>
		/// <returns>User input or default value.</returns>
		private static string PromptWithDefault(string prompt, string defaultValue)
		{
			string? inputValue = InstallerProcessHelper.PromptForInput($"{prompt} [{defaultValue}]: ");
			if (string.IsNullOrWhiteSpace(inputValue))
			{
				return defaultValue;
			}

			return inputValue.Trim();
		}

		/// <summary>
		/// Parses domain input from comma-separated text and returns unique valid hostnames.
		/// </summary>
		/// <param name="domainInput">Raw domain input text.</param>
		/// <returns>Validated domain array.</returns>
		private static string[] ParseDomains(string domainInput)
		{
			if (string.IsNullOrWhiteSpace(domainInput))
			{
				return Array.Empty<string>();
			}

			Regex hostnameRegex = new Regex(@"^(?:\*\.)?(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,}$", RegexOptions.Compiled);
			return domainInput
				.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Where(domain => !domain.StartsWith("*.", StringComparison.Ordinal))
				.Where(domain => hostnameRegex.IsMatch(domain))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray();
		}
	}
}