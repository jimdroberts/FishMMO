using FishMMO.Database;
using FishMMO.Logging;
using Npgsql;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace FishMMO.Installer
{
	/// <summary>
	/// PostgreSQL security hardening: rewrites <c>pg_hba.conf</c> to require
	/// <c>scram-sha-256</c> on all TCP entries, sets <c>password_encryption</c> and
	/// <c>listen_addresses</c> in <c>postgresql.conf</c>, then reloads the server via
	/// <c>pg_reload_conf()</c>. Idempotent: re-runs detect the managed marker and skip.
	/// Linux-only; on Windows the installer prints a hint and exits.
	/// </summary>
	public static partial class PostgreSQLInstaller
	{
		/// <summary>
		/// Apply PostgreSQL security hardening using an already-connected superuser session.
		/// Safe to call repeatedly; existing edits are detected by the managed marker.
		/// </summary>
		public static async Task HardenPostgreSQLAsync(NpgsqlConnection connection, AppSettings appSettings)
		{
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				await Log.Info("FishMMOInstaller",
					"PostgreSQL hardening on Windows is not automated. " +
					"Manually edit pg_hba.conf to require 'scram-sha-256' and set 'listen_addresses' in postgresql.conf.");
				return;
			}

			try
			{
				string? hbaPath = await GetPgFileSettingAsync(connection, "hba_file");
				string? confPath = await GetPgFileSettingAsync(connection, "config_file");

				if (string.IsNullOrWhiteSpace(hbaPath) || string.IsNullOrWhiteSpace(confPath))
				{
					await Log.Warning("FishMMOInstaller", "Could not determine pg_hba.conf / postgresql.conf path. Skipping hardening.");
					return;
				}

				await Log.Info("FishMMOInstaller", $"PostgreSQL hardening: hba_file={hbaPath}, config_file={confPath}");

				string? hbaContent = await LinuxConfigHardeningHelper.SudoReadAsync(hbaPath);
				string? confContent = await LinuxConfigHardeningHelper.SudoReadAsync(confPath);
				if (hbaContent == null || confContent == null)
				{
					await Log.Warning("FishMMOInstaller", "Could not read PostgreSQL config files (sudo cat failed). Skipping hardening.");
					return;
				}

				// pg_hba.conf
				string newHba = HardenPgHbaContent(hbaContent, appSettings);
				if (!string.Equals(newHba, hbaContent, StringComparison.Ordinal))
				{
					if (!await LinuxConfigHardeningHelper.EnsureBackupAsync(hbaPath)) return;
					if (!await LinuxConfigHardeningHelper.SudoInstallAsync(newHba, hbaPath, "postgres", "postgres", "0600"))
						return;
					await Log.Info("FishMMOInstaller", $"Rewrote pg_hba.conf to require scram-sha-256 for TCP connections.");
				}
				else
				{
					await Log.Info("FishMMOInstaller", "pg_hba.conf already hardened. No changes.");
				}

				// postgresql.conf
				string newConf = HardenPostgresqlConfContent(confContent);
				if (!string.Equals(newConf, confContent, StringComparison.Ordinal))
				{
					if (!await LinuxConfigHardeningHelper.EnsureBackupAsync(confPath)) return;
					if (!await LinuxConfigHardeningHelper.SudoInstallAsync(newConf, confPath, "postgres", "postgres", "0600"))
						return;
					await Log.Info("FishMMOInstaller", "Updated postgresql.conf (password_encryption, listen_addresses).");
				}
				else
				{
					await Log.Info("FishMMOInstaller", "postgresql.conf already hardened. No changes.");
				}

				// listen_addresses requires a restart; password_encryption and pg_hba take effect on reload.
				try
				{
					await using var reload = new NpgsqlCommand("SELECT pg_reload_conf()", connection);
					await reload.ExecuteScalarAsync();
					await Log.Info("FishMMOInstaller", "Issued pg_reload_conf(). pg_hba changes are now active.");
					await Log.Info("FishMMOInstaller",
						"NOTE: 'listen_addresses' changes require a full restart: 'sudo systemctl restart postgresql'.");
				}
				catch (Exception ex)
				{
					await Log.Warning("FishMMOInstaller", $"pg_reload_conf() failed: {ex.Message}. Run 'sudo systemctl reload postgresql' manually.");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("FishMMOInstaller", "PostgreSQL hardening failed", ex);
			}
		}

		/// <summary>
		/// Rewrites pg_hba.conf so every <c>host</c>/<c>hostssl</c>/<c>hostnossl</c> rule using
		/// <c>trust</c>, <c>password</c>, or <c>md5</c> auth becomes <c>scram-sha-256</c>.
		/// Comments and local-socket rules are preserved untouched. The marker tag is appended
		/// to every modified line so subsequent runs can detect prior edits.
		/// </summary>
		private static string HardenPgHbaContent(string content, AppSettings appSettings)
		{
			if (content.Contains(LinuxConfigHardeningHelper.ManagedMarker))
			{
				// Already managed by this installer; do nothing.
				return content;
			}

			var sb = new StringBuilder();
			using var reader = new StringReader(content);
			string? line;
			while ((line = reader.ReadLine()) != null)
			{
				string trimmed = line.TrimStart();
				if (trimmed.Length == 0 || trimmed.StartsWith("#"))
				{
					sb.AppendLine(line);
					continue;
				}

				// Match TCP rules: host | hostssl | hostnossl ... <auth-method> [options]
				// We split on whitespace and inspect/replace token index 4 (auth method) when present.
				string[] tokens = Regex.Split(line, @"\s+");
				if (tokens.Length >= 5 &&
					(tokens[0] == "host" || tokens[0] == "hostssl" || tokens[0] == "hostnossl"))
				{
					string method = tokens[4].ToLowerInvariant();
					if (method == "trust" || method == "password" || method == "md5")
					{
						tokens[4] = "scram-sha-256";
						sb.AppendLine(string.Join(' ', tokens) + "  " + LinuxConfigHardeningHelper.ManagedMarker + $" (was: {method})");
						continue;
					}
				}

				sb.AppendLine(line);
			}

			sb.AppendLine();
			sb.AppendLine($"{LinuxConfigHardeningHelper.ManagedMarker} block applied by FishMMO-Installer.");
			return sb.ToString();
		}

		/// <summary>
		/// Ensures postgresql.conf has <c>password_encryption = scram-sha-256</c> and
		/// <c>listen_addresses = 'localhost'</c>. Existing uncommented occurrences are
		/// commented out so the appended fishmmo-managed block takes precedence.
		/// </summary>
		private static string HardenPostgresqlConfContent(string content)
		{
			if (content.Contains(LinuxConfigHardeningHelper.ManagedMarker))
			{
				return content;
			}

			content = CommentOutDirective(content, "password_encryption");
			content = CommentOutDirective(content, "listen_addresses");

			var sb = new StringBuilder(content);
			if (!content.EndsWith('\n')) sb.AppendLine();
			sb.AppendLine();
			sb.AppendLine(LinuxConfigHardeningHelper.ManagedMarker + " block applied by FishMMO-Installer.");
			sb.AppendLine("password_encryption = scram-sha-256");
			string listenAddr = Environment.GetEnvironmentVariable("FISHMMO_PG_LISTEN_ADDRESSES") ?? "localhost";
			sb.AppendLine($"listen_addresses = '{EscapeSqlLiteral(listenAddr)}'");
			return sb.ToString();
		}

		/// <summary>
		/// Comments out any uncommented occurrence of a postgresql.conf directive
		/// (matches both 'key = value' and 'key=value' with leading whitespace).
		/// </summary>
		private static string CommentOutDirective(string content, string key)
		{
			var sb = new StringBuilder();
			using var reader = new StringReader(content);
			string? line;
			var pattern = new Regex(@"^\s*" + Regex.Escape(key) + @"\s*=", RegexOptions.IgnoreCase);
			while ((line = reader.ReadLine()) != null)
			{
				if (pattern.IsMatch(line) && !line.TrimStart().StartsWith("#"))
				{
					sb.AppendLine("# " + line + "  " + LinuxConfigHardeningHelper.ManagedMarker + " commented");
				}
				else
				{
					sb.AppendLine(line);
				}
			}
			return sb.ToString();
		}

		/// <summary>
		/// Queries a path setting from the live server (e.g. <c>hba_file</c>, <c>config_file</c>).
		/// </summary>
		private static async Task<string?> GetPgFileSettingAsync(NpgsqlConnection connection, string name)
		{
			try
			{
				await using var cmd = new NpgsqlCommand("SELECT setting FROM pg_settings WHERE name = @name", connection);
				cmd.Parameters.AddWithValue("name", name);
				object? result = await cmd.ExecuteScalarAsync();
				return result as string;
			}
			catch
			{
				return null;
			}
		}
	}
}