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
			string dbUser = appSettings.Npgsql?.Username ?? "fishmmo";

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
		///   function. No SCRAM hashes are stored on disk.</item>
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

			string superPassword = InstallerProcessHelper.PromptForPassword(
				$"Enter password for PostgreSQL superuser '{superUser}': ");

			string dbHost = appSettings.Npgsql?.Host ?? "127.0.0.1";
			string dbPort = appSettings.Npgsql?.Port ?? "5432";
			string dbName = appSettings.Npgsql?.Database ?? "fishmmo";
			string dbUser = appSettings.Npgsql?.Username ?? "fishmmo";

			string connectionString =
				$"Host={dbHost};Port={dbPort};Username={superUser};Password={superPassword};Database={InstallationConstants.PostgreSQLDefaultAdminDb};Pooling=false";

			bool useAuthQuery = InstallerProcessHelper.PromptForYesNo(
				"Use auth_query mode (recommended)? PgBouncer will authenticate via PostgreSQL directly instead of storing SCRAM hashes in a file. Say N to use traditional auth_file mode.");

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

				// Grant the auth role permission to read pg_shadow (needed for auth_query)
				await using var grantExec = new NpgsqlCommand(
					$"GRANT EXECUTE ON FUNCTION pg_catalog.pg_stat_get_activity(int) TO {authUser}", connection);
				try { await grantExec.ExecuteNonQueryAsync(); } catch { /* may already be granted */ }

				// Create the auth_query function in the target database
				await using var switchDb = new NpgsqlCommand(
					$"SELECT format('GRANT CONNECT ON DATABASE %I TO %I', '{dbName}', '{authUser}')", connection);
				string grantConnectSql = (string)(await switchDb.ExecuteScalarAsync())!;
				await using var grantConnect = new NpgsqlCommand(grantConnectSql, connection);
				await grantConnect.ExecuteNonQueryAsync();

				// Create auth_query function via superuser connection to the target database
				string userDbConnString = connectionString.Replace(
					$"Database={InstallationConstants.PostgreSQLDefaultAdminDb}",
					$"Database={dbName}");
				await using var userDbConn = new NpgsqlConnection(userDbConnString);
				await userDbConn.OpenAsync();

				await using var createFunc = new NpgsqlCommand(
					$"CREATE OR REPLACE FUNCTION pgbouncer_auth_query(p_username TEXT) " +
					$"RETURNS TABLE(username TEXT, password TEXT) AS $$ " +
					$"SELECT usename::TEXT, passwd::TEXT FROM pg_catalog.pg_shadow WHERE usename = p_username " +
					$"$$ LANGUAGE sql SECURITY DEFINER; " +
					$"REVOKE ALL ON FUNCTION pgbouncer_auth_query(TEXT) FROM PUBLIC; " +
					$"GRANT EXECUTE ON FUNCTION pgbouncer_auth_query(TEXT) TO {authUser};", userDbConn);
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
				ini.AppendLine($"auth_query = SELECT username, password FROM pgbouncer_auth_query($1)");
				ini.AppendLine($"auth_user = {authUser}");
				ini.AppendLine("pool_mode = transaction");
				ini.AppendLine("max_client_conn = 1000");
				ini.AppendLine("default_pool_size = 25");
				ini.AppendLine("reserve_pool_size = 5");
				ini.AppendLine("reserve_pool_timeout = 3");
				ini.AppendLine("server_reset_query = DISCARD ALL");
				ini.AppendLine("ignore_startup_parameters = extra_float_digits");
				ini.AppendLine("logfile = /var/log/pgbouncer/pgbouncer.log");
				ini.AppendLine("pidfile = /var/run/pgbouncer/pgbouncer.pid");
				ini.AppendLine("admin_users = " + superUser);
				ini.AppendLine("stats_users = " + superUser);

				// Write pgbouncer.ini only (no userlist.txt needed)
				const string iniPath = "/etc/pgbouncer/pgbouncer.ini";
				await LinuxConfigHardeningHelper.EnsureBackupAsync(iniPath);

				if (!await LinuxConfigHardeningHelper.SudoInstallAsync(ini.ToString(), iniPath, "pgbouncer", "pgbouncer", "0640"))
					return;

				// Remove stale userlist.txt if it exists from a previous auth_file config
				const string userlistPath = "/etc/pgbouncer/userlist.txt";
				(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();
				await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix,
					$"sudo rm -f {userlistPath}", "Could not remove old userlist.txt (ignored).");

				await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix,
					$"sudo systemctl restart {InstallationConstants.PgBouncerLinuxServiceName}",
					"PgBouncer service restart failed. Run 'sudo journalctl -u pgbouncer -n 50' to investigate.");

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
			ini.AppendLine("logfile = /var/log/pgbouncer/pgbouncer.log");
			ini.AppendLine("pidfile = /var/run/pgbouncer/pgbouncer.pid");
			ini.AppendLine("admin_users = " + superUser);
			ini.AppendLine("stats_users = " + superUser);

			var userlist = new StringBuilder();
			userlist.AppendLine(LinuxConfigHardeningHelper.ManagedMarker + " /etc/pgbouncer/userlist.txt generated by FishMMO-Installer.");
			userlist.AppendLine($"\"{dbUser}\" \"{scramHash}\"");

			const string iniPath = "/etc/pgbouncer/pgbouncer.ini";
			const string userlistPath = "/etc/pgbouncer/userlist.txt";

			await LinuxConfigHardeningHelper.EnsureBackupAsync(iniPath);
			await LinuxConfigHardeningHelper.EnsureBackupAsync(userlistPath);

			if (!await LinuxConfigHardeningHelper.SudoInstallAsync(ini.ToString(), iniPath, "pgbouncer", "pgbouncer", "0640"))
				return;
			if (!await LinuxConfigHardeningHelper.SudoInstallAsync(userlist.ToString(), userlistPath, "pgbouncer", "pgbouncer", "0600"))
				return;

			(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();
			await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix,
				$"sudo systemctl restart {InstallationConstants.PgBouncerLinuxServiceName}",
				"PgBouncer service restart failed. Run 'sudo journalctl -u pgbouncer -n 50' to investigate.");

			await Log.Info("FishMMOInstaller",
				$"PgBouncer configured (auth_file mode): 127.0.0.1:{InstallationConstants.PgBouncerDefaultPort}, " +
				$"pool_mode=transaction, auth=scram-sha-256, database='{dbName}', user='{dbUser}'.");
		}
	}
}