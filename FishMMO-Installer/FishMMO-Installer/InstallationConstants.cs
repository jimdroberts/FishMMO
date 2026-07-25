using System.Reflection;

namespace FishMMO.Installer
{
	/// <summary>
	/// Contains constants for installation URLs, filenames, default configuration values,
	/// and EF Core project paths for FishMMO dependencies.
	/// </summary>
	public static class InstallationConstants
	{
		/// <summary>
		/// URL for the DotNet install shell script, used to install the ASP.NET
		/// Core runtime on Linux via the --runtime aspnetcore flag.
		/// </summary>
		public const string DotNetInstallScriptUrl = "https://dot.net/v1/dotnet-install.sh";

		/// <summary>
		/// Filename for the downloaded DotNet install shell script.
		/// </summary>
		public const string DotNetInstallScriptFileName = "dotnet-install.sh";

		/// <summary>
		/// DotNet SDK major version for compatibility checks.
		/// </summary>
		public const string DotNetSDKMajorVersion = "8.0";

		/// <summary>
		/// DotNet Entity Framework tool version to install.
		/// </summary>
		public const string DotNetEFVersion = "5.0.17";

		/// <summary>
		/// ASP.NET Core runtime major version to install (e.g. "8.0").
		/// </summary>
		public const string AspNetRuntimeMajorVersion = "8.0";

		/// <summary>
		/// Download URL for the ASP.NET Core 8 Runtime Windows installer (x64 Hosting Bundle).
		/// Includes Kestrel runtime, IIS module, and the ASP.NET Core shared framework.
		/// </summary>
		public const string AspNetRuntimeWindowsUrl = "https://download.visualstudio.microsoft.com/download/pr/751d3fe9-0a4e-4cb7-aa28-b4d3b55f2df2/2de0e4eea90cc10c26df6b0bec0748fa/dotnet-hosting-8.0.16-win.exe";

		/// <summary>
		/// Filename for the downloaded ASP.NET Core Windows Hosting Bundle installer.
		/// </summary>
		public const string AspNetRuntimeWindowsFileName = "dotnet-hosting-8.0.16-win.exe";

		/// <summary>
		/// .NET release metadata channel for dynamic runtime URL resolution (e.g. "8.0").
		/// </summary>
		public const string DotNetRuntimeChannel = "8.0";

		/// <summary>
		/// ASP.NET Core runtime version string used for Linux installation via
		/// dotnet-install.sh --runtime aspnetcore --version {version}.
		/// </summary>
		public const string AspNetRuntimeLinuxVersion = "8.0.16";

		/// <summary>
		/// Download URL for the PostgreSQL Windows installer.
		/// </summary>
		public const string PostgreSQLWindowsInstallerUrl = @"https://sbp.enterprisedb.com/getfile.jsp?fileid=1259105";

		/// <summary>
		/// Filename for the downloaded PostgreSQL Windows installer.
		/// </summary>
		public const string PostgreSQLWindowsInstallerFileName = "PostgreSQLInstaller.exe";

		/// <summary>
		/// Default PostgreSQL superuser account name.
		/// </summary>
		public const string PostgreSQLDefaultSuperuser = "postgres";

		/// <summary>
		/// Default PostgreSQL administrative database name.
		/// </summary>
		public const string PostgreSQLDefaultAdminDb = "postgres";

		/// <summary>
		/// Default PgBouncer listen port.
		/// </summary>
		public const string PgBouncerDefaultPort = "6432";

		/// <summary>
		/// Default Linux systemd service name for PgBouncer.
		/// </summary>
		public const string PgBouncerLinuxServiceName = "pgbouncer";

		/// <summary>
		/// Download URL for the NGINX Windows zip archive.
		/// </summary>
		public const string NGINXWindowsDownloadUrl = "https://github.com/nginx/nginx/releases/download/release-1.29.5/nginx-1.29.5.zip";

		/// <summary>
		/// Filename for the downloaded NGINX Windows zip archive.
		/// </summary>
		public const string NGINXWindowsFileName = "nginx-1.29.5.zip";

		/// <summary>
		/// Default extraction path for NGINX on Windows.
		/// </summary>
		public const string NGINXWindowsExtractPath = "C:\\nginx";

