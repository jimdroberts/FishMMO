using FishMMO.Logging;
using System.Text.RegularExpressions;

namespace FishMMO.Installer;

/// <summary>Deploys a Certbot lineage for non-root game servers, including subsequent renewals.</summary>
public static class GameServerCertificateInstaller
{
    public const string CertificateDirectory = "/etc/fishmmo/certs";
    public const string ReaderGroup = "fishmmo-certs";
    public const string HookPath = "/etc/letsencrypt/renewal-hooks/deploy/fishmmo-certificates";

    public static async Task<bool> InstallAsync(string lineage)
    {
        string defaultUser = Environment.GetEnvironmentVariable("SUDO_USER") ?? Environment.UserName;
        string user = (InstallerProcessHelper.PromptForInput(
            $"Game-server OS account allowed to read the TLS key [{defaultUser}]: ") ?? "").Trim();
        if (user.Length == 0) user = defaultUser;
        if (!Regex.IsMatch(user, @"^[a-zA-Z_][a-zA-Z0-9_-]*\$?$"))
        {
            await Log.Error("FishMMOInstaller", "Invalid game-server account name.");
            return false;
        }
        var (shell, prefix) = InstallerProcessHelper.GetShellCommand();
        async Task<bool> Run(string command) => await InstallerProcessHelper.RunShellCommandAsync(
            shell, prefix, command, "Game-server certificate deployment failed.");
        if (!await Run($"id -u {Quote(user)}")) return false;
        // groupadd -f preserves an existing group; no private-key permissions are broadened to others.
        if (!await Run($"sudo groupadd -f {ReaderGroup}")) return false;
        if (!await Run($"sudo usermod -a -G {ReaderGroup} {Quote(user)}")) return false;
        if (!await Run("sudo install -d -o root -g root -m 0755 /etc/fishmmo /etc/letsencrypt/renewal-hooks/deploy")) return false;
        if (!await Run($"sudo install -d -o root -g {ReaderGroup} -m 0750 {CertificateDirectory}")) return false;
        if (!await LinuxConfigHardeningHelper.SudoInstallAsync(
            GenerateHook(lineage, CertificateDirectory, ReaderGroup), HookPath, "root", "root", "0750")) return false;
        if (!await Run($"sudo /bin/bash {HookPath}")) return false;
        if (!await EnsureRenewalTimerAsync()) return false;
        await Log.Info("FishMMOInstaller",
            $"Game-server certificates deployed to {CertificateDirectory}; renewals will update them automatically. " +
            $"Account '{user}' must start a new login session to acquire group '{ReaderGroup}'. " +
            "Restart running game servers after certificate renewal; certificates are loaded at startup. " +
            "For other service accounts, grant membership in the same reader group.");
        return true;
    }

    private static async Task<bool> EnsureRenewalTimerAsync()
    {
        var (shell, prefix) = InstallerProcessHelper.GetShellCommand();
        // Prefer the distribution's scheduler; installing a package does not always enable it.
        foreach (string timer in new[] { "certbot.timer", "certbot-renew.timer" })
        {
            if (new[] { "/etc/systemd/system", "/usr/lib/systemd/system", "/lib/systemd/system" }
                .Any(directory => File.Exists(Path.Combine(directory, timer))))
                return await InstallerProcessHelper.RunShellCommandAsync(shell, prefix,
                    $"sudo systemctl enable --now {timer}", "Failed to enable certificate renewal timer.");
        }
        const string service = "[Unit]\nDescription=Renew Let's Encrypt certificates for FishMMO\n" +
            "[Service]\nType=oneshot\nExecStart=/usr/bin/env certbot renew --quiet\n";
        const string timerContent = "[Unit]\nDescription=Check FishMMO TLS certificate renewals twice daily\n" +
            "[Timer]\nOnCalendar=*-*-* 00,12:00:00\nRandomizedDelaySec=1h\nPersistent=true\n" +
            "[Install]\nWantedBy=timers.target\n";
        if (!await LinuxConfigHardeningHelper.SudoInstallAsync(service,
            "/etc/systemd/system/fishmmo-certbot-renew.service", "root", "root", "0644")) return false;
        if (!await LinuxConfigHardeningHelper.SudoInstallAsync(timerContent,
            "/etc/systemd/system/fishmmo-certbot-renew.timer", "root", "root", "0644")) return false;
        if (!await InstallerProcessHelper.RunShellCommandAsync(shell, prefix,
            "sudo systemctl daemon-reload", "Failed to reload systemd for certificate renewal.")) return false;
        return await InstallerProcessHelper.RunShellCommandAsync(shell, prefix,
            "sudo systemctl enable --now fishmmo-certbot-renew.timer", "Failed to enable certificate renewal timer.");
    }

    internal static string Quote(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";

    internal static string GenerateHook(string lineage, string destination, string group) => $$"""
        #!/bin/bash
        set -euo pipefail
        umask 077
        lineage={{Quote(lineage)}}
        destination={{Quote(destination)}}
        reader_group={{Quote(group)}}
        # Certbot invokes every deployment hook for every renewed lineage.
        if [[ "${RENEWED_LINEAGE:-$lineage}" != "$lineage" ]]; then exit 0; fi
        exec 9>"$destination/.deploy.lock"
        flock -x 9
        openssl x509 -in "$lineage/fullchain.pem" -noout -checkend 0
        cert_public=$(openssl x509 -in "$lineage/fullchain.pem" -pubkey -noout | openssl pkey -pubin -outform DER | openssl dgst -sha256)
        key_public=$(openssl pkey -in "$lineage/privkey.pem" -pubout -outform DER | openssl dgst -sha256)
        if [[ "$cert_public" != "$key_public" ]]; then
            echo 'FishMMO certificate and private key do not match.' >&2
            exit 1
        fi
        # Publish the pair through one atomic symlink switch. Keep the previous pair for recovery.
        release=$(mktemp -d "$destination/release.XXXXXXXX")
        published=0
        cleanup() {
            rm -f "$destination/.current.next"
            if [[ "$published" == 0 ]]; then rm -rf "$release"; fi
        }
        trap cleanup EXIT
        chgrp "$reader_group" "$release"
        chmod 0750 "$release"
        install -g "$reader_group" -m 0640 "$lineage/fullchain.pem" "$release/fullchain.pem"
        install -g "$reader_group" -m 0640 "$lineage/privkey.pem" "$release/privkey.pem"
        ln -sfn "$(basename "$release")" "$destination/.current.next"
        mv -Tf "$destination/.current.next" "$destination/current"
        published=1
        ln -sfn current/fullchain.pem "$destination/fullchain.pem"
        ln -sfn current/privkey.pem "$destination/privkey.pem"
        if [[ -n "${RENEWED_LINEAGE:-}" ]] && command -v systemctl >/dev/null && systemctl is-active --quiet nginx; then
            nginx -t
            systemctl reload nginx
        fi
        echo 'FishMMO TLS files updated. Restart running game servers to load renewed certificates.'
        """ + "\n";
}
