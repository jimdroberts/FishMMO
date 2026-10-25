using System.Diagnostics;
using AppHealthMonitor;
using System.Text.Json;

if (args.Length == 3 && args[0] == "--owner-fixture")
{
    var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(args[1]))!;
    if (!config.TryApplyDefaultsAndValidate(out var failure)) throw new Exception(failure);
    using var owned = await TerminalProcessLauncher.StartAsync(config, config.CreateStartInfo(false), CancellationToken.None);
    File.WriteAllText(args[2], owned!.Id.ToString());
    await Task.Delay(Timeout.Infinite);
    return;
}

string root = Path.Combine(Path.GetTempPath(), "fishmmo-terminal-test-" + Guid.NewGuid());
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
async Task WaitFor(Func<bool> condition, string message)
{
    var timer = Stopwatch.StartNew();
    while (!condition()) { if (timer.Elapsed.TotalSeconds > 20) throw new Exception(message); await Task.Delay(100); }
}
Process? app = null;
try
{
    string script = Path.Combine(root, "child with spaces.sh");
    string observed = Path.Combine(root,"observed.txt");
    File.WriteAllText(script, """
#!/bin/sh
printf '%s\n' "$$" "$PWD" "$FISHMMO_TERMINAL_TEST" "$1" "$2" > "$FISHMMO_TERMINAL_OBSERVED"
printf 'stdout-visible\n'
printf 'stderr-visible\n' >&2
exec sleep 60
""" + "\n");
    File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    var cfg = new AppConfig
    {
        Name = "FishMMO terminal verification", ApplicationExePath = script, WorkingDirectory = root,
        LaunchArguments = "\"argument with spaces\" \"literal;$(not-executed)\"", LaunchInTerminal = true,
        TerminalLogPath = "output.log", EnvironmentVariables = new()
        {
            ["FISHMMO_TERMINAL_TEST"] = "child-only", ["FISHMMO_TERMINAL_OBSERVED"] = observed
        }
    };
    Check(cfg.TryApplyDefaultsAndValidate(out string error), error);
    Check(TerminalProcessLauncher.FindTerminal() != null, "A desktop terminal must be installed for this integration check.");
    app = await TerminalProcessLauncher.StartAsync(cfg, cfg.CreateStartInfo(false), CancellationToken.None);
    Check(app != null && !app.HasExited, "Terminal must launch a running app.");
    await WaitFor(() => File.Exists(observed) && File.ReadAllLines(observed).Length == 5, "Child did not receive arguments/environment.");
    var lines = File.ReadAllLines(observed);
    Check(int.Parse(lines[0]) == app!.Id, "Monitor must own the app PID, not terminal/helper PID.");
    Check(lines[1] == root, "Working directory must be preserved.");
    Check(lines[2] == "child-only", "Environment overrides must reach the child.");
    Check(lines[3] == "argument with spaces" && lines[4] == "literal;$(not-executed)", "Arguments must remain literal, with no shell interpolation.");
    await WaitFor(() => File.Exists(Path.Combine(root,"output.log")) && File.ReadAllText(Path.Combine(root,"output.log")).Contains("stderr-visible"), "Output was not logged.");
    string output = File.ReadAllText(Path.Combine(root,"output.log"));
    Check(output.Contains("stdout-visible") && output.Contains("stderr-visible"), "Both output streams must be visible and logged.");
    Check(Environment.GetEnvironmentVariable("FISHMMO_TERMINAL_TEST") == null, "Per-child environment must not leak into monitor.");
    app.Kill(true); await app.WaitForExitAsync(); app.Dispose(); app = null;
    await Task.Delay(1000);

    // Headless mode must never require or open a terminal, even with an invalid override.
    cfg.TerminalExecutable = "/does/not/exist";
    app = await TerminalProcessLauncher.StartAsync(cfg, cfg.CreateStartInfo(true), CancellationToken.None);
    Check(app != null && !app.HasExited, "Headless mode must bypass terminal lookup.");
    app!.Kill(true); await app.WaitForExitAsync(); app.Dispose(); app = null;

    string savedArguments = cfg.LaunchArguments;
    cfg.LaunchArguments = "-logFile -";
    cfg.TerminalLogPath = "headless with spaces.log";
    var headlessStart = cfg.CreateStartInfo(true);
    File.Delete(observed);
    app = await TerminalProcessLauncher.StartAsync(cfg, headlessStart, CancellationToken.None);
    await WaitFor(() => File.Exists(observed) && File.ReadAllLines(observed).Length == 5, "Headless logging fixture failed.");
    lines = File.ReadAllLines(observed);
    Check(lines[3] == "-logFile" && lines[4] == "headless with spaces.log", "Headless Unity must retain file logging with spaced paths.");
    Check(headlessStart.Arguments == "-logFile -", "Fallback must not mutate cached arguments.");
    app!.Kill(true); await app.WaitForExitAsync(); app.Dispose(); app = null;
    cfg.LaunchArguments = savedArguments;
    cfg.TerminalExecutable = "";
    cfg.TerminalLogPath = "owner-output.log";
    string ownerConfig = Path.Combine(root, "owner.json");
    string ownerReady = Path.Combine(root, "owner-ready.txt");
    File.WriteAllText(ownerConfig, JsonSerializer.Serialize(cfg));
    var fixtureStart = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
    fixtureStart.ArgumentList.Add("--owner-fixture"); fixtureStart.ArgumentList.Add(ownerConfig); fixtureStart.ArgumentList.Add(ownerReady);
    using (var fixture = Process.Start(fixtureStart)!)
    {
        try
        {
            await WaitFor(() => File.Exists(ownerReady), "Owner fixture did not launch its app.");
            int ownedId = int.Parse(File.ReadAllText(ownerReady));
            // Kill the monitor fixture alone, not its descendants: the helper must clean up.
            fixture.Kill(); await fixture.WaitForExitAsync();
            await WaitFor(() => { try { using var owned = Process.GetProcessById(ownedId); return owned.HasExited; } catch (ArgumentException) { return true; } }, "App survived its monitor owner.");
            Check(true, "Owner death must stop the app.");
        }
        finally { if (!fixture.HasExited) { fixture.Kill(true); await fixture.WaitForExitAsync(); } }
    }

    string fakePidFile = Path.Combine(root, "fake-pid.txt");
    string fakeTerminal = Path.Combine(root, "konsole");
    File.WriteAllText(fakeTerminal, $$"""
#!/bin/sh
printf '%s\n' "$$" > "{{fakePidFile}}"
exec sleep 60
""" + "\n");
    File.SetUnixFileMode(fakeTerminal, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    cfg.TerminalExecutable = fakeTerminal; cfg.TerminalLaunchTimeoutSeconds = 1;
    bool timedOut = false;
    try { using var unexpected = await TerminalProcessLauncher.StartAsync(cfg, cfg.CreateStartInfo(false), CancellationToken.None); }
    catch (TimeoutException) { timedOut = true; }
    Check(timedOut, "Missing terminal handshake must time out.");
    int fakeId = int.Parse(File.ReadAllText(fakePidFile));
    await WaitFor(() => { try { using var fake = Process.GetProcessById(fakeId); return fake.HasExited; } catch (ArgumentException) { return true; } }, "Timed-out terminal was left running.");
    Check(true, "Timed-out terminal must be cleaned up.");

    using var canceled = new CancellationTokenSource(); canceled.Cancel();
    bool wasCanceled = false;
    try { using var unexpected = await TerminalProcessLauncher.StartAsync(cfg, cfg.CreateStartInfo(false), canceled.Token); }
    catch (OperationCanceledException) { wasCanceled = true; }
    Check(wasCanceled, "Canceled terminal launch must not leave an unowned app.");

    Console.WriteLine($"PASS: {checks} terminal/app PID, literal argument, environment, log, headless, owner-death, timeout and cancellation checks.");
}
finally
{
    try { if (app != null && !app.HasExited) { app.Kill(true); await app.WaitForExitAsync(); } } finally { app?.Dispose(); }
    Directory.Delete(root,true);
}
