using FishMMO.Logging;
using System.Security.Cryptography;
using System.Text.Json;

namespace FishMMO.Installer
{
    /// <summary>
    /// Handles file downloads with SHA256 integrity verification, progress reporting,
    /// and intelligent skip-when-already-downloaded behavior.
    /// </summary>
    public static class DownloadHelper
    {
        /// <summary>
        /// Shared HttpClient instance for all downloads. Timeout set to 30 minutes
        /// to accommodate large installers (VS Build Tools, Unity Editor).
        /// </summary>
        public static readonly HttpClient Client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30),
        };

        /// <summary>In-memory checksum map: filename → SHA256 hex string (lowercase).</summary>
        private static Dictionary<string, string> _checksums = new(StringComparer.OrdinalIgnoreCase);

        private static bool _checksumsLoaded;

        /// <summary>
        /// Loads checksums from <c>checksums.json</c> in the working directory.
        /// Call once at startup. Safe to call multiple times (idempotent).
        /// </summary>
        public static async Task LoadChecksumsAsync()
        {
            if (_checksumsLoaded) return;

            string path = Path.Combine(AppContext.BaseDirectory, "checksums.json");
            if (!File.Exists(path))
            {
                await Log.Warning("FishMMOInstaller", "checksums.json not found; downloads will skip integrity verification.");
                _checksumsLoaded = true;
                return;
            }

            try
            {
                string json = await File.ReadAllTextAsync(path);
                _checksums = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                             ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                int populated = _checksums.Count(kv => !string.IsNullOrWhiteSpace(kv.Value));
                await Log.Info("FishMMOInstaller",
                    $"Loaded {populated} checksum(s) from checksums.json ({_checksums.Count - populated} pending).");
            }
            catch (Exception ex)
            {
                await Log.Warning("FishMMOInstaller",
                    $"Failed to parse checksums.json: {ex.Message}. Integrity verification disabled.");
            }

            _checksumsLoaded = true;
        }

        /// <summary>
        /// Verifies a downloaded file against its registered SHA256 checksum.
        /// Returns true if the file matches or no checksum is registered (skip verification).
        /// Returns false if checksum exists but does not match.
        /// </summary>
        public static bool VerifyChecksum(string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            if (!_checksums.TryGetValue(fileName, out string? expected)
                || string.IsNullOrWhiteSpace(expected))
            {
                return true; // no checksum registered — skip verification
            }

            try
            {
                using var stream = File.OpenRead(filePath);
                byte[] hash = SHA256.HashData(stream);
                string actual = Convert.ToHexString(hash).ToLowerInvariant();
                bool matches = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
                if (!matches)
                {
                    _ = Log.Warning("FishMMOInstaller",
                        $"Checksum mismatch for {fileName}: expected {expected}, actual {actual}");
                }
                return matches;
            }
            catch (Exception ex)
            {
                _ = Log.Warning("FishMMOInstaller", $"Failed to verify checksum for {fileName}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Downloads a file with console progress reporting (percentage + MB).
        /// Skips download if file already exists with valid checksum.
        /// Throws <see cref="InvalidDataException"/> on checksum mismatch.
        /// </summary>
        /// <param name="url">Download URL.</param>
        /// <param name="fileName">Local filename.</param>
        /// <param name="progress">Optional progress reporter (receives 0–100).</param>
        /// <returns>Full path to downloaded file, or null on failure.</returns>
        public static async Task<string?> DownloadFileWithProgressAsync(
            string url,
            string fileName,
            IProgress<int>? progress = null)
        {
            string outputPath = Path.Combine(AppContext.BaseDirectory, fileName);

            // Skip if already downloaded and valid
            if (File.Exists(outputPath) && VerifyChecksum(outputPath))
            {
                await Log.Info("FishMMOInstaller", $"{fileName} already downloaded with valid checksum; skipping.");
                return outputPath;
            }

            if (File.Exists(outputPath))
            {
                // File exists but checksum failed — delete and re-download
                File.Delete(outputPath);
                await Log.Info("FishMMOInstaller", $"{fileName} exists but checksum invalid; re-downloading.");
            }

            await Log.Info("FishMMOInstaller", $"Downloading {fileName}...");

            try
            {
                using HttpResponseMessage response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                long totalBytes = response.Content.Headers.ContentLength ?? -1;
                using Stream stream = await response.Content.ReadAsStreamAsync();
                using Stream fileStream = File.Create(outputPath);

                var buffer = new byte[81920]; // 80 KB buffer
                long bytesRead = 0;
                int bytesJustRead;
                int lastReportedPercent = -1;

                while ((bytesJustRead = await stream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesJustRead));
                    bytesRead += bytesJustRead;

                    if (totalBytes > 0)
                    {
                        int percent = (int)(bytesRead * 100 / totalBytes);
                        if (percent != lastReportedPercent)
                        {
                            progress?.Report(percent);
                            lastReportedPercent = percent;
                        }
                    }
                }

                // Finish progress line
                progress?.Report(100);

                // Verify checksum
                if (!VerifyChecksum(outputPath))
                {
                    File.Delete(outputPath);
                    throw new InvalidDataException(
                        $"Checksum verification failed for {fileName}. The download may be corrupted or tampered.");
                }

                string sizeDisplay = totalBytes > 0
                    ? $"{totalBytes / (1024.0 * 1024.0):F1} MB"
                    : $"{bytesRead:N0} bytes";
                await Log.Info("FishMMOInstaller", $"Downloaded {fileName} ({sizeDisplay})");
                return outputPath;
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex)
            {
                await Log.Error("FishMMOInstaller", $"Failed to download {fileName}", ex);
                // Clean up partial download
                try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { /* best-effort */ }
                return null;
            }
        }

        /// <summary>
        /// Generates SHA256 checksums for all files listed in <c>checksums.json</c>
        /// that are present in the working directory and writes the updated file back.
        /// Files not found locally keep their existing checksum (or empty) values.
        /// </summary>
        public static async Task GenerateChecksumsAsync()
        {
            string checksumsPath = Path.Combine(AppContext.BaseDirectory, "checksums.json");
            if (!File.Exists(checksumsPath))
            {
                await Log.Warning("FishMMOInstaller", $"checksums.json not found at '{checksumsPath}'. Nothing to generate.");
                return;
            }

            string json = await File.ReadAllTextAsync(checksumsPath);
            var checksums = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            int updated = 0;
            int missing = 0;
            foreach (string fileName in checksums.Keys.ToList())
            {
                string filePath = Path.Combine(AppContext.BaseDirectory, fileName);
                if (!File.Exists(filePath))
                {
                    missing++;
                    continue;
                }

                try
                {
                    using var stream = File.OpenRead(filePath);
                    byte[] hash = SHA256.HashData(stream);
                    string hexHash = Convert.ToHexString(hash).ToLowerInvariant();
                    checksums[fileName] = hexHash;
                    await Log.Info("FishMMOInstaller", $"  {fileName} → {hexHash}");
                    updated++;
                }
                catch (Exception ex)
                {
                    await Log.Warning("FishMMOInstaller", $"Failed to hash {fileName}: {ex.Message}");
                }
            }

            string updatedJson = JsonSerializer.Serialize(checksums, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(checksumsPath, updatedJson);

            await Log.Info("FishMMOInstaller",
                $"Checksums updated: {updated} files hashed, {missing} files not found locally.");
            Console.WriteLine();
            Console.WriteLine($"Checksums written to: {checksumsPath}");
            Console.WriteLine($"  {updated} file(s) hashed, {missing} file(s) not found locally.");
            Console.WriteLine("Copy this file back to the source directory to persist across builds.");
        }

        /// <summary>
        /// Checks available disk space on the working directory drive and warns if
        /// less than <paramref name="requiredBytes"/> is free. Returns false only
        /// when the check itself fails (drive not found); a low-space condition
        /// logs a warning but returns true so the caller can decide whether to abort.
        /// </summary>
        public static bool CheckDiskSpace(long requiredBytes)
        {
            try
            {
                string workingDir = AppContext.BaseDirectory;
                var drive = DriveInfo.GetDrives().FirstOrDefault(d =>
                    workingDir.StartsWith(d.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase));

                if (drive == null)
                    return true; // can't determine — don't block

                long freeMB = drive.AvailableFreeSpace / (1024 * 1024);
                long requiredMB = requiredBytes / (1024 * 1024);

                if (drive.AvailableFreeSpace < requiredBytes)
                {
                    _ = Log.Warning("FishMMOInstaller",
                        $"Low disk space on {drive.Name}: {freeMB} MB free, ~{requiredMB} MB required. " +
                        "The download may fail. Free up space or continue at your own risk.");
                }
                else
                {
                    _ = Log.Debug("FishMMOInstaller",
                        $"Disk space OK: {freeMB} MB free on {drive.Name} (need ~{requiredMB} MB).");
                }
                return true;
            }
            catch
            {
                return true; // can't check — don't block
            }
        }

        /// <summary>
        /// Simple console progress bar implementation for use as IProgress&lt;int&gt;.
        /// </summary>
        public sealed class ConsoleProgress : IProgress<int>
        {
            private int _lastPercent = -1;

            public void Report(int percent)
            {
                if (percent == _lastPercent) return;
                _lastPercent = percent;

                int barWidth = 40;
                int filled = barWidth * percent / 100;
                string bar = new string('#', filled) + new string('-', barWidth - filled);
                Console.Write($"\r  [{bar}] {percent,3}%");
                if (percent >= 100)
                    Console.WriteLine();
            }
        }
    }
}