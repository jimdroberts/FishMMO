using FishMMO.Logging;
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
	public static partial class PostgreSQLInstaller
	{
		/// <summary>
		/// Installs PostgreSQL on the current platform by dispatching to the appropriate
		/// platform-specific method.
		/// </summary>
		/// <param name="appSettings">Application settings for database configuration.</param>
		public static async Task InstallPostgreSQL(AppSettings appSettings)
		{
			if (await IsPostgreSQLInstalledAsync())
			{
				await Log.Warning("FishMMOInstaller", "PostgreSQL is already installed.");
				return;
			}

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
				await Log.Warning("FishMMOInstaller", "Unsupported operating system for PostgreSQL installation.");
			}
		}

		/// <summary>
		/// Returns true if the pg_isready or psql binary is accessible, indicating PostgreSQL is installed.
		/// </summary>
		public static async Task<bool> IsPostgreSQLInstalledAsync()
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();
				return await InstallerProcessHelper.RunProcessAsync(
					shell,
					$"{argPrefix} \"where psql\"",
					(exitCode, _, _) => exitCode == 0);
			}
			else
			{
				(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();
				return await InstallerProcessHelper.RunProcessAsync(
					shell,
					$"{argPrefix} \"command -v pg_isready\"",
					(exitCode, _, _) => exitCode == 0);
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
			if (string.IsNullOrWhiteSpace(identifier))
			{
				throw new ArgumentException(
					$"{description} is not configured. Set '{paramName}' in appsettings.json or via the FISHMMO_DB_USERNAME environment variable.",
					paramName);
			}
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
		internal static string EscapeSqlLiteral(string value)
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
			string superPassword = InstallerProcessHelper.PromptForRequiredPassword($"Enter new PostgreSQL Superuser Password (username is '{superUsername}'): ");

			if (!InstallerProcessHelper.PromptForYesNo("Install PostgreSQL server?"))
			{
				return false;
			}

			await Log.Info("FishMMOInstaller", "Installing PostgreSQL...");

			string? installerPath;
			try
			{
				installerPath = await DownloadHelper.DownloadFileWithProgressAsync(
					InstallationConstants.PostgreSQLWindowsInstallerUrl,
					InstallationConstants.PostgreSQLWindowsInstallerFileName,
					new DownloadHelper.ConsoleProgress());
			}
			catch (Exception ex)
			{
				await Log.Error("FishMMOInstaller", "Failed to download PostgreSQL installer", ex);
				return false;
			}

			if (installerPath == null)
			{
				await Log.Error("FishMMOInstaller", "Failed to download PostgreSQL installer.");
				return false;
			}

			string? optionFilePath = null;
			try
			{
				// EnterpriseDB's "one-click" Windows installer supports --optionfile,
				// which lets us pass the superuser password in a file instead of on
				// the command line where it would be visible to any user via
				// `tasklist /v`, Process Explorer, or the Windows event log.
				// We create the file in a per-user temp directory and restrict ACLs
				// to the current user before writing the secret. The file is removed
				// in the finally block whether the install succeeds or fails.
				optionFilePath = WriteRestrictedOptionFile(
					$"mode=unattended\n" +
					$"unattendedmodeui=minimal\n" +
					$"superaccount={superUsername}\n" +
					$"superpassword={superPassword}\n" +
					$"serverport={appSettings.Npgsql.Port}\n" +
					$"disable-components=pgAdmin,stackbuilder\n");

				string arguments = $"--optionfile \"{optionFilePath}\"";

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
					await Log.Error("FishMMOInstaller", "Failed to start PostgreSQL installer process.");
					return false;
				}
				await process.WaitForExitAsync();

				int exitCode = process.ExitCode;
				if (exitCode == 0)
				{
					await Log.Info("FishMMOInstaller", "PostgreSQL installation successful.");
					return true;
				}
				else
				{
					await Log.Error("FishMMOInstaller", $"PostgreSQL installation failed with exit code {exitCode}. Please check installer logs or try running the installer manually.");
					return false;
				}
			}
			catch (Exception ex)
			{
				await Log.Error("FishMMOInstaller", "Error installing PostgreSQL", ex);
				return false;
			}
			finally
			{
				if (optionFilePath != null)
				{
					try
					{
						// Best-effort overwrite of the file contents before deletion so
						// the password is not left in slack space on disk.
						if (File.Exists(optionFilePath))
						{
							long len = new FileInfo(optionFilePath).Length;
							File.WriteAllBytes(optionFilePath, new byte[len]);
							File.Delete(optionFilePath);
						}
					}
					catch
					{
						// Swallow cleanup failures; the temp directory ACL keeps the
						// file out of reach of other users in the worst case.
					}
				}
			}
		}

		/// <summary>
		/// Writes the supplied text to a freshly created file in the per-user
		/// temp directory with an ACL/permissions set restricting access to
		/// the current user only. Used to pass secrets to external installers
		/// via <c>--optionfile</c>-style arguments without exposing them on the
		/// command line.
		/// </summary>
		private static string WriteRestrictedOptionFile(string contents)
		{
			string path = Path.Combine(Path.GetTempPath(), $"fishmmo-pg-{Guid.NewGuid():N}.ini");

			// Create the file empty first so we can lock down ACLs before writing the secret.
			using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				// nothing — just create the file
			}

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				// Best-effort: rely on the default DACL inherited from %TEMP%, which on
				// modern Windows is per-user. We do not attempt to rewrite the ACL with
				// System.Security.AccessControl here because that API surface is not
				// available on all target frameworks; %TEMP% is already user-private.
			}
			else
			{
				try
				{
					File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
				}
				catch
				{
					// SetUnixFileMode is only available on .NET 7+. On older runtimes the
					// file inherits the user's umask which is typically restrictive enough.
				}
			}

			File.WriteAllText(path, contents);
			return path;
		}

		/// <summary>
		/// Installs PostgreSQL on Linux using the appropriate system package manager.
		/// Detects pacman (Arch/CachyOS), apt-get (Debian/Ubuntu), dnf, and yum.
		/// On Arch/CachyOS the data directory is initialized via initdb with scram-sha-256
		/// enforced for both local and host connections; the superuser password is set
		/// interactively by initdb's --pwprompt flag so no post-install ALTER USER step
		/// is needed. On other distributions the superuser password can be updated after
		/// the service starts.
		/// </summary>
		/// <returns>True if installation succeeded, otherwise false.</returns>
		private static async Task<bool> InstallPostgreSQLLinux()
		{
			if (!InstallerProcessHelper.PromptForYesNo("Install PostgreSQL server?"))
			{
				return false;
			}

			await Log.Info("FishMMOInstaller", "Installing PostgreSQL...");

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

				var detected = await LinuxPackageManagerHelper.DetectAsync(packageNames);
				if (detected == null)
				{
					await Log.Warning("FishMMOInstaller", "No supported package manager (pacman, apt-get, dnf, yum) found. Please install PostgreSQL manually.");
					return false;
				}

				await Log.Info("FishMMOInstaller", $"Using {detected.ManagerName} for PostgreSQL installation.");

				if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, detected.UpdateCommand, "Failed to update package lists."))
					return false;

				if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, detected.InstallCommand, "Failed to install PostgreSQL."))
					return false;

				bool isArch = detected.ManagerName.Contains("pacman");

				if (isArch)
				{
					await Log.Info("FishMMOInstaller", "Arch/CachyOS detected. Initializing PostgreSQL data directory with scram-sha-256 authentication...");
					await Log.Info("FishMMOInstaller", "You will be prompted to set the PostgreSQL superuser password during initdb.");
					if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix,
						"sudo -u postgres initdb -D /var/lib/postgres/data " +
						"--auth-local=scram-sha-256 " +
						"--auth-host=scram-sha-256 " +
						"--pwprompt",
						"Failed to initialize PostgreSQL data directory. It may already be initialized."))
					{
						await Log.Warning("FishMMOInstaller",
							"Continuing anyway. If PostgreSQL fails to start, run manually: " +
							"sudo -u postgres initdb -D /var/lib/postgres/data --auth-local=scram-sha-256 --auth-host=scram-sha-256 --pwprompt");
					}
				}

				if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, "sudo systemctl start postgresql", "Failed to start PostgreSQL."))
					return false;

				if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, "sudo systemctl enable postgresql", "Failed to enable PostgreSQL to start on boot."))
					return false;

				// On Arch/CachyOS the superuser password was already set by initdb --pwprompt.
				// Only prompt for a password change on other distributions.
				if (!isArch && InstallerProcessHelper.PromptForYesNo("Update PostgreSQL Superuser Password?"))
				{
					string superUsername = InstallationConstants.PostgreSQLDefaultSuperuser;
					string superPassword = InstallerProcessHelper.PromptForRequiredPassword($"Enter new PostgreSQL Superuser Password (username is '{superUsername}'): ");
					ValidateIdentifier(superUsername, nameof(superUsername), "superuser name");

					// Pipe the ALTER USER statement to psql via stdin so the password
					// never appears on the process command line (where it would be visible
					// to any user via /proc/<pid>/cmdline or `ps`) nor in shell history.
					// The single-quoted password is escaped with EscapeSqlLiteral; identifiers
					// have been validated against [A-Za-z0-9_]+.
					string sql = $"ALTER USER \"{superUsername}\" WITH PASSWORD '{EscapeSqlLiteral(superPassword)}';\n\\q\n";
					bool ok = await InstallerProcessHelper.RunProcessWithStdinAsync(
						"sudo",
						"-u postgres psql -d postgres -v ON_ERROR_STOP=1",
						sql,
						(exitCode, stdout, err) =>
						{
							if (exitCode != 0)
							{
								_ = Log.Warning("FishMMOInstaller", $"Failed to update PostgreSQL superuser password. Error: {err}");
								return false;
							}
							return true;
						});
					if (!ok)
						return false;
				}

				await Log.Info("FishMMOInstaller", "PostgreSQL installation successful.");
				return true;
			}
			catch (Exception ex)
			{
				await Log.Error("FishMMOInstaller", "Error installing PostgreSQL", ex);
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
				// Resolve database credentials from DatabaseSecrets (env vars or secrets file)
				string dbUsername = DatabaseSecrets.TryResolveUsername()
					?? throw new InvalidOperationException("Database username not configured. Run 'Configure Database Secrets' first.");
				string dbPassword = DatabaseSecrets.TryResolvePassword()
					?? throw new InvalidOperationException("Database password not configured. Run 'Configure Database Secrets' first.");
				ValidateIdentifier(appSettings.Npgsql.Database, nameof(appSettings.Npgsql.Database), "database name");
				ValidateIdentifier(dbUsername, nameof(dbUsername), "username");

				await Log.Info("FishMMOInstaller", $"Attempting to connect to PostgreSQL at {appSettings.Npgsql.Host}:{appSettings.Npgsql.Port}");
				string connectionString = BuildConnectionString(appSettings.Npgsql.Host, appSettings.Npgsql.Port, superUsername, superPassword, InstallationConstants.PostgreSQLDefaultAdminDb);

				using (var connection = new NpgsqlConnection(connectionString))
				{
					await connection.OpenAsync();
					await WarnIfTrustAuthAsync(connection);
					await HardenPostgreSQLAsync(connection, appSettings);
					await Log.Info("FishMMOInstaller", "Successfully connected to PostgreSQL server.");

					if (InstallerProcessHelper.PromptForYesNo($"Create Database '{appSettings.Npgsql.Database}'?"))
					{
						using (var checkDbCommand = new NpgsqlCommand($"SELECT 1 FROM pg_database WHERE datname = @dbName", connection))
						{
							checkDbCommand.Parameters.AddWithValue("dbName", appSettings.Npgsql.Database);
							var result = await checkDbCommand.ExecuteScalarAsync();
							if (result != null)
							{
								await Log.Info("FishMMOInstaller", $"Database '{appSettings.Npgsql.Database}' already exists. Skipping creation.");
							}
							else
							{
								await Log.Info("FishMMOInstaller", $"Creating database '{appSettings.Npgsql.Database}'...");
								await CreateDatabase(connection, appSettings.Npgsql.Database);
								await Log.Info("FishMMOInstaller", $"Database '{appSettings.Npgsql.Database}' created successfully.");
							}
						}
					}

					if (InstallerProcessHelper.PromptForYesNo($"Create User Role '{dbUsername}' for database access?"))
					{
						using (var checkUserCommand = new NpgsqlCommand($"SELECT 1 FROM pg_roles WHERE rolname = @username", connection))
						{
							checkUserCommand.Parameters.AddWithValue("username", dbUsername);
							var result = await checkUserCommand.ExecuteScalarAsync();
							if (result != null)
							{
								await Log.Info("FishMMOInstaller", $"User role '{dbUsername}' already exists. Skipping creation.");
							}
							else
							{
								await Log.Info("FishMMOInstaller", $"Creating user role '{dbUsername}'...");
								await CreateUser(connection, dbUsername, dbPassword);
								await Log.Info("FishMMOInstaller", $"User role '{dbUsername}' created successfully.");
							}
						}
						await Log.Info("FishMMOInstaller", $"Granting privileges on database '{appSettings.Npgsql.Database}' to user '{dbUsername}'...");
						await GrantPrivileges(connection, dbUsername, appSettings.Npgsql.Database);
						await Log.Info("FishMMOInstaller", "Privileges granted successfully.");

						// Credentials are NOT written here — use 'Configure Database Secrets'
						// (Step 1) to write /etc/fishmmo/db-secrets.env.
					}

					await Log.Info("FishMMOInstaller", "FishMMO Database components installed/configured.");
				}

				if (InstallerProcessHelper.PromptForYesNo("Create Initial Migration and apply to database?"))
				{
					// Check if migration files already exist (e.g. from a previous run).
					// If they do, skip 'migrations add' to avoid "The name 'Initial' is used
					// by an existing migration" errors and go straight to 'database update'.
					string migrationsDir = InstallationConstants.MigrationsOutputDirectory;
					bool migrationFilesExist = Directory.Exists(migrationsDir)
						&& Directory.GetFiles(migrationsDir, "*.cs", SearchOption.TopDirectoryOnly).Length > 0;

					if (!migrationFilesExist)
					{
						Console.WriteLine("Creating Initial database migration...");
						bool initialMigrationCreated = await DotNetInstaller.RunEFMigrationAsync("Initial");
						if (!initialMigrationCreated)
						{
							await Log.Error("FishMMOInstaller", "Failed to create the initial migration.");
							return;
						}
					}
					else
					{
						await Log.Info("FishMMOInstaller", "Migration files already exist — skipping 'migrations add' step.");
					}

					Console.WriteLine("Updating database...");
					string superuserConnStr = BuildConnectionString(
						appSettings.Npgsql.Host, appSettings.Npgsql.Port,
						superUsername, superPassword, appSettings.Npgsql.Database);
					bool initialMigrationApplied = await DotNetInstaller.RunEFDatabaseUpdateAsync(superuserConnStr);
					if (!initialMigrationApplied)
					{
						await Log.Error("FishMMOInstaller", "Initial migration was created but database update failed.");
						return;
					}

					await Log.Info("FishMMOInstaller", "Migration completed and applied.");
				}
			}
			catch (NpgsqlException npgEx)
			{
				await Log.Error("FishMMOInstaller", $"PostgreSQL connection or database operation error: {npgEx.Message}. Check your appsettings.json and PostgreSQL server status.");
			}
			catch (Exception ex)
			{
				await Log.Error("FishMMOInstaller", "General error installing FishMMO database components", ex);
			}
		}

		/// <summary>
		/// Creates a new database migration and applies it using dotnet ef commands.
		/// Prompts for the PostgreSQL superuser password so the migration can be
		/// applied via <c>dotnet ef database update --connection</c>, which bypasses
		/// the design-time factory and its potentially stale appsettings.json.
		/// </summary>
		public static async Task CreateMigration(AppSettings appSettings)
		{
			Console.Clear();

			string? migrationName = InstallerProcessHelper.PromptForInput("Enter a name for the new migration (e.g., 'AddPlayerInventory'): ");
			if (string.IsNullOrWhiteSpace(migrationName))
			{
				await Log.Info("FishMMOInstaller", "Migration name cannot be empty. Aborting migration creation.");
				return;
			}

			if (!Regex.IsMatch(migrationName, "^[A-Za-z][A-Za-z0-9]*$"))
			{
				await Log.Warning("FishMMOInstaller", "Invalid migration name. Use alphanumeric characters only and start with a letter.");
				return;
			}

			// Check for duplicate migration name before prompting for credentials.
			// EF Core names files as <timestamp>_<Name>.cs — if any file matches
			// the pattern *_<name>.cs, the migration already exists.
			string migrationsDir = InstallationConstants.MigrationsOutputDirectory;
			if (Directory.Exists(migrationsDir))
			{
				string pattern = $"*_{migrationName}.cs";
				string[] existing = Directory.GetFiles(migrationsDir, pattern, SearchOption.TopDirectoryOnly);
				if (existing.Length > 0)
				{
					await Log.Warning("FishMMOInstaller",
						$"A migration named '{migrationName}' already exists ({Path.GetFileName(existing[0])}). " +
						"Choose a different name.");
					return;
				}
			}

			// Prompt for superuser credentials so 'database update' can pass them
			// via --connection, which avoids the design-time factory's potentially
			// stale appsettings.json (e.g. wrong database name in bin/ output).
			string superUsername = InstallationConstants.PostgreSQLDefaultSuperuser;
			string superPassword = InstallerProcessHelper.PromptForRequiredPassword(
				$"Enter PostgreSQL Superuser Password (username is '{superUsername}'): ");

			await Log.Info("FishMMOInstaller", $"Creating a new migration '{migrationName}'...");

			bool migrationSuccess = await DotNetInstaller.RunEFMigrationAsync(migrationName);

			if (!migrationSuccess)
			{
				await Log.Error("FishMMOInstaller", $"Failed to create migration '{migrationName}'. Please check the console output for details.");
				return;
			}

			await Log.Info("FishMMOInstaller", $"Updating the database with migration '{migrationName}'...");

			string superuserConnStr = BuildConnectionString(
				appSettings.Npgsql.Host, appSettings.Npgsql.Port,
				superUsername, superPassword, appSettings.Npgsql.Database);
			bool updateSuccess = await DotNetInstaller.RunEFDatabaseUpdateAsync(superuserConnStr);

			if (updateSuccess)
			{
				await Log.Info("FishMMOInstaller", $"Migration '{migrationName}' created and applied successfully.");
			}
			else
			{
				await Log.Error("FishMMOInstaller", $"Failed to apply migration '{migrationName}' to the database. Please check the console output for details.");
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
				await Log.Info("FishMMOInstaller", "Database deletion cancelled by user.");
				return;
			}

			try
			{
				string adminConnectionString = BuildConnectionString(appSettings.Npgsql.Host, appSettings.Npgsql.Port, superUsername, superPassword, InstallationConstants.PostgreSQLDefaultAdminDb);

				using (var connection = new NpgsqlConnection(adminConnectionString))
				{
					await connection.OpenAsync();
					await WarnIfTrustAuthAsync(connection);
					await Log.Info("FishMMOInstaller", $"Connected to '{InstallationConstants.PostgreSQLDefaultAdminDb}' database as superuser.");

					await Log.Info("FishMMOInstaller", $"Terminating active connections to database '{databaseToDelete}'...");
					using (var terminateCommand = new NpgsqlCommand(
						$"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @dbName;", connection))
					{
						terminateCommand.Parameters.AddWithValue("dbName", databaseToDelete);
						await terminateCommand.ExecuteNonQueryAsync();
						await Log.Info("FishMMOInstaller", "Active connections terminated.");
					}

					await Log.Info("FishMMOInstaller", $"Attempting to drop database '{databaseToDelete}'...");
					string dropSql;
					using (var dropSqlCommand = new NpgsqlCommand("SELECT format('DROP DATABASE %I', @dbName)", connection))
					{
						dropSqlCommand.Parameters.AddWithValue("dbName", databaseToDelete);
						dropSql = (string)(await dropSqlCommand.ExecuteScalarAsync())!;
					}

					using (var dropCommand = new NpgsqlCommand(dropSql, connection))
					{
						await dropCommand.ExecuteNonQueryAsync();
						await Log.Info("FishMMOInstaller", $"Database '{databaseToDelete}' deleted successfully.");
					}
				}
			}
			catch (NpgsqlException npgEx)
			{
				await Log.Error("FishMMOInstaller", $"PostgreSQL error during database deletion: {npgEx.Message}. Ensure correct superuser password and permissions.");
			}
			catch (Exception ex)
			{
				await Log.Error("FishMMOInstaller", "An unexpected error occurred during database deletion", ex);
			}
		}

		/// <summary>
		/// Grants comprehensive permissions to a specified user on a specific database.
		/// Includes permissions on existing and future tables, sequences, and functions.
		/// </summary>
		/// <param name="superUsername">PostgreSQL superuser name.</param>
		/// <param name="superPassword">PostgreSQL superuser password.</param>
		/// <param name="appSettings">Application settings for database configuration.</param>
		public static async Task GrantUserPermissions(string superUsername, string superPassword, AppSettings appSettings, string? appUsername = null)
		{
			Console.Clear();
			await Log.Info("FishMMOInstaller", "--- Grant User Permissions ---");

			string defaultDbName = appSettings.Npgsql.Database ?? "fishmmo_database";
			string? dbName = InstallerProcessHelper.PromptForInput($"Enter database name to grant permissions on (default: {defaultDbName}): ");
			if (string.IsNullOrWhiteSpace(dbName)) dbName = defaultDbName;
			ValidateIdentifier(dbName, nameof(dbName), "database name");

			string defaultUsername = appUsername ?? "fishmmo_user";
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
					await WarnIfTrustAuthAsync(connection);
					await Log.Info("FishMMOInstaller", $"Successfully connected to database '{dbName}' as superuser.");

					await Log.Info("FishMMOInstaller", $"Granting comprehensive permissions to '{usernameToGrant}' on '{dbName}'...");
					await GrantPrivileges(connection, usernameToGrant, dbName);
					await Log.Info("FishMMOInstaller", $"Successfully granted comprehensive permissions to user '{usernameToGrant}' on database '{dbName}'.");
				}
			}
			catch (NpgsqlException npgEx)
			{
				await Log.Error("FishMMOInstaller", $"PostgreSQL error granting permissions: {npgEx.Message}. Ensure database '{dbName}' exists and superuser credentials are correct.");
			}
			catch (Exception ex)
			{
				await Log.Error("FishMMOInstaller", "An unexpected error occurred during permission granting", ex);
			}
		}

		/// <summary>
		/// Checks pg_hba_file_rules for TCP 'trust' authentication entries and logs a warning
		/// if found. Local Unix-socket trust entries are normal on many distros and are excluded.
		/// On default Arch/CachyOS installs, TCP trust auth means the supplied password
		/// is never verified by the server for network connections.
		/// </summary>
		private static async Task WarnIfTrustAuthAsync(NpgsqlConnection connection)
		{
			try
			{
				// Only warn for host/hostssl/hostnossl entries — local socket trust is expected.
				using var cmd = new NpgsqlCommand(
					"SELECT EXISTS(SELECT 1 FROM pg_hba_file_rules WHERE auth_method = 'trust' AND type != 'local')",
					connection);
				var result = await cmd.ExecuteScalarAsync();
				if (result is true)
				{
					await Log.Info("FishMMOInstaller",
						"WARNING: pg_hba.conf has 'trust' authentication for TCP/network connections. " +
						"Passwords for those entries are NOT verified by the server. " +
						"Consider changing pg_hba.conf to 'scram-sha-256' for security.");
				}
			}
			catch
			{
				// pg_hba_file_rules requires PostgreSQL 10+ and superuser; silently skip if unavailable.
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
		/// Grants comprehensive privileges on the specified database to the specified user,
		/// covering database-level access, all existing tables/sequences/functions in the
		/// public schema, and default privileges for future objects.
		/// </summary>
		/// <param name="connection">Open NpgsqlConnection.</param>
		/// <param name="username">Username to grant privileges to.</param>
		/// <param name="dbName">Database name.</param>
		private static async Task GrantPrivileges(NpgsqlConnection connection, string username, string dbName)
		{
			ValidateIdentifier(username, nameof(username), "username");
			ValidateIdentifier(dbName, nameof(dbName), "database name");

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