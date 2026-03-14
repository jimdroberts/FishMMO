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
				Console.WriteLine("Welcome to the FishMMO Installer Tool.");
				Console.WriteLine("Press a key (0-9, A-D):");
				Console.WriteLine("1 : Install DotNet");
				Console.WriteLine("2 : Install Visual Studio Build Tools (Windows Only)");
				Console.WriteLine("3 : Install PgBouncer (Connection Pooler)");
				Console.WriteLine("4 : Build all C# Projects");
				Console.WriteLine("5 : Install Unity Hub");
				Console.WriteLine("6 : Install Unity Editor (+Modules)");
				Console.WriteLine("7 : Install NGINX (Web Server/Reverse Proxy)");
				Console.WriteLine("8 : Install/Renew Let's Encrypt Certificate (NGINX)");
				Console.WriteLine("9 : Install PostgreSQL (Database Server)");
				Console.WriteLine("A : Install FishMMO Database (User/Schema/Initial Migration)");
				Console.WriteLine("B : Create new database migration");
				Console.WriteLine("C : Grant User Permissions on Database");
				Console.WriteLine("D : Delete FishMMO Database (DANGEROUS!)");
				Console.WriteLine("0 : Quit");

				ConsoleKeyInfo key = Console.ReadKey(true);

				switch (key.Key)
				{
					case ConsoleKey.D1:
						await DotNetInstaller.InstallDotNet();
						break;
					case ConsoleKey.D2:
						await VSBuildToolsInstaller.InstallVSBuildTools();
						break;
					case ConsoleKey.D3:
						await PgBouncerInstaller.InstallPgBouncer(appSettings);
						break;
					case ConsoleKey.D4:
						await ProjectBuildInstaller.BuildAllProjectsInSelectedRootAsync();
						break;
					case ConsoleKey.D5:
						await UnityInstaller.InstallUnityHub();
						break;
					case ConsoleKey.D6:
						await UnityInstaller.InstallUnityVersion();
						break;
					case ConsoleKey.D7:
						await NGINXInstaller.InstallNGINX();
						break;
					case ConsoleKey.D8:
						await LetsEncryptInstaller.InstallLetsEncryptCertificate();
						break;
					case ConsoleKey.D9:
						await HandleWithSettings(
							s => s.Npgsql?.Host,
							"Npgsql host",
							s => PostgreSQLInstaller.InstallPostgreSQL(s));
						break;
					case ConsoleKey.A:
						await HandleWithSuperuser(
							s => s.Npgsql?.Database,
							"Npgsql database",
							PostgreSQLInstaller.InstallFishMMODatabase);
						break;
					case ConsoleKey.B:
						await PostgreSQLInstaller.CreateMigration();
						break;
					case ConsoleKey.C:
						await HandleWithSuperuser(
							s => s.Npgsql?.Username,
							"Npgsql database/username",
							PostgreSQLInstaller.GrantUserPermissions);
						break;
					case ConsoleKey.D:
						await HandleWithSuperuser(
							s => s.Npgsql?.Database,
							"Npgsql database",
							PostgreSQLInstaller.DeleteFishMMODatabase);
						break;
					case ConsoleKey.D0:
					case ConsoleKey.NumPad0:
						return;
					default:
						// KeyChar fallback: some Linux terminals (e.g. Konsole + fish) send
						// raw char '0' without mapping it to ConsoleKey.D0/NumPad0.
						if (key.KeyChar == '0')
						{
							return;
						}
						Console.WriteLine("Invalid input. Please enter a valid option.");
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