using AppHealthMonitor;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using FishMMO.Database.Npgsql;
using Npgsql;

int checks = 0;
void Check(bool success, string message) { if (!success) throw new Exception(message); checks++; }
string root = Path.Combine(Path.GetTempPath(), "fishmmo-monitor-test-" + Guid.NewGuid());
Directory.CreateDirectory(root);
try
{
    const string key = "FISHMMO_MONITOR_CHILD_TEST";
    string? parentValue = Environment.GetEnvironmentVariable(key);
    foreach (bool headless in new[] { false, true })
    {
        var config = new AppConfig
        {
            Name = "Child environment test", ApplicationExePath = "/bin/sh", WorkingDirectory = root,
            LaunchArguments = "-c \"printf '%s\\n' \\\"$FISHMMO_MONITOR_CHILD_TEST\\\"; pwd\"",
            EnvironmentVariables = new() { [key] = "child-only" }
        };
        Check(config.TryApplyDefaultsAndValidate(out string error), error);
        var info = config.CreateStartInfo(headless);
        Check(!info.UseShellExecute, "Child environments require direct execution.");
        info.RedirectStandardOutput = true;
        using var child = Process.Start(info)!;
        string stdout = await child.StandardOutput.ReadToEndAsync();
        await child.WaitForExitAsync();
        Check(child.ExitCode == 0, "Child process failed.");
        Check(stdout.Replace("\r", "").TrimEnd() == "child-only\n" + root, "Child must receive configured environment and working directory.");
        Check(Environment.GetEnvironmentVariable(key) == parentValue, "Child override must not modify the monitor's environment.");
    }
    var invalid = new AppConfig { Name = "Invalid env", ApplicationExePath = "/bin/sh", EnvironmentVariables = new() { ["BAD=NAME"] = "x" } };
    Check(!invalid.TryApplyDefaultsAndValidate(out _), "Invalid environment names must be rejected.");
    // Centralized output puts this test at Builds/Configuration.Tests/Debug/net8.0.
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory != null && directory.Name != "Builds") directory = directory.Parent;
    var configPath = Path.Combine(directory!.Parent!.FullName, "FishMMO-Setup/Development/appsettings.AppHealthMonitor.json");
    var configuration = new ConfigurationBuilder().AddJsonFile(configPath).Build();
    var entries = configuration.GetSection("Applications").Get<List<AppConfig>>()!;
    Check(entries.Count(a => a.Enabled) == 7, "Three game tiers and four web services should be enabled.");
    var withDisabled = entries.Concat(new[] { new AppConfig { Name = "Disabled fixture", Enabled = false, ApplicationExePath = "/missing/disabled/executable" } }).ToList();
    await using var orchestrator = new DaemonOrchestrator(withDisabled, false, configuration);
    Check(orchestrator.ConfiguredApplications.Count == 7, "Disabled executables must not be validated or launched.");
    foreach (var entry in orchestrator.ConfiguredApplications)
    {
        Check(File.Exists(entry.ApplicationExePath), entry.Name + " executable missing.");
        Check(Directory.Exists(entry.WorkingDirectory), entry.Name + " working directory missing.");
        bool gameTier = !string.IsNullOrEmpty(entry.PulseTier);
        Check(entry.PortTypes.SequenceEqual(new[] { gameTier ? PortType.DatabasePulse : PortType.TCP }), entry.Name + " should use the appropriate health signal.");
        if (gameTier)
        {
            var settings = File.ReadAllLines(Path.Combine(entry.WorkingDirectory, entry.Name + ".cfg"));
            string registeredName = settings.Single(line => line.StartsWith("ServerName=")).Split('=', 2)[1].Trim();
            Check(entry.PulseServerName == registeredName, entry.Name + " must match the actual registered heartbeat name.");
            Check(entry.LaunchArguments.StartsWith(entry.PulseTier.ToUpperInvariant() + " ", StringComparison.Ordinal), entry.Name + " must pass the role as the first argument.");
            Check(entry.MonitoredPort == 0, "Database pulse checks do not need socket ports.");
        }
        Check(entry.EnvironmentVariables["Npgsql__Port"] == "6432", entry.Name + " must use PgBouncer.");
        if (entry.EnvironmentVariables.TryGetValue("ASPNETCORE_WEBROOT", out string? webRoot))
            Check(File.Exists(Path.Combine(webRoot, "index.html")), entry.Name + " content root must contain index.html.");
    }
    Check(new NpgsqlDbConfiguration(configuration).Settings.Port == "6432", "Monitor database must use PgBouncer 6432.");
    if (args.Contains("--database"))
    {
        var db = new NpgsqlDbConfiguration(configuration);
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        Check(Convert.ToInt32(await command.ExecuteScalarAsync()) == 1, "PgBouncer authentication/read-only query failed.");
        Console.WriteLine("PgBouncer 6432: authentication and SELECT 1 passed.");
    }
    Console.WriteLine($"PASS: {checks} monitor configuration and child-process checks; no application services started.");
}
finally { Directory.Delete(root, true); }
