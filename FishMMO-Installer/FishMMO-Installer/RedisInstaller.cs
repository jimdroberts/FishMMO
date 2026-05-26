using FishMMO.Database;
using FishMMO.Logging;
using System.Runtime.InteropServices;

namespace FishMMO.Installer
{
	/// <summary>
	/// Installs and hardens a localhost-only Redis instance for FishMMO.
	/// Supports Linux (pacman/apt-get/dnf/yum) via the official redis package; on
	/// Windows the official Redis project does not ship binaries, so this falls back to
	/// emitting clear manual-install guidance after attempting winget/choco installation
	/// of Memurai (a drop-in Redis-on-Windows fork).
	///
	/// Hardening applied on Linux:
	///   - bind 127.0.0.1 -::1      (no external listeners)
	///   - protected-mode yes
	///   - requirepass &lt;prompted&gt;  (random suggestion offered when empty)
	///   - daemonize handled by systemd; service enabled + started
	///   - /etc/redis/redis.conf chmod 640 root:redis
	/// </summary>
	public static class RedisInstaller
	{
		/// <summary>Returns true if the redis-server binary is present on PATH.</summary>
		public static async Task<bool> IsRedisInstalledAsync()
		{
			(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();
			string probe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
				? "where memurai || where redis-server"
				: "command -v redis-server";
			return await InstallerProcessHelper.RunProcessAsync(
				shell,
				$"{argPrefix} \"{probe}\"",
				(exitCode, _, _) => exitCode == 0);
		}

		/// <summary>Top-level entry point: dispatches to the correct platform implementation.</summary>
		public static async Task InstallRedis(AppSettings appSettings)
		{
			Console.Clear();
			await Log.Info("FishMMOInstaller", "--- Install Redis ---");

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				await InstallRedisWindows(appSettings);
				return;
			}

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				await InstallRedisLinux(appSettings);
				return;
			}

			await Log.Warning("FishMMOInstaller", "Unsupported operating system for Redis installation.");
		}

