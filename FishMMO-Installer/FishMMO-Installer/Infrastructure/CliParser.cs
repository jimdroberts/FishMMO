using FishMMO.Logging;

namespace FishMMO.Installer
{
    /// <summary>
    /// Parses CLI arguments into a <see cref="CliCommand"/> model.
    /// Supports interactive (no args), non-interactive (--non-interactive), single-component (--component),
    /// dry-run (--dry-run), validate (--validate), help (--help), and version (--version).
    /// </summary>
    public static class CliParser
    {
        public static CliCommand Parse(string[] args)
        {
            if (args.Length == 0)
                return new CliCommand(); // interactive mode (IsDefault = true)

            var cmd = new CliCommand();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].ToLowerInvariant();
                switch (arg)
                {
                    case "--help":
                    case "-h":
                    case "/?":
                        cmd = new CliCommand { ShowHelp = true };
                        break;

                    case "--version":
                    case "-v":
                        cmd = new CliCommand { ShowVersion = true };
                        break;

                    case "--non-interactive":
                        cmd = cmd with { NonInteractive = true };
                        break;

                    case "--dry-run":
                        cmd = cmd with { DryRun = true };
                        break;

                    case "--validate":
                        cmd = cmd with { ValidateMode = true };
                        break;

                    case "--generate-checksums":
                        cmd = cmd with { GenerateChecksums = true };
                        break;

                    case "--quickstart":
                        cmd = cmd with { Quickstart = true };
                        break;
                    case "--configure-server-secrets":
                        if (++i < args.Length) cmd = cmd with { DeploymentSecretsRegions = args[i] };
                        else cmd = cmd with { ShowHelp = true };
                        break;

                    case "--accept-defaults":
                    case "-y":
                    case "--yes":
                        cmd = cmd with { AcceptDefaults = true };
                        break;

                    case "--list-components":
                        cmd = cmd with { ListComponents = true };
                        break;

                    case "--component":
                    case "-c":
                        if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                            cmd = cmd with { ComponentName = args[++i].ToLowerInvariant() };
                        break;

                    case "--config":
                    case "-f":
                        if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                            cmd = cmd with { ConfigFilePath = args[++i] };
                        break;

                    case "--log-file":
                        if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                            cmd = cmd with { LogFilePath = args[++i] };
                        break;

                    default:
                        // Unknown flag or positional — ignored gracefully
                        break;
                }
            }
            return cmd;
        }

        public static void PrintHelp()
        {
            Console.WriteLine("FishMMO-Installer — FishMMO server dependency installer");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  FishMMO-Installer                           Interactive menu mode (default)");
            Console.WriteLine("  FishMMO-Installer --help / -h               Show this help");
            Console.WriteLine("  FishMMO-Installer --version / -v            Show version");
            Console.WriteLine("  FishMMO-Installer --component <name>        Install one component interactively");
            Console.WriteLine("  FishMMO-Installer --non-interactive -f cfg  Unattended install from config file");
            Console.WriteLine("  FishMMO-Installer --dry-run                 Simulate without making changes");
            Console.WriteLine("  FishMMO-Installer --validate                Run post-install health checks");
            Console.WriteLine("  FishMMO-Installer --generate-checksums      Generate SHA256 hashes for downloaded files");
            Console.WriteLine("  FishMMO-Installer --quickstart              Unattended install with quickstart defaults");
            Console.WriteLine("  FishMMO-Installer --accept-defaults / -y    Skip confirmation prompts");
            Console.WriteLine("  FishMMO-Installer --list-components         List available component names and exit");
            Console.WriteLine("  FishMMO-Installer --log-file <path>         Write log output to a file in addition to console");
            Console.WriteLine();
            Console.WriteLine("Components:");
            Console.WriteLine("  dotnet-ef         dotnet-ef global tool");
            Console.WriteLine("  aspnet-runtime    ASP.NET Core Runtime 8.0");
            Console.WriteLine("  vs-build-tools    Visual Studio Build Tools (Windows only)");
            Console.WriteLine("  postgresql        PostgreSQL server");
            Console.WriteLine("  pgbouncer         PgBouncer connection pooler");
            Console.WriteLine("  fishmmo-db        FishMMO database, user, and initial migration");
            Console.WriteLine("  nginx             NGINX reverse proxy");
            Console.WriteLine("  letsencrypt       Let's Encrypt TLS certificate");
            Console.WriteLine("  unity-hub         Unity Hub");
            Console.WriteLine("  unity-editor      Unity Editor + modules");
            Console.WriteLine("  build-projects    Build all C# projects");
            Console.WriteLine("  build-unity       Build FishMMO-Unity player/addressables");
            Console.WriteLine("  appsettings       Configure appsettings.json + secrets");
            Console.WriteLine("  create-migration  Create a new EF Core database migration");
            Console.WriteLine("  firewall          Configure host firewall rules");
            Console.WriteLine("  systemd-services  Register FishMMO web servers as systemd services");
            Console.WriteLine("  all               Run every component in dependency order");
            Console.WriteLine();
            Console.WriteLine("Example install-config.json:");
            Console.WriteLine(@"  {
            ""components"": [""postgresql"", ""nginx""],
            ""configureFirewall"": true,
            ""firewallPorts"": [80, 443],
            ""registerSystemdServices"": true,
            ""validateAfterInstall"": true
          }");
        }

        public static void PrintVersion()
        {
            string version = typeof(CliParser).Assembly.GetName().Version?.ToString() ?? "1.0.0";
            Console.WriteLine($"FishMMO-Installer {version}");
        }

        public static void PrintComponents()
        {
            Console.WriteLine("Available components:");
            Console.WriteLine("  dotnet-ef         dotnet-ef global tool");
            Console.WriteLine("  aspnet-runtime    ASP.NET Core Runtime 8.0");
            Console.WriteLine("  vs-build-tools    Visual Studio Build Tools (Windows only)");
            Console.WriteLine("  postgresql        PostgreSQL server");
            Console.WriteLine("  pgbouncer         PgBouncer connection pooler");
            Console.WriteLine("  fishmmo-db        FishMMO database, user, and initial migration");
            Console.WriteLine("  nginx             NGINX reverse proxy");
            Console.WriteLine("  letsencrypt       Let's Encrypt TLS certificate");
            Console.WriteLine("  unity-hub         Unity Hub");
            Console.WriteLine("  unity-editor      Unity Editor + modules");
            Console.WriteLine("  build-projects    Build all C# projects");
            Console.WriteLine("  build-unity       Build FishMMO-Unity player/addressables");
            Console.WriteLine("  appsettings       Configure appsettings.json + secrets");
            Console.WriteLine("  create-migration  Create a new EF Core database migration");
            Console.WriteLine("  firewall          Configure host firewall rules");
            Console.WriteLine("  systemd-services  Register FishMMO web servers as systemd services");
            Console.WriteLine("  all               Run every component in dependency order");
        }
    }
}