		/// <summary>
		/// Windows service name used for the NGINX service instance managed by this installer.
		/// </summary>
		public const string NGINXWindowsServiceName = "FishMMO-NGINX";
		/// <summary>
		/// Windows service names for FishMMO web servers managed by NSSM.
		/// </summary>
		public const string IpFetchWindowsServiceName = "FishMMO-IpFetch";
		public const string PatcherWindowsServiceName = "FishMMO-Patcher";
		public const string WebGLWindowsServiceName = "FishMMO-WebGL";
		public const string AppHealthMonitorWindowsServiceName = "FishMMO-AppHealthMonitor";

		/// <summary>
		/// Systemd service name for the AppHealthMonitor process supervisor daemon.
		/// </summary>
		public const string AppHealthMonitorSystemdServiceName = "fishmmo-apphealthmonitor";

		/// <summary>
		/// Download URL for the win-acme client (Windows Let's Encrypt automation).
		/// </summary>
		public const string WinAcmeDownloadUrl = "https://github.com/win-acme/win-acme/releases/download/v2.2.9.1701/win-acme.v2.2.9.1701.x64.trimmed.zip";

		/// <summary>
		/// Filename for the downloaded win-acme zip archive.
		/// </summary>
		public const string WinAcmeFileName = "win-acme.v2.2.9.1701.x64.trimmed.zip";

		/// <summary>
		/// Download URL for the NSSM zip archive used to run NGINX as a proper Windows service.
		/// </summary>
		public const string NssmDownloadUrl = "https://nssm.cc/release/nssm-2.24.zip";

		/// <summary>
		/// Filename for the downloaded NSSM zip archive.
		/// </summary>
		public const string NssmFileName = "nssm-2.24.zip";

		/// <summary>
		/// Default web root for ACME HTTP-01 challenges in Linux deployments.
		/// </summary>
		public const string LinuxCertbotWebRoot = "/var/www/certbot";

		/// <summary>
		/// Download URL for the Visual Studio Build Tools bootstrapper.
		/// </summary>
		public const string VSBuildToolsUrl = "https://aka.ms/vs/17/release/vs_buildtools.exe";

		/// <summary>
		/// Filename for the downloaded Visual Studio Build Tools bootstrapper.
		/// </summary>
		public const string VSBuildToolsFileName = "vs_buildtools.exe";

		/// <summary>
		/// Download URL for the Unity Hub Windows installer.
		/// </summary>
		public const string UnityHubWindowsDownloadUrl = "https://public-cdn.cloud.unity3d.com/hub/prod/UnityHubSetup.exe";

		/// <summary>
		/// Filename for the downloaded Unity Hub Windows installer.
		/// </summary>
		public const string UnityHubWindowsFileName = "UnityHubSetup.exe";

		/// <summary>
		/// Default Unity Editor version to install.
		/// </summary>
		public const string UnityDefaultVersion = "6000.3.2f1";

		/// <summary>
		/// Available Unity modules that can be installed alongside the editor.
		/// Each entry is a (moduleId, displayName) tuple for the Unity Hub CLI.
		/// </summary>
		public static readonly (string moduleId, string displayName)[] UnityAvailableModules =
		{
			("linux-il2cpp", "Linux Build Support (IL2CPP)"),
			("linux-mono", "Linux Build Support (Mono)"),
			("linux-dedicated-server", "Linux Dedicated Server Build Support"),
			("mac-mono", "Mac Build Support (Mono)"),
			("mac-il2cpp", "Mac Build Support (IL2CPP)"),
			("mac-server", "Mac Dedicated Server Build Support"),
			("windows-mono", "Windows Build Support (Mono)"),
			("windows-il2cpp", "Windows Build Support (IL2CPP)"),
			("windows-dedicated-server", "Windows Dedicated Server Build Support"),
			("webgl", "Web Build Support"),
			("android", "Android Build Support"),
			("ios", "iOS Build Support"),
		};

		/// <summary>
		/// Default module identifiers to install when the user accepts defaults.
		/// </summary>
		public static readonly string[] UnityDefaultModules =
		{
			"linux-il2cpp",
			"linux-dedicated-server",
			"webgl",
			"windows-mono",
			"windows-il2cpp",
			"windows-dedicated-server",
		};

		/// <summary>
		/// Relative path to the FishMMO-DB EF Core project file for migrations.
		/// Resolved against <see cref="AppContext.BaseDirectory"/> at runtime
		/// by <see cref="DotNetInstaller.RunEFMigrationAsync"/>.
		/// </summary>
		public static readonly string ProjectPath = Path.Combine(".", "FishMMO-Database", "FishMMO-DB", "FishMMO-DB.csproj");