		private static async Task InstallRedisLinux(AppSettings appSettings)
		{
			(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();
			bool alreadyInstalled = await IsRedisInstalledAsync();

			if (!alreadyInstalled)
			{
				if (!InstallerProcessHelper.PromptForYesNo("Install Redis (in-memory cache)?"))
				{
					await Log.Info("FishMMOInstaller", "Redis installation cancelled by user.");
					return;
				}

				var packageNames = new Dictionary<string, string>
				{
					["pacman"] = "redis",
					["apt-get"] = "redis-server",
					["dnf"] = "redis",
					["yum"] = "redis",
				};

				var detected = await InstallerProcessHelper.DetectLinuxPackageManagerAsync(packageNames);
				if (detected == null)
				{
					await Log.Warning("FishMMOInstaller", "No supported package manager found. Please install Redis manually.");
					return;
				}

				var (updateCommand, installCommand, managerName) = detected.Value;
				await Log.Info("FishMMOInstaller", $"Using {managerName} for Redis installation.");

				if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, updateCommand, "Failed to update package metadata."))
				{
					await Log.Warning("FishMMOInstaller", "Continuing in spite of metadata update failure.");
				}
				if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, installCommand, "Failed to install Redis."))
				{
					return;
				}
			}
			else
			{
				await Log.Info("FishMMOInstaller", "Redis already installed.");
			}

			// Apply hardening. Use the password already in appsettings.json if present,
			// otherwise prompt securely.
			string password = appSettings.Redis?.Password ?? string.Empty;
			if (string.IsNullOrWhiteSpace(password) || password == "__REPLACE_ME__")
			{
				password = InstallerProcessHelper.PromptForPassword("Enter Redis password (used as requirepass): ");
			}

			await HardenLinuxConfigAsync(password);

			await Log.Info("FishMMOInstaller", "Enabling and starting Redis service...");
			if (!await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix,
					$"sudo systemctl enable --now {InstallationConstants.RedisLinuxServiceName}",
					"Failed to enable/start the Redis service."))
			{
				await Log.Warning("FishMMOInstaller", "Verify with: systemctl status redis");
			}
			else
			{
				await Log.Info("FishMMOInstaller", "Redis is enabled and running on 127.0.0.1:" + InstallationConstants.RedisDefaultPort);
			}
		}

		/// <summary>
		/// Rewrites <c>/etc/redis/redis.conf</c> to bind to localhost, enable protected-mode,
		/// and set <c>requirepass</c>. Uses a streaming rewrite with a temp file + atomic install
		/// so a partial write cannot corrupt the live config.
		/// </summary>
		private static async Task HardenLinuxConfigAsync(string password)
		{
			string confPath = InstallationConstants.RedisLinuxConfigurationPath;

			// The Redis config is root:redis 640; read it via sudo cat.
			string original = string.Empty;
			await InstallerProcessHelper.RunProcessAsync("/usr/bin/sudo",
				$"cat \"{confPath}\"",
				(exit, stdout, _) =>
				{
					if (exit == 0) original = stdout;
					return exit == 0;
				});

			if (string.IsNullOrEmpty(original))
			{
				await Log.Warning("FishMMOInstaller", $"Could not read {confPath} (file missing or sudo failed). Skipping hardening.");
				return;
			}

			// Back up once.
			string backupPath = confPath + ".pre-fishmmo.bak";
			await InstallerProcessHelper.RunShellCommandAsync("/usr/bin/fish", "-lc",
				$"sudo test -e '{backupPath}' || sudo cp -a '{confPath}' '{backupPath}'",
				"Failed to back up redis.conf");

			string hardened = ApplyRedisDirective(original, "bind", "127.0.0.1 -::1");
			hardened = ApplyRedisDirective(hardened, "protected-mode", "yes");
			hardened = ApplyRedisDirective(hardened, "requirepass", QuoteIfNeeded(password));
			hardened = ApplyRedisDirective(hardened, "supervised", "systemd");

			string tmpPath = Path.Combine(Path.GetTempPath(), "redis.conf.fishmmo");
			try
			{
				await File.WriteAllTextAsync(tmpPath, hardened);

				if (!await InstallerProcessHelper.RunShellCommandAsync("/usr/bin/fish", "-lc",
						$"sudo install -o root -g redis -m 0640 '{tmpPath}' '{confPath}'",
						"Failed to install hardened redis.conf"))
				{
					await Log.Warning("FishMMOInstaller", "redis.conf was NOT updated; check sudo permissions.");
					return;
				}

				await Log.Info("FishMMOInstaller", $"Redis hardened: bind 127.0.0.1, protected-mode yes, requirepass set; backup at {backupPath}");
			}
			finally
			{
				try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { /* best-effort */ }
			}
		}

		/// <summary>
		/// Replaces (or appends) a single Redis directive in the given config text.
		/// Comments-out any existing uncommented occurrences before appending the canonical value
		/// so multiple historical "bind" lines do not silently override us.
		/// </summary>
		private static string ApplyRedisDirective(string config, string directive, string value)
		{
			if (string.IsNullOrEmpty(config))
			{
				return $"{directive} {value}\n";
			}

			var lines = config.Replace("\r\n", "\n").Split('\n');
			for (int i = 0; i < lines.Length; i++)
			{
				string trimmed = lines[i].TrimStart();
				if (trimmed.Length == 0 || trimmed[0] == '#')
				{
					continue;
				}
				int sp = trimmed.IndexOf(' ');
				string token = sp < 0 ? trimmed : trimmed.Substring(0, sp);
				if (string.Equals(token, directive, StringComparison.OrdinalIgnoreCase))
				{
					lines[i] = "# " + lines[i] + "  # disabled by FishMMO-Installer";
				}
			}

			string body = string.Join("\n", lines).TrimEnd('\n');
			return body + $"\n\n# Added by FishMMO-Installer\n{directive} {value}\n";
		}

		private static string QuoteIfNeeded(string value)
		{
			if (string.IsNullOrEmpty(value)) return "\"\"";
			if (value.IndexOfAny(new[] { ' ', '\t', '"', '\'', '#', '\\' }) < 0) return value;
			return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
		}

		private static async Task InstallRedisWindows(AppSettings appSettings)
		{
			if (await IsRedisInstalledAsync())
			{
				await Log.Info("FishMMOInstaller", "Redis-compatible service already installed on Windows.");
				return;
			}

			if (!InstallerProcessHelper.PromptForYesNo("Install Memurai (Redis-compatible service for Windows)?"))
			{
				await Log.Info("FishMMOInstaller", "Redis installation cancelled by user.");
				return;
			}

			(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();
			bool hasWinget = await InstallerProcessHelper.RunProcessAsync(shell, $"{argPrefix} \"where winget\"", (exit, _, _) => exit == 0);
			bool installed = false;

			if (hasWinget)
			{
				installed = await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix,
					"winget install --id Memurai.MemuraiDeveloper -e --silent --accept-source-agreements --accept-package-agreements --disable-interactivity",
					"winget install of Memurai failed.");
			}

			if (!installed)
			{
				bool hasChoco = await InstallerProcessHelper.RunProcessAsync(shell, $"{argPrefix} \"where choco\"", (exit, _, _) => exit == 0);
				if (hasChoco)
				{
					installed = await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix,
						"choco install memurai-developer -y",
						"Chocolatey install of memurai-developer failed.");
				}
			}

			if (!installed)
			{
				await Log.Warning("FishMMOInstaller", "Could not install Redis automatically on Windows. Download Memurai from https://www.memurai.com/get-memurai, then re-run this option.");
				return;
			}

			await Log.Info("FishMMOInstaller", "Memurai installed. Configure requirepass in C:\\Program Files\\Memurai\\memurai.conf and restart the 'Memurai' Windows service.");
		}
	}
}
