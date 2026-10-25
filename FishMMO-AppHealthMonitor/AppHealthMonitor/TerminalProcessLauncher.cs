using System.Diagnostics;
using System.Text.Json;
using FishMMO.Logging;

namespace AppHealthMonitor;

/// <summary>Launches through a terminal without confusing its PID with the supervised app.</summary>
public static class TerminalProcessLauncher
{
    private sealed record LaunchSpec(string Name, string Executable, string Arguments, string Directory,
        Dictionary<string, string> Environment, string LogPath, int OwnerPid, long OwnerStart, int TimeoutSeconds);
    private sealed record Receipt(int AppPid, long AppStart, int HelperPid, long HelperStart);

    private static readonly string[] Candidates = { "konsole", "gnome-terminal", "xfce4-terminal", "kitty", "alacritty", "foot", "xterm" };
    private static readonly JsonSerializerOptions JsonOptions = new();

    public static string? FindTerminal(string configured = "")
    {
        foreach (string candidate in string.IsNullOrWhiteSpace(configured) ? Candidates : new[] { configured })
        {
            foreach (string path in Path.IsPathRooted(candidate) ? new[] { candidate } :
                (System.Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(p => Path.Combine(p, candidate)))
                if (File.Exists(path)) return Path.GetFullPath(path);
        }
        return null;
    }

    public static async Task<Process?> StartAsync(AppConfig config, ProcessStartInfo direct, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!config.LaunchInTerminal || direct.CreateNoWindow) return StartDirect(config, direct);
        bool desktop = !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("DISPLAY")) ||
            !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
        string? terminal = OperatingSystem.IsLinux() && desktop ? FindTerminal(config.TerminalExecutable) : null;
        if (terminal == null)
        {
            if (!string.IsNullOrWhiteSpace(config.TerminalExecutable) && OperatingSystem.IsLinux() && desktop)
                throw new FileNotFoundException("Configured terminal executable was not found.", config.TerminalExecutable);
            Log.Warning(config.Name, "No supported desktop terminal is available; launching directly.");
            return StartDirect(config, direct);
        }

        string folder = Directory.CreateTempSubdirectory("fishmmo-terminal-").FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string specPath = Path.Combine(folder, "launch.json");
        using var owner = Process.GetCurrentProcess();
        var spec = new LaunchSpec(config.Name, direct.FileName, direct.Arguments, direct.WorkingDirectory,
            config.EnvironmentVariables, config.TerminalLogPath, owner.Id, Stamp(owner), config.TerminalLaunchTimeoutSeconds);
        File.WriteAllText(specPath, JsonSerializer.Serialize(spec, JsonOptions));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(specPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Process? helper = null;
        Process? window = null;
        Process? app = null;
        bool acknowledged = false;
        try
        {
            var start = new ProcessStartInfo(terminal) { UseShellExecute = false, WorkingDirectory = direct.WorkingDirectory };
            foreach (string arg in Prefix(Path.GetFileName(terminal), config.Name)) start.ArgumentList.Add(arg);
            string assembly = typeof(TerminalProcessLauncher).Assembly.Location;
            string apphost = Path.Combine(Path.GetDirectoryName(assembly)!, "AppHealthMonitor");
            if (File.Exists(apphost)) start.ArgumentList.Add(apphost);
            else { start.ArgumentList.Add("dotnet"); start.ArgumentList.Add(assembly); }
            start.ArgumentList.Add("--terminal-child");
            start.ArgumentList.Add(specPath);
            window = Process.Start(start) ?? throw new InvalidOperationException("Terminal did not start.");
            string ready = Path.Combine(folder, "ready.json");
            var timeout = Stopwatch.StartNew();
            while (!File.Exists(ready))
            {
                token.ThrowIfCancellationRequested();
                string failure = Path.Combine(folder, "error.txt");
                if (File.Exists(failure)) throw new InvalidOperationException("Terminal helper failed: " + File.ReadAllText(failure));
                if (!Directory.Exists(folder)) throw new InvalidOperationException("Application or terminal exited before the launch handshake completed.");
                if (timeout.Elapsed.TotalSeconds >= spec.TimeoutSeconds) throw new TimeoutException("Terminal launch handshake timed out.");
                await Task.Delay(50, token);
            }
            var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllText(ready), JsonOptions)
                ?? throw new InvalidOperationException("Invalid terminal handshake.");
            helper = Process.GetProcessById(receipt.HelperPid);
            if (Stamp(helper) != receipt.HelperStart) throw new InvalidOperationException("Terminal helper identity changed.");
            app = Process.GetProcessById(receipt.AppPid);
            if (Stamp(app) != receipt.AppStart) throw new InvalidOperationException("Application identity changed during launch.");
            token.ThrowIfCancellationRequested();
            File.WriteAllText(Path.Combine(folder, "ack"), "owned");
            acknowledged = true;
            return app;
        }
        finally
        {
            if (!acknowledged)
            {
                Kill(app);
                Kill(window);
                // Removing the private request cancels late GUI launches. An already
                // running helper sees the missing directory and kills its unowned child.
                try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            helper?.Dispose();
            window?.Dispose();
            if (!acknowledged) app?.Dispose();
        }
    }

