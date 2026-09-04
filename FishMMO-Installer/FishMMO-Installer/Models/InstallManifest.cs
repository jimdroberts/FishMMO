using System.Text.Json.Serialization;

namespace FishMMO.Installer
{
    /// <summary>
    /// Deserialized from <c>install-config.json</c> for non-interactive installation.
    /// </summary>
    public sealed record InstallManifest
    {
        /// <summary>Component names to install, in desired order (e.g. ["postgresql", "nginx"]).</summary>
        [JsonPropertyName("components")]
        public List<string> Components { get; init; } = new();

        /// <summary>Whether to configure firewall rules after component installation.</summary>
        [JsonPropertyName("configureFirewall")]
        public bool ConfigureFirewall { get; init; }

        /// <summary>
        /// Firewall rules to open. Entries are a bare port number (TCP) or a string
        /// with an optional range and protocol, e.g. <c>[80, 443, "7770-7999/udp"]</c>.
        /// </summary>
        [JsonPropertyName("firewallPorts")]
        public List<FirewallPortSpec> FirewallPorts { get; init; } = new();

        /// <summary>Whether to register FishMMO web servers as systemd services.</summary>
        [JsonPropertyName("registerSystemdServices")]
        public bool RegisterSystemdServices { get; init; }

        /// <summary>
        /// Web server names to register. Both short names (<c>ipfetch</c>) and
        /// full service names (<c>fishmmo-ipfetch</c>) are accepted. When empty
        /// or omitted, all three servers are registered.
        /// </summary>
        [JsonPropertyName("webServers")]
        public List<string> WebServers { get; init; } = new();

        /// <summary>Run health checks after installation completes.</summary>
        [JsonPropertyName("validateAfterInstall")]
        public bool ValidateAfterInstall { get; init; }

        /// <summary>Simulate without making changes.</summary>
        [JsonPropertyName("dryRun")]
        public bool DryRun { get; init; }
    }
}