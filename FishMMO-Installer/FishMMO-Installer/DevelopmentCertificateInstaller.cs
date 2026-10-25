using FishMMO.Logging;
using System.Text.RegularExpressions;

namespace FishMMO.Installer;

/// <summary>Local Linux TLS setup without public DNS, ACME, or nginx.</summary>
public static class DevelopmentCertificateInstaller
{
    public static async Task<bool> InstallAsync()
    {
        if (!OperatingSystem.IsLinux())
        {
            await Log.Warning("FishMMOInstaller", "Development certificate deployment currently supports Linux.");
            return false;
        }
        await Log.Info("FishMMOInstaller", "--- Install / Renew Local Development Certificates ---");
        string defaultUser = Environment.GetEnvironmentVariable("SUDO_USER") ?? Environment.UserName;
        string user = (InstallerProcessHelper.PromptForInput($"Game-server OS account [{defaultUser}]: ") ?? "").Trim();
        if (user.Length == 0) user = defaultUser;
        if (!Regex.IsMatch(user, @"^[a-zA-Z_][a-zA-Z0-9_-]*\$?$"))
        {
            await Log.Error("FishMMOInstaller", "Invalid account name.");
            return false;
        }
        bool trust = InstallerProcessHelper.PromptForYesNo(
            "Trust the FishMMO local development CA on this machine (needed for native clients)?");
        string temporaryScript = Path.Combine(Path.GetTempPath(), $"fishmmo-dev-certificates-{Guid.NewGuid():N}.sh");
        try
        {
            var assembly = typeof(DevelopmentCertificateInstaller).Assembly;
            using var resource = assembly.GetManifestResourceStream(
                "FishMMO.Installer.Resources.InstallDevelopmentCertificates.sh")
                ?? throw new InvalidOperationException("Development certificate script is missing from installer resources.");
            await using (var file = new FileStream(temporaryScript, new FileStreamOptions
            {
                Mode = FileMode.CreateNew, Access = FileAccess.Write,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            }))
                await resource.CopyToAsync(file);
            string command = $"sudo /bin/bash {GameServerCertificateInstaller.Quote(temporaryScript)} --user {GameServerCertificateInstaller.Quote(user)}" +
                (trust ? " --trust" : "");
            var (shell, prefix) = InstallerProcessHelper.GetShellCommand();
            return await InstallerProcessHelper.RunShellCommandWithLiveOutputAsync(shell, prefix, command,
                "Development certificate installation failed. Review the output above.");
        }
        finally
        {
            if (File.Exists(temporaryScript)) File.Delete(temporaryScript);
        }
    }
}
