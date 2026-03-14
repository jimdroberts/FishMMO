using FishMMO.Logging;
using System.Runtime.InteropServices;
using FishMMO.Database;

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
	}
}