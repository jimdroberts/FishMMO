using FishMMO.Logging;
using System.Diagnostics;
using System.Text;

namespace FishMMO.Installer
{
	/// <summary>
	/// The write path for the platform secrets file, and the shell snippet exporters that share it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The interactive database-secrets wizard that used to live here has moved into
	/// <see cref="SecretsEnvironmentInstaller"/>, which edits every <c>FISHMMO_*</c> variable in one
	/// pass. It had to move: two wizards writing the same file each rewrote it from scratch and
	/// emitted only their own keys, so configuring one silently deleted the other's credentials.
	/// </para>
	/// <para>
	/// What remains here is the part that knows how to put a file into <c>/etc/fishmmo/</c> —
	/// direct write when the directory allows it, sudo when it does not, chmod 600 and chown back
	/// to the invoking user either way — plus the snippet writers for machines that set the
	/// variables in a shell profile instead.
	/// </para>
	/// </remarks>
	public static class DatabaseSecretsInstaller
	{
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

		/// <summary>Writes the given variables as a fish shell profile snippet.</summary>
		internal static async Task WriteFishSnippet(Dictionary<string, string> s)
		{
			string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "fish", "conf.d", "fishmmo-secrets.fish");
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			var sb = new StringBuilder("# FishMMO secrets and environment\n");
			foreach (var kv in s) if (!string.IsNullOrEmpty(kv.Value)) sb.AppendLine($"set -gx {kv.Key} \"{Esc(kv.Value)}\"");
			await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
			Chmod600(path);
			Console.WriteLine($"Written: {path}");
		}

		/// <summary>Writes the given variables as a systemd-style environment file.</summary>
		internal static async Task WriteEnvFile(Dictionary<string, string> s)
		{
			string path = Path.Combine(Environment.CurrentDirectory, "fishmmo-secrets.env");
			var sb = new StringBuilder($"# FishMMO secrets and environment\n# systemd: EnvironmentFile={path}\n");
			foreach (var kv in s) if (!string.IsNullOrEmpty(kv.Value)) sb.AppendLine($"{kv.Key}={EscEnv(kv.Value)}");
			await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
			Chmod600(path);
			Console.WriteLine($"Written: {path}");
		}

		/// <summary>Writes the given variables as PowerShell and CMD snippets.</summary>
		internal static async Task WriteWindowsSnippets(Dictionary<string, string> s)
		{
			string p = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			string pp = Path.Combine(p, "Documents", "WindowsPowerShell", "fishmmo-secrets.ps1");
			Directory.CreateDirectory(Path.GetDirectoryName(pp)!);
			var ps = new StringBuilder("# FishMMO secrets and environment\n");
			foreach (var kv in s) if (!string.IsNullOrEmpty(kv.Value)) ps.AppendLine($"$env:{kv.Key} = \"{EscPS(kv.Value)}\"");
			await File.WriteAllTextAsync(pp, ps.ToString(), Encoding.UTF8);
			string cp = Path.Combine(p, "fishmmo-secrets.cmd");
			var cmd = new StringBuilder("@echo off\nREM FishMMO secrets and environment\n");
			foreach (var kv in s) if (!string.IsNullOrEmpty(kv.Value)) cmd.AppendLine($"set {kv.Key}={EscCmd(kv.Value)}");
			await File.WriteAllTextAsync(cp, cmd.ToString(), Encoding.UTF8);
			Console.WriteLine($"PowerShell: {pp}");
			Console.WriteLine($"CMD:        {cp}");
		}

		private static string Esc(string v) => v.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("$", "\\$");
		private static string EscPS(string v) => v.Replace("\"", "`\"").Replace("$", "`$");
		private static string EscCmd(string v) => v.Replace("%", "%%").Replace("^", "^^").Replace("&", "^&").Replace("<", "^<").Replace(">", "^>").Replace("|", "^|");
		private static string EscEnv(string v) { if (v.Any(c => char.IsWhiteSpace(c) || c == '#' || c == '\'')) return "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; return v; }
	}
}
