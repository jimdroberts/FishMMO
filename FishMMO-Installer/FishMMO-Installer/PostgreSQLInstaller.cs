using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using FishMMO.Database;
using Npgsql;

namespace FishMMO.Installer
{
	/// <summary>
	/// Handles PostgreSQL server installation, FishMMO database creation, user role management,
	/// migration execution, and database deletion.
	/// Supports Windows and Linux (Arch/CachyOS via pacman, Ubuntu/Debian via apt, RHEL/Fedora via dnf/yum).
	/// </summary>
	public static class PostgreSQLInstaller
	{
		/// <summary>
		/// Installs PostgreSQL on the current platform by dispatching to the appropriate
		/// platform-specific method.
		/// </summary>
		/// <param name="appSettings">Application settings for database configuration.</param>
		public static async Task InstallPostgreSQL(AppSettings appSettings)
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				await InstallPostgreSQLWindows(appSettings);
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				await InstallPostgreSQLLinux();
			}
			else
			{
				InstallerProcessHelper.Log("Unsupported operating system for PostgreSQL installation.");
			}
		}

		/// <summary>
		/// Builds a PostgreSQL connection string from the given parameters.
		/// </summary>
		/// <param name="host">Database server host.</param>
		/// <param name="port">Database server port.</param>
		/// <param name="username">Connection username.</param>
		/// <param name="password">Connection password.</param>
		/// <param name="database">Target database name.</param>
		/// <returns>A formatted PostgreSQL connection string.</returns>
		private static string BuildConnectionString(string host, string port, string username, string password, string database)
		{
			return $"Host={host};Port={port};Username={username};Password={password};Database={database}";
		}

		/// <summary>
		/// Validates that the given identifier contains only alphanumeric characters and underscores.
		/// </summary>
		/// <param name="identifier">The identifier to validate.</param>
		/// <param name="paramName">Parameter name for the exception message.</param>
		/// <param name="description">Human-readable description of the identifier type.</param>
		private static void ValidateIdentifier(string identifier, string paramName, string description)
		{
			if (!Regex.IsMatch(identifier, @"^[a-zA-Z0-9_]+$"))
			{
				throw new ArgumentException($"Invalid {description} format. Values can only contain alphanumeric characters and underscores.", paramName);
			}
		}

		/// <summary>
		/// Escapes a SQL string literal value for safe interpolation into SQL text.
		/// </summary>
		/// <param name="value">Raw value.</param>
		/// <returns>Escaped SQL literal content.</returns>
		private static string EscapeSqlLiteral(string value)
		{
			return value.Replace("'", "''");
		}

		/// <summary>
		/// Installs PostgreSQL on Windows using the official EXE installer.
		/// </summary>
		/// <param name="appSettings">Application settings for database configuration.</param>
		/// <returns>True if installation succeeded, otherwise false.</returns>
		private static async Task<bool> InstallPostgreSQLWindows(AppSettings appSettings)
		{
			string superUsername = InstallationConstants.PostgreSQLDefaultSuperuser;
			string superPassword = InstallerProcessHelper.PromptForPassword($"Enter new PostgreSQL Superuser Password (username is '{superUsername}'): ");

			if (!InstallerProcessHelper.PromptForYesNo("Install PostgreSQL server?"))
			{
				return false;
			}

			InstallerProcessHelper.Log("Installing PostgreSQL...");

			string installerPath;
			try
			{
				installerPath = await InstallerProcessHelper.DownloadFileAsync(
					InstallationConstants.PostgreSQLWindowsInstallerUrl,
					InstallationConstants.PostgreSQLWindowsInstallerFileName);
			}
			catch (Exception ex)
			{
				InstallerProcessHelper.Log($"Failed to download PostgreSQL installer: {ex.Message}");
				return false;
			}

			try
			{
				string arguments = $"--unattendedmodeui minimal " +
								   $"--mode unattended " +
								   $"--superaccount \"{superUsername}\" " +
								   $"--superpassword \"{superPassword}\" " +
								   $"--serverport {appSettings.Npgsql.Port} " +
								   $"--disable-components pgAdmin,stackbuilder";

				InstallerProcessHelper.LogElevatedProcessEnvironmentWarning("PostgreSQL installer");

				ProcessStartInfo startInfo = new ProcessStartInfo
				{
					FileName = installerPath,
					Arguments = arguments,
					CreateNoWindow = true,
					WorkingDirectory = Path.GetDirectoryName(installerPath) ?? InstallerProcessHelper.GetWorkingDirectory(),
					UseShellExecute = true,
					Verb = "runas"
				};

				Process? process = Process.Start(startInfo);
				if (process == null)
				{
					InstallerProcessHelper.Log("Failed to start PostgreSQL installer process.");
					return false;
				}
				await process.WaitForExitAsync();

				int exitCode = process.ExitCode;
				if (exitCode == 0)
				{
					InstallerProcessHelper.Log("PostgreSQL installation successful.");
					return true;
				}
				else
				{
					InstallerProcessHelper.Log($"PostgreSQL installation failed with exit code {exitCode}. Please check installer logs or try running the installer manually.");
					return false;
				}
			}
			catch (Exception ex)
			{
				InstallerProcessHelper.Log($"Error installing PostgreSQL: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// Installs PostgreSQL on Linux using the appropriate system package manager.
		/// Detects pacman (Arch/CachyOS), apt-get (Debian/Ubuntu), dnf, and yum.
		/// </summary>
		/// <returns>True if installation succeeded, otherwise false.</returns>
		private static async Task<bool> InstallPostgreSQLLinux()
		{
			if (!InstallerProcessHelper.PromptForYesNo("Install PostgreSQL server?"))
			{
				return false;
			}

			InstallerProcessHelper.Log("Installing PostgreSQL...");

			try
			{
				(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();

				var packageNames = new Dictionary<string, string>
				{
					["pacman"] = "postgresql",
					["apt-get"] = "postgresql postgresql-contrib",
					["dnf"] = "postgresql-server postgresql-contrib",
					["yum"] = "postgresql-server postgresql-contrib"
				};

				var detected = await InstallerProcessHelper.DetectLinuxPackageManagerAsync(packageNames);
				if (detected == null)
				{
					InstallerProcessHelper.Log("No supported package manager (pacman, apt-get, dnf, yum) found. Please install PostgreSQL manually.");
					return false;
				}

				var (updateCommand, installCommand, managerName) = detected.Value;
				InstallerProcessHelper.Log($"Using {managerName} for PostgreSQL installation.");

				if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, updateCommand, "Failed to update package lists."))
					return false;

				if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, installCommand, "Failed to install PostgreSQL."))
					return false;

				if (managerName.Contains("pacman"))
				{
					InstallerProcessHelper.Log("Arch/CachyOS detected. Initializing PostgreSQL data directory...");
					if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix,
						"sudo -u postgres initdb -D /var/lib/postgres/data",
						"Failed to initialize PostgreSQL data directory. It may already be initialized."))
					{
						InstallerProcessHelper.Log("Continuing anyway. If PostgreSQL fails to start, run 'sudo -u postgres initdb -D /var/lib/postgres/data' manually.");
					}
				}

				if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, "sudo systemctl start postgresql", "Failed to start PostgreSQL."))
					return false;

				if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, "sudo systemctl enable postgresql", "Failed to enable PostgreSQL to start on boot."))
					return false;

				if (InstallerProcessHelper.PromptForYesNo("Update PostgreSQL Superuser Password?"))
				{
					string superUsername = InstallationConstants.PostgreSQLDefaultSuperuser;
					string superPassword = InstallerProcessHelper.PromptForPassword($"Enter new PostgreSQL Superuser Password (username is '{superUsername}'): ");
					string escapedPassword = EscapeSqlLiteral(superPassword);
					string updateUserCommand = $"sudo -u postgres psql -d postgres -v ON_ERROR_STOP=1 -c \"ALTER USER \\\"{superUsername}\\\" WITH PASSWORD '{escapedPassword}';\"";
					if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, updateUserCommand, "Failed to update PostgreSQL superuser password."))
						return false;
				}

				InstallerProcessHelper.Log("PostgreSQL installation successful.");
				return true;
			}
			catch (Exception ex)
			{
				InstallerProcessHelper.Log($"Error installing PostgreSQL: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// Installs the FishMMO database, creates the application user role, grants privileges,
		/// and optionally runs the initial migration.
		/// </summary>
		/// <param name="superUsername">PostgreSQL superuser name.</param>
		/// <param name="superPassword">PostgreSQL superuser password.</param>
		/// <param name="appSettings">Application settings for database configuration.</param>
		public static async Task InstallFishMMODatabase(string superUsername, string superPassword, AppSettings appSettings)
		{
			try
			{
				ValidateIdentifier(appSettings.Npgsql.Database, nameof(appSettings.Npgsql.Database), "database name");
				ValidateIdentifier(appSettings.Npgsql.Username, nameof(appSettings.Npgsql.Username), "username");

				InstallerProcessHelper.Log($"Attempting to connect to PostgreSQL at {appSettings.Npgsql.Host}:{appSettings.Npgsql.Port}");
				string connectionString = BuildConnectionString(appSettings.Npgsql.Host, appSettings.Npgsql.Port, superUsername, superPassword, InstallationConstants.PostgreSQLDefaultAdminDb);

				using (var connection = new NpgsqlConnection(connectionString))
				{
					await connection.OpenAsync();
					InstallerProcessHelper.Log("Successfully connected to PostgreSQL server.");

					if (InstallerProcessHelper.PromptForYesNo($"Create Database '{appSettings.Npgsql.Database}'?"))
					{
						using (var checkDbCommand = new NpgsqlCommand($"SELECT 1 FROM pg_database WHERE datname = @dbName", connection))
						{
							checkDbCommand.Parameters.AddWithValue("dbName", appSettings.Npgsql.Database);
							var result = await checkDbCommand.ExecuteScalarAsync();
							if (result != null)
							{
								InstallerProcessHelper.Log($"Database '{appSettings.Npgsql.Database}' already exists. Skipping creation.");
							}
							else
							{
								InstallerProcessHelper.Log($"Creating database '{appSettings.Npgsql.Database}'...");
								await CreateDatabase(connection, appSettings.Npgsql.Database);
								InstallerProcessHelper.Log($"Database '{appSettings.Npgsql.Database}' created successfully.");
							}
						}
					}

					if (InstallerProcessHelper.PromptForYesNo($"Create User Role '{appSettings.Npgsql.Username}' for database access?"))
					{
						using (var checkUserCommand = new NpgsqlCommand($"SELECT 1 FROM pg_roles WHERE rolname = @username", connection))
						{
							checkUserCommand.Parameters.AddWithValue("username", appSettings.Npgsql.Username);
							var result = await checkUserCommand.ExecuteScalarAsync();
							if (result != null)
							{
								InstallerProcessHelper.Log($"User role '{appSettings.Npgsql.Username}' already exists. Skipping creation.");
							}
							else
							{
								InstallerProcessHelper.Log($"Creating user role '{appSettings.Npgsql.Username}'...");
								await CreateUser(connection, appSettings.Npgsql.Username, appSettings.Npgsql.Password);
								InstallerProcessHelper.Log($"User role '{appSettings.Npgsql.Username}' created successfully.");
							}
						}
						InstallerProcessHelper.Log($"Granting privileges on database '{appSettings.Npgsql.Database}' to user '{appSettings.Npgsql.Username}'...");
						await GrantPrivileges(connection, appSettings.Npgsql.Username, appSettings.Npgsql.Database);
						InstallerProcessHelper.Log("Privileges granted successfully.");
					}

					InstallerProcessHelper.Log("FishMMO Database components installed/configured.");
				}

				if (InstallerProcessHelper.PromptForYesNo("Create Initial Migration and apply to database?"))
				{
					Console.WriteLine("Creating Initial database migration...");
					bool initialMigrationCreated = await DotNetInstaller.RunEFMigrationAsync("Initial");
					if (!initialMigrationCreated)
					{
						InstallerProcessHelper.Log("Failed to create the initial migration.");
						return;
					}

					Console.WriteLine("Updating database...");
					bool initialMigrationApplied = await DotNetInstaller.RunEFDatabaseUpdateAsync();
					if (!initialMigrationApplied)
					{
						InstallerProcessHelper.Log("Initial migration was created but database update failed.");
						return;
					}

					InstallerProcessHelper.Log("Initial Migration completed and applied.");
				}
			}
			catch (NpgsqlException npgEx)
			{
				InstallerProcessHelper.Log($"PostgreSQL connection or database operation error: {npgEx.Message}. Check your appsettings.json and PostgreSQL server status.");
			}
			catch (Exception ex)
			{
				InstallerProcessHelper.Log($"General error installing FishMMO database components: {ex.Message}");
			}
		}

		/// <summary>
		/// Creates a new database migration and applies it using dotnet ef commands.
		/// </summary>
		public static async Task CreateMigration()
		{
			Console.Clear();

			string? migrationName = InstallerProcessHelper.PromptForInput("Enter a name for the new migration (e.g., 'AddPlayerInventory'): ");
			if (string.IsNullOrWhiteSpace(migrationName))
			{
				InstallerProcessHelper.Log("Migration name cannot be empty. Aborting migration creation.");
				return;
			}

			if (!Regex.IsMatch(migrationName, "^[A-Za-z][A-Za-z0-9]*$"))
			{
				InstallerProcessHelper.Log("Invalid migration name. Use alphanumeric characters only and start with a letter.");
				return;
			}

			InstallerProcessHelper.Log($"Creating a new migration '{migrationName}'...");

			bool migrationSuccess = await DotNetInstaller.RunEFMigrationAsync(migrationName);

			if (!migrationSuccess)
			{
				InstallerProcessHelper.Log($"Failed to create migration '{migrationName}'. Please check the console output for details.");
				return;
			}

			InstallerProcessHelper.Log($"Updating the database with migration '{migrationName}'...");

			bool updateSuccess = await DotNetInstaller.RunEFDatabaseUpdateAsync();

			if (updateSuccess)
			{
				InstallerProcessHelper.Log($"Migration '{migrationName}' created and applied successfully.");
			}
			else
			{
				InstallerProcessHelper.Log($"Failed to apply migration '{migrationName}' to the database. Please check the console output for details.");
			}
		}

		/// <summary>
		/// Deletes the FishMMO database as defined in appsettings.json.
		/// Requires PostgreSQL superuser credentials. This operation is DANGEROUS and irreversible.
		/// </summary>
		/// <param name="superUsername">PostgreSQL superuser name.</param>
		/// <param name="superPassword">PostgreSQL superuser password.</param>
		/// <param name="appSettings">Application settings for database configuration.</param>
		public static async Task DeleteFishMMODatabase(string superUsername, string superPassword, AppSettings appSettings)
		{
			string databaseToDelete = appSettings.Npgsql.Database;
			ValidateIdentifier(databaseToDelete, nameof(databaseToDelete), "database name");

			Console.WriteLine($"\n!!! DANGER ZONE: YOU ARE ABOUT TO DELETE THE DATABASE !!!");
			Console.WriteLine($"This action is irreversible and will permanently delete all data in '{databaseToDelete}'.");
			Console.WriteLine($"Are you absolutely sure you want to delete the database '{databaseToDelete}'?");

			string? confirmationInput = InstallerProcessHelper.PromptForInput("Type 'DELETE' (all caps) to confirm: ");
			if (confirmationInput?.Trim().Equals("DELETE", StringComparison.Ordinal) != true)
			{
				InstallerProcessHelper.Log("Database deletion cancelled by user.");
				return;
			}

			try
			{
				string adminConnectionString = BuildConnectionString(appSettings.Npgsql.Host, appSettings.Npgsql.Port, superUsername, superPassword, InstallationConstants.PostgreSQLDefaultAdminDb);

				using (var connection = new NpgsqlConnection(adminConnectionString))
				{
					await connection.OpenAsync();
					InstallerProcessHelper.Log($"Connected to '{InstallationConstants.PostgreSQLDefaultAdminDb}' database as superuser.");

					InstallerProcessHelper.Log($"Terminating active connections to database '{databaseToDelete}'...");
					using (var terminateCommand = new NpgsqlCommand(
						$"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @dbName;", connection))
					{
						terminateCommand.Parameters.AddWithValue("dbName", databaseToDelete);
						await terminateCommand.ExecuteNonQueryAsync();
						InstallerProcessHelper.Log("Active connections terminated.");
					}

					InstallerProcessHelper.Log($"Attempting to drop database '{databaseToDelete}'...");
					string dropSql;
					using (var dropSqlCommand = new NpgsqlCommand("SELECT format('DROP DATABASE %I', @dbName)", connection))
					{
						dropSqlCommand.Parameters.AddWithValue("dbName", databaseToDelete);
						dropSql = (string)(await dropSqlCommand.ExecuteScalarAsync())!;
					}

					using (var dropCommand = new NpgsqlCommand(dropSql, connection))
					{
						await dropCommand.ExecuteNonQueryAsync();
						InstallerProcessHelper.Log($"Database '{databaseToDelete}' deleted successfully.");
					}
				}
			}
			catch (NpgsqlException npgEx)
			{
				InstallerProcessHelper.Log($"PostgreSQL error during database deletion: {npgEx.Message}. Ensure correct superuser password and permissions.");
			}
			catch (Exception ex)
			{
				InstallerProcessHelper.Log($"An unexpected error occurred during database deletion: {ex.Message}");
			}
		}

		/// <summary>
		/// Grants comprehensive permissions to a specified user on a specific database.
		/// Includes permissions on existing and future tables, sequences, and functions.
		/// </summary>
		/// <param name="superUsername">PostgreSQL superuser name.</param>
		/// <param name="superPassword">PostgreSQL superuser password.</param>
		/// <param name="appSettings">Application settings for database configuration.</param>
		public static async Task GrantUserPermissions(string superUsername, string superPassword, AppSettings appSettings)
		{
			Console.Clear();
			InstallerProcessHelper.Log("--- Grant User Permissions ---");

			string defaultDbName = appSettings.Npgsql.Database ?? "fishmmo_database";
			string? dbName = InstallerProcessHelper.PromptForInput($"Enter database name to grant permissions on (default: {defaultDbName}): ");
			if (string.IsNullOrWhiteSpace(dbName)) dbName = defaultDbName;
			ValidateIdentifier(dbName, nameof(dbName), "database name");

			string defaultUsername = appSettings.Npgsql.Username ?? "fishmmo_user";
			string? usernameToGrant = InstallerProcessHelper.PromptForInput($"Enter username to grant permissions to (default: {defaultUsername}): ");
			if (string.IsNullOrWhiteSpace(usernameToGrant)) usernameToGrant = defaultUsername;
			ValidateIdentifier(usernameToGrant, nameof(usernameToGrant), "username");

			Console.WriteLine($"Attempting to grant permissions for user '{usernameToGrant}' on database '{dbName}'.");

			try
			{
				string connectionString = BuildConnectionString(appSettings.Npgsql.Host, appSettings.Npgsql.Port, superUsername, superPassword, dbName);

				using (var connection = new NpgsqlConnection(connectionString))
				{
					await connection.OpenAsync();
					InstallerProcessHelper.Log($"Successfully connected to database '{dbName}' as superuser.");

					InstallerProcessHelper.Log($"Granting comprehensive permissions to '{usernameToGrant}' on '{dbName}'...");

					string formatSql =
						"SELECT format('" +
						"GRANT ALL PRIVILEGES ON DATABASE %I TO %I; " +
						"GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO %I; " +
						"GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO %I; " +
						"GRANT ALL PRIVILEGES ON ALL FUNCTIONS IN SCHEMA public TO %I; " +
						"ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL PRIVILEGES ON TABLES TO %I; " +
						"ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL PRIVILEGES ON SEQUENCES TO %I; " +
						"ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL PRIVILEGES ON FUNCTIONS TO %I', " +
						"@dbName, @username, @username, @username, @username, @username, @username, @username)";

					string batchSql;
					using (var formatCommand = new NpgsqlCommand(formatSql, connection))
					{
						formatCommand.Parameters.AddWithValue("dbName", dbName);
						formatCommand.Parameters.AddWithValue("username", usernameToGrant);
						batchSql = (string)(await formatCommand.ExecuteScalarAsync())!;
					}

					using (var cmd = new NpgsqlCommand(batchSql, connection))
					{
						await cmd.ExecuteNonQueryAsync();
					}

					InstallerProcessHelper.Log($"Successfully granted comprehensive permissions to user '{usernameToGrant}' on database '{dbName}'.");
				}
			}
			catch (NpgsqlException npgEx)
			{
				InstallerProcessHelper.Log($"PostgreSQL error granting permissions: {npgEx.Message}. Ensure database '{dbName}' exists and superuser credentials are correct.");
			}
			catch (Exception ex)
			{
				InstallerProcessHelper.Log($"An unexpected error occurred during permission granting: {ex.Message}");
			}
		}

		/// <summary>
		/// Creates a new PostgreSQL database with the specified name.
		/// Uses pg format() to safely quote the identifier.
		/// </summary>
		/// <param name="connection">Open NpgsqlConnection.</param>
		/// <param name="dbName">Database name.</param>
		private static async Task CreateDatabase(NpgsqlConnection connection, string dbName)
		{
			ValidateIdentifier(dbName, nameof(dbName), "database name");

			string formatSql = $"SELECT format('CREATE DATABASE %I', @dbNameParam)";
			string createDatabaseCommandText;

			using (var command = new NpgsqlCommand(formatSql, connection))
			{
				command.Parameters.AddWithValue("dbNameParam", dbName);
				createDatabaseCommandText = (string)(await command.ExecuteScalarAsync())!;
			}

			using (var createDbCommand = new NpgsqlCommand(createDatabaseCommandText, connection))
			{
				await createDbCommand.ExecuteNonQueryAsync();
			}
		}

		/// <summary>
		/// Creates a new PostgreSQL user role with the specified username and password.
		/// Uses pg format() to safely quote identifiers and literals.
		/// </summary>
		/// <param name="connection">Open NpgsqlConnection.</param>
		/// <param name="username">Username for the role.</param>
		/// <param name="password">Password for the role.</param>
		private static async Task CreateUser(NpgsqlConnection connection, string username, string password)
		{
			ValidateIdentifier(username, nameof(username), "username");

			string formatSql = $"SELECT format('CREATE ROLE %I WITH LOGIN PASSWORD %L', @usernameParam, @passwordParam)";
			string createRoleCommandText;

			using (var command = new NpgsqlCommand(formatSql, connection))
			{
				command.Parameters.AddWithValue("usernameParam", username);
				command.Parameters.AddWithValue("passwordParam", password);
				createRoleCommandText = (string)(await command.ExecuteScalarAsync())!;
			}

			using (var createRoleCommand = new NpgsqlCommand(createRoleCommandText, connection))
			{
				await createRoleCommand.ExecuteNonQueryAsync();
			}
		}

		/// <summary>
		/// Grants all privileges on the specified database to the specified user
		/// and transfers database ownership.
		/// </summary>
		/// <param name="connection">Open NpgsqlConnection.</param>
		/// <param name="username">Username to grant privileges to.</param>
		/// <param name="dbName">Database name.</param>
		private static async Task GrantPrivileges(NpgsqlConnection connection, string username, string dbName)
		{
			ValidateIdentifier(username, nameof(username), "username");
			ValidateIdentifier(dbName, nameof(dbName), "database name");

			string formatSql = "SELECT format('GRANT ALL PRIVILEGES ON DATABASE %I TO %I; ALTER DATABASE %I OWNER TO %I', @dbName, @username, @dbName, @username)";
			string commandText;

			using (var formatCommand = new NpgsqlCommand(formatSql, connection))
			{
				formatCommand.Parameters.AddWithValue("dbName", dbName);
				formatCommand.Parameters.AddWithValue("username", username);
				commandText = (string)(await formatCommand.ExecuteScalarAsync())!;
			}

			using (var cmd = new NpgsqlCommand(commandText, connection))
			{
				await cmd.ExecuteNonQueryAsync();
			}
		}
	}
}