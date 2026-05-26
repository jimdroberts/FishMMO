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

			var detected = await InstallerProcessHelper.DetectLinuxPackageManagerAsync(packageNames);
			if (detected == null)
			{
				await Log.Warning("FishMMOInstaller", "No supported package manager (pacman, apt-get, dnf, yum) found. Please install PgBouncer manually.");
				return;
			}

			var (updateCommand, installCommand, managerName) = detected.Value;
			await Log.Info("FishMMOInstaller", $"Using {managerName} for PgBouncer installation.");

			if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, updateCommand, "Failed to update package metadata."))
			{
				return;
			}

			if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, installCommand, "Failed to install PgBouncer."))
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
		/// Generates <c>/etc/pgbouncer/pgbouncer.ini</c> (transaction pooling, scram-sha-256) and
		/// <c>/etc/pgbouncer/userlist.txt</c> (containing the SCRAM-SHA-256 hash for the FishMMO
		/// app user fetched from <c>pg_shadow</c>), then restarts the pgbouncer service.
		/// Requires PostgreSQL superuser credentials to read <c>pg_shadow</c>.
		/// </summary>
		public static async Task ConfigurePgBouncerLinuxAsync(AppSettings appSettings)
		{
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				await Log.Info("FishMMOInstaller", "Automated PgBouncer configuration is currently supported on Linux only.");
				return;
			}

			string superUser = InstallerProcessHelper.PromptForInput(
				$"Enter PostgreSQL superuser to read pg_shadow [{InstallationConstants.PostgreSQLDefaultSuperuser}]: ") ?? string.Empty;
			if (string.IsNullOrWhiteSpace(superUser))
			{
				superUser = InstallationConstants.PostgreSQLDefaultSuperuser;
			}
			string superPassword = InstallerProcessHelper.PromptForPassword(
				$"Enter password for PostgreSQL superuser '{superUser}': ");

			string dbHost = appSettings.Npgsql?.Host ?? "127.0.0.1";
			string dbPort = appSettings.Npgsql?.Port ?? "5432";
			string dbName = appSettings.Npgsql?.Database ?? "fishmmo";
			string dbUser = appSettings.Npgsql?.Username ?? "fishmmo";

			string connectionString =
				$"Host={dbHost};Port={dbPort};Username={superUser};Password={superPassword};Database={InstallationConstants.PostgreSQLDefaultAdminDb};Pooling=false";

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

			// Build pgbouncer.ini
			var ini = new StringBuilder();
			ini.AppendLine(LinuxConfigHardeningHelper.ManagedMarker + " /etc/pgbouncer/pgbouncer.ini generated by FishMMO-Installer.");
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

			// Build userlist.txt
			// pgbouncer expects: "username" "scram-sha-256-hash"
			var userlist = new StringBuilder();
			userlist.AppendLine(LinuxConfigHardeningHelper.ManagedMarker + " /etc/pgbouncer/userlist.txt generated by FishMMO-Installer.");
			userlist.AppendLine($"\"{dbUser}\" \"{scramHash}\"");

			const string iniPath = "/etc/pgbouncer/pgbouncer.ini";
			const string userlistPath = "/etc/pgbouncer/userlist.txt";

			await LinuxConfigHardeningHelper.EnsureBackupAsync(iniPath);
			await LinuxConfigHardeningHelper.EnsureBackupAsync(userlistPath);

			if (!await LinuxConfigHardeningHelper.SudoInstallAsync(ini.ToString(), iniPath, "pgbouncer", "pgbouncer", "0640"))
			{
				return;
			}
			if (!await LinuxConfigHardeningHelper.SudoInstallAsync(userlist.ToString(), userlistPath, "pgbouncer", "pgbouncer", "0600"))
			{
				return;
			}

			(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();
			await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix,
				$"sudo systemctl restart {InstallationConstants.PgBouncerLinuxServiceName}",
				"PgBouncer service restart failed. Run 'sudo journalctl -u pgbouncer -n 50' to investigate.");

			await Log.Info("FishMMOInstaller",
				$"PgBouncer configured: listening on 127.0.0.1:{InstallationConstants.PgBouncerDefaultPort}, " +
				$"pool_mode=transaction, auth=scram-sha-256, database='{dbName}', user='{dbUser}'.");
		}
	}
}