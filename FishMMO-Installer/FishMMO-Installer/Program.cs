using FishMMO.Database;
using FishMMO.Logging;
using Microsoft.Extensions.Configuration;

namespace FishMMO.Installer
{
	/// <summary>
	/// Console-based installer tool for FishMMO dependencies and database setup.
	/// Delegates all work to focused installer classes: <see cref="DotNetInstaller"/>,
	/// <see cref="PgBouncerInstaller"/>,
	/// <see cref="PostgreSQLInstaller"/>, <see cref="NGINXInstaller"/>,
	/// <see cref="VSBuildToolsInstaller"/>, <see cref="UnityInstaller"/>, <see cref="LetsEncryptInstaller"/>,
	/// and <see cref="ProjectBuildInstaller"/>.
	/// </summary>
	public static class Program
	{
		/// <summary>
		/// Name of the logging configuration file.
		/// </summary>
		private const string LoggingConfigName = "logging.json";

		/// <summary>
		/// Stores the loaded application settings from appsettings.json.
		/// </summary>
		private static AppSettings appSettings = new AppSettings();

		/// <summary>
		/// Entry point. Loads appsettings.json and runs the installer menu loop.
		/// </summary>
		public static async Task Main(string[] args)
		{
			/// Set the working directory to the EXE location to ensure relative paths work correctly.
			string applicationBaseDirectory = AppContext.BaseDirectory;

			// Normalize environment selection once and propagate to standard variables.
			string environmentName = DatabaseConfigurationHelper.ResolveEnvironmentName();

			Environment.SetEnvironmentVariable("FISHMMO_ENVIRONMENT", environmentName);
			Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", environmentName);
			Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", environmentName);

			string configFilePath = Path.Combine(applicationBaseDirectory, LoggingConfigName);
			try
			{
				await Log.Initialize(configFilePath, new ConsoleFormatter());
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"Failed to initialize logging from '{LoggingConfigName}': {ex.Message}");
				Console.Error.WriteLine($"Ensure {LoggingConfigName} exists in '{applicationBaseDirectory}' and contains valid JSON.");
				Environment.ExitCode = 1;
				return;
			}

			LoadAppSettings(environmentName);
			await RunMenuLoop();
			await Log.Shutdown();
			Environment.Exit(Environment.ExitCode);
		}

		/// <summary>
		/// Loads application settings using ConfigurationBuilder.
		/// Looks first in the EXE directory for appsettings.json, then falls back to
		/// FishMMO-Database/FishMMO-DB/ where the canonical settings live after a build copy.
		/// </summary>
		private static void LoadAppSettings(string environmentName)
		{
			string exeDir = InstallerProcessHelper.GetWorkingDirectory();
			string dbSubDir = Path.Combine(exeDir, "FishMMO-Database", "FishMMO-DB");

			// Prefer the EXE dir; fall back to the database sub-directory.
			string basePath = File.Exists(Path.Combine(exeDir, "appsettings.json"))
				? exeDir
				: dbSubDir;

			_ = Log.Debug("FishMMOInstaller", $"Loading configuration from: {basePath}");

			try
			{
				IConfiguration configuration = DatabaseConfigurationHelper.BuildDesignTimeConfiguration(basePath);

				appSettings = configuration.Get<AppSettings>() ?? new AppSettings();

				_ = Log.Info("FishMMOInstaller", $"Configuration successfully loaded for Environment: {environmentName}");
			}
			catch (Exception ex)
			{
				_ = Log.Error("FishMMOInstaller", "Critical error loading configuration", ex);
				_ = Log.Warning("FishMMOInstaller", $"Ensure appsettings.json exists in '{exeDir}' or '{dbSubDir}'.");
				appSettings = new AppSettings();
			}
		}

		/// <summary>
		/// Runs the interactive console menu loop until the user quits.
		/// </summary>
		private static async Task RunMenuLoop()
		{
			while (true)
			{
				Console.Clear();
				Console.WriteLine("=== FishMMO Installer ===");
				Console.WriteLine();
				Console.WriteLine("1 : Runtime & Tooling");
				Console.WriteLine("2 : Database");
				Console.WriteLine("3 : Web Server");
				Console.WriteLine("4 : Unity & Build");
				Console.WriteLine("5 : Configuration");
				Console.WriteLine("0 : Quit");

				ConsoleKeyInfo key = Console.ReadKey(true);

				switch (key.Key)
				{
					case ConsoleKey.D1:
						await RuntimeMenu();
						break;
					case ConsoleKey.D2:
						await DatabaseMenu();
						break;
					case ConsoleKey.D3:
						await WebServerMenu();
						break;
					case ConsoleKey.D4:
						await UnityBuildMenu();
						break;
					case ConsoleKey.D5:
						await ConfigurationMenu();
						break;
					case ConsoleKey.D0:
					case ConsoleKey.NumPad0:
						return;
					default:
						if (key.KeyChar == '0') return;
						break;
				}
			}
		}

