using FishMMO.Logging;
using System.Runtime.InteropServices;

namespace FishMMO.Installer
{
    /// <summary>
    /// Generates and installs systemd unit files for FishMMO ASP.NET web servers
    /// (IPFetch, Patcher, WebGL). On Windows, logs a reminder and skips.
    /// </summary>
    public static class SystemdServiceInstaller
    {
        /// <summary>Web server service definitions.</summary>
        private static readonly (string serviceName, string projectDir, string description)[] WebServers =
        {
            ("fishmmo-ipfetch", "IPFetchASP.NET/IpFetchServer", "FishMMO IP Fetch Web Server"),
            ("fishmmo-patcher", "PatcherASP.NET/Patcher", "FishMMO Patcher Web Server"),
            ("fishmmo-webgl", "WebGLServerASP.NET/WebGLServer", "FishMMO WebGL Web Server"),
        };

        /// <summary>
        /// Installs systemd units for all FishMMO web servers found under the monorepo root.
        /// </summary>
        /// <param name="fishmmoRoot">FishMMO monorepo root path.</param>
        /// <param name="onlyServers">Optional list of server names to restrict registration to. When null or empty, all three are registered.</param>
        /// <returns>InstallResult indicating success or failure.</returns>
        public static async Task<InstallResult> InstallAllAsync(string fishmmoRoot, IReadOnlyList<string>? onlyServers = null)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                await Log.Info("FishMMOInstaller",
                    "Systemd service registration is Linux-only. On Windows, configure services via NSSM manually or use the NGINX service pattern.");
                return InstallResult.Ok("systemd-services");
            }

            var serversToInstall = WebServers.AsEnumerable();
            if (onlyServers != null && onlyServers.Count > 0)
            {
                serversToInstall = WebServers.Where(ws =>
                    onlyServers.Contains(ws.serviceName, StringComparer.OrdinalIgnoreCase)
                    || onlyServers.Contains(ws.serviceName.Replace("fishmmo-", ""), StringComparer.OrdinalIgnoreCase));
            }

            bool anyInstalled = false;
            bool anyFailed = false;

