namespace FishMMO.Installer
{
    /// <summary>
    /// Information about a detected Linux package manager.
    /// </summary>
    /// <param name="UpdateCommand">Shell command to update the package database.</param>
    /// <param name="InstallCommand">Shell command to install the requested packages.</param>
    /// <param name="ManagerName">Human-readable manager name for logging.</param>
    public sealed record PackageManagerInfo(
        string UpdateCommand,
        string InstallCommand,
        string ManagerName);

    /// <summary>
    /// Detects and wraps the Linux package manager (pacman, apt-get, dnf, yum).
    /// Extracted from InstallerProcessHelper to keep infrastructure concerns separated.
    /// </summary>
    public static class LinuxPackageManagerHelper
    {
        /// <summary>
        /// Detects the available package manager and returns prepopulated update/install commands.
        /// </summary>
        /// <param name="packageNames">
        /// Dictionary mapping package manager name to the install package argument.
        /// E.g. { "pacman": "postgresql", "apt-get": "postgresql postgresql-contrib" }.
        /// </param>
        /// <returns>Package manager info, or null if none detected.</returns>
        public static async Task<PackageManagerInfo?> DetectAsync(Dictionary<string, string> packageNames)
        {
            IPlatform platform = PlatformFactory.Current;

            if (packageNames.ContainsKey("pacman")
                && await platform.IsCommandAvailableAsync("pacman"))
            {
                return new PackageManagerInfo(
                    "sudo pacman -Sy --noconfirm",
                    $"sudo pacman -S --noconfirm --needed {packageNames["pacman"]}",
                    "pacman (Arch/CachyOS)");
            }

            if (packageNames.ContainsKey("apt-get")
                && await platform.IsCommandAvailableAsync("apt-get"))
            {
                return new PackageManagerInfo(
                    "sudo apt-get update -qq",
                    $"sudo apt-get install -y {packageNames["apt-get"]}",
                    "apt-get (Debian/Ubuntu)");
            }

            if (packageNames.ContainsKey("dnf")
                && await platform.IsCommandAvailableAsync("dnf"))
            {
                return new PackageManagerInfo(
                    "sudo dnf makecache",
                    $"sudo dnf install -y {packageNames["dnf"]}",
                    "dnf (Fedora/RHEL)");
            }

            if (packageNames.ContainsKey("yum")
                && await platform.IsCommandAvailableAsync("yum"))
            {
                return new PackageManagerInfo(
                    "sudo yum makecache",
                    $"sudo yum install -y {packageNames["yum"]}",
                    "yum (RHEL)");
            }

            return null;
        }
    }
}