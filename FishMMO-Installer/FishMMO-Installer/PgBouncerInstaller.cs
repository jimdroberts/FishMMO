using FishMMO.Logging;
using System.Runtime.InteropServices;
using System.Text;
using FishMMO.Database;
using Npgsql;

namespace FishMMO.Installer
{
	/// <summary>
	/// Handles PgBouncer installation and baseline service setup.
	/// Supports Windows and Linux (Arch/CachyOS, Ubuntu/Debian).
	/// </summary>
	public static class PgBouncerInstaller
	{
		/// <summary>
		/// Returns true if the pgbouncer binary is present on PATH.
		/// </summary>
		public static async Task<bool> IsPgBouncerInstalledAsync()
		{
			(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();
			return await InstallerProcessHelper.RunProcessAsync(
				shell,
				$"{argPrefix} \"command -v pgbouncer\"",
				(exitCode, _, _) => exitCode == 0);
		}
		/// <summary>
		/// Installs PgBouncer on the current platform and attempts to enable/start the service.
		/// </summary>
		/// <param name="appSettings">Application settings used for post-install guidance.</param>
		public static async Task InstallPgBouncer(AppSettings appSettings)
		{
			Console.Clear();
			await Log.Info("FishMMOInstaller", "--- Install PgBouncer ---");

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				await InstallPgBouncerWindows(appSettings);
				return;
			}

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				await InstallPgBouncerLinux(appSettings);
				return;
			}

			await Log.Warning("FishMMOInstaller", "Unsupported operating system for PgBouncer installation.");
		}

		/// <summary>
		/// Installs PgBouncer on Linux using the detected package manager.
		/// </summary>
		private static async Task InstallPgBouncerLinux(AppSettings appSettings)
		{
			if (await IsPgBouncerInstalledAsync())
			{
				await Log.Info("FishMMOInstaller", "PgBouncer is already installed.");
				PrintPostInstallHints(appSettings, isWindows: false);
				return;
			}

			if (!InstallerProcessHelper.PromptForYesNo("Install PgBouncer connection pooler?"))
			{
				await Log.Info("FishMMOInstaller", "PgBouncer installation cancelled by user.");
				return;
			}

			(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();

			var packageNames = new Dictionary<string, string>
			{
				["pacman"] = "pgbouncer",
				["apt-get"] = "pgbouncer",
				["dnf"] = "pgbouncer",
				["yum"] = "pgbouncer"
			};

			var detected = await LinuxPackageManagerHelper.DetectAsync(packageNames);
			if (detected == null)
			{
				await Log.Warning("FishMMOInstaller", "No supported package manager (pacman, apt-get, dnf, yum) found. Please install PgBouncer manually.");
				return;
			}

			await Log.Info("FishMMOInstaller", $"Using {detected.ManagerName} for PgBouncer installation.");

			if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, detected.UpdateCommand, "Failed to update package metadata."))
			{
				return;
			}

