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
        private static string GenerateSystemdUnit(string serviceName, string dllPath, string description)
        {
            string workingDir = Path.GetDirectoryName(dllPath) ?? "/opt/fishmmo";
            string envFilePath = Path.Combine(workingDir, "fishmmo-secrets.env");
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
                    EnvironmentFile=-{envFilePath}

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
    }
}