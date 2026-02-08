using System.Text.Json;
using FishMMO.Database;

namespace FishMMO.Installer
{
	/// <summary>
	/// Console-based installer tool for FishMMO dependencies and database setup.
	/// Delegates all work to focused installer classes: <see cref="DotNetInstaller"/>,
	/// <see cref="PostgreSQLInstaller"/>, <see cref="NGINXInstaller"/>,
	/// <see cref="VSBuildToolsInstaller"/>, and <see cref="UnityInstaller"/>.
	/// </summary>
	public static class Program
	{
		/// <summary>
		/// Stores the loaded application settings from appsettings.json.
		/// </summary>
		private static AppSettings appSettings = new AppSettings();

		/// <summary>
		/// Entry point. Loads appsettings.json and runs the installer menu loop.
		/// </summary>
		public static async Task Main(string[] args)
		{
			LoadAppSettings();
			await RunMenuLoop();
		}

		/// <summary>
		/// Loads application settings from appsettings.json in the working directory.
		/// </summary>
		private static void LoadAppSettings()
		{
			string workingDirectory = InstallerProcessHelper.GetWorkingDirectory();
			string appSettingsPath = Path.Combine(workingDirectory, "appsettings.json");

			if (File.Exists(appSettingsPath))
			{
				try
				{
					string jsonContent = File.ReadAllText(appSettingsPath);
					appSettings = JsonSerializer.Deserialize<AppSettings>(jsonContent) ?? new AppSettings();
					Console.WriteLine("appsettings.json loaded successfully.");
				}
				catch (Exception ex)
				{
					InstallerProcessHelper.Log($"Error loading appsettings.json: {ex.Message}. Database operations may be affected.");
					appSettings = new AppSettings();
				}
			}
			else
			{
				InstallerProcessHelper.Log("appsettings.json file not found. Database operations will be limited or unavailable.");
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
				Console.WriteLine("Press a key (0-9, A-B):");
				Console.WriteLine("1 : Install DotNet");
				Console.WriteLine("2 : Install Visual Studio Build Tools (Windows Only)");
				Console.WriteLine("3 : Install Unity Hub");
				Console.WriteLine("4 : Install Unity Editor (+Modules)");
				Console.WriteLine("5 : Install NGINX (Web Server/Reverse Proxy)");
				Console.WriteLine("6 : Install PostgreSQL (Database Server)");
				Console.WriteLine("7 : Install FishMMO Database (User/Schema/Initial Migration)");
				Console.WriteLine("8 : Create new database migration");
				Console.WriteLine("9 : Grant User Permissions on Database");
				Console.WriteLine("A : Delete FishMMO Database (DANGEROUS!)");
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
						await UnityInstaller.InstallUnityHub();
						break;
					case ConsoleKey.D4:
						await UnityInstaller.InstallUnityVersion();
						break;
					case ConsoleKey.D5:
						await NGINXInstaller.InstallNGINX();
						break;
					case ConsoleKey.D6:
						await HandleWithSettings(
							s => s.Npgsql?.Host,
							"Npgsql host",
							s => PostgreSQLInstaller.InstallPostgreSQL(s));
						break;
					case ConsoleKey.D7:
						await HandleWithSuperuser(
							s => s.Npgsql?.Database,
							"Npgsql database",
							PostgreSQLInstaller.InstallFishMMODatabase);
						break;
					case ConsoleKey.D8:
						await PostgreSQLInstaller.CreateMigration();
						break;
					case ConsoleKey.D9:
						await HandleWithSuperuser(
							s => s.Npgsql?.Username,
							"Npgsql database/username",
							PostgreSQLInstaller.GrantUserPermissions);
						break;
					case ConsoleKey.A:
						await HandleWithSuperuser(
							s => s.Npgsql?.Database,
							"Npgsql database",
							PostgreSQLInstaller.DeleteFishMMODatabase);
						break;
					case ConsoleKey.D0:
						return;
					default:
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
				InstallerProcessHelper.Log($"appsettings.json is not loaded or {fieldDescription} is not defined. Cannot proceed without configuration.");
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
				InstallerProcessHelper.Log($"appsettings.json is not loaded or {fieldDescription} is not defined. Cannot proceed without configuration.");
				return;
			}
			string superUsername = InstallationConstants.PostgreSQLDefaultSuperuser;
			string superPassword = InstallerProcessHelper.PromptForPassword($"Enter PostgreSQL Superuser Password (username is '{superUsername}'): ");
			await handler(superUsername, superPassword, appSettings);
		}
	}
}