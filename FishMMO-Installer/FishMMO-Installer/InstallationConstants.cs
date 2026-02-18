namespace FishMMO.Installer
{
	/// <summary>
	/// Contains constants for installation URLs, filenames, default configuration values,
	/// and EF Core project paths for FishMMO dependencies.
	/// </summary>
	public static class InstallationConstants
	{
		/// <summary>
		/// Download URL for the DotNet SDK Windows installer.
		/// </summary>
		public const string DotNetSDKUrl = "https://download.visualstudio.microsoft.com/download/pr/b6f19ef3-52ca-40b1-b78b-0712d3c8bf4d/426bd0d376479d551ce4d5ac0ecf63a5/dotnet-sdk-8.0.302-win-x64.exe";

		/// <summary>
		/// Filename for the downloaded DotNet SDK Windows installer.
		/// </summary>
		public const string DotNetSDKFileName = "dotnet-sdk-8.0.302-win-x64.exe";

		/// <summary>
		/// URL for the DotNet install shell script (Linux).
		/// </summary>
		public const string DotNetInstallScriptUrl = "https://dot.net/v1/dotnet-install.sh";

		/// <summary>
		/// Filename for the downloaded DotNet install shell script.
		/// </summary>
		public const string DotNetInstallScriptFileName = "dotnet-install.sh";

		/// <summary>
		/// Full DotNet SDK version string to install.
		/// </summary>
		public const string DotNetSDKVersion = "8.0.302";

		/// <summary>
		/// DotNet SDK major version for compatibility checks.
		/// </summary>
		public const string DotNetSDKMajorVersion = "8.0";

		/// <summary>
		/// DotNet Entity Framework tool version to install.
		/// </summary>
		public const string DotNetEFVersion = "5.0.17";

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
		/// Download URL for the win-acme client (Windows Let's Encrypt automation).
		/// </summary>
		public const string WinAcmeDownloadUrl = "https://github.com/win-acme/win-acme/releases/download/v2.2.9.1701/win-acme.v2.2.9.1701.x64.trimmed.zip";

		/// <summary>
		/// Filename for the downloaded win-acme zip archive.
		/// </summary>
		public const string WinAcmeFileName = "win-acme.v2.2.9.1701.x64.trimmed.zip";

		/// <summary>
		/// Default nginx.conf path used in Linux setup automation.
		/// </summary>
		public const string LinuxNginxConfigurationPath = "/home/Dev/FishMMO Dev/FishMMO-Setup/nginx.conf";

		/// <summary>
		/// Default web root for ACME HTTP-01 challenges in Linux deployments.
		/// </summary>
		public const string LinuxCertbotWebRoot = "/var/www/certbot";

		/// <summary>
		/// Default FishMMO web servers directory path used for operational context prompts.
		/// </summary>
		public const string LinuxFishMMOWebServersPath = "/home/Dev/FishMMO Dev/FishMMO-WebServers";

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
		/// </summary>
		public static readonly string ProjectPath = Path.Combine(".", "FishMMO-Database", "FishMMO-DB", "FishMMO-DB.csproj");

		/// <summary>
		/// Relative path to the FishMMO-DB-Migrator startup project for EF Core commands.
		/// </summary>
		public static readonly string StartupProject = Path.Combine(".", "FishMMO-Database", "FishMMO-DB-Migrator", "FishMMO-DB-Migrator.csproj");
	}
}