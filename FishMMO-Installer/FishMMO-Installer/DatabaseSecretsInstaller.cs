using FishMMO.Database;
using FishMMO.Logging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FishMMO.Installer
{
	/// <summary>
	/// Interactive wizard for configuring database connection parameters.
	/// On Linux, sudo is used automatically when /etc/fishmmo/ requires root.
	/// </summary>
	public static class DatabaseSecretsInstaller
	{
		public static async Task ConfigureDatabaseSecrets()
		{
			string? currentUsername = DatabaseSecrets.TryResolveUsername();
			string? currentPassword = DatabaseSecrets.TryResolvePassword();
			string currentHost = DatabaseSecrets.TryResolveHost();
			int currentPort = DatabaseSecrets.TryResolvePort();
			string currentDbName = DatabaseSecrets.TryResolveDbName();
			string secretsPath = DatabaseSecrets.DefaultSecretsFilePath;

			string? localUsername = currentUsername;
			string? localPassword = currentPassword;
			string localHost = currentHost;
			int localPort = currentPort;
			string localDbName = currentDbName;

			while (true)
			{
				Console.Clear();
				Console.WriteLine("=== Configure Database Secrets ===");
				Console.WriteLine($"Secrets file: {secretsPath}");
				Console.WriteLine();
				Console.WriteLine($"  Connection: {localUsername ?? "?"}@{localHost}:{localPort}/{localDbName}");
				Console.WriteLine($"  DSN: Host={localHost};Port={localPort};Database={localDbName};Username={localUsername ?? "?"};Password=***");
				Console.WriteLine();
				Console.WriteLine($"1 : Database Username  [{currentUsername ?? "not set"}]");
				Console.WriteLine($"2 : Database Password  [{MaskSecret(currentPassword)}]");
				Console.WriteLine($"3 : Database Host      [{currentHost}]");
				Console.WriteLine($"4 : Database Port      [{currentPort}]");
				Console.WriteLine($"5 : Database Name      [{currentDbName}]");
				Console.WriteLine("6 : Write to secrets file");
				Console.WriteLine("7 : Export as environment variables");
				Console.WriteLine("0 : Back");

				ConsoleKeyInfo key = Console.ReadKey(true);
				Console.WriteLine();

				switch (key.Key)
				{
					case ConsoleKey.D1:
						Console.Write($"Database username [{currentUsername ?? "fishmmo"}]: ");
						string? u = Console.ReadLine();
						if (!string.IsNullOrWhiteSpace(u)) { localUsername = u.Trim(); currentUsername = localUsername; }
						break;
					case ConsoleKey.D2:
						localPassword = InstallerProcessHelper.PromptForRequiredPassword("Database password: ");
						currentPassword = localPassword;
						break;
					case ConsoleKey.D3:
						Console.Write($"Database host [{currentHost}]: ");
						string? h = Console.ReadLine();
						if (!string.IsNullOrWhiteSpace(h)) { localHost = h.Trim(); currentHost = localHost; }
						break;
					case ConsoleKey.D4:
						Console.Write($"Database port [{currentPort}]: ");
						string? p = Console.ReadLine();
						if (!string.IsNullOrWhiteSpace(p) && int.TryParse(p.Trim(), out int pv) && pv > 0)
						{ localPort = pv; currentPort = localPort; }
						break;
					case ConsoleKey.D5:
						Console.Write($"Database name [{currentDbName}]: ");
						string? d = Console.ReadLine();
						if (!string.IsNullOrWhiteSpace(d)) { localDbName = d.Trim(); currentDbName = localDbName; }
						break;
					case ConsoleKey.D6:
						await WriteSecretsFile(localUsername, localPassword, localHost, localPort, localDbName, secretsPath);
						break;
					case ConsoleKey.D7:
						await ExportEnvVars(localUsername, localPassword, localHost, localPort, localDbName);
						break;
					case ConsoleKey.D0:
					case ConsoleKey.NumPad0:
						return;
				}

				if (key.Key != ConsoleKey.D6 && key.Key != ConsoleKey.D7)
				{
					Console.WriteLine("Press any key to continue...");
					Console.ReadKey(true);
				}
			}
		}

		private static async Task WriteSecretsFile(string? username, string? password, string host, int port, string dbName, string secretsPath)
		{
			var sb = new StringBuilder();
			sb.AppendLine("# FishMMO Database Connection — chmod 600");
			sb.AppendLine("# Do NOT store these values in appsettings.json.");
			sb.AppendLine("#");
			sb.AppendLine($"# Connection: {username ?? "?"}@{host}:{port}/{dbName}");
			sb.AppendLine($"# DSN: Host={host};Port={port};Database={dbName};Username={username ?? "?"};Password=***");
			sb.AppendLine();
			sb.AppendLine($"FISHMMO_DB_HOST={host}");
			sb.AppendLine($"FISHMMO_DB_PORT={port}");
			sb.AppendLine($"FISHMMO_DB_NAME={dbName}");
			if (!string.IsNullOrEmpty(username))
				sb.AppendLine($"FISHMMO_DB_USERNAME={username}");
			if (!string.IsNullOrEmpty(password))
				sb.AppendLine($"FISHMMO_DB_PASSWORD={password}");
			string content = sb.ToString();

			await WriteWithSudoIfNeeded(secretsPath, content);
			Console.WriteLine("Press any key to continue...");
			Console.ReadKey(true);
		}

		/// <summary>
		/// Writes content to the given path. If the directory isn't writable by the
		/// current user, elevates via sudo. The resulting file is chmod 600 and
		/// chown'd back to the invoking user (via $SUDO_USER) so the non-root
		/// process can read it at runtime.
		/// </summary>
		internal static async Task WriteWithSudoIfNeeded(string path, string content)
		{
			string? dir = Path.GetDirectoryName(path);

			// Try direct write if the directory exists and is writable.
			if (dir != null && Directory.Exists(dir) && CanWriteToDirectory(dir))
			{
				await File.WriteAllTextAsync(path, content);
				Chmod600(path);
				await Log.Info("FishMMOInstaller", $"Secrets written to {path}");
				Console.WriteLine($"Secrets written to: {path}");
				return;
			}

			// Need root. Write to a temp file first (chmod 600), then sudo cp.
			// We intentionally do NOT redirect stdin — sudo needs the terminal
			// TTY to prompt for the password.
			if (!OperatingSystem.IsWindows())
			{
				Console.WriteLine();
				Console.WriteLine($"{dir}/ requires root privileges.");
				Console.WriteLine();

				string tempPath = Path.GetTempFileName();
				try
				{
					await File.WriteAllTextAsync(tempPath, content);
					Chmod600(tempPath);

					// mkdir -p, cp the file, chown back to $SUDO_USER, chmod 600.
					// $SUDO_USER is the user who invoked sudo — the file must be
					// readable by that user so the non-root server process can
					// read it at runtime via DatabaseSecrets.
					string script =
						$"mkdir -p '{dir}' && " +
						$"cp '{tempPath}' '{path}' && " +
						$"chown $SUDO_USER:$SUDO_USER '{path}' && " +
						$"chmod 600 '{path}'";

					var psi = new ProcessStartInfo
					{
						FileName = "sudo",
						Arguments = $"sh -c \"{script}\"",
						UseShellExecute = false,
						CreateNoWindow = true,
					};
					using var proc = Process.Start(psi)!;
					await proc.WaitForExitAsync();

					if (proc.ExitCode != 0)
						throw new IOException($"sudo exited with code {proc.ExitCode}");

					await Log.Info("FishMMOInstaller", $"Secrets written to {path} via sudo");
					Console.WriteLine($"Secrets written to: {path}");
				}
				catch (Exception ex)
				{
					await Log.Error("FishMMOInstaller", $"sudo write failed: {ex.Message}");
				}
				finally
				{
					try { File.Delete(tempPath); } catch { }
				}
			}
		}

		private static bool CanWriteToDirectory(string dir)
		{
			if (!Directory.Exists(dir)) return false;
			try
			{
				string test = Path.Combine(dir, ".fishmmo_write_test");
				File.WriteAllText(test, "");
				File.Delete(test);
				return true;
			}
			catch { return false; }
		}

		private static void Chmod600(string path)
		{
			if (OperatingSystem.IsWindows()) return;
			try { Process.Start("chmod", $"600 {path}")?.WaitForExit(1000); } catch { }
		}

		private static async Task ExportEnvVars(string? username, string? password, string host, int port, string dbName)
		{
			Console.WriteLine("1 : fish shell  (~/.config/fish/conf.d/fishmmo-secrets.fish)");
			Console.WriteLine("2 : systemd / .env (fishmmo-secrets.env in current dir)");
			Console.WriteLine("3 : Windows PowerShell / CMD");
			Console.WriteLine("0 : Back");

			var key = Console.ReadKey(true);
			if (key.Key == ConsoleKey.D0 || key.KeyChar == '0') return;

			var secrets = new Dictionary<string, string>
			{
				["FISHMMO_DB_HOST"] = host,
				["FISHMMO_DB_PORT"] = port.ToString(),
				["FISHMMO_DB_NAME"] = dbName,
			};
			if (!string.IsNullOrEmpty(username)) secrets["FISHMMO_DB_USERNAME"] = username;
			if (!string.IsNullOrEmpty(password)) secrets["FISHMMO_DB_PASSWORD"] = password;

			switch (key.Key)
			{
				case ConsoleKey.D1: await WriteFishSnippet(secrets); break;
				case ConsoleKey.D2: await WriteEnvFile(secrets); break;
				case ConsoleKey.D3: await WriteWindowsSnippets(secrets); break;
			}
			Console.WriteLine("Press any key to continue...");
			Console.ReadKey(true);
		}

		private static async Task WriteFishSnippet(Dictionary<string, string> s)
		{
			string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "fish", "conf.d", "fishmmo-secrets.fish");
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			var sb = new StringBuilder("# FishMMO database secrets\n");
			foreach (var kv in s) if (!string.IsNullOrEmpty(kv.Value)) sb.AppendLine($"set -gx {kv.Key} \"{Esc(kv.Value)}\"");
			await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
			Chmod600(path);
			Console.WriteLine($"Written: {path}");
		}

		private static async Task WriteEnvFile(Dictionary<string, string> s)
		{
			string path = Path.Combine(Environment.CurrentDirectory, "fishmmo-secrets.env");
			var sb = new StringBuilder("# FishMMO database secrets\n# systemd: EnvironmentFile={path}\n");
			foreach (var kv in s) if (!string.IsNullOrEmpty(kv.Value)) sb.AppendLine($"{kv.Key}={EscEnv(kv.Value)}");
			await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
			Chmod600(path);
			Console.WriteLine($"Written: {path}");
		}

		private static async Task WriteWindowsSnippets(Dictionary<string, string> s)
		{
			string p = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			string pp = Path.Combine(p, "Documents", "WindowsPowerShell", "fishmmo-secrets.ps1");
			Directory.CreateDirectory(Path.GetDirectoryName(pp)!);
			var ps = new StringBuilder("# FishMMO database secrets\n");
			foreach (var kv in s) if (!string.IsNullOrEmpty(kv.Value)) ps.AppendLine($"$env:{kv.Key} = \"{EscPS(kv.Value)}\"");
			await File.WriteAllTextAsync(pp, ps.ToString(), Encoding.UTF8);
			string cp = Path.Combine(p, "fishmmo-secrets.cmd");
			var cmd = new StringBuilder("@echo off\nREM FishMMO database secrets\n");
			foreach (var kv in s) if (!string.IsNullOrEmpty(kv.Value)) cmd.AppendLine($"set {kv.Key}={EscCmd(kv.Value)}");
			await File.WriteAllTextAsync(cp, cmd.ToString(), Encoding.UTF8);
			Console.WriteLine($"PowerShell: {pp}");
			Console.WriteLine($"CMD:        {cp}");
		}

		private static string MaskSecret(string? v) => string.IsNullOrEmpty(v) ? "****" : new string('*', Math.Min(v!.Length, 8));
		private static string Esc(string v) => v.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("$", "\\$");
		private static string EscPS(string v) => v.Replace("\"", "`\"").Replace("$", "`$");
		private static string EscCmd(string v) => v.Replace("%", "%%").Replace("^", "^^").Replace("&", "^&").Replace("<", "^<").Replace(">", "^>").Replace("|", "^|");
		private static string EscEnv(string v) { if (v.Any(c => char.IsWhiteSpace(c) || c == '#' || c == '\'')) return "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; return v; }
	}
}