		/// <summary>Runtime &amp; Tooling sub-menu: DotNet SDK, ASP.NET Runtime, VS Build Tools.</summary>
		private static async Task RuntimeMenu()
		{
			while (true)
			{
				Console.Clear();
				Console.WriteLine("=== Runtime & Tooling ===");
				Console.WriteLine();
				Console.WriteLine("1 : Install DotNet SDK");
				Console.WriteLine("2 : Install ASP.NET Runtime");
				Console.WriteLine("3 : Install Visual Studio Build Tools (Windows Only)");
				Console.WriteLine("0 : Back");

				ConsoleKeyInfo key = Console.ReadKey(true);
				Console.WriteLine();

				switch (key.Key)
				{
					case ConsoleKey.D1:
						await DotNetInstaller.InstallDotNet();
						break;
					case ConsoleKey.D2:
						await DotNetInstaller.InstallAspNetRuntime();
						break;
					case ConsoleKey.D3:
						await VSBuildToolsInstaller.InstallVSBuildTools();
						break;
					case ConsoleKey.D0:
					case ConsoleKey.NumPad0:
						return;
					default:
						if (key.KeyChar == '0') return;
						Console.WriteLine("Invalid option.");
						break;
				}

				Console.WriteLine("Press any key to continue...");
				Console.ReadKey(true);
			}
		}

		/// <summary>Database sub-menu: PostgreSQL, PgBouncer, FishMMO DB management.</summary>
		private static async Task DatabaseMenu()
		{
			while (true)
			{
				Console.Clear();
				Console.WriteLine("=== Database ===");
				Console.WriteLine();
				Console.WriteLine("1 : Install PostgreSQL");
				Console.WriteLine("2 : Install PgBouncer (Connection Pooler)");
				Console.WriteLine("3 : Install Redis (In-Memory Cache)");
				Console.WriteLine("4 : Install FishMMO Database (User/Schema/Initial Migration)");
				Console.WriteLine("5 : Create New Database Migration");
				Console.WriteLine("6 : Grant User Permissions on Database");
				Console.WriteLine("7 : Delete FishMMO Database (DANGEROUS!)");
				Console.WriteLine("8 : Configure PgBouncer (generate pgbouncer.ini + userlist.txt, Linux)");
				Console.WriteLine("0 : Back");

				ConsoleKeyInfo key = Console.ReadKey(true);
				Console.WriteLine();

				switch (key.Key)
				{
					case ConsoleKey.D1:
						await HandleWithSettings(
							s => s.Npgsql?.Host,
							"Npgsql host",
							s => PostgreSQLInstaller.InstallPostgreSQL(s));
						break;
					case ConsoleKey.D2:
						await PgBouncerInstaller.InstallPgBouncer(appSettings);
						break;
					case ConsoleKey.D3:
						await RedisInstaller.InstallRedis(appSettings);
						break;
					case ConsoleKey.D4:
						await HandleWithSuperuser(
							s => s.Npgsql?.Database,
							"Npgsql database",
							PostgreSQLInstaller.InstallFishMMODatabase);
						break;
					case ConsoleKey.D5:
						await PostgreSQLInstaller.CreateMigration();
						break;
					case ConsoleKey.D6:
						await HandleWithSuperuser(
							s => s.Npgsql?.Username,
							"Npgsql database/username",
							PostgreSQLInstaller.GrantUserPermissions);
						break;
					case ConsoleKey.D7:
						await HandleWithSuperuser(
							s => s.Npgsql?.Database,
							"Npgsql database",
							PostgreSQLInstaller.DeleteFishMMODatabase);
						break;
					case ConsoleKey.D8:
						await PgBouncerInstaller.ConfigurePgBouncerLinuxAsync(appSettings);
						break;
					case ConsoleKey.D0:
					case ConsoleKey.NumPad0:
						return;
					default:
						if (key.KeyChar == '0') return;
						Console.WriteLine("Invalid option.");
						break;
				}

				Console.WriteLine("Press any key to continue...");
				Console.ReadKey(true);
			}
		}

		/// <summary>Web Server sub-menu: NGINX, Let's Encrypt.</summary>
		private static async Task WebServerMenu()
		{
			while (true)
			{
				Console.Clear();
				Console.WriteLine("=== Web Server ===");
				Console.WriteLine();
				Console.WriteLine("1 : Install NGINX (Web Server/Reverse Proxy)");
				Console.WriteLine("2 : Install/Renew Let's Encrypt Certificate (NGINX)");
				Console.WriteLine("3 : Deploy FishMMO nginx.conf (from FishMMO-Setup/)");
				Console.WriteLine("0 : Back");

				ConsoleKeyInfo key = Console.ReadKey(true);
				Console.WriteLine();

				switch (key.Key)
				{
					case ConsoleKey.D1:
						await NGINXInstaller.InstallNGINX();
						break;
					case ConsoleKey.D2:
						await LetsEncryptInstaller.InstallLetsEncryptCertificate();
						break;
					case ConsoleKey.D3:
						await NGINXInstaller.DeployNginxConfigAsync();
						break;
					case ConsoleKey.D0:
					case ConsoleKey.NumPad0:
						return;
					default:
						if (key.KeyChar == '0') return;
						Console.WriteLine("Invalid option.");
						break;
				}

				Console.WriteLine("Press any key to continue...");
				Console.ReadKey(true);
			}
		}

