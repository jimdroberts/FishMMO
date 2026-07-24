using FishMMO.Database;
using FishMMO.Logging;
using System.Runtime.InteropServices;

namespace FishMMO.Installer
{
    /// <summary>
    /// A single health check result.
    /// </summary>
    /// <param name="CheckName">Human-readable check name.</param>
    /// <param name="Passed">Whether the check passed.</param>
    /// <param name="Detail">Optional detail message.</param>
    public sealed record HealthCheckResult(string CheckName, bool Passed, string? Detail = null);

    /// <summary>
    /// Runs post-install validation checks and returns a pass/fail report.
    /// Used by <c>--validate</c> mode and the non-interactive install pipeline.
    /// </summary>
    public static class HealthChecker
    {
        /// <summary>
        /// Runs all health checks and returns the results.
        /// </summary>
        /// <param name="appSettings">
        /// Optional application settings. When provided, also checks database connectivity.
        /// </param>
        /// <returns>List of health check results.</returns>
        public static async Task<List<HealthCheckResult>> RunAllChecksAsync(AppSettings? appSettings = null)
        {
            var results = new List<HealthCheckResult>();

            // .NET SDK
            bool dotNetInstalled = await DotNetInstaller.IsDotNetInstalledAsync();
            results.Add(new HealthCheckResult(".NET SDK", dotNetInstalled,
                dotNetInstalled ? "Detected" : "Not found. Install via menu 1 → 1."));

            // ASP.NET Core Runtime
            bool aspNetInstalled = await DotNetInstaller.IsAspNetRuntimeInstalledAsync();
            results.Add(new HealthCheckResult("ASP.NET Core Runtime", aspNetInstalled,
                aspNetInstalled ? "Detected" : "Not found. Install via menu 1 → 2."));

            // PostgreSQL
            bool pgInstalled = await PostgreSQLInstaller.IsPostgreSQLInstalledAsync();
            results.Add(new HealthCheckResult("PostgreSQL", pgInstalled,
                pgInstalled ? "Binary detected" : "Not found. Install via menu 2 → 1."));

            // NGINX
            bool nginxInstalled = await NGINXInstaller.IsNGINXInstalledAsync();
            results.Add(new HealthCheckResult("NGINX", nginxInstalled,
                nginxInstalled ? "Binary detected" : "Not found. Install via menu 3 → 1."));

            // PgBouncer
            bool pgbInstalled = await PgBouncerInstaller.IsPgBouncerInstalledAsync();
            results.Add(new HealthCheckResult("PgBouncer", pgbInstalled,
                pgbInstalled ? "Binary detected" : "Not found. Install via menu 2 → 2."));

            // Systemd services (Linux only)
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                foreach (string svc in new[] { "fishmmo-ipfetch", "fishmmo-patcher", "fishmmo-webgl" })
                {
                    bool active = await IsSystemdServiceActiveAsync(svc);
                    results.Add(new HealthCheckResult($"systemd: {svc}", active,
                        active ? "Active" : "Not found or inactive."));
                }
            }

            // Database connectivity
            if (appSettings?.Npgsql != null
                && !string.IsNullOrWhiteSpace(appSettings.Npgsql.Host)
                && !string.IsNullOrWhiteSpace(appSettings.Npgsql.Database))
            {
                bool dbReachable = await CanConnectToDatabaseAsync(appSettings);
                results.Add(new HealthCheckResult("Database connectivity", dbReachable,
                    dbReachable
                        ? $"Connected to {appSettings.Npgsql.Host}:{appSettings.Npgsql.Port}/{appSettings.Npgsql.Database}"
                        : $"Could not connect to {appSettings.Npgsql.Host}:{appSettings.Npgsql.Port}/{appSettings.Npgsql.Database}"));
            }

            // Disk space
            try
            {
                string workingDir = AppContext.BaseDirectory;
                var drive = DriveInfo.GetDrives().FirstOrDefault(d =>
                    workingDir.StartsWith(d.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase));
                if (drive != null)
                {
                    long freeMB = drive.AvailableFreeSpace / (1024 * 1024);
                    bool hasSpace = drive.AvailableFreeSpace > 500_000_000; // 500 MB
                    results.Add(new HealthCheckResult("Disk space", hasSpace,
                        $"{freeMB} MB free on {drive.Name}"));
                }
            }
            catch { /* skip */ }

            return results;
        }

        /// <summary>Checks whether a systemd service is active.</summary>
        private static async Task<bool> IsSystemdServiceActiveAsync(string name)
        {
            try
            {
                IPlatform platform = PlatformFactory.Current;
                (string shell, string argPrefix) = platform.GetShellCommand();
                return await InstallerProcessHelper.RunProcessAsync(
                    shell,
                    $"{argPrefix} \"systemctl is-active --quiet {name}\"",
                    (exit, _, _) => exit == 0);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Tests a short-lived connection to the configured PostgreSQL database.</summary>
        private static async Task<bool> CanConnectToDatabaseAsync(AppSettings settings)
        {
            try
            {
                string? username = DatabaseSecrets.TryResolveUsername();
                string? password = DatabaseSecrets.TryResolvePassword();
                string cs = $"Host={settings.Npgsql!.Host};Port={settings.Npgsql.Port};" +
                            $"Database={settings.Npgsql.Database};Username={username ?? "unknown"};" +
                            $"Password={password ?? "unknown"};ConnectionTimeout=5;Pooling=false";
                await using var conn = new Npgsql.NpgsqlConnection(cs);
                await conn.OpenAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}