    private static Process? StartDirect(AppConfig config, ProcessStartInfo direct)
    {
        // Unity uses stdout in a terminal, but keeps its existing per-role file in
        // headless/no-display mode. Never mutate the cached launch arguments.
        if (!string.IsNullOrWhiteSpace(config.TerminalLogPath) && direct.Arguments.EndsWith("-logFile -", StringComparison.Ordinal))
        {
            string path = config.TerminalLogPath.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var copy = new ProcessStartInfo(direct.FileName, direct.Arguments[..^1] + "\"" + path + "\"")
            {
                WorkingDirectory = direct.WorkingDirectory, UseShellExecute = false, CreateNoWindow = direct.CreateNoWindow
            };
            foreach (var entry in direct.Environment) copy.Environment[entry.Key] = entry.Value;
            return Process.Start(copy);
        }
        return Process.Start(direct);
    }

    private static string[] Prefix(string terminal, string title) => terminal switch
    {
        "konsole" => new[] { "--separate", "--nofork", "-p", "tabtitle=" + title, "-e" },
        "gnome-terminal" => new[] { "--wait", "--title=" + title, "--" },
        "xfce4-terminal" => new[] { "--disable-server", "--title=" + title, "-x" },
        "kitty" => new[] { "--title", title, "--" },
        "alacritty" => new[] { "--title", title, "-e" },
        "foot" => new[] { "--title=" + title, "--" },
        "xterm" => new[] { "-T", title, "-e" },
        _ => throw new InvalidOperationException("Unsupported terminal executable. Use konsole, gnome-terminal, xfce4-terminal, kitty, alacritty, foot or xterm.")
    };

    public static async Task<int> RunChildAsync(string specPath)
    {
        string folder = Path.GetDirectoryName(specPath)!;
        Process? app = null;
        bool failed = false;
        try
        {
            var spec = JsonSerializer.Deserialize<LaunchSpec>(File.ReadAllText(specPath), JsonOptions)
                ?? throw new InvalidOperationException("Missing launch specification.");
            if (!Directory.Exists(folder)) return 1;
            if (!Alive(spec.OwnerPid, spec.OwnerStart)) throw new InvalidOperationException("Monitor owner identity could not be verified.");
            var start = new ProcessStartInfo(spec.Executable, spec.Arguments)
            {
                WorkingDirectory = spec.Directory, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var entry in spec.Environment) start.Environment[entry.Key] = entry.Value;
            using var log = string.IsNullOrWhiteSpace(spec.LogPath) ? null : TextWriter.Synchronized(
                new StreamWriter(new FileStream(Path.IsPathRooted(spec.LogPath) ? spec.LogPath : Path.Combine(spec.Directory, spec.LogPath),
                    FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true });
            string banner = $"[{DateTimeOffset.UtcNow:O}] [{spec.Name}] Starting {Path.GetFileName(spec.Executable)}";
            Console.WriteLine(banner);
            log?.WriteLine(banner);
            app = Process.Start(start) ?? throw new InvalidOperationException("Application did not start.");
            Task output = Pump(app.StandardOutput, Console.Out, log);
            Task error = Pump(app.StandardError, Console.Error, log);
            using var self = Process.GetCurrentProcess();
            var receipt = new Receipt(app.Id, Stamp(app), self.Id, Stamp(self));
            string temporary = Path.Combine(folder, "ready.tmp");
            File.WriteAllText(temporary, JsonSerializer.Serialize(receipt, JsonOptions));
            File.Move(temporary, Path.Combine(folder, "ready.json"));
            var timer = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(folder, "ack")))
            {
                if (app.HasExited) throw new InvalidOperationException("Application exited before ownership was acknowledged. Exit code: " + app.ExitCode);
                if (!Directory.Exists(folder) || !Alive(spec.OwnerPid, spec.OwnerStart) || timer.Elapsed.TotalSeconds > spec.TimeoutSeconds) return 1;
                await Task.Delay(50);
            }
            // Ignore the helper's Ctrl+C; the foreground child receives the same signal.
            ConsoleCancelEventHandler cancel = (_, e) => e.Cancel = true;
            Console.CancelKeyPress += cancel;
            try
            {
                while (!app.HasExited)
                {
                    if (!Alive(spec.OwnerPid, spec.OwnerStart)) return 1;
                    await Task.Delay(250);
                }
                await Task.WhenAll(output, error);
                return app.ExitCode;
            }
            finally { Console.CancelKeyPress -= cancel; }
        }
        catch (Exception ex)
        {
            failed = true;
            Console.Error.WriteLine("Terminal application launch failed: " + ex.Message);
            try { File.WriteAllText(Path.Combine(folder, "error.txt"), ex.ToString()); } catch (IOException) { }
            return 1;
        }
        finally
        {
            Kill(app);
            app?.Dispose();
            if (!failed)
                try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task Pump(StreamReader reader, TextWriter console, TextWriter? log)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            await console.WriteLineAsync(line);
            if (log != null) await log.WriteLineAsync(line);
        }
    }

    private static long Stamp(Process process)
    {
        if (!OperatingSystem.IsLinux()) return process.StartTime.ToUniversalTime().Ticks;
        // Kernel start ticks are stable across processes. DateTime-based process
        // start estimates can differ slightly between the terminal and monitor.
        string stat = File.ReadAllText($"/proc/{process.Id}/stat");
        string[] fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return long.Parse(fields[19], System.Globalization.CultureInfo.InvariantCulture);
    }
    private static bool Alive(int pid, long start)
    {
        try { using var process = Process.GetProcessById(pid); return !process.HasExited && Stamp(process) == start; }
        catch (ArgumentException) { return false; } catch (InvalidOperationException) { return false; } catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; }
    }
    private static void Kill(Process? process)
    {
        try { if (process != null && !process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
    }
}
