using FishMMO.Logging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace FishMMO.Installer
{
    /// <summary>
    /// Result of pre-flight system readiness checks.
    /// </summary>
    public sealed record PreFlightResult
    {
        /// <summary>True when all checks passed (no errors).</summary>
        public bool AllChecksPassed { get; init; }

        /// <summary>Advisory warnings that do not block installation.</summary>
        public List<string> Warnings { get; init; } = new();

        /// <summary>Blocking errors that prevent installation.</summary>
        public List<string> Errors { get; init; } = new();
    }

    /// <summary>
    /// Validates system readiness before any install operation begins.
    /// Checks internet, disk, RAM, admin access, and port conflicts.
    /// Runs all checks in parallel for speed. Results are cached for the
    /// process lifetime to avoid redundant I/O.
    /// </summary>
    public static class PreFlightChecker
    {
        private static readonly int[] PortsToCheck = { 80, 443, 5432, 6432, 8000, 8080, 8090 };

        private static PreFlightResult? _cachedResult;

        /// <summary>
        /// Runs all pre-flight checks and returns the aggregated result.
        /// Results are cached for the process lifetime to avoid redundant checks.
        /// </summary>
        public static async Task<PreFlightResult> RunAllChecksAsync()
        {
            if (_cachedResult != null)
                return _cachedResult;

            var result = new PreFlightResult();

            // Run I/O-bound checks in parallel
            var internetTask = CheckInternetAsync(result);
            var diskTask = CheckDiskSpaceAsync(result);
            var memoryTask = CheckMemoryAsync(result);
            var adminTask = CheckAdminAccessAsync(result);
            var portTask = CheckPortConflictsAsync(result);

            await Task.WhenAll(internetTask, diskTask, memoryTask, adminTask, portTask);

            result = result with { AllChecksPassed = result.Errors.Count == 0 };
            _cachedResult = result;
            return result;
        }

        /// <summary>Checks internet connectivity by pinging the .NET download endpoint.</summary>
        private static async Task CheckInternetAsync(PreFlightResult result)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var response = await DownloadHelper.Client.GetAsync(
                    "https://dot.net/v1/dotnet-install.sh",
                    HttpCompletionOption.ResponseHeadersRead,
                    cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    await Log.Info("FishMMOInstaller", "[PRE-FLIGHT] Internet connectivity: OK");
                }
                else
                {
                    result.Warnings.Add("Internet check: dot.net returned HTTP " + (int)response.StatusCode);
                }
            }
            catch (TaskCanceledException)
            {
                result.Errors.Add("Internet check timed out after 10s. Downloads will fail.");
            }
            catch (Exception ex)
            {
                result.Errors.Add($"No internet connectivity detected: {ex.Message}");
            }
        }

        /// <summary>Checks available disk space on the working directory drive.</summary>
        private static Task CheckDiskSpaceAsync(PreFlightResult result)
        {
            try
            {
                string workingDir = AppContext.BaseDirectory;
                var drives = DriveInfo.GetDrives();
                DriveInfo? drive = drives.FirstOrDefault(d =>
                    workingDir.StartsWith(d.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase));

                if (drive != null)
                {
                    long freeGB = drive.AvailableFreeSpace / (1024L * 1024 * 1024);
                    if (freeGB < 5)
                    {
                        result.Warnings.Add(
                            $"Low disk space on {drive.Name}: ~{freeGB} GB free. At least 5 GB recommended for Unity Editor and build tools.");
                    }
                    else
                    {
                        _ = Log.Info("FishMMOInstaller", $"[PRE-FLIGHT] Disk space: OK ({freeGB} GB free on {drive.Name})");
                    }
                }
            }
            catch (Exception ex)
            {
                _ = Log.Warning("FishMMOInstaller", $"[PRE-FLIGHT] Could not check disk space: {ex.Message}");
            }
            return Task.CompletedTask;
        }

        /// <summary>Checks available system memory.</summary>
        private static Task CheckMemoryAsync(PreFlightResult result)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    string memInfo = File.ReadAllText("/proc/meminfo");
                    var match = Regex.Match(memInfo, @"MemTotal:\s+(\d+)");
                    if (match.Success && long.TryParse(match.Groups[1].Value, out long memKb))
                    {
                        long memGB = memKb / (1024 * 1024);
                        if (memGB < 2)
                        {
                            result.Warnings.Add(
                                $"Less than 2 GB RAM detected (~{memGB} GB). FishMMO game servers may run poorly.");
                        }
                        else
                        {
                            _ = Log.Info("FishMMOInstaller", $"[PRE-FLIGHT] Memory: OK (~{memGB} GB)");
                        }
                    }
                }
                // Windows: skip explicit RAM check; VS Build Tools installer handles its own requirements
            }
            catch (Exception ex)
            {
                _ = Log.Warning("FishMMOInstaller", $"[PRE-FLIGHT] Could not check memory: {ex.Message}");
            }
            return Task.CompletedTask;
        }

        /// <summary>Checks sudo (Linux) or Administrator (Windows) access.</summary>
        private static async Task CheckAdminAccessAsync(PreFlightResult result)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Check high integrity level via whoami
                bool isAdmin = await InstallerProcessHelper.RunProcessAsync(
                    "whoami", "/groups",
                    (exit, output, _) => exit == 0 && output.Contains("S-1-16-12288"));

                if (!isAdmin)
                {
                    result.Warnings.Add(
                        "Not running as Administrator. UAC-elevated installers will prompt. Non-elevated operations (dotnet tool install, certbot) may fail.");
                }
                else
                {
                    await Log.Info("FishMMOInstaller", "[PRE-FLIGHT] Admin access: OK (Administrator)");
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Check for passwordless sudo (-n = non-interactive)
                bool canSudo = await InstallerProcessHelper.RunProcessAsync(
                    "sudo", "-n true",
                    (exit, _, _) => exit == 0);

                if (!canSudo)
                {
                    // This is a warning, not an error — users can still type their
                    // password when sudo prompts in a terminal.
                    result.Warnings.Add(
                        "Passwordless sudo not available. Commands that require root will prompt " +
                        "for a password. Run 'sudo -v' before launching to cache credentials.");
                }
                else
                {
                    await Log.Info("FishMMOInstaller", "[PRE-FLIGHT] Admin access: OK (passwordless sudo)");
                }
            }
        }

        /// <summary>Checks for port conflicts on common FishMMO ports.</summary>
        private static async Task CheckPortConflictsAsync(PreFlightResult result)
        {
            var occupied = new List<int>();

            foreach (int port in PortsToCheck)
            {
                if (await IsPortInUseAsync(port))
                {
                    occupied.Add(port);
                }
            }

            if (occupied.Count > 0)
            {
                result.Warnings.Add(
                    $"Port(s) already in use: {string.Join(", ", occupied)}. " +
                    "Conflicts may occur if new services try to bind to these ports.");
            }
            else
            {
                await Log.Info("FishMMOInstaller", $"[PRE-FLIGHT] Port check: OK (no conflicts on standard FishMMO ports)");
            }
        }

        /// <summary>Returns true if the given TCP port is bound/listening.</summary>
        private static async Task<bool> IsPortInUseAsync(int port)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return await InstallerProcessHelper.RunProcessAsync(
                    "ss", $"-tlnp sport = :{port}",
                    (exit, output, _) => exit == 0 && !string.IsNullOrWhiteSpace(output));
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return await InstallerProcessHelper.RunProcessAsync(
                    "netstat", $"-ano | findstr :{port}",
                    (exit, _, _) => exit == 0);
            }

            return false;
        }
    }
}