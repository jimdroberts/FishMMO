using FishMMO.Logging;
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
			await Log.Info("FishMMOInstaller", "--- Install / Renew Let's Encrypt Certificate ---");

			// The DNS challenge needs neither nginx nor an open port 80, which is what lets the
			// certificate exist BEFORE the FishMMO nginx.conf (which references it) is deployed.
			bool useDns = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && PromptForDnsChallenge();

			if (!useDns && !await NGINXInstaller.IsNGINXInstalledAsync())
			{
				await Log.Info("FishMMOInstaller", "NGINX was not detected. Install and configure NGINX before requesting certificates.");
				return;
			}

			string[] domains;
			if (useDns)
			{
				string rootInput = (InstallerProcessHelper.PromptForInput(
					"Enter your root domain (e.g. example.com). The certificate covers it and *.example.com: ") ?? string.Empty)
					.Trim().TrimEnd('.').ToLowerInvariant();
				string[] parsed = ParseDomains(rootInput);
				if (parsed.Length != 1)
				{
					await Log.Info("FishMMOInstaller", "A single valid root domain is required. Certificate operation cancelled.");
					return;
				}
				domains = new[] { parsed[0], "*." + parsed[0] };
			}
			else
			{
				string domainInput = (InstallerProcessHelper.PromptForInput("Enter domains (comma-separated, e.g. fishmmo.com,api.fishmmo.com,play.fishmmo.com,game.fishmmo.com,panel.fishmmo.com): ") ?? string.Empty).Trim();
				domains = ParseDomains(domainInput);
				if (domains.Length == 0)
				{
					await Log.Info("FishMMOInstaller", "No valid domains were provided. Certificate operation cancelled.");
					return;
				}
			}

			string email = (InstallerProcessHelper.PromptForInput("Enter email for Let's Encrypt registration: ") ?? string.Empty).Trim();
			if (!EmailPattern.IsMatch(email))
			{
				await Log.Info("FishMMOInstaller", "A valid email is required. Certificate operation cancelled.");
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
			else if (useDns)
			{
				await InstallLetsEncryptLinuxDns(domains[0], email, useStaging);
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				string nginxConfPath = PromptWithDefault("Enter active nginx.conf path (not the repository template)", "/etc/nginx/nginx.conf");
				string webRootPath = PromptWithDefault("Enter ACME challenge web root path", InstallationConstants.LinuxCertbotWebRoot);
				string webServersPath = PromptWithDefault("Enter FishMMO web servers path", InstallationConstants.LinuxFishMMOWebServersPath);

				await InstallLetsEncryptLinux(domains, email, nginxConfPath, webRootPath, webServersPath, useStaging);
			}
			else
			{
				await Log.Warning("FishMMOInstaller", "Unsupported operating system for Let's Encrypt certificate automation.");
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
			await Log.Info("FishMMOInstaller", "Configuring Let's Encrypt on Linux...");
			(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();

			if (!await InstallCertbotPackagesAsync(shell, argPrefix, includeDnsTools: false))
			{
				return;
			}

			if (!Directory.Exists(webServersPath))
			{
				await Log.Warning("FishMMOInstaller", $"Warning: Web servers path '{webServersPath}' was not found. Verify deployment layout before proceeding.");
			}

			if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, $"sudo mkdir -p \"{webRootPath}\"", "Failed to create certbot web root directory."))
			{
				return;
			}

			ValidateLinuxNginxAcmeConfiguration(nginxConfPath, webRootPath);

			string domainArguments = string.Join(" ", domains.Select(domain => $"-d \"{domain}\""));
			string stagingFlag = useStaging ? " --staging" : string.Empty;
			string certbotCommand = $"sudo certbot certonly --cert-name \"{domains[0]}\" --webroot -w \"{webRootPath}\" {domainArguments} --agree-tos -m \"{email}\" --non-interactive --keep-until-expiring --reuse-key{stagingFlag}";

			if (!await InstallerProcessHelper.RunShellCommandWithLiveOutputAsync(shell, argPrefix, certbotCommand, "Failed to request/renew Let's Encrypt certificate."))
			{
				return;
			}

			if (!await GameServerCertificateInstaller.InstallAsync($"/etc/letsencrypt/live/{domains[0]}"))
			{
				return;
			}

			ApplyCertificatePathsToNginxConfig(nginxConfPath, domains[0], isLinux: true);

			if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, "sudo nginx -t", "NGINX config test failed after certificate updates."))
			{
				return;
			}

			if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, "sudo systemctl reload nginx", "Failed to reload NGINX after certificate update."))
			{
				return;
			}

			await Log.Info("FishMMOInstaller", "Let's Encrypt certificate was installed/renewed and NGINX was reloaded.");
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
			await Log.Info("FishMMOInstaller", "Configuring Let's Encrypt on Windows using win-acme...");

			string? downloadPath = await DownloadHelper.DownloadFileWithProgressAsync(
				InstallationConstants.WinAcmeDownloadUrl,
				InstallationConstants.WinAcmeFileName,
				new DownloadHelper.ConsoleProgress());

			if (downloadPath == null)
			{
				await Log.Error("FishMMOInstaller", "Failed to download win-acme.");
				return;
			}
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
				await Log.Info("FishMMOInstaller", $"win-acme executable was not found at '{winAcmeExecutablePath}'.");
				return;
			}

			string domainList = string.Join(",", domains);
			string stagingFlag = useStaging ? "--test " : string.Empty;
			string arguments =
				$"{stagingFlag}" +
				"--source manual " +
				$"--host \"{domainList}\" " +
				"--validation filesystem " +
				$"--webroot \"{webRootPath}\" " +
				"--store pemfiles " +
				$"--pemfilespath \"{certificateOutputDirectory}\" " +
				"--installation none " +
				"--accepttos " +
				$"--emailaddress \"{email}\"";

			bool certificateInstalled = await InstallerProcessHelper.RunProcessWithLiveOutputAsync(
				winAcmeExecutablePath,
				arguments,
				(exitCode, output, error) =>
				{
					if (exitCode != 0)
					{
						if (!string.IsNullOrWhiteSpace(output))
						{
							Console.Error.WriteLine($"win-acme output:\n{output}");
						}
						if (!string.IsNullOrWhiteSpace(error))
						{
							Console.Error.WriteLine($"win-acme errors:\n{error}");
						}
						return false;
					}
					return true;
				});

			if (!certificateInstalled)
			{
				await Log.Error("FishMMOInstaller", "win-acme failed to request/renew certificate. Verify DNS and port 80 reachability.");
				return;
			}

			(string certificatePath, string privateKeyPath) = ResolveWindowsCertificatePaths(certificateOutputDirectory);
			if (string.IsNullOrWhiteSpace(certificatePath) || string.IsNullOrWhiteSpace(privateKeyPath))
			{
				await Log.Error("FishMMOInstaller", "Could not locate generated PEM certificate files from win-acme output.");
				return;
			}

			ApplyCertificatePathsToNginxConfig(nginxConfPath, certificatePath, privateKeyPath);

			string nginxExecutablePath = Path.Combine(NGINXInstaller.GetExpectedWindowsNginxHomePath(), "nginx.exe");
			if (!File.Exists(nginxExecutablePath))
			{
				await Log.Error("FishMMOInstaller", $"NGINX executable was not found at '{nginxExecutablePath}'. Install NGINX first.");
				return;
			}

			if (!await InstallerProcessHelper.RunProcessAsync(nginxExecutablePath, "-t", (exitCode, output, error) => exitCode == 0))
			{
				await Log.Error("FishMMOInstaller", "NGINX config test failed after certificate updates.");
				return;
			}

			await InstallerProcessHelper.RunProcessAsync(nginxExecutablePath, "-s reload");
			await InstallerProcessHelper.RunProcessAsync("sc.exe", $"start \"{InstallationConstants.NGINXWindowsServiceName}\"");

			await Log.Info("FishMMOInstaller", "Let's Encrypt certificate was installed/renewed and NGINX was reloaded.");
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
				_ = Log.Warning("FishMMOInstaller", $"Skipping nginx.conf update because file '{nginxConfPath}' does not exist.");
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
					_ = Log.Warning("FishMMOInstaller", "Skipping nginx.conf update because generated Windows PEM files were not found.");
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
				_ = Log.Warning("FishMMOInstaller", $"Skipping nginx.conf update because file '{nginxConfPath}' does not exist.");
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
				_ = Log.Info("FishMMOInstaller", $"Updated certificate paths in '{nginxConfPath}'.");
			}
			else
			{
				_ = Log.Warning("FishMMOInstaller", "No SSL directives were updated in nginx.conf (directives not found or already up to date).");
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
				_ = Log.Warning("FishMMOInstaller", $"Warning: nginx.conf was not found at '{nginxConfPath}'. Skipping ACME location validation.");
				return;
			}

			string configContent = File.ReadAllText(nginxConfPath);
			bool hasAcmeLocation = configContent.Contains("/.well-known/acme-challenge/", StringComparison.OrdinalIgnoreCase);
			bool hasExpectedRoot = configContent.Contains($"root {webRootPath};", StringComparison.OrdinalIgnoreCase);

			if (!hasAcmeLocation || !hasExpectedRoot)
			{
				_ = Log.Warning("FishMMOInstaller", "The selected nginx.conf does not directly declare the ACME challenge location and web root. If an included file does not supply it, configure HTTP port 80 to serve /.well-known/acme-challenge/ from the specified web root before requesting a certificate. The repository template alone does not configure the running nginx service.");
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

		/// <summary>Where the Porkbun DNS-01 hook is installed. Certbot's renewal configuration records this path.</summary>
		public const string PorkbunHookPath = "/etc/letsencrypt/fishmmo-porkbun-hook.py";

		/// <summary>Root-only Porkbun API credentials read by <see cref="PorkbunHookPath"/>.</summary>
		public const string PorkbunCredentialsPath = "/etc/letsencrypt/fishmmo-porkbun.ini";

		// The address is placed inside a double-quoted shell argument, so quotes, '$', '`' and
		// backslashes are refused rather than escaped.
		private static readonly Regex EmailPattern = new Regex(@"^[^@\s""'`$\\]+@[^@\s""'`$\\]+\.[^@\s""'`$\\]+$", RegexOptions.Compiled);

		// Porkbun keys are 'pk1_' / 'sk1_' followed by hex; anything else is a paste error.
		private static readonly Regex PorkbunKeyPattern = new Regex(@"^[A-Za-z0-9_]{8,128}$", RegexOptions.Compiled);

		/// <summary>Asks which ACME challenge to use. Returns true for DNS via the Porkbun API.</summary>
		private static bool PromptForDnsChallenge()
		{
			Console.WriteLine("How should Let's Encrypt check that you control the domain?");
			Console.WriteLine("1 : DNS, through the Porkbun API (recommended). Needs no open port 80 and no nginx yet,");
			Console.WriteLine("    and issues one wildcard certificate (example.com + *.example.com) for every host.");
			Console.WriteLine("2 : HTTP on port 80. nginx must already serve /.well-known/acme-challenge/ from the web root,");
			Console.WriteLine("    and every host name must be listed.");
			return PromptWithDefault("Choice", "1") != "2";
		}

		/// <summary>Installs certbot (and, for the DNS challenge, dig) with the distribution's package manager.</summary>
		private static async Task<bool> InstallCertbotPackagesAsync(string shell, string argPrefix, bool includeDnsTools)
		{
			var packageNames = new Dictionary<string, string>
			{
				["pacman"] = "certbot certbot-nginx openssl" + (includeDnsTools ? " bind" : ""),
				["apt-get"] = "certbot python3-certbot-nginx openssl" + (includeDnsTools ? " dnsutils" : ""),
				["dnf"] = "certbot python3-certbot-nginx openssl" + (includeDnsTools ? " bind-utils" : ""),
				["yum"] = "certbot python3-certbot-nginx openssl" + (includeDnsTools ? " bind-utils" : "")
			};

			var detected = await LinuxPackageManagerHelper.DetectAsync(packageNames);
			if (detected == null)
			{
				await Log.Warning("FishMMOInstaller", "No supported package manager found for certbot installation.");
				return false;
			}

			await Log.Info("FishMMOInstaller", $"Using {detected.ManagerName} to install certbot dependencies.");

			if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, detected.UpdateCommand, "Failed to update package metadata for certbot."))
			{
				return false;
			}

			return await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, detected.InstallCommand, "Failed to install certbot packages.");
		}

		/// <summary>
		/// Requests (or renews) a wildcard certificate for <paramref name="rootDomain"/> with the
		/// DNS-01 challenge answered through the Porkbun API, then deploys it for the game servers.
		/// </summary>
		/// <remarks>
		/// The certificate is issued with <c>--reuse-key</c>, as is the HTTP route: the release
		/// client pins the SPKI of this key, and certbot's default of a fresh key on every renewal
		/// would break every shipped client about every 60 days.
		/// </remarks>
		private static async Task InstallLetsEncryptLinuxDns(string rootDomain, string email, bool useStaging)
		{
			await Log.Info("FishMMOInstaller", "Configuring Let's Encrypt (DNS challenge through Porkbun) on Linux...");
			(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();

			if (!await InstallCertbotPackagesAsync(shell, argPrefix, includeDnsTools: true))
			{
				return;
			}

			if (!await EnsurePorkbunCredentialsAsync(rootDomain))
			{
				return;
			}

			string hook = ReadEmbeddedText("FishMMO.Installer.Resources.porkbun-dns-hook.py");
			if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, "sudo install -d -o root -g root -m 0755 /etc/letsencrypt", "Failed to create /etc/letsencrypt.") ||
				!await LinuxConfigHardeningHelper.SudoInstallAsync(hook, PorkbunHookPath, "root", "root", "0755"))
			{
				return;
			}

			string stagingFlag = useStaging ? " --staging" : string.Empty;
			string certbotCommand =
				$"sudo certbot certonly --cert-name \"{rootDomain}\" --manual --preferred-challenges dns " +
				$"--manual-auth-hook \"{PorkbunHookPath} auth\" --manual-cleanup-hook \"{PorkbunHookPath} cleanup\" " +
				$"-d \"{rootDomain}\" -d \"*.{rootDomain}\" --agree-tos -m \"{email}\" --non-interactive " +
				$"--keep-until-expiring --reuse-key{stagingFlag}";

			await Log.Info("FishMMOInstaller", "Requesting the certificate. Waiting for the TXT records to reach every Porkbun nameserver can take a few minutes.");
			if (!await InstallerProcessHelper.RunShellCommandWithLiveOutputAsync(shell, argPrefix, certbotCommand,
				"Failed to request/renew the Let's Encrypt certificate. If the hook reported API_ACCESS_DISABLED, turn on API Access for the domain in Porkbun."))
			{
				return;
			}

			if (!await GameServerCertificateInstaller.InstallAsync($"/etc/letsencrypt/live/{rootDomain}"))
			{
				return;
			}

			await Log.Info("FishMMOInstaller",
				$"Certificate for {rootDomain} and *.{rootDomain} is in /etc/letsencrypt/live/{rootDomain}/ and deployed to " +
				$"{GameServerCertificateInstaller.CertificateDirectory}. Renewals repeat the DNS challenge automatically. " +
				"Next: Web Server > Deploy FishMMO nginx.conf, entering the same root domain.");
		}

		// One DNS label or several (e.g. 'eu.play'), relative to the zone; no apex, no wildcard.
		private static readonly Regex RecordNamePattern = new Regex(
			@"^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)*$", RegexOptions.Compiled);

		/// <summary>
		/// Points the game's host names (api, play, game, panel by default) at this server's public
		/// IPv4 address with A records at Porkbun, using the same root-only API credentials as the
		/// DNS challenge. Shows the changes first and applies them only after confirmation.
		/// </summary>
		public static async Task PointDnsRecordsAsync()
		{
			Console.Clear();
			await Log.Info("FishMMOInstaller", "--- Point DNS records at this host (Porkbun) ---");
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				await Log.Warning("FishMMOInstaller", "This option is Linux only. Create the A records in Porkbun's DNS page instead.");
				return;
			}

			string rootDomain = (InstallerProcessHelper.PromptForInput("Root domain registered at Porkbun (e.g. example.com): ") ?? "")
				.Trim().TrimEnd('.').ToLowerInvariant();
			if (!NGINXInstaller.RootDomainPattern.IsMatch(rootDomain))
			{
				await Log.Warning("FishMMOInstaller", "A valid root domain such as example.com is required. Nothing was changed.");
				return;
			}

			Console.WriteLine();
			Console.WriteLine("Each name below gets one A record pointing at this server's public IPv4 address:");
			Console.WriteLine($"  api.{rootDomain} (IPFetch/Patcher), play.{rootDomain} (browser client), " +
				$"game.{rootDomain} (QUIC game servers), panel.{rootDomain} (Control Panel).");
			Console.WriteLine($"The bare {rootDomain}, www and every other record are left alone.");
			string[] names = PromptWithDefault("Host names, space separated", "api play game panel")
				.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (names.Length == 0 || names.Any(name => !RecordNamePattern.IsMatch(name)))
			{
				await Log.Warning("FishMMOInstaller", "Host names must be plain labels such as 'play' (no apex, no '*'). Nothing was changed.");
				return;
			}

			Console.WriteLine("Public IPv4 address: leave blank to use the address Porkbun sees this server connect from");
			Console.WriteLine("(right for a home connection; enter one only if the server's traffic leaves through another address).");
			string address = (InstallerProcessHelper.PromptForInput("Public IPv4 address [detect]: ") ?? "").Trim();
			if (address.Length > 0 && !System.Net.IPAddress.TryParse(address, out _))
			{
				await Log.Warning("FishMMOInstaller", $"'{address}' is not an IP address. Nothing was changed.");
				return;
			}

			if (!await EnsurePorkbunCredentialsAsync(rootDomain))
			{
				return;
			}
			(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();
			string hook = ReadEmbeddedText("FishMMO.Installer.Resources.porkbun-dns-hook.py");
			if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, "sudo install -d -o root -g root -m 0755 /etc/letsencrypt", "Failed to create /etc/letsencrypt.") ||
				!await LinuxConfigHardeningHelper.SudoInstallAsync(hook, PorkbunHookPath, "root", "root", "0755"))
			{
				return;
			}

			string arguments = (address.Length > 0 ? $"--ip {address} " : "") + string.Join(' ', names);
			await Log.Info("FishMMOInstaller", "Checking the current records (nothing is changed yet)...");
			if (!await InstallerProcessHelper.RunShellCommandWithLiveOutputAsync(shell, argPrefix,
					$"sudo {PorkbunHookPath} records --dry-run {arguments}",
					"Reading the Porkbun records failed. If it reported API_ACCESS_DISABLED, turn on API Access for the domain in Porkbun.") ||
				!InstallerProcessHelper.PromptForYesNo("Apply these DNS changes?"))
			{
				return;
			}
			if (await InstallerProcessHelper.RunShellCommandWithLiveOutputAsync(shell, argPrefix,
					$"sudo {PorkbunHookPath} records {arguments}", "Updating the Porkbun records failed. Review the output above."))
			{
				await Log.Info("FishMMOInstaller",
					"DNS records updated. New names usually resolve within minutes (TTL 600 s). " +
					$"If the ISP changes the address, run this again or schedule 'sudo {PorkbunHookPath} records {string.Join(' ', names)}'.");
			}
		}

		/// <summary>
		/// Writes <see cref="PorkbunCredentialsPath"/> (root:root 0600), reusing saved keys when the
		/// operator agrees. The zone is always rewritten to <paramref name="rootDomain"/>.
		/// </summary>
		private static async Task<bool> EnsurePorkbunCredentialsAsync(string rootDomain)
		{
			string? apiKey = null;
			string? secretKey = null;

			string? existing = await LinuxConfigHardeningHelper.SudoReadAsync(PorkbunCredentialsPath);
			if (!string.IsNullOrWhiteSpace(existing) &&
				InstallerProcessHelper.PromptForYesNo($"Reuse the Porkbun API keys saved in {PorkbunCredentialsPath}?"))
			{
				foreach (string line in existing.Split('\n'))
				{
					string trimmed = line.Trim();
					if (trimmed.StartsWith("PORKBUN_API_KEY=", StringComparison.Ordinal)) apiKey = trimmed["PORKBUN_API_KEY=".Length..];
					if (trimmed.StartsWith("PORKBUN_SECRET_API_KEY=", StringComparison.Ordinal)) secretKey = trimmed["PORKBUN_SECRET_API_KEY=".Length..];
				}
			}

			if (apiKey == null || secretKey == null)
			{
				Console.WriteLine();
				Console.WriteLine("Porkbun setup (once):");
				Console.WriteLine("  1. https://porkbun.com/account/api : create an API key with DNS permission.");
				Console.WriteLine($"  2. Domain Management > {rootDomain} > Details : turn on API Access.");
				Console.WriteLine("The keys are stored root-only and used only for _acme-challenge TXT records and the game's A records.");
				apiKey = InstallerProcessHelper.PromptForRequiredInput("Porkbun API key (pk1_...): ");
				secretKey = InstallerProcessHelper.PromptForRequiredPassword("Porkbun secret API key (sk1_...): ").Trim();
			}

			if (!PorkbunKeyPattern.IsMatch(apiKey) || !PorkbunKeyPattern.IsMatch(secretKey))
			{
				await Log.Error("FishMMOInstaller", "The Porkbun keys must be the pk1_/sk1_ values from the API page, without spaces.");
				return false;
			}

			string content =
				"# Porkbun API credentials for the FishMMO certbot DNS-01 hook. root:root 0600.\n" +
				$"PORKBUN_API_KEY={apiKey}\n" +
				$"PORKBUN_SECRET_API_KEY={secretKey}\n" +
				$"PORKBUN_DOMAIN={rootDomain}\n";
			return await LinuxConfigHardeningHelper.SudoInstallAsync(content, PorkbunCredentialsPath, "root", "root", "0600");
		}

		/// <summary>Reads a text file embedded in the installer assembly.</summary>
		private static string ReadEmbeddedText(string resourceName)
		{
			using Stream resource = typeof(LetsEncryptInstaller).Assembly.GetManifestResourceStream(resourceName)
				?? throw new InvalidOperationException($"'{resourceName}' is missing from the installer resources.");
			using var reader = new StreamReader(resource);
			return reader.ReadToEnd();
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