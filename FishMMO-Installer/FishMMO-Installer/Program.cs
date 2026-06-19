using FishMMO.Database;
using FishMMO.Logging;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace FishMMO.Installer
{
    /// <summary>
    /// Console-based installer tool for FishMMO dependencies and database setup.
    /// Supports interactive menu mode (default) and CLI-driven non-interactive mode
    /// for headless/automated deployment.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Name of the logging configuration file.
        /// </summary>
        private const string LoggingConfigName = "logging.json";

        /// <summary>
        /// Stores the loaded application settings from appsettings.json.
        /// </summary>
        private static AppSettings appSettings = new AppSettings();

        /// <summary>
        /// Entry point. Parses CLI arguments, dispatches to the correct mode,
        /// or enters the interactive menu loop by default.
        /// </summary>
        public static async Task Main(string[] args)
        {
            // Parse CLI arguments before any setup
            CliCommand cliCommand = CliParser.Parse(args);

            // Fast-path: help and version need no initialization
            if (cliCommand.ShowHelp)
            {
                CliParser.PrintHelp();
                return;
            }

            if (cliCommand.ShowVersion)
            {
                CliParser.PrintVersion();
                return;
            }

            if (cliCommand.ListComponents)
            {
                CliParser.PrintComponents();
                return;
            }

            // Apply accept-defaults flag globally so PromptForYesNo always returns true
            if (cliCommand.AcceptDefaults)
            {
                InstallerProcessHelper.AcceptDefaults = true;
                await Console.Error.WriteLineAsync("[--accept-defaults] All confirmation prompts will auto-accept.");
            }

            if (cliCommand.GenerateChecksums)
            {
                Console.WriteLine("=== Generate SHA256 Checksums ===");
                Console.WriteLine();
                await DownloadHelper.LoadChecksumsAsync();
                await DownloadHelper.GenerateChecksumsAsync();
                return;
            }

            // --quickstart: shortcut for unattended install using the quickstart template
            if (cliCommand.Quickstart)
            {
                string quickstartConfig = Path.Combine(AppContext.BaseDirectory, "templates", "install-config.quickstart.json");
                if (!File.Exists(quickstartConfig))
                {
                    Console.Error.WriteLine($"Quickstart template not found at '{quickstartConfig}'.");
                    Environment.ExitCode = 1;
                    return;
                }
                cliCommand = cliCommand with { NonInteractive = true, ConfigFilePath = quickstartConfig };
            }

            // Set the working directory to the EXE location
            string applicationBaseDirectory = AppContext.BaseDirectory;

            // Normalize environment selection once and propagate to standard variables.
            string environmentName = DatabaseConfigurationHelper.ResolveEnvironmentName();
            Environment.SetEnvironmentVariable("FISHMMO_ENVIRONMENT", environmentName);
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", environmentName);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", environmentName);

            // Apply --log-file and FISHMMO_LOG_LEVEL overrides before initializing logging
            string configFilePath = Path.Combine(applicationBaseDirectory, LoggingConfigName);
            string? logLevelOverride = Environment.GetEnvironmentVariable("FISHMMO_LOG_LEVEL");
            string? logFilePath = cliCommand.LogFilePath;
            bool needsConfigRewrite = !string.IsNullOrWhiteSpace(logLevelOverride) || !string.IsNullOrWhiteSpace(logFilePath);

            if (needsConfigRewrite)
            {
                try
                {
                    string json = await File.ReadAllTextAsync(configFilePath);
                    var root = System.Text.Json.Nodes.JsonNode.Parse(json)?.AsObject();
                    if (root != null)
                    {
                        // FISHMMO_LOG_LEVEL: override console verbosity
                        if (!string.IsNullOrWhiteSpace(logLevelOverride))
                        {
                            var lm = root["LoggingManager"]?.AsObject();
                            if (lm != null)
                            {
                                var levels = new System.Text.Json.Nodes.JsonArray();
                                foreach (string lvl in new[] { "Info", "Warning", "Error", "Critical", logLevelOverride! })
                                    if (!levels.Select(n => n?.GetValue<string>()).Contains(lvl))
                                        levels.Add(lvl);
                                lm["ConsoleAllowedLevels"] = levels;
                            }
                        }

                        // --log-file: add FileLogger to Loggers array
                        if (!string.IsNullOrWhiteSpace(logFilePath))
                        {
                            var loggers = root["Loggers"]?.AsArray();
                            if (loggers == null)
                            {
                                loggers = new System.Text.Json.Nodes.JsonArray();
                                root["Loggers"] = loggers;
                            }
                            string? logDir = Path.GetDirectoryName(logFilePath);
                            string logFile = Path.GetFileName(logFilePath);
                            var fileLogger = new System.Text.Json.Nodes.JsonObject
                            {
                                ["Type"] = "FileLoggerConfig",
                                ["LoggerType"] = "FileLogger",
                                ["Enabled"] = true,
                                ["AllowedLevels"] = new System.Text.Json.Nodes.JsonArray(
                                    "Info", "Warning", "Error", "Critical", "Debug"),
                                ["LogDirectory"] = string.IsNullOrEmpty(logDir) ? "." : logDir,
                                ["FileName"] = logFile
                            };
                            loggers.Add(fileLogger);
                        }

                        string tmpPath = Path.Combine(applicationBaseDirectory, "logging.tmp.json");
                        await File.WriteAllTextAsync(tmpPath, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                        configFilePath = tmpPath;
                    }
                }
                catch { /* if override fails, fall through with original config */ }
            }

            // Initialize logging
            bool usedTempConfig = configFilePath.EndsWith(".tmp.json");
            try
            {
                await Log.Initialize(configFilePath, new ConsoleFormatter());
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to initialize logging from '{LoggingConfigName}': {ex.Message}");
                Console.Error.WriteLine($"Ensure {LoggingConfigName} exists in '{applicationBaseDirectory}' and contains valid JSON.");
                Environment.ExitCode = 1;
                return;
            }
            finally
            {
                if (usedTempConfig)
                {
                    try { File.Delete(configFilePath); } catch { /* best-effort cleanup */ }
                }
            }

            LoadAppSettings(environmentName);

            // ──────────────── Non-interactive / CLI mode dispatch ────────────────
            bool isCliMode = cliCommand.NonInteractive || cliCommand.ComponentName != null
                             || cliCommand.ValidateMode;

            if (isCliMode)
            {
                // Pre-flight checks
                await Log.Info("FishMMOInstaller", "Running pre-flight checks...");
                PreFlightResult preFlight = await PreFlightChecker.RunAllChecksAsync();

                foreach (string warning in preFlight.Warnings)
                    await Log.Warning("FishMMOInstaller", $"[PRE-FLIGHT] {warning}");

                foreach (string error in preFlight.Errors)
                    await Log.Error("FishMMOInstaller", $"[PRE-FLIGHT] {error}");

                if (!preFlight.AllChecksPassed && !cliCommand.ValidateMode)
                {
                    await Log.Error("FishMMOInstaller",
                        "Pre-flight checks failed. Fix the errors above or use --validate to run health checks only.");
                    Environment.ExitCode = 2;
                    await Log.Shutdown();
                    return;
                }

                // Load checksums for download integrity
                await DownloadHelper.LoadChecksumsAsync();
            }

            if (cliCommand.ValidateMode)
            {
                await RunValidateMode();
                await Log.Shutdown();
                return;
            }

            if (cliCommand.NonInteractive)
            {
                await RunNonInteractiveMode(cliCommand);
                await Log.Shutdown();
                return;
            }

            if (cliCommand.ComponentName != null)
            {
                await RunComponentMode(cliCommand);
                await Log.Shutdown();
                return;
            }

            // ──────────────── Interactive menu mode (default) ────────────────
            await RunMenuLoop();
            await Log.Shutdown();
            Environment.Exit(Environment.ExitCode);
        }

        // ──────────────────────────────────────────────────────────────────────
        //  CLI mode handlers
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>Runs health checks and prints a pass/fail report.</summary>
        private static async Task RunValidateMode()
        {
            Console.WriteLine();
            Console.WriteLine("=== FishMMO Health Check ===");
            Console.WriteLine();

            var results = await HealthChecker.RunAllChecksAsync(appSettings);

            int passed = 0;
            int failed = 0;
            foreach (var hr in results)
            {
                string status = hr.Passed ? "[PASS]" : "[FAIL]";
                Console.WriteLine($"{status} {hr.CheckName}");
                if (hr.Detail != null)
                    Console.WriteLine($"      {hr.Detail}");
                if (hr.Passed) passed++; else failed++;
            }

            Console.WriteLine();
            Console.WriteLine($"Results: {passed} passed, {failed} failed");
            Environment.ExitCode = failed == 0 ? 0 : 1;
        }

        /// <summary>Runs a full unattended installation from a config file.</summary>
        private static async Task RunNonInteractiveMode(CliCommand cliCommand)
        {
            string configPath = cliCommand.ConfigFilePath
                                ?? Path.Combine(AppContext.BaseDirectory, "install-config.json");

            if (!File.Exists(configPath))
            {
                await Log.Error("FishMMOInstaller",
                    $"Install config not found: {configPath}. Create one or use --config <path>.");
                Environment.ExitCode = 1;
                return;
            }

            InstallManifest? manifest;
            try
            {
                string json = await File.ReadAllTextAsync(configPath);
                manifest = JsonSerializer.Deserialize<InstallManifest>(json);
            }
            catch (Exception ex)
            {
                await Log.Error("FishMMOInstaller", $"Failed to parse {configPath}: {ex.Message}");
                Environment.ExitCode = 1;
                return;
            }

            if (manifest == null || manifest.Components.Count == 0)
            {
                await Log.Error("FishMMOInstaller",
                    $"Install config at {configPath} is empty or has no 'components' array.");
                Environment.ExitCode = 1;
                return;
            }

            // Apply --dry-run override if specified on the CLI
            if (cliCommand.DryRun)
                manifest = manifest with { DryRun = true };

            Console.WriteLine();
            Console.WriteLine("=== FishMMO Non-Interactive Installation ===");
            Console.WriteLine($"Config: {configPath}");
            Console.WriteLine($"Components: {string.Join(", ", manifest.Components)}");
            Console.WriteLine($"Dry run: {(manifest.DryRun ? "YES" : "no")}");
            Console.WriteLine();

            var results = await InstallOrchestrator.RunManifestAsync(manifest, appSettings);

            int succeeded = results.Count(r => r.Success);
            int failed = results.Count(r => !r.Success);
            double totalSeconds = results.Sum(r => r.Duration.TotalSeconds);

            Console.WriteLine();
            foreach (var r in results)
                Console.WriteLine($"  {r}");

            Console.WriteLine();
            Console.WriteLine($"Total: {succeeded} succeeded, {failed} failed ({totalSeconds:F1}s)");
            Environment.ExitCode = failed == 0 ? 0 : 1;
        }

        /// <summary>Runs a single component by name.</summary>
        private static async Task RunComponentMode(CliCommand cliCommand)
        {
            string component = cliCommand.ComponentName!;

            if (cliCommand.DryRun)
            {
                Console.WriteLine($"[DRY-RUN] Would install: {component}");
                Environment.ExitCode = 0;
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"=== Installing: {component} ===");
            Console.WriteLine();

            InstallResult result = await InstallOrchestrator.RunComponentAsync(component, appSettings);

            Console.WriteLine();
            if (result.Success)
            {
                Console.WriteLine($"Component '{component}' completed successfully ({result.Duration.TotalSeconds:F1}s).");
            }
            else
            {
                Console.WriteLine($"Component '{component}' FAILED: {result.ErrorMessage}");
            }

            Environment.ExitCode = result.Success ? 0 : 1;
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Configuration loading
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Loads application settings using ConfigurationBuilder.
        /// Looks first in the EXE directory for appsettings.json, then falls back to
        /// FishMMO-Database/FishMMO-DB/ where the canonical settings live after a build copy.
        /// </summary>
        private static void LoadAppSettings(string environmentName)
        {
            string exeDir = InstallerProcessHelper.GetWorkingDirectory();
            string dbSubDir = Path.Combine(exeDir, "FishMMO-Database", "FishMMO-DB");

            // Prefer the EXE dir; fall back to the database sub-directory.
            string basePath = File.Exists(Path.Combine(exeDir, "appsettings.json"))
                ? exeDir
                : dbSubDir;

            _ = Log.Debug("FishMMOInstaller", $"Loading configuration from: {basePath}");

            try
            {
                IConfiguration configuration = DatabaseConfigurationHelper.BuildDesignTimeConfiguration(basePath);
                appSettings = configuration.Get<AppSettings>() ?? new AppSettings();
                _ = Log.Info("FishMMOInstaller", $"Configuration successfully loaded for Environment: {environmentName}");
            }
            catch (Exception ex)
            {
                _ = Log.Error("FishMMOInstaller", "Critical error loading configuration", ex);
                _ = Log.Warning("FishMMOInstaller", $"Ensure appsettings.json exists in '{exeDir}' or '{dbSubDir}'.");
                appSettings = new AppSettings();
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Interactive menu loop (unchanged behavior from original)
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Runs the interactive console menu loop until the user quits.
        /// </summary>
        private static async Task RunMenuLoop()
        {
            // Load checksums for interactive mode too
            await DownloadHelper.LoadChecksumsAsync();

            // Run pre-flight checks with warnings only (non-blocking in interactive mode)
            await Log.Info("FishMMOInstaller", "Running pre-flight checks...");
            PreFlightResult preFlight = await PreFlightChecker.RunAllChecksAsync();
            foreach (string warning in preFlight.Warnings)
                await Log.Warning("FishMMOInstaller", $"[PRE-FLIGHT] {warning}");
            foreach (string error in preFlight.Errors)
                await Log.Error("FishMMOInstaller", $"[PRE-FLIGHT] {error}");

            while (true)
            {
                Console.Clear();
                Console.WriteLine("=== FishMMO Installer ===");
                Console.WriteLine();
                Console.WriteLine("1 : Runtime & Tooling");
                Console.WriteLine("2 : Database");
                Console.WriteLine("3 : Web Server");
                Console.WriteLine("4 : Unity & Build");
                Console.WriteLine("5 : Configuration");
                Console.WriteLine("0 : Quit");

                ConsoleKeyInfo key = Console.ReadKey(true);

                switch (key.Key)
                {
                    case ConsoleKey.D1:
                        await RuntimeMenu();
                        break;
                    case ConsoleKey.D2:
                        await DatabaseMenu();
                        break;
                    case ConsoleKey.D3:
                        await WebServerMenu();
                        break;
                    case ConsoleKey.D4:
                        await UnityBuildMenu();
                        break;
                    case ConsoleKey.D5:
                        await ConfigurationMenu();
                        break;
                    case ConsoleKey.D0:
                    case ConsoleKey.NumPad0:
                        return;
                    default:
                        if (key.KeyChar == '0') return;
                        break;
                }
            }
        }

        /// <summary>Runtime &amp; Tooling sub-menu: dotnet-ef, ASP.NET Runtime, VS Build Tools.</summary>
        private static async Task RuntimeMenu()
        {
            while (true)
            {
                Console.Clear();
                Console.WriteLine("=== Runtime & Tooling ===");
                Console.WriteLine();
                Console.WriteLine("1 : Install dotnet-ef Tool");
                Console.WriteLine("2 : Install ASP.NET Runtime");
                Console.WriteLine("3 : Install Visual Studio Build Tools (Windows Only)");
                Console.WriteLine("0 : Back");

                ConsoleKeyInfo key = Console.ReadKey(true);
                Console.WriteLine();

                switch (key.Key)
                {
                    case ConsoleKey.D1:
                        _ = await DotNetInstaller.InstallDotNetEF();
                        break;
                    case ConsoleKey.D2:
                        _ = await DotNetInstaller.InstallAspNetRuntime();
                        break;
                    case ConsoleKey.D3:
                        await VSBuildToolsInstaller.InstallVSBuildTools();
                        break;
                    case ConsoleKey.D0:
                    case ConsoleKey.NumPad0:
                        return;
                    default:
                        if (key.KeyChar == '0') return;
                        Console.WriteLine("Invalid option.");
                        break;
                }

                Console.WriteLine("Press any key to continue...");
                Console.ReadKey(true);
            }
        }

        /// <summary>Database sub-menu: PostgreSQL, PgBouncer, FishMMO DB management.</summary>
        private static async Task DatabaseMenu()
        {
            while (true)
            {
                Console.Clear();
                Console.WriteLine("=== Database ===");
                Console.WriteLine();
                Console.WriteLine("1 : Install PostgreSQL");
                Console.WriteLine("2 : Install PgBouncer (Connection Pooler)");
                Console.WriteLine("3 : Install FishMMO Database (User/Schema/Initial Migration)");
                Console.WriteLine("4 : Create New Database Migration");
                Console.WriteLine("5 : Grant User Permissions on Database");
                Console.WriteLine("6 : Delete FishMMO Database (DANGEROUS!)");
                Console.WriteLine("7 : Configure PgBouncer (generate pgbouncer.ini + userlist.txt, Linux)");
                Console.WriteLine("0 : Back");

                ConsoleKeyInfo key = Console.ReadKey(true);
                Console.WriteLine();

                switch (key.Key)
                {
                    case ConsoleKey.D1:
                        await HandleWithSettings(
                            s => s.Npgsql?.Host,
                            "Npgsql host",
                            s => PostgreSQLInstaller.InstallPostgreSQL(s));
                        break;
                    case ConsoleKey.D2:
                        await PgBouncerInstaller.InstallPgBouncer(appSettings);
                        break;
                    case ConsoleKey.D3:
                        await HandleWithSuperuser(
                            s => s.Npgsql?.Database,
                            "Npgsql database",
                            PostgreSQLInstaller.InstallFishMMODatabase);
                        break;
                    case ConsoleKey.D4:
                        await PostgreSQLInstaller.CreateMigration();
                        break;
                    case ConsoleKey.D5:
                        await HandleWithSuperuser(
                            s => s.Npgsql?.Username,
                            "Npgsql database/username",
                            PostgreSQLInstaller.GrantUserPermissions);
                        break;
                    case ConsoleKey.D6:
                        await HandleWithSuperuser(
                            s => s.Npgsql?.Database,
                            "Npgsql database",
                            PostgreSQLInstaller.DeleteFishMMODatabase);
                        break;
                    case ConsoleKey.D7:
                        await PgBouncerInstaller.ConfigurePgBouncerLinuxAsync(appSettings);
                        break;
                    case ConsoleKey.D0:
                    case ConsoleKey.NumPad0:
                        return;
                    default:
                        if (key.KeyChar == '0') return;
                        Console.WriteLine("Invalid option.");
                        break;
                }

                Console.WriteLine("Press any key to continue...");
                Console.ReadKey(true);
            }
        }

        /// <summary>Web Server sub-menu: NGINX, Let's Encrypt, Firewall, Services.</summary>
        private static async Task WebServerMenu()
        {
            while (true)
            {
                Console.Clear();
                Console.WriteLine("=== Web Server ===");
                Console.WriteLine();
                Console.WriteLine("1 : Install NGINX (Web Server/Reverse Proxy)");
                Console.WriteLine("2 : Install/Renew Let's Encrypt Certificate (NGINX)");
                Console.WriteLine("3 : Deploy FishMMO nginx.conf (from FishMMO-Setup/)");
                Console.WriteLine("4 : Configure Firewall Rules (open ports 80, 443)");
                Console.WriteLine("5 : Register FishMMO Web Servers as Services");
                Console.WriteLine("0 : Back");

                ConsoleKeyInfo key = Console.ReadKey(true);
                Console.WriteLine();

                switch (key.Key)
                {
                    case ConsoleKey.D1:
                        await NGINXInstaller.InstallNGINX();
                        break;
                    case ConsoleKey.D2:
                        await LetsEncryptInstaller.InstallLetsEncryptCertificate();
                        break;
                    case ConsoleKey.D3:
                        await NGINXInstaller.DeployNginxConfigAsync();
                        break;
                    case ConsoleKey.D4:
                        _ = await FirewallInstaller.OpenPortsAsync(new[] { 80, 443 }, prompt: true);
                        break;
                    case ConsoleKey.D5:
                        _ = await SystemdServiceInstaller.InstallAllAsync(
                            InstallationConstants.FishMMOMonorepoRoot);
                        break;
                    case ConsoleKey.D0:
                    case ConsoleKey.NumPad0:
                        return;
                    default:
                        if (key.KeyChar == '0') return;
                        Console.WriteLine("Invalid option.");
                        break;
                }

                Console.WriteLine("Press any key to continue...");
                Console.ReadKey(true);
            }
        }

        /// <summary>Unity &amp; Build sub-menu: Unity Hub, Unity Editor, C# project build.</summary>
        private static async Task UnityBuildMenu()
        {
            while (true)
            {
                Console.Clear();
                Console.WriteLine("=== Unity & Build ===");
                Console.WriteLine();
                Console.WriteLine("1 : Install Unity Hub");
                Console.WriteLine("2 : Install Unity Editor (+Modules)");
                Console.WriteLine("3 : Build all C# Projects");
                Console.WriteLine("4 : Build FishMMO-Unity (Client/Server/Addressables)");
                Console.WriteLine("0 : Back");

                ConsoleKeyInfo key = Console.ReadKey(true);
                Console.WriteLine();

                switch (key.Key)
                {
                    case ConsoleKey.D1:
                        await UnityInstaller.InstallUnityHub();
                        break;
                    case ConsoleKey.D2:
                        await UnityInstaller.InstallUnityVersion();
                        break;
                    case ConsoleKey.D3:
                        await ProjectBuildInstaller.BuildAllProjectsInSelectedRootAsync();
                        break;
                    case ConsoleKey.D4:
                        await UnityBuildInstaller.RunInteractiveBuild();
                        break;
                    case ConsoleKey.D0:
                    case ConsoleKey.NumPad0:
                        return;
                    default:
                        if (key.KeyChar == '0') return;
                        Console.WriteLine("Invalid option.");
                        break;
                }

                Console.WriteLine("Press any key to continue...");
                Console.ReadKey(true);
            }
        }

        /// <summary>Configuration sub-menu: AppSettings secure setup.</summary>
        private static async Task ConfigurationMenu()
        {
            while (true)
            {
                Console.Clear();
                Console.WriteLine("=== Configuration ===");
                Console.WriteLine();
                Console.WriteLine("1 : Configure AppSettings (Secure Setup)");
                Console.WriteLine("0 : Back");

                ConsoleKeyInfo key = Console.ReadKey(true);
                Console.WriteLine();

                switch (key.Key)
                {
                    case ConsoleKey.D1:
                        await AppSettingsInstaller.ConfigureAppSettings();
                        break;
                    case ConsoleKey.D0:
                    case ConsoleKey.NumPad0:
                        return;
                    default:
                        if (key.KeyChar == '0') return;
                        Console.WriteLine("Invalid option.");
                        break;
                }

                Console.WriteLine("Press any key to continue...");
                Console.ReadKey(true);
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Shared helpers for menu handlers
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Validates that the required Npgsql setting is present, then delegates to the handler.
        /// </summary>
        private static async Task HandleWithSettings(
            Func<AppSettings, string?> requiredField,
            string fieldDescription,
            Func<AppSettings, Task> handler)
        {
            if (string.IsNullOrWhiteSpace(requiredField(appSettings)))
            {
                await Log.Warning("FishMMOInstaller", $"appsettings.json is not loaded or {fieldDescription} is not defined. Cannot proceed without configuration.");
                return;
            }
            await handler(appSettings);
        }

        /// <summary>
        /// Validates settings, prompts for superuser credentials, then delegates to the handler.
        /// </summary>
        private static async Task HandleWithSuperuser(
            Func<AppSettings, string?> requiredField,
            string fieldDescription,
            Func<string, string, AppSettings, Task> handler)
        {
            if (string.IsNullOrWhiteSpace(requiredField(appSettings)))
            {
                await Log.Warning("FishMMOInstaller", $"appsettings.json is not loaded or {fieldDescription} is not defined. Cannot proceed without configuration.");
                return;
            }
            string superUsername = InstallationConstants.PostgreSQLDefaultSuperuser;
            string superPassword = InstallerProcessHelper.PromptForPassword($"Enter PostgreSQL Superuser Password (username is '{superUsername}'): ");
            await handler(superUsername, superPassword, appSettings);
        }
    }
}