		/// <summary>
		/// Relative path to the FishMMO-DB-Migrator startup project for EF Core commands.
		/// Resolved against <see cref="AppContext.BaseDirectory"/> at runtime
		/// by <see cref="DotNetInstaller.RunEFDatabaseUpdateAsync"/>.
		/// </summary>
		public static readonly string StartupProject = Path.Combine(".", "FishMMO-Database", "FishMMO-DB-Migrator", "FishMMO-DB-Migrator.csproj");



		// ---------------------------------------------------------------------
		// PgBouncer configuration generation
		// ---------------------------------------------------------------------

		/// <summary>Linux configuration directory for PgBouncer.</summary>
		public const string PgBouncerLinuxConfigDirectory = "/etc/pgbouncer";

		/// <summary>Linux pgbouncer.ini path.</summary>
		public const string PgBouncerLinuxIniPath = "/etc/pgbouncer/pgbouncer.ini";

		/// <summary>Linux userlist.txt path.</summary>
		public const string PgBouncerLinuxUserlistPath = "/etc/pgbouncer/userlist.txt";

		/// <summary>
		/// Dedicated auth lookup role for PgBouncer's auth_query function.
		/// Not yet wired into config generation; reserved for future use.
		/// </summary>
		public const string PgBouncerAuthUser = "fishmmo_pgb_auth";

		// ---------------------------------------------------------------------
		// FishMMO repository layout (monorepo root detected from assembly location)
		// ---------------------------------------------------------------------

		/// <summary>
		/// Root path of the FishMMO monorepo workspace.
		/// Walks up from the executing assembly until it finds a directory containing
		/// both FishMMO-Unity and FishMMO-Setup subdirectories (monorepo signature).
		/// Falls back to ~/Dev/FishMMO-Dev if the root cannot be auto-detected.
		/// </summary>
		public static readonly string FishMMOMonorepoRoot = FindMonorepoRoot();

		/// <summary>
		/// Absolute path to the shared EF Core Migrations directory at the monorepo root.
		/// Migrations live here rather than inside the FishMMO-DB project so they survive
		/// clean builds, database drops, and Installer rebuilds without stashing/restoring.
		/// </summary>
		/// <remarks>MUST come after <see cref="FishMMOMonorepoRoot"/> — static init order matters.</remarks>
		public static readonly string MigrationsOutputDirectory = Path.Combine(FishMMOMonorepoRoot, "FishMMO", "Migrations");

		/// <summary>Path to the FishMMO-Setup directory containing canonical nginx.conf, .cfg files, and environment overlays.</summary>
		public static readonly string FishMMOSetupPath = Path.Combine(FishMMOMonorepoRoot, "FishMMO-Setup");

		/// <summary>Default nginx.conf path used in Linux setup automation.</summary>
		/// <remarks>MUST come after <see cref="FishMMOSetupPath"/> — static init order matters.</remarks>
		public static readonly string LinuxNginxConfigurationPath = Path.Combine(FishMMOSetupPath, "nginx.conf");

		/// <summary>Default FishMMO web servers directory path used for operational context prompts.</summary>
		/// <remarks>MUST come after <see cref="FishMMOMonorepoRoot"/> — static init order matters.</remarks>
		public static readonly string LinuxFishMMOWebServersPath = Path.Combine(FishMMOMonorepoRoot, "FishMMO-WebServers");

		/// <summary>FishMMO-Setup/Development directory (Development environment overlay assets).</summary>
		public static readonly string FishMMOSetupDevelopmentPath = Path.Combine(FishMMOSetupPath, "Development");

		/// <summary>FishMMO-Setup/Release directory (Release/Production environment overlay assets).</summary>
		public static readonly string FishMMOSetupReleasePath = Path.Combine(FishMMOSetupPath, "Release");

		// ---------------------------------------------------------------------
		// Systemd unit file targets
		// ---------------------------------------------------------------------

		/// <summary>Linux directory where generated systemd service unit files are placed.</summary>
		public const string LinuxSystemdUnitDirectory = "/etc/systemd/system";

		/// <summary>Service-name prefix applied to all FishMMO-managed systemd units.</summary>
		public const string FishMMOServiceNamePrefix = "fishmmo-";

		// ---------------------------------------------------------------------
		// FishMMO-Unity project + CLI build entry points
		// ---------------------------------------------------------------------

