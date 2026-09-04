using FishMMO.Logging;
using System.Runtime.InteropServices;

namespace FishMMO.Installer
{
    /// <summary>
    /// Automates host firewall rule creation for FishMMO server ports.
    /// Linux: ufw first, firewalld fallback. Windows: netsh.
    /// </summary>
    public static class FirewallInstaller
    {
        /// <summary>Ports opened when a manifest or menu does not name any: HTTP and HTTPS.</summary>
        public static readonly IReadOnlyList<FirewallPortSpec> DefaultPorts = new[]
        {
            FirewallPortSpec.TcpPort(80),
            FirewallPortSpec.TcpPort(443),
        };

        /// <summary>
        /// Opens the specified ports or port ranges on the host firewall.
        /// </summary>
        /// <param name="ports">Rules to open; each is a single port or a range on TCP or UDP.</param>
        /// <param name="prompt">When true, prompts for confirmation before making changes.</param>
        /// <returns>InstallResult indicating success or failure.</returns>
        public static async Task<InstallResult> OpenPortsAsync(IReadOnlyList<FirewallPortSpec> ports, bool prompt = true)
        {
            if (ports.Count == 0)
                return InstallResult.Ok("firewall");

            if (prompt && !InstallerProcessHelper.PromptForYesNo(
                    $"Open firewall ports {string.Join(", ", ports)}?"))
                return InstallResult.Fail("firewall", "User cancelled.");

            await Log.Info("FishMMOInstaller", $"Configuring firewall for ports: {string.Join(", ", ports)}");

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return await ConfigureWindowsFirewallAsync(ports);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return await ConfigureLinuxFirewallAsync(ports, prompt);
                }
                else
                {
                    return InstallResult.Fail("firewall", "Unsupported operating system.");
                }
            }
            catch (Exception ex)
            {
                await Log.Error("FishMMOInstaller", "Firewall configuration failed", ex);
                return InstallResult.Fail("firewall", ex.Message);
            }
        }

        /// <summary>Adds Windows Firewall rules via netsh.</summary>
        private static async Task<InstallResult> ConfigureWindowsFirewallAsync(IReadOnlyList<FirewallPortSpec> ports)
        {
            foreach (FirewallPortSpec port in ports)
            {
                // netsh accepts hyphenated ranges for localport (e.g. 7770-7999).
                string protocol = port.Protocol.ToUpperInvariant();
                string ruleName = $"FishMMO Port {port.PortText} {protocol}";
                bool ok = await InstallerProcessHelper.RunShellCommandAsync(
                    "cmd.exe", "/c",
                    $"netsh advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol={protocol} localport={port.PortText}",
                    $"Failed to add Windows Firewall rule for port {port}.");

                if (!ok)
                    return InstallResult.Fail("firewall", $"netsh failed for port {port}. Run as Administrator.");
            }

            await Log.Info("FishMMOInstaller", $"Windows Firewall rules added for ports: {string.Join(", ", ports)}");
            return InstallResult.Ok("firewall");
        }

        /// <summary>Adds Linux firewall rules via ufw (preferred) or firewalld.</summary>
        private static async Task<InstallResult> ConfigureLinuxFirewallAsync(IReadOnlyList<FirewallPortSpec> ports, bool prompt)
        {
            IPlatform platform = PlatformFactory.Current;
            (string shell, string argPrefix) = platform.GetShellCommand();

            bool ufwAvailable = await platform.IsCommandAvailableAsync("ufw");
            if (ufwAvailable)
            {
                // Check if ufw is already active; if not, warn before force-enabling
                // to avoid locking the user out of SSH.
                bool ufwActive = await InstallerProcessHelper.RunProcessAsync(
                    shell,
                    $"{argPrefix} \"sudo ufw status\"",
                    (exit, output, _) => exit == 0 && output.Contains("Status: active"));

                if (!ufwActive)
                {
                    await Log.Warning("FishMMOInstaller",
                        "ufw is not currently active. Enabling it will apply a default-deny inbound policy.");
                    await Log.Warning("FishMMOInstaller",
                        "If you are connected via SSH, ensure port 22 is allowed first.");

                    if (prompt && !InstallerProcessHelper.PromptForYesNo(
                            "Enable ufw now? (If unsure, say N and add your SSH port first: sudo ufw allow 22/tcp)"))
                    {
                        return InstallResult.Fail("firewall",
                            "User declined ufw enable. Add required inbound rules manually, then re-run.");
                    }

                    // Best-effort: add SSH before enabling so the user doesn't lose connectivity.
                    // Only add if port 22/tcp is not already in the ruleset.
                    bool sshAllowed = await InstallerProcessHelper.RunProcessAsync(
                        shell,
                        $"{argPrefix} \"sudo ufw status\"",
                        (exit, output, _) => exit == 0 && output.Contains("22/tcp"));
                    if (!sshAllowed)
                    {
                        await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix,
                            "sudo ufw allow 22/tcp",
                            "Failed to add SSH rule before enabling ufw. You may lose connectivity.");
                    }

                    // Enable ufw (--force skips the interactive prompt).
                    if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix,
                            "sudo ufw --force enable",
                            "ufw enable failed. Firewall rules cannot be applied."))
                    {
                        return InstallResult.Fail("firewall", "Failed to enable ufw.");
                    }
                }

                foreach (FirewallPortSpec port in ports)
                {
                    // ufw writes ranges with a colon: 7770:7999/udp.
                    if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix,
                            $"sudo ufw allow {port.UfwPortText}/{port.Protocol}",
                            $"Failed to add ufw rule for port {port}."))
                        return InstallResult.Fail("firewall", $"ufw rule failed for port {port}.");
                }

                await Log.Info("FishMMOInstaller", $"ufw rules added for ports: {string.Join(", ", ports)}");
                return InstallResult.Ok("firewall");
            }

            bool firewalldAvailable = await platform.IsCommandAvailableAsync("firewall-cmd");
            if (firewalldAvailable)
            {
                foreach (FirewallPortSpec port in ports)
                {
                    // firewalld writes ranges with a hyphen: 7770-7999/udp.
                    if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix,
                            $"sudo firewall-cmd --permanent --add-port={port}",
                            $"Failed to add firewalld rule for port {port}."))
                        return InstallResult.Fail("firewall", $"firewalld rule failed for port {port}.");
                }

                if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix,
                        "sudo firewall-cmd --reload",
                        "firewalld reload failed; rules may not take effect until next reload."))
                {
                    return InstallResult.Fail("firewall", "firewalld reload failed.");
                }

                await Log.Info("FishMMOInstaller", $"firewalld rules added for ports: {string.Join(", ", ports)}");
                return InstallResult.Ok("firewall");
            }

            return InstallResult.Fail("firewall",
                "No supported firewall (ufw, firewalld) detected. Install ufw or firewalld, then re-run.");
        }
    }
}