			if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, detected.InstallCommand, "Failed to install PgBouncer."))
			{
				return;
			}

			bool serviceEnabled = await InstallerProcessHelper.RunShellCommandAsync(
				shell,
				argPrefix,
				$"sudo systemctl enable --now {InstallationConstants.PgBouncerLinuxServiceName}",
				"PgBouncer package was installed, but systemd enable/start failed.");

			if (!serviceEnabled)
			{
				await Log.Info("FishMMOInstaller", "This is often caused by missing or incomplete /etc/pgbouncer configuration. Finish config, then run 'sudo systemctl restart pgbouncer'.");
			}

			PrintPostInstallHints(appSettings, isWindows: false);

			if (InstallerProcessHelper.PromptForYesNo("Generate /etc/pgbouncer/pgbouncer.ini and /etc/pgbouncer/userlist.txt now?"))
			{
				await ConfigurePgBouncerLinuxAsync(appSettings);
			}
		}

		/// <summary>
		/// Installs PgBouncer on Windows using winget (preferred) with Chocolatey fallback.
		/// </summary>
		private static async Task InstallPgBouncerWindows(AppSettings appSettings)
		{
			if (!InstallerProcessHelper.PromptForYesNo("Install PgBouncer connection pooler?"))
			{
				await Log.Info("FishMMOInstaller", "PgBouncer installation cancelled by user.");
				return;
			}

			(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();
			bool installed = false;

			bool hasWinget = await InstallerProcessHelper.RunProcessAsync(shell, $"{argPrefix} \"where winget\"", (exitCode, output, error) => exitCode == 0);
			if (hasWinget)
			{
				string[] candidateWingetIds =
				[
					"PgBouncer.PgBouncer",
					"EnterpriseDB.pgBouncer"
				];

				foreach (string packageId in candidateWingetIds)
				{
					installed = await InstallerProcessHelper.RunShellCommandAsync(
						shell,
						argPrefix,
						$"winget install --id {packageId} -e --silent --accept-source-agreements --accept-package-agreements --disable-interactivity",
						$"winget install failed for package id '{packageId}'.");

					if (installed)
					{
						await Log.Info("FishMMOInstaller", $"PgBouncer installed via winget package '{packageId}'.");
						break;
					}
				}
			}

			if (!installed)
			{
				bool hasChoco = await InstallerProcessHelper.RunProcessAsync(shell, $"{argPrefix} \"where choco\"", (exitCode, output, error) => exitCode == 0);
				if (hasChoco)
				{
					installed = await InstallerProcessHelper.RunShellCommandAsync(
						shell,
						argPrefix,
						"choco install pgbouncer -y",
						"Chocolatey PgBouncer installation failed.");
				}
			}

			if (!installed)
			{
				await Log.Info("FishMMOInstaller", "Automatic Windows installation was not successful. Install PgBouncer manually, then re-run this menu option for guidance.");
				await Log.Info("FishMMOInstaller", "Suggested sources: winget packages for pgBouncer or Chocolatey package 'pgbouncer'.");
				PrintPostInstallHints(appSettings, isWindows: true);
				return;
			}

			await Log.Info("FishMMOInstaller", "PgBouncer installation attempt completed on Windows.");
			PrintPostInstallHints(appSettings, isWindows: true);
		}

		/// <summary>
		/// Prints baseline PgBouncer configuration hints based on current appsettings.
		/// </summary>
		private static void PrintPostInstallHints(AppSettings appSettings, bool isWindows)
		{
			string dbName = appSettings.Npgsql?.Database ?? "fishmmo";
			string dbHost = appSettings.Npgsql?.Host ?? "127.0.0.1";
			string dbPort = appSettings.Npgsql?.Port ?? "5432";
			string dbUser = DatabaseSecrets.TryResolveUsername() ?? "fishmmo";

			_ = Log.Info("FishMMOInstaller", "PgBouncer next-step checklist:");
			_ = Log.Info("FishMMOInstaller", $" - Listen on localhost:{InstallationConstants.PgBouncerDefaultPort}");
			_ = Log.Info("FishMMOInstaller", $" - Map database '{dbName}' => host={dbHost} port={dbPort} dbname={dbName}");
			_ = Log.Info("FishMMOInstaller", $" - Ensure pool user '{dbUser}' exists in PgBouncer auth file/mechanism");

			if (isWindows)
			{
				_ = Log.Info("FishMMOInstaller", " - Typical config files: pgbouncer.ini and userlist.txt (install-path dependent)");
				_ = Log.Info("FishMMOInstaller", " - If installed as a Windows service, verify with: sc.exe query pgbouncer");
			}
			else
			{
				_ = Log.Info("FishMMOInstaller", " - Typical config paths: /etc/pgbouncer/pgbouncer.ini and /etc/pgbouncer/userlist.txt");
				_ = Log.Info("FishMMOInstaller", " - Service checks: sudo systemctl status pgbouncer && sudo systemctl restart pgbouncer");
			}
		}

		/// <summary>
		/// Generates <c>/etc/pgbouncer/pgbouncer.ini</c> with transaction pooling and
		/// scram-sha-256 authentication. Offers two auth modes:
		/// <list type="number">
		///   <item><b>auth_query</b> (recommended): PgBouncer delegates authentication to
		///   PostgreSQL via a dedicated <c>fishmmo_pgb_auth</c> role and an auth_query
		///   function. Only the lookup role credential is stored on disk.</item>
		///   <item><b>auth_file</b>: SCRAM-SHA-256 hashes from <c>pg_shadow</c> are
		///   written to <c>/etc/pgbouncer/userlist.txt</c>.</item>
		/// </list>
		/// Requires PostgreSQL superuser credentials.
		/// </summary>
		public static async Task ConfigurePgBouncerLinuxAsync(AppSettings appSettings)
		{
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				await Log.Info("FishMMOInstaller", "Automated PgBouncer configuration is currently supported on Linux only.");
				return;
			}

			string superUser = InstallerProcessHelper.PromptForInput(
				$"Enter PostgreSQL superuser [{InstallationConstants.PostgreSQLDefaultSuperuser}]: ") ?? string.Empty;
			if (string.IsNullOrWhiteSpace(superUser))
				superUser = InstallationConstants.PostgreSQLDefaultSuperuser;

			string superPassword = InstallerProcessHelper.PromptForRequiredPassword(
				$"Enter password for PostgreSQL superuser '{superUser}': ");

			string dbHost = appSettings.Npgsql?.Host ?? "127.0.0.1";
			string configuredPort = appSettings.Npgsql?.Port ?? "5432";
			string defaultBackendPort = configuredPort == InstallationConstants.PgBouncerDefaultPort
				? "5432" : configuredPort;
			string dbPort = InstallerProcessHelper.PromptForInput(
				$"PostgreSQL backend port (not PgBouncer's listening port) [{defaultBackendPort}]: ") ?? string.Empty;
			if (string.IsNullOrWhiteSpace(dbPort)) dbPort = defaultBackendPort;
			if (!int.TryParse(dbPort, out int backendPort) || backendPort < 1 || backendPort > 65535 ||
				backendPort.ToString() == InstallationConstants.PgBouncerDefaultPort)
			{
				await Log.Warning("FishMMOInstaller", "Choose a valid PostgreSQL backend port distinct from PgBouncer's listening port.");
				return;
			}
			string dbName = appSettings.Npgsql?.Database ?? "fishmmo";
			string dbUser = DatabaseSecrets.TryResolveUsername() ?? "fishmmo";

			// The builder preserves passwords containing connection-string punctuation.
			string connectionString = new NpgsqlConnectionStringBuilder
			{
				Host = dbHost, Port = backendPort, Username = superUser, Password = superPassword,
				Database = InstallationConstants.PostgreSQLDefaultAdminDb, Pooling = false
			}.ConnectionString;

			bool useAuthQuery = InstallerProcessHelper.PromptForYesNo(
				"Use auth_query mode (recommended)? PgBouncer will authenticate via PostgreSQL directly and store only its lookup-role credential in a protected file. Say N to use traditional auth_file mode.");

			if (useAuthQuery)
			{
				await ConfigureWithAuthQueryAsync(connectionString, superUser, dbHost, dbPort, dbName, dbUser);
			}
			else
			{
				await ConfigureWithAuthFileAsync(connectionString, superUser, dbHost, dbPort, dbName, dbUser);
			}
		}

		/// <summary>
		/// Configures PgBouncer with auth_query — creates a dedicated auth role and function
		/// in PostgreSQL, then writes pgbouncer.ini with auth_query instead of auth_file.
		/// </summary>
		private static async Task ConfigureWithAuthQueryAsync(
			string connectionString, string superUser,
			string dbHost, string dbPort, string dbName, string dbUser)
		{
			string authUser = InstallationConstants.PgBouncerAuthUser;
			try
			{
				await using var connection = new NpgsqlConnection(connectionString);
				await connection.OpenAsync();

				// Generate a random password for the auth role
				byte[] pwdBytes = new byte[24];
				System.Security.Cryptography.RandomNumberGenerator.Fill(pwdBytes);
				string authPassword = Convert.ToBase64String(pwdBytes);

				// Create the auth lookup role
				await using var createRole = new NpgsqlCommand(
					$"DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '{authUser}') " +
					$"THEN CREATE ROLE {authUser} WITH LOGIN PASSWORD '{PostgreSQLInstaller.EscapeSqlLiteral(authPassword)}'; END IF; END $$", connection);
				await createRole.ExecuteNonQueryAsync();
				await Log.Info("FishMMOInstaller", $"Created/verified PgBouncer auth role '{authUser}'.");

				// Always rotate the credential, including when this role already exists.
				await using var setPassword = new NpgsqlCommand(
					$"ALTER ROLE {authUser} WITH LOGIN PASSWORD '{PostgreSQLInstaller.EscapeSqlLiteral(authPassword)}'", connection);
				await setPassword.ExecuteNonQueryAsync();

				await using var grantConnect = new NpgsqlCommand(
					$"GRANT CONNECT ON DATABASE {new NpgsqlCommandBuilder().QuoteIdentifier(dbName)} TO {authUser}", connection);
				await grantConnect.ExecuteNonQueryAsync();

				var userDbSettings = new NpgsqlConnectionStringBuilder(connectionString) { Database = dbName };
				await using var userDbConn = new NpgsqlConnection(userDbSettings.ConnectionString);
				await userDbConn.OpenAsync();

				await using var createFunc = new NpgsqlCommand(
					$"CREATE OR REPLACE FUNCTION public.pgbouncer_auth_query(p_username TEXT) " +
					$"RETURNS TABLE(username TEXT, password TEXT) AS $$ " +
					$"SELECT rolname::TEXT, rolpassword::TEXT FROM pg_catalog.pg_authid " +
					$"WHERE rolname = p_username AND rolcanlogin " +
					$"AND (rolvaliduntil IS NULL OR rolvaliduntil > CURRENT_TIMESTAMP) " +
					$"$$ LANGUAGE sql SECURITY DEFINER SET search_path = pg_catalog; " +
					$"REVOKE ALL ON FUNCTION public.pgbouncer_auth_query(TEXT) FROM PUBLIC; " +
					$"GRANT EXECUTE ON FUNCTION public.pgbouncer_auth_query(TEXT) TO {authUser};", userDbConn);
				await createFunc.ExecuteNonQueryAsync();
				await Log.Info("FishMMOInstaller", "Created pgbouncer_auth_query function.");

				// Build pgbouncer.ini with auth_query
				var ini = new StringBuilder();
				ini.AppendLine(LinuxConfigHardeningHelper.ManagedMarker + " /etc/pgbouncer/pgbouncer.ini generated by FishMMO-Installer (auth_query mode).");
				ini.AppendLine("[databases]");
				ini.AppendLine($"{dbName} = host={dbHost} port={dbPort} dbname={dbName}");
				ini.AppendLine();
				ini.AppendLine("[pgbouncer]");
				ini.AppendLine("listen_addr = 127.0.0.1");
				ini.AppendLine($"listen_port = {InstallationConstants.PgBouncerDefaultPort}");
				ini.AppendLine("auth_type = scram-sha-256");
				ini.AppendLine("auth_file = /etc/pgbouncer/userlist.txt");
				ini.AppendLine("auth_query = SELECT username, password FROM public.pgbouncer_auth_query($1)");
				ini.AppendLine($"auth_user = {authUser}");
				ini.AppendLine("pool_mode = transaction");
				ini.AppendLine("max_client_conn = 1000");
				ini.AppendLine("default_pool_size = 25");
				ini.AppendLine("reserve_pool_size = 5");
				ini.AppendLine("reserve_pool_timeout = 3");
				ini.AppendLine("server_reset_query = DISCARD ALL");
				ini.AppendLine("ignore_startup_parameters = extra_float_digits");
				ini.AppendLine("; Foreground systemd service logs to stderr/journald; no log directory required.");
				ini.AppendLine("; systemd tracks the foreground process; no pidfile required.");
				ini.AppendLine("admin_users = " + superUser);
				ini.AppendLine("stats_users = " + superUser);

				// The lookup role needs its plaintext password for backend SCRAM authentication.
				// A SCRAM verifier alone cannot authenticate this independent connection.
				const string iniPath = "/etc/pgbouncer/pgbouncer.ini";
				if (!await LinuxConfigHardeningHelper.EnsureBackupAsync(iniPath)) return;

				const string userlistPath = "/etc/pgbouncer/userlist.txt";
				if (!await BackupUserlistIfPresentAsync()) return;
				string userlist = $"\"{authUser}\" \"{authPassword}\"\n";
				if (!await LinuxConfigHardeningHelper.SudoInstallAsync(userlist, userlistPath, "pgbouncer", "pgbouncer", "0600"))
					return;
				if (!await LinuxConfigHardeningHelper.SudoInstallAsync(ini.ToString(), iniPath, "pgbouncer", "pgbouncer", "0640"))
					return;
				if (!await RestartAndVerifyAsync(connectionString, dbName, dbUser)) return;

				await Log.Info("FishMMOInstaller",
					$"PgBouncer configured (auth_query mode): 127.0.0.1:{InstallationConstants.PgBouncerDefaultPort}, " +
					$"pool_mode=transaction, auth=scram-sha-256 via auth_query, database='{dbName}', auth_user='{authUser}'.");
			}
			catch (Exception ex)
			{
				await Log.Error("FishMMOInstaller", "Failed to configure PgBouncer with auth_query", ex);
			}
		}

		/// <summary>
		/// Configures PgBouncer with traditional auth_file — reads the SCRAM-SHA-256 hash
		/// from pg_shadow and writes it to /etc/pgbouncer/userlist.txt.
		/// </summary>
		private static async Task ConfigureWithAuthFileAsync(
			string connectionString, string superUser,
			string dbHost, string dbPort, string dbName, string dbUser)
		{
			string? scramHash = null;
			try
			{
				await using var connection = new NpgsqlConnection(connectionString);
				await connection.OpenAsync();

				await using var cmd = new NpgsqlCommand(
					"SELECT passwd FROM pg_shadow WHERE usename = @u", connection);
				cmd.Parameters.AddWithValue("u", dbUser);
				object? result = await cmd.ExecuteScalarAsync();
				scramHash = result as string;
			}
			catch (Exception ex)
			{
				await Log.Error("FishMMOInstaller", $"Failed to query pg_shadow for user '{dbUser}'", ex);
				return;
			}

			if (string.IsNullOrEmpty(scramHash) || !scramHash.StartsWith("SCRAM-SHA-256$", StringComparison.Ordinal))
			{
				await Log.Warning("FishMMOInstaller",
					$"User '{dbUser}' was not found in pg_shadow with a SCRAM-SHA-256 password. " +
					"Create the FishMMO database/user first (Install FishMMO Database) then re-run PgBouncer configuration.");
				return;
			}

			var ini = new StringBuilder();
			ini.AppendLine(LinuxConfigHardeningHelper.ManagedMarker + " /etc/pgbouncer/pgbouncer.ini generated by FishMMO-Installer (auth_file mode).");
			ini.AppendLine("[databases]");
			ini.AppendLine($"{dbName} = host={dbHost} port={dbPort} dbname={dbName}");
			ini.AppendLine();
			ini.AppendLine("[pgbouncer]");
			ini.AppendLine("listen_addr = 127.0.0.1");
			ini.AppendLine($"listen_port = {InstallationConstants.PgBouncerDefaultPort}");
			ini.AppendLine("auth_type = scram-sha-256");
			ini.AppendLine("auth_file = /etc/pgbouncer/userlist.txt");
			ini.AppendLine("pool_mode = transaction");
			ini.AppendLine("max_client_conn = 1000");
			ini.AppendLine("default_pool_size = 25");
			ini.AppendLine("reserve_pool_size = 5");
			ini.AppendLine("reserve_pool_timeout = 3");
			ini.AppendLine("server_reset_query = DISCARD ALL");
			ini.AppendLine("ignore_startup_parameters = extra_float_digits");
			ini.AppendLine("; Foreground systemd service logs to stderr/journald; no log directory required.");
			ini.AppendLine("; systemd tracks the foreground process; no pidfile required.");
			ini.AppendLine("admin_users = " + superUser);
			ini.AppendLine("stats_users = " + superUser);

			var userlist = new StringBuilder();
			// Unlike pgbouncer.ini, the auth file does not accept our # marker comment.
			userlist.AppendLine($"\"{dbUser}\" \"{scramHash}\"");

			const string iniPath = "/etc/pgbouncer/pgbouncer.ini";
			const string userlistPath = "/etc/pgbouncer/userlist.txt";

			if (!await LinuxConfigHardeningHelper.EnsureBackupAsync(iniPath)) return;
			if (!await BackupUserlistIfPresentAsync()) return;

			if (!await LinuxConfigHardeningHelper.SudoInstallAsync(ini.ToString(), iniPath, "pgbouncer", "pgbouncer", "0640"))
				return;
			if (!await LinuxConfigHardeningHelper.SudoInstallAsync(userlist.ToString(), userlistPath, "pgbouncer", "pgbouncer", "0600"))
				return;

			if (!await RestartAndVerifyAsync(connectionString, dbName, dbUser)) return;

			await Log.Info("FishMMOInstaller",
				$"PgBouncer configured (auth_file mode): 127.0.0.1:{InstallationConstants.PgBouncerDefaultPort}, " +
				$"pool_mode=transaction, auth=scram-sha-256, database='{dbName}', user='{dbUser}'.");
		}

		private static async Task<bool> BackupUserlistIfPresentAsync()
		{
			(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();
			return await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix,
				"if sudo test -e /etc/pgbouncer/userlist.txt; then " +
				"sudo test -e /etc/pgbouncer/userlist.txt.pre-fishmmo.bak || " +
				"sudo cp -a /etc/pgbouncer/userlist.txt /etc/pgbouncer/userlist.txt.pre-fishmmo.bak; fi",
				"Could not back up the existing PgBouncer credentials.");
		}

		private static async Task<bool> RestartAndVerifyAsync(string connectionString, string dbName, string dbUser)
		{
			(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();
			if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix,
				$"sudo systemctl restart {InstallationConstants.PgBouncerLinuxServiceName} && " +
				$"sudo systemctl is-active --quiet {InstallationConstants.PgBouncerLinuxServiceName}",
				"PgBouncer restart failed. Run 'sudo journalctl -u pgbouncer -n 50' to investigate.")) return false;

			string? password = DatabaseSecrets.TryResolvePassword();
			if (string.IsNullOrEmpty(password))
			{
				await Log.Warning("FishMMOInstaller", "PgBouncer service started, but no application DB password is available to test authentication. Test with psql before switching runtime connections to port 6432.");
				return true;
			}
			try
			{
				var settings = new NpgsqlConnectionStringBuilder(connectionString)
				{
					Host = "127.0.0.1", Port = int.Parse(InstallationConstants.PgBouncerDefaultPort),
					Database = dbName, Username = dbUser, Password = password, Pooling = false, Timeout = 5
				};
				await using var connection = new NpgsqlConnection(settings.ConnectionString);
				// Type=simple services may be active before their listener is ready.
				for (int attempt = 0; ; attempt++)
				{
					try
					{
						await connection.OpenAsync();
						break;
					}
					catch (NpgsqlException ex) when (attempt < 19 && ex.InnerException is System.Net.Sockets.SocketException)
					{
						await Task.Delay(250);
					}
				}
				await using var command = new NpgsqlCommand("SELECT 1", connection);
				await command.ExecuteScalarAsync();
				await Log.Info("FishMMOInstaller", "Application authentication and SELECT 1 through PgBouncer succeeded.");
				return true;
			}
			catch (Exception ex)
			{
				await Log.Error("FishMMOInstaller", "PgBouncer started but the application connection test failed. Keep runtime connections on PostgreSQL until this is resolved.", ex);
				return false;
			}
		}
	}
}