		/// <summary>Absolute path to the FishMMO-Unity project root (contains Assets/, ProjectSettings/, Packages/).</summary>
		public static readonly string FishMMOUnityProjectPath = Path.Combine(FishMMOMonorepoRoot, "FishMMO-Unity");

		/// <summary>Fully-qualified Unity -executeMethod target for a client player build.</summary>
		public const string UnityBuildClientMethod = "FishMMO.Shared.CustomBuildTool.Core.CustomBuildTool.BuildClientCLI";

		/// <summary>Fully-qualified Unity -executeMethod target for a server player build.</summary>
		public const string UnityBuildServerMethod = "FishMMO.Shared.CustomBuildTool.Core.CustomBuildTool.BuildServerCLI";

		/// <summary>Fully-qualified Unity -executeMethod target for an Addressables-only build.</summary>
		public const string UnityBuildAddressablesMethod = "FishMMO.Shared.CustomBuildTool.Core.CustomBuildTool.BuildAddressablesCLI";

		/// <summary>Fully-qualified Unity -executeMethod target for a Client + Addressables combined build.</summary>
		public const string UnityBuildClientWithAddressablesMethod = "FishMMO.Shared.CustomBuildTool.Core.CustomBuildTool.BuildClientWithAddressablesCLI";

		/// <summary>Fully-qualified Unity -executeMethod target for a Server + Addressables combined build.</summary>
		public const string UnityBuildServerWithAddressablesMethod = "FishMMO.Shared.CustomBuildTool.Core.CustomBuildTool.BuildServerWithAddressablesCLI";

		/// <summary>Default Linux install root for Unity Hub-managed editors. Combined with version + Editor/Unity.</summary>
		public static readonly string UnityLinuxEditorRoot = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Unity", "Hub", "Editor");

		/// <summary>Default Windows install root for Unity Hub-managed editors. Combined with version + Editor\Unity.exe.</summary>
		public static readonly string UnityWindowsEditorRoot = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity", "Hub", "Editor");

		// ---------------------------------------------------------------------
		//  Monorepo root auto-detection
		// ---------------------------------------------------------------------

		/// <summary>
		/// Walks up from the executing assembly until it finds a directory containing
		/// both FishMMO-Unity and FishMMO-Setup subdirectories (the monorepo signature).
		/// Falls back to ~/Dev/FishMMO-Dev if the root cannot be auto-detected.
		/// </summary>
		private static string FindMonorepoRoot()
		{
			// Allow explicit override via environment variable (useful for published/binary distributions)
			string? envOverride = Environment.GetEnvironmentVariable("FISHMMO_INSTALL_ROOT");
			if (!string.IsNullOrWhiteSpace(envOverride))
			{
				if (!Directory.Exists(envOverride))
				{
					// Log a warning through the static logger if available; fall back to stderr
					try { _ = FishMMO.Logging.Log.Warning("FishMMOInstaller", $"FISHMMO_INSTALL_ROOT is set to '{envOverride}' but the directory does not exist. Falling back to auto-detection."); }
					catch { Console.Error.WriteLine($"[WARNING] FISHMMO_INSTALL_ROOT='{envOverride}' does not exist. Falling back to auto-detection."); }
				}
				else if (!Directory.Exists(Path.Combine(envOverride, "FishMMO-Unity")) ||
					     !Directory.Exists(Path.Combine(envOverride, "FishMMO-Setup")))
				{
					try { _ = FishMMO.Logging.Log.Warning("FishMMOInstaller", $"FISHMMO_INSTALL_ROOT='{envOverride}' does not contain expected FishMMO-Unity and/or FishMMO-Setup subdirectories. Continuing anyway."); }
					catch { Console.Error.WriteLine($"[WARNING] FISHMMO_INSTALL_ROOT='{envOverride}' missing FishMMO-Unity or FishMMO-Setup."); }
					return envOverride;
				}
				else
				{
					return envOverride;
				}
			}

			string? assemblyPath = Assembly.GetExecutingAssembly().Location;
			string? dir = Path.GetDirectoryName(assemblyPath);

			while (dir != null)
			{
				if (Directory.Exists(Path.Combine(dir, "FishMMO-Unity")) &&
					Directory.Exists(Path.Combine(dir, "FishMMO-Setup")))
				{
					return dir;
				}
				dir = Path.GetDirectoryName(dir);
			}

			// Fallback when running outside the monorepo tree (e.g., published build).
			return Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				"Dev", "FishMMO-Dev");
		}
	}
}