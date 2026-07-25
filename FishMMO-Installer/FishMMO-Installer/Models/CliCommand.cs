namespace FishMMO.Installer
{
    /// <summary>
    /// Parsed CLI command from <c>Program.Main(string[] args)</c>.
    /// </summary>
    public sealed record CliCommand
    {
        /// <summary>Show help text and exit.</summary>
        public bool ShowHelp { get; init; }

        /// <summary>Show version and exit.</summary>
        public bool ShowVersion { get; init; }

        /// <summary>Run without prompting for user input.</summary>
        public bool NonInteractive { get; init; }

        /// <summary>Simulate installation without making changes.</summary>
        public bool DryRun { get; init; }

        /// <summary>Run post-install health checks and exit.</summary>
        public bool ValidateMode { get; init; }

        /// <summary>Single component to install (bypasses menu).</summary>
        public string? ComponentName { get; init; }

        /// <summary>Path to install-config.json for non-interactive mode.</summary>
        public string? ConfigFilePath { get; init; }

        /// <summary>Generate SHA256 checksums for all downloaded files.</summary>
        public bool GenerateChecksums { get; init; }

        /// <summary>Shortcut for --non-interactive with the quickstart template config.</summary>
        public bool Quickstart { get; init; }

        /// <summary>Skip all Y/N confirmation prompts (auto-accept defaults).</summary>
        public bool AcceptDefaults { get; init; }

        /// <summary>List available component names and exit.</summary>
        public bool ListComponents { get; init; }

        /// <summary>Path to write log output in addition to console.</summary>

		/// <summary>
		/// Comma-separated list of region IDs for --configure-server-secrets.
		/// Each region gets its own keyId + HMAC key pair.
		/// </summary>
		public string? DeploymentSecretsRegions { get; init; }
        public string? LogFilePath { get; init; }

        /// <summary>True when no CLI flags were provided — enter interactive menu.</summary>
        public bool IsDefault => !ShowHelp && !ShowVersion && !NonInteractive && !DryRun
                                && !ValidateMode && !GenerateChecksums && !Quickstart
                                && !AcceptDefaults && !ListComponents
                                && ComponentName == null && ConfigFilePath == null
								&& LogFilePath == null && DeploymentSecretsRegions == null;
    }
}