		/// <summary>Unity &amp; Build sub-menu: Unity Hub, Unity Editor, C# project build.</summary>
		private static async Task UnityBuildMenu()
		{
			while (true)
			{
				Console.Clear();
				Console.WriteLine("=== Unity & Build ===");
				Console.WriteLine();
				Console.WriteLine("1 : Install Unity Hub");
				Console.WriteLine("2 : Install Unity Editor (+Modules)");
				Console.WriteLine("3 : Build all C# Projects");
				Console.WriteLine("4 : Build FishMMO-Unity (Client/Server/Addressables)");
				Console.WriteLine("0 : Back");

				ConsoleKeyInfo key = Console.ReadKey(true);
				Console.WriteLine();

				switch (key.Key)
				{
					case ConsoleKey.D1:
						await UnityInstaller.InstallUnityHub();
						break;
					case ConsoleKey.D2:
						await UnityInstaller.InstallUnityVersion();
						break;
					case ConsoleKey.D3:
						await ProjectBuildInstaller.BuildAllProjectsInSelectedRootAsync();
						break;
					case ConsoleKey.D4:
						await UnityBuildInstaller.RunInteractiveBuild();
						break;
					case ConsoleKey.D0:
					case ConsoleKey.NumPad0:
						return;
					default:
						if (key.KeyChar == '0') return;
						Console.WriteLine("Invalid option.");
						break;
				}

				Console.WriteLine("Press any key to continue...");
				Console.ReadKey(true);
			}
		}

		/// <summary>Configuration sub-menu: AppSettings secure setup.</summary>
		private static async Task ConfigurationMenu()
		{
			while (true)
			{
				Console.Clear();
				Console.WriteLine("=== Configuration ===");
				Console.WriteLine();
				Console.WriteLine("1 : Configure AppSettings (Secure Setup)");
				Console.WriteLine("0 : Back");

				ConsoleKeyInfo key = Console.ReadKey(true);
				Console.WriteLine();

				switch (key.Key)
				{
					case ConsoleKey.D1:
						await AppSettingsInstaller.ConfigureAppSettings();
						break;
					case ConsoleKey.D0:
					case ConsoleKey.NumPad0:
						return;
					default:
						if (key.KeyChar == '0') return;
						Console.WriteLine("Invalid option.");
						break;
				}

				Console.WriteLine("Press any key to continue...");
				Console.ReadKey(true);
			}
		}

		/// <summary>
		/// Validates that the required Npgsql setting is present, then delegates to the handler.
		/// </summary>
		/// <param name="requiredField">Selector for the required field to validate.</param>
		/// <param name="fieldDescription">Human-readable name of the required field for error messages.</param>
		/// <param name="handler">Async action receiving the validated app settings.</param>
		private static async Task HandleWithSettings(
			Func<AppSettings, string?> requiredField,
			string fieldDescription,
			Func<AppSettings, Task> handler)
		{
			if (string.IsNullOrWhiteSpace(requiredField(appSettings)))
			{
				await Log.Warning("FishMMOInstaller", $"appsettings.json is not loaded or {fieldDescription} is not defined. Cannot proceed without configuration.");
				return;
			}
			await handler(appSettings);
		}

		/// <summary>
		/// Validates settings, prompts for superuser credentials, then delegates to the handler.
		/// </summary>
		/// <param name="requiredField">Selector for the required field to validate.</param>
		/// <param name="fieldDescription">Human-readable name of the required field for error messages.</param>
		/// <param name="handler">Async action receiving (superUsername, superPassword, appSettings).</param>
		private static async Task HandleWithSuperuser(
			Func<AppSettings, string?> requiredField,
			string fieldDescription,
			Func<string, string, AppSettings, Task> handler)
		{
			if (string.IsNullOrWhiteSpace(requiredField(appSettings)))
			{
				await Log.Warning("FishMMOInstaller", $"appsettings.json is not loaded or {fieldDescription} is not defined. Cannot proceed without configuration.");
				return;
			}
			string superUsername = InstallationConstants.PostgreSQLDefaultSuperuser;
			string superPassword = InstallerProcessHelper.PromptForPassword($"Enter PostgreSQL Superuser Password (username is '{superUsername}'): ");
			await handler(superUsername, superPassword, appSettings);
		}
	}
}