using System.Runtime.InteropServices;

namespace FishMMO.Installer
{
    /// <summary>
    /// Abstracts OS-specific operations so installer components don't scatter
    /// <see cref="RuntimeInformation.IsOSPlatform"/> checks everywhere.
    /// </summary>
    public interface IPlatform
    {
        /// <summary>True when running on Windows.</summary>
        bool IsWindows { get; }

        /// <summary>True when running on Linux.</summary>
        bool IsLinux { get; }

        /// <summary>
        /// Returns the OS-appropriate shell executable and argument prefix.
        /// Windows: (cmd.exe, /c). Linux: fish if available, else bash.
        /// </summary>
        (string shell, string argPrefix) GetShellCommand();

        /// <summary>Checks whether a given executable is available on PATH.</summary>
        Task<bool> IsCommandAvailableAsync(string command);
    }

    /// <summary>Windows platform implementation.</summary>
    public class WindowsPlatform : IPlatform
    {
        public bool IsWindows => true;
        public bool IsLinux => false;

        public (string shell, string argPrefix) GetShellCommand()
            => ("cmd.exe", "/c");

        public async Task<bool> IsCommandAvailableAsync(string command)
        {
            return await InstallerProcessHelper.RunProcessAsync(
                "where", command,
                (exit, _, _) => exit == 0);
        }
    }

    /// <summary>Linux platform implementation.</summary>
    public class LinuxPlatform : IPlatform
    {
        public bool IsWindows => false;
        public bool IsLinux => true;

        public (string shell, string argPrefix) GetShellCommand()
        {
            // Prefer fish for developer workstations, fall back to bash (always available on servers)
            const string fishShellPath = "/usr/bin/fish";
            if (File.Exists(fishShellPath))
            {
                return (fishShellPath, "-lc");
            }
            return ("/bin/bash", "-c");
        }

        public async Task<bool> IsCommandAvailableAsync(string command)
        {
            (string shell, string argPrefix) = GetShellCommand();
            return await InstallerProcessHelper.RunProcessAsync(
                shell,
                $"{argPrefix} \"command -v {command}\"",
                (exit, _, _) => exit == 0);
        }
    }

    /// <summary>Provides the singleton platform instance for the current OS.</summary>
    public static class PlatformFactory
    {
        /// <summary>The platform instance for the current OS.</summary>
        public static IPlatform Current { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new WindowsPlatform()
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? new LinuxPlatform()
                : throw new PlatformNotSupportedException(
                    "FishMMO-Installer supports Windows and Linux only.");
    }
}