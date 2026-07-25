using FishMMO.Database;
using FishMMO.Logging;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FishMMO.Installer
{
    /// <summary>
    /// Orchestrates multi-component installations in dependency order.
    /// Handles both single-component dispatch and full manifest-driven installs
    /// for the non-interactive pipeline.
    /// </summary>
    public static class InstallOrchestrator
    {
        /// <summary>
        /// Component dependency ordering. Lower number = installed first.
        /// Components not listed here are assigned order 999 (last).
        /// </summary>
        private static readonly Dictionary<string, int> DependencyOrder =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["dotnet-ef"] = 0,
                ["aspnet-runtime"] = 1,
                ["vs-build-tools"] = 1,
                ["postgresql"] = 2,
                ["pgbouncer"] = 3,
                ["fishmmo-db"] = 4,
                ["nginx"] = 5,
                ["letsencrypt"] = 6,
                ["firewall"] = 7,
                ["systemd-services"] = 8,
                ["unity-hub"] = 9,
                ["unity-editor"] = 10,
                ["build-projects"] = 11,
                ["build-unity"] = 12,
                ["appsettings"] = 13,
                ["create-migration"] = 14,
            };

        /// <summary>
        /// Runs a single component by name. Dispatches to the correct installer.
        /// </summary>
        public static async Task<InstallResult> RunComponentAsync(string componentName, AppSettings appSettings)
        {
            var sw = Stopwatch.StartNew();
            InstallResult result;

            try
            {
                result = componentName.ToLowerInvariant() switch
                {
                    "dotnet-ef" => await FromBoolAsync("dotnet-ef", () => DotNetInstaller.InstallDotNetEF()),
                    "aspnet-runtime" => await FromBoolAsync("aspnet-runtime", () => DotNetInstaller.InstallAspNetRuntime()),
                    "vs-build-tools" => await FromVoidAsync("vs-build-tools", async () =>
                    {
                        await VSBuildToolsInstaller.InstallVSBuildTools();
                        return true;
                    }),
                    "postgresql" => await FromVoidAsync("postgresql", async () =>
                    {
                        await PostgreSQLInstaller.InstallPostgreSQL(appSettings);
                        return true;
                    }),
                    "pgbouncer" => await FromVoidAsync("pgbouncer", async () =>
                    {
                        await PgBouncerInstaller.InstallPgBouncer(appSettings);
                        return true;
                    }),
                    "fishmmo-db" => await FromVoidAsync("fishmmo-db", async () =>
                    {
                        string superUser = InstallationConstants.PostgreSQLDefaultSuperuser;
                        string? superPass = Environment.GetEnvironmentVariable("FISHMMO_PG_SUPERUSER_PASSWORD");
                        if (string.IsNullOrEmpty(superPass))
                        {
                            superPass = InstallerProcessHelper.PromptForPassword(
                                $"Enter PostgreSQL superuser password (user '{superUser}'): ");
                        }
                        else
                        {
                            await Log.Info("FishMMOInstaller",
                                "Using PostgreSQL superuser password from FISHMMO_PG_SUPERUSER_PASSWORD environment variable.");
                        }
                        await PostgreSQLInstaller.InstallFishMMODatabase(superUser, superPass, appSettings);
                        return true;
                    }),
                    "nginx" => await FromVoidAsync("nginx", async () =>
                    {
                        await NGINXInstaller.InstallNGINX();
                        return true;
                    }),
                    "letsencrypt" => await FromVoidAsync("letsencrypt", async () =>
                    {
                        await LetsEncryptInstaller.InstallLetsEncryptCertificate();
                        return true;
                    }),
                    "unity-hub" => await FromVoidAsync("unity-hub", async () =>
                    {
                        await UnityInstaller.InstallUnityHub();
                        return true;
                    }),
                    "unity-editor" => await FromVoidAsync("unity-editor", async () =>
                    {
                        await UnityInstaller.InstallUnityVersion();
                        return true;
                    }),
                    "build-projects" => await FromVoidAsync("build-projects", async () =>
                    {
                        await ProjectBuildInstaller.BuildAllProjectsInSelectedRootAsync();
                        return true;
                    }),
                    "build-unity" => await FromVoidAsync("build-unity", async () =>
                    {
                        await UnityBuildInstaller.RunInteractiveBuild();
                        return true;
                    }),
                    "appsettings" => await FromVoidAsync("appsettings", async () =>
                    {
                        await AppSettingsInstaller.ConfigureAppSettings();
                        return true;
                    }),
                    "firewall" => await FirewallInstaller.OpenPortsAsync(new[] { 80, 443 }, prompt: false),
                    "systemd-services" => await SystemdServiceInstaller.InstallAllAsync(
                        InstallationConstants.FishMMOMonorepoRoot),
                    "create-migration" => await FromVoidAsync("create-migration", async () =>
                    {
                        await PostgreSQLInstaller.CreateMigration();
                        return true;
                    }),
                    "all" => await RunAllComponentsAsync(appSettings),
                    _ => InstallResult.Fail(componentName, $"Unknown component: '{componentName}'. Use --help for component list."),
                };
            }
            catch (Exception ex)
            {
                await Log.Error("FishMMOInstaller", $"Component '{componentName}' threw an exception", ex);
                result = InstallResult.Fail(componentName, ex.Message);
            }

            sw.Stop();
            return result with { Duration = sw.Elapsed };
        }

        /// <summary>
        /// Runs a full install manifest — validates component names, pre-sorts by
        /// dependency order, installs each, configures firewall and systemd services
        /// if requested, and optionally validates the result.
        /// </summary>
        public static async Task<List<InstallResult>> RunManifestAsync(
            InstallManifest manifest,
            AppSettings appSettings)
        {
            var results = new List<InstallResult>();

            // Validate component names before starting
            var unknown = manifest.Components
                .Where(c => !DependencyOrder.ContainsKey(c) && !c.Equals("all", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (unknown.Count > 0)
            {
                await Log.Error("FishMMOInstaller",
                    $"Unknown component(s) in install config: {string.Join(", ", unknown)}. " +
                    "Run --list-components for valid names. Aborting.");
                results.Add(InstallResult.Fail("validate",
                    $"Unknown component(s): {string.Join(", ", unknown)}"));
                return results;
            }

            // Sort components by dependency order
            var sorted = manifest.Components
                .Select(c => (name: c, order: DependencyOrder.GetValueOrDefault(c, 999)))
                .OrderBy(x => x.order)
                .ToList();

            await Log.Info("FishMMOInstaller",
                $"Installing {sorted.Count} component(s) in dependency order: " +
                string.Join(" → ", sorted.Select(s => s.name)));

            foreach (var (name, _) in sorted)
            {
                if (manifest.DryRun)
                {
                    await Log.Info("FishMMOInstaller", $"[DRY-RUN] Would install: {name}");
                    results.Add(InstallResult.Ok(name));
                    continue;
                }

                _ = Log.Info("FishMMOInstaller", $"--- Installing: {name} ---");
                InstallResult result = await RunComponentAsync(name, appSettings);
                results.Add(result);

                if (!result.Success)
                {
                    await Log.Error("FishMMOInstaller",
                        $"Component '{name}' failed: {result.ErrorMessage}. Aborting remaining installations.");
                    break;
                }
            }

            // Post-install: firewall
            if (manifest.ConfigureFirewall)
            {
                var ports = manifest.FirewallPorts.Count > 0
                    ? manifest.FirewallPorts
                    : new List<int> { 80, 443 };

                if (manifest.DryRun)
                {
                    await Log.Info("FishMMOInstaller",
                        $"[DRY-RUN] Would configure firewall for ports: {string.Join(", ", ports)}");
                    results.Add(InstallResult.Ok("firewall"));
                }
                else
                {
                    _ = Log.Info("FishMMOInstaller", "--- Configuring firewall ---");
                    results.Add(await FirewallInstaller.OpenPortsAsync(ports, prompt: false));
                }
            }

            // Post-install: systemd services
            if (manifest.RegisterSystemdServices && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (manifest.DryRun)
                {
                    await Log.Info("FishMMOInstaller", "[DRY-RUN] Would register systemd services for web servers.");
                    results.Add(InstallResult.Ok("systemd-services"));
                }
                else
                {
                    _ = Log.Info("FishMMOInstaller", "--- Registering systemd services ---");
                    results.Add(await SystemdServiceInstaller.InstallAllAsync(
                        InstallationConstants.FishMMOMonorepoRoot,
                        manifest.WebServers.Count > 0 ? manifest.WebServers : null));
                }
            }

            // Post-install: validation
            if (manifest.ValidateAfterInstall && !manifest.DryRun)
            {
                _ = Log.Info("FishMMOInstaller", "--- Running post-install validation ---");
                var healthResults = await HealthChecker.RunAllChecksAsync(appSettings);
                foreach (var hr in healthResults)
                {
                    string status = hr.Passed ? "PASS" : "FAIL";
                    string line = $"[{status}] {hr.CheckName}";
                    if (hr.Detail != null) line += $" — {hr.Detail}";
                    Console.WriteLine(line);
                }
            }

            // Summary
            int succeeded = results.Count(r => r.Success);
            int failed = results.Count(r => !r.Success);
            await Log.Info("FishMMOInstaller",
                $"Installation complete: {succeeded} succeeded, {failed} failed.");

            return results;
        }

        /// <summary>
        /// Runs every known component in dependency order. Used by the "all" pseudo-component.
        /// Stops on the first failure.
        /// </summary>
        private static async Task<InstallResult> RunAllComponentsAsync(AppSettings appSettings)
        {
            var allComponents = DependencyOrder
                .OrderBy(kv => kv.Value)
                .Select(kv => kv.Key)
                .ToList();

            await Log.Info("FishMMOInstaller",
                $"Installing all {allComponents.Count} components in dependency order: " +
                string.Join(" → ", allComponents));

            int succeeded = 0;
            int failed = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            foreach (string name in allComponents)
            {
                _ = Log.Info("FishMMOInstaller", $"--- Installing: {name} ---");
                InstallResult result = await RunComponentAsync(name, appSettings);

                if (result.Success)
                {
                    succeeded++;
                }
                else
                {
                    failed++;
                    await Log.Error("FishMMOInstaller",
                        $"Component '{name}' failed: {result.ErrorMessage}. Stopping.");
                    sw.Stop();
                    return InstallResult.Fail("all",
                        $"{succeeded} succeeded, {failed} failed (stopped at '{name}': {result.ErrorMessage})",
                        sw.Elapsed);
                }
            }

            sw.Stop();
            return InstallResult.Ok("all", sw.Elapsed);
        }

        /// <summary>Wraps a bool-returning async install method in an InstallResult.</summary>
        private static async Task<InstallResult> FromBoolAsync(string name, Func<Task<bool>> action)
        {
            try
            {
                bool ok = await action();
                return ok
                    ? InstallResult.Ok(name)
                    : InstallResult.Fail(name, "Installation returned failure.");
            }
            catch (Exception ex)
            {
                return InstallResult.Fail(name, ex.Message);
            }
        }

        /// <summary>Wraps a void-returning install method in an InstallResult with error handling.</summary>
        private static async Task<InstallResult> FromVoidAsync(string name, Func<Task<bool>> action)
        {
            try
            {
                bool ok = await action();
                return ok
                    ? InstallResult.Ok(name)
                    : InstallResult.Fail(name, "Installation returned failure.");
            }
            catch (Exception ex)
            {
                return InstallResult.Fail(name, ex.Message);
            }
        }
    }
}