            foreach (var (serviceName, projectDir, description) in serversToInstall)
            {
                string? publishDir = FindPublishDirectory(fishmmoRoot, projectDir);
                if (publishDir == null)
                {
                    await Log.Warning("FishMMOInstaller",
                        $"Skipping {serviceName}: publish directory not found at " +
                        $"FishMMO-WebServers/{projectDir}/bin/Release/net8.0/publish. Build the project first.");
                    continue;
                }

                string? dllPath = FindEntryPointDll(publishDir);
                if (dllPath == null)
                {
                    await Log.Warning("FishMMOInstaller",
                        $"Skipping {serviceName}: no server DLL found in {publishDir}. Build the project first.");
                    continue;
                }

                string unitContent = GenerateSystemdUnit(serviceName, dllPath, description);
                string unitPath = Path.Combine(InstallationConstants.LinuxSystemdUnitDirectory, $"{serviceName}.service");

                await LinuxConfigHardeningHelper.EnsureBackupAsync(unitPath);
                bool written = await LinuxConfigHardeningHelper.SudoInstallAsync(
                    unitContent, unitPath, "root", "root", "0644");

                if (!written)
                {
                    await Log.Error("FishMMOInstaller", $"Failed to write systemd unit: {unitPath}");
                    anyFailed = true;
                    continue;
                }

                // Reload systemd and enable + start
                IPlatform platform = PlatformFactory.Current;
                (string shell, string argPrefix) = platform.GetShellCommand();

                if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix,
                        $"sudo systemctl daemon-reload && sudo systemctl enable --now {serviceName}.service",
                        $"Failed to enable and start {serviceName}."))
                {
                    anyFailed = true;
                    continue;
                }

                await Log.Info("FishMMOInstaller", $"Installed and started systemd service: {serviceName}");
                anyInstalled = true;
            }

            if (!anyInstalled && !anyFailed)
            {
                return InstallResult.Ok("systemd-services");
            }

            if (anyFailed)
            {
                return InstallResult.Fail("systemd-services",
                    "Some services failed to install. Check log output above.");
            }

            return InstallResult.Ok("systemd-services");
        }

        /// <summary>
        /// Generates a systemd .service file content for an ASP.NET Core application.
        /// </summary>
		/// <summary>
		/// Path to the system-wide FishMMO secrets file shared by all services.
		/// All systemd units reference this single file so secrets (gate secret,
		/// HMAC key, KEK) are identical across IpFetchServer, Patcher, WebGLServer,
		/// and the AppHealthMonitor (which passes them to Login/World/Scene servers).
		/// </summary>
		internal const string SystemWideSecretsPath = "/etc/fishmmo/secrets.env";

		private static string GenerateSystemdUnit(string serviceName, string dllPath, string description)
		{
			string workingDir = Path.GetDirectoryName(dllPath) ?? "/opt/fishmmo";
			string user = Environment.UserName;

			return $"""
			        [Unit]
			        Description={description}
			        After=network.target postgresql.service

			        [Service]
			        WorkingDirectory={workingDir}
			        ExecStart=/usr/bin/dotnet "{dllPath}"
			        Restart=always
			        RestartSec=5
			        User={user}
			        Environment=ASPNETCORE_ENVIRONMENT=Production
			        Environment=FISHMMO_ENVIRONMENT=Production
			        EnvironmentFile=-{SystemWideSecretsPath}
			        EnvironmentFile=-/etc/fishmmo/db-secrets.env

			        [Install]
			        WantedBy=multi-user.target
			        """;
		}

        /// <summary>Finds the publish output directory for a given project.</summary>
        private static string? FindPublishDirectory(string root, string projectDir)
        {
            string candidate = Path.Combine(root, "FishMMO-WebServers", projectDir,
                "bin", "Release", "net8.0", "publish");
            return Directory.Exists(candidate) ? candidate : null;
        }

        /// <summary>
        /// Finds the main entry-point DLL in the publish directory.
        /// Looks for DLLs ending with "Server" or "ASP.NET".
        /// </summary>
        private static string? FindEntryPointDll(string publishDir)
        {
            try
            {
                return Directory.GetFiles(publishDir, "*.dll", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(f =>
                    {
                        string name = Path.GetFileNameWithoutExtension(f);
                        return name.EndsWith("Server", StringComparison.OrdinalIgnoreCase)
                               || name.Contains("ASP.NET", StringComparison.OrdinalIgnoreCase);
                    });
            }
            catch
            {
                return null;
            }
        }

		/// <summary>
		/// Registers FishMMO web servers as Windows services via NSSM.
		/// Only runs on Windows; Linux uses systemd via <see cref="InstallAllAsync"/>.
		/// </summary>
		public static async Task<InstallResult> InstallWindowsServicesAsync(string fishmmoRoot, IReadOnlyList<string>? onlyServers = null)
		{
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				return InstallResult.Ok("windows-services");

			if (!NGINXInstaller.IsRunningAsAdministrator())
			{
				await Log.Error("FishMMOInstaller",
					"Windows service registration requires Administrator privileges.\n" +
					"  -> Right-click the terminal and select 'Run as administrator'.");
				return InstallResult.Fail("windows-services", "Administrator privileges required.");
			}

			string nssmExe = await NGINXInstaller.EnsureNssmInstalledAsync();
			if (string.IsNullOrWhiteSpace(nssmExe))
			{
				await Log.Error("FishMMOInstaller", "NSSM is required for Windows service registration.");
				return InstallResult.Fail("windows-services", "NSSM not found.");
			}

			bool anyFailed = false;
			foreach (var (serviceName, projectDir, description) in WebServers)
			{
				string windowsServiceName = projectDir switch
				{
					"IPFetchASP.NET/IpFetchServer" => InstallationConstants.IpFetchWindowsServiceName,
					"PatcherASP.NET/Patcher" => InstallationConstants.PatcherWindowsServiceName,
					"WebGLServerASP.NET/WebGLServer" => InstallationConstants.WebGLWindowsServiceName,
					_ => $"FishMMO-{serviceName}"
				};

				if (onlyServers != null && onlyServers.Count > 0 &&
					!onlyServers.Contains(serviceName, StringComparer.OrdinalIgnoreCase) &&
					!onlyServers.Contains(serviceName.Replace("fishmmo-", ""), StringComparer.OrdinalIgnoreCase))
					continue;

				string? publishDir = FindPublishDirectory(fishmmoRoot, projectDir);
				if (publishDir == null)
				{
					await Log.Warning("FishMMOInstaller",
						$"Skipping {windowsServiceName}: publish directory not found. Build the project first.");
					continue;
				}

				string? dllPath = FindEntryPointDll(publishDir);
				if (dllPath == null)
				{
					await Log.Warning("FishMMOInstaller",
						$"Skipping {windowsServiceName}: no server DLL found in {publishDir}.");
					continue;
				}

				bool exists = await NGINXInstaller.NssmServiceExistsAsync(nssmExe, windowsServiceName);
				if (!exists)
				{
					await InstallerProcessHelper.RunProcessAsync("sc.exe",
						$"delete \"{windowsServiceName}\"", (_, __, ___) => true);

					string fullDllPath = Path.Combine(publishDir, Path.GetFileName(dllPath));
					bool created = await NGINXInstaller.NssmInstallAsync(nssmExe, windowsServiceName,
						fullDllPath, publishDir, $"\"{fullDllPath}\"");
					if (!created)
					{
						await Log.Error("FishMMOInstaller", $"Failed to create Windows service: {windowsServiceName}");
						anyFailed = true;
						continue;
					}

					string envName = ResolveServiceEnvironmentName();
					var envVars = new List<string>
					{
						$"ASPNETCORE_ENVIRONMENT={envName}",
						$"DOTNET_ENVIRONMENT={envName}",
						$"FISHMMO_ENVIRONMENT={envName}"
					};

					// Load secrets from fishmmo-secrets.env if present in the publish directory.
					string secretsEnvPath = Path.Combine(publishDir, "fishmmo-secrets.env");
					if (File.Exists(secretsEnvPath))
					{
						try
						{
							foreach (string line in await File.ReadAllLinesAsync(secretsEnvPath))
							{
								string trimmed = line.Trim();
								if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
									continue;
								int eq = trimmed.IndexOf('=');
								if (eq > 0)
								{
									envVars.Add(trimmed);
								}
							}
							await Log.Info("FishMMOInstaller",
								$"Loaded secrets from {secretsEnvPath} for {windowsServiceName}.");
						}
						catch (Exception ex)
						{
							await Log.Warning("FishMMOInstaller",
								$"Failed to read {secretsEnvPath}: {ex.Message}");
						}
					}
					else
					{
						await Log.Warning("FishMMOInstaller",
							$"No fishmmo-secrets.env found in {publishDir}. " +
							$"Run 'Configure AppSettings → Generate secrets file' to create one, " +
							$"or set environment variables manually on the {windowsServiceName} service.");
					}

					await NGINXInstaller.NssmSetEnvironmentAsync(nssmExe, windowsServiceName, envVars);

					await NGINXInstaller.SetNssmParamAsync(nssmExe, windowsServiceName, "AppStdout",
						Path.Combine(publishDir, "logs", "service-out.log"));
					await NGINXInstaller.SetNssmParamAsync(nssmExe, windowsServiceName, "AppStderr",
						Path.Combine(publishDir, "logs", "service-err.log"));
					await NGINXInstaller.SetNssmParamAsync(nssmExe, windowsServiceName, "AppRotateFiles", "1");

					await InstallerProcessHelper.RunProcessAsync("sc.exe",
						$"description \"{windowsServiceName}\" \"{description}\"",
						(_, __, ___) => true);
				}

				bool started = await NGINXInstaller.NssmStartServiceAsync(nssmExe, windowsServiceName);
				if (!started)
				{
					await Log.Error("FishMMOInstaller", $"Failed to start {windowsServiceName}.");
					anyFailed = true;
					continue;
				}

				await Log.Info("FishMMOInstaller", $"Windows service '{windowsServiceName}' installed and running.");
			}

			return anyFailed
				? InstallResult.Fail("windows-services", "Some services failed. Check log output above.")
				: InstallResult.Ok("windows-services");
		}

		/// <summary>
		/// Installs the AppHealthMonitor daemon as a systemd service (Linux only).
		/// </summary>
		public static async Task<InstallResult> InstallAppHealthMonitorServiceAsync(string fishmmoRoot)
		{
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				await Log.Info("FishMMOInstaller",
					"AppHealthMonitor service is Linux-only. On Windows, use NSSM via InstallWindowsServicesAsync.");
				return InstallResult.Ok("apphealthmonitor-service");
			}

			string candidate = Path.Combine(fishmmoRoot, "FishMMO-AppHealthMonitor", "AppHealthMonitor",
				"bin", "Release", "net8.0", "publish");
			if (!Directory.Exists(candidate))
				candidate = Path.Combine(fishmmoRoot, "FishMMO-AppHealthMonitor", "AppHealthMonitor",
					"bin", "Debug", "net8.0");

			if (!Directory.Exists(candidate))
			{
				await Log.Warning("FishMMOInstaller",
					"AppHealthMonitor publish directory not found. Build the project first (dotnet publish -c Release).");
				return InstallResult.Fail("apphealthmonitor-service", "Publish directory not found.");
			}

			string dllPath = Path.Combine(candidate, "AppHealthMonitor.dll");
			if (!File.Exists(dllPath))
			{
				await Log.Warning("FishMMOInstaller", $"AppHealthMonitor.dll not found in {candidate}.");
				return InstallResult.Fail("apphealthmonitor-service", "DLL not found.");
			}

			string workingDir = Path.GetDirectoryName(dllPath) ?? candidate;
			// Use system-wide secrets path shared by all FishMMO services.
			string serviceName = InstallationConstants.AppHealthMonitorSystemdServiceName;
			string unitPath = Path.Combine(InstallationConstants.LinuxSystemdUnitDirectory, $"{serviceName}.service");
			string envName = ResolveServiceEnvironmentName();
			string user = Environment.UserName;

			string unitContent =
				$"[Unit]\n" +
				$"Description=FishMMO Application Health Monitor Daemon\n" +
				$"After=network.target postgresql.service\n" +
				$"\n" +
				$"[Service]\n" +
				$"WorkingDirectory={workingDir}\n" +
				$"ExecStart=/usr/bin/dotnet \"{dllPath}\"\n" +
				$"Restart=always\n" +
				$"RestartSec=10\n" +
				$"User={user}\n" +
				$"Environment=ASPNETCORE_ENVIRONMENT={envName}\n" +
				$"Environment=DOTNET_ENVIRONMENT={envName}\n" +
				$"Environment=FISHMMO_ENVIRONMENT={envName}\n" +
				$"EnvironmentFile=-{SystemWideSecretsPath}\n" +
				$"EnvironmentFile=-/etc/fishmmo/db-secrets.env\n" +
				$"\n" +
				$"[Install]\n" +
				$"WantedBy=multi-user.target\n";

			await LinuxConfigHardeningHelper.EnsureBackupAsync(unitPath);
			bool written = await LinuxConfigHardeningHelper.SudoInstallAsync(
				unitContent, unitPath, "root", "root", "0644");

			if (!written)
			{
				await Log.Error("FishMMOInstaller", $"Failed to write systemd unit: {unitPath}");
				return InstallResult.Fail("apphealthmonitor-service", "Failed to write unit file.");
			}

			IPlatform platform = PlatformFactory.Current;
			(string shell, string argPrefix) = platform.GetShellCommand();

			if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix,
				$"sudo systemctl daemon-reload && sudo systemctl enable --now {serviceName}.service",
				$"Failed to enable and start {serviceName}."))
			{
				return InstallResult.Fail("apphealthmonitor-service", "Failed to enable/start service.");
			}

			await Log.Info("FishMMOInstaller", $"AppHealthMonitor systemd service installed: {serviceName}");
			return InstallResult.Ok("apphealthmonitor-service");
		}


		/// <summary>
		/// Resolves the environment name for service units.
		/// Checks FISHMMO_SERVICE_ENVIRONMENT, then FISHMMO_ENVIRONMENT, then DOTNET_ENVIRONMENT.
		/// Defaults to "Production" for service units.
		/// </summary>
		private static string ResolveServiceEnvironmentName()
		{
			string? env = Environment.GetEnvironmentVariable("FISHMMO_SERVICE_ENVIRONMENT")
				?? Environment.GetEnvironmentVariable("FISHMMO_ENVIRONMENT")
				?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
			return !string.IsNullOrWhiteSpace(env) ? env : "Production";
		}
    }
}
