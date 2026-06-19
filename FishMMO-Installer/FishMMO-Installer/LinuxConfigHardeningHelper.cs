using FishMMO.Logging;

namespace FishMMO.Installer
{
	/// <summary>
	/// Shared helpers for reading and atomically writing Linux system configuration files
	/// that are owned by privileged users (root, postgres, pgbouncer, ...). All writes go
	/// through a temp file in /tmp followed by <c>sudo install -o &lt;owner&gt; -g &lt;group&gt; -m &lt;mode&gt;</c>
	/// which provides atomic rename + ownership/mode in a single privileged step.
	/// </summary>
	public static class LinuxConfigHardeningHelper
	{
		/// <summary>
		/// Marker comment string appended to every block this installer adds to a system
		/// configuration file, allowing future runs to detect prior edits and stay idempotent.
		/// </summary>
		public const string ManagedMarker = "# fishmmo-managed";

		/// <summary>
		/// Suffix used for the one-time backup of any system configuration file before
		/// FishMMO-Installer modifies it.
		/// </summary>
		public const string BackupSuffix = ".pre-fishmmo.bak";

		/// <summary>
		/// Reads a privileged file via <c>sudo cat</c>. Returns null on failure.
		/// </summary>
		public static async Task<string?> SudoReadAsync(string path)
		{
			(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();
			string captured = string.Empty;
			bool ok = await InstallerProcessHelper.RunProcessAsync(
				shell,
				$"{argPrefix} \"sudo cat '{path}'\"",
				(exitCode, output, error) =>
				{
					captured = output ?? string.Empty;
					return exitCode == 0;
				});
			return ok ? captured : null;
		}

		/// <summary>
		/// Creates a one-time backup at <c>path + BackupSuffix</c> owned by the same uid/gid
		/// as the original. Returns true if the backup already existed or was created.
		/// </summary>
		public static async Task<bool> EnsureBackupAsync(string path)
		{
			(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();
			string backupPath = path + BackupSuffix;
			string command = $"sudo test -e '{backupPath}' || sudo cp -a '{path}' '{backupPath}'";
			return await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, command,
				$"Failed to create backup of '{path}'.");
		}

		/// <summary>
		/// Writes <paramref name="content"/> to <paramref name="destinationPath"/> atomically via
		/// a temp file and <c>sudo install</c> with the requested ownership and mode.
		/// </summary>
		/// <param name="content">Full new file contents.</param>
		/// <param name="destinationPath">Destination path (will be overwritten).</param>
		/// <param name="owner">Unix owner (e.g. <c>postgres</c>, <c>root</c>).</param>
		/// <param name="group">Unix group (e.g. <c>postgres</c>, <c>pgbouncer</c>).</param>
		/// <param name="mode">Octal mode string (e.g. <c>0600</c>, <c>0640</c>, <c>0644</c>).</param>
		public static async Task<bool> SudoInstallAsync(string content, string destinationPath, string owner, string group, string mode)
		{
			string tempPath = Path.Combine(Path.GetTempPath(),
				$"fishmmo-{Path.GetFileName(destinationPath)}-{Guid.NewGuid():N}");
			try
			{
				await File.WriteAllTextAsync(tempPath, content);

				(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();
				string command = $"sudo install -o {owner} -g {group} -m {mode} '{tempPath}' '{destinationPath}'";
				return await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, command,
					$"Failed to atomically install '{destinationPath}'.");
			}
			catch (Exception ex)
			{
				await Log.Error("FishMMOInstaller", $"Failed to write temp file for '{destinationPath}'", ex);
				return false;
			}
			finally
			{
				try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best-effort */ }
			}
		}
	}
}