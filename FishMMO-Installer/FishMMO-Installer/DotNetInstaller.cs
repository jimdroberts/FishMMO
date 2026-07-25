using FishMMO.Logging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace FishMMO.Installer
{
	/// <summary>
	/// Handles ASP.NET Core Runtime installation, dotnet-ef global tool installation,
	/// and EF Core migration/database-update commands. Supports Windows and Linux.
	/// </summary>
	public static class DotNetInstaller
	{
		/// <summary>
		/// Installs the dotnet-ef global tool if not already present.
		/// The .NET SDK itself is a prerequisite for running this installer,
		/// so SDK installation is not offered here.
		/// </summary>
		/// <returns>True if dotnet-ef is installed or was already present.</returns>
		public static async Task<bool> InstallDotNetEF()
		{
			if (await IsDotNetEFInstalledAsync())
			{
				await Log.Info("FishMMOInstaller", "DotNet-EF is already installed.");
				return true;
			}

			if (!InstallerProcessHelper.PromptForYesNo("DotNet-EF is not installed, would you like to install it?"))
				return false;

			await Log.Info("FishMMOInstaller", $"Installing DotNet-EF v{InstallationConstants.DotNetEFVersion}...");
			bool efInstalled = await RunDotNetCommandAsync($"tool install --global dotnet-ef --version {InstallationConstants.DotNetEFVersion}");
			if (!efInstalled)
			{
				await Log.Error("FishMMOInstaller", "DotNet-EF installation failed.");
				return false;
			}

			// Verify the tool is functional
			bool verified = await RunDotNetCommandAsync("ef --version", (exitCode, output, error) =>
				output.Contains("Entity Framework Core", StringComparison.OrdinalIgnoreCase));

			if (!verified)
				await Log.Warning("FishMMOInstaller", "dotnet-ef was installed but 'dotnet ef --version' failed. Ensure ~/.dotnet/tools is in your PATH.");
			else
				await Log.Info("FishMMOInstaller", "dotnet-ef verified successfully.");

			await Log.Info("FishMMOInstaller", "DotNet-EF has been installed.");
			return true;
		}

		/// <summary>
		/// Checks if a compatible DotNet SDK is installed.
		/// Uses 'dotnet --list-sdks' to verify SDK availability for build/migration tasks.
		/// </summary>
		/// <returns>True if installed, otherwise false.</returns>
		public static async Task<bool> IsDotNetInstalledAsync()
		{
			return await InstallerProcessHelper.RunDotNetProcessAsync("--list-sdks", (e, o, err) =>
			{
				if (e != 0) return false;

				// Parse the required minimum major version (e.g. "8" from "8.0").
				if (!int.TryParse(InstallationConstants.DotNetSDKMajorVersion.Split('.')[0], out int requiredMajor))
					return false;

				// Each line looks like: "8.0.302 [/usr/share/dotnet/sdk]" or "10.0.104 [/usr/share/dotnet/sdk]"
				using var reader = new StringReader(o);
				string? line;
				while ((line = reader.ReadLine()) != null)
				{
					string versionPart = line.Split(' ')[0];
					if (int.TryParse(versionPart.Split('.')[0], out int installedMajor) && installedMajor >= requiredMajor)
					{
						return true;
					}
				}
				return false;
			});
		}

		/// <summary>
		/// Checks if DotNet-EF tool is installed globally.
		/// </summary>
		/// <returns>True if installed, otherwise false.</returns>
		public static async Task<bool> IsDotNetEFInstalledAsync()
		{
			try
			{
				return await RunDotNetCommandAsync(
					"tool list --global",
					(e, o, err) => o.Contains("dotnet-ef", StringComparison.OrdinalIgnoreCase));
			}
			catch (Exception ex)
			{
				await Log.Error("FishMMOInstaller", "Error checking dotnet-ef tool", ex);
				return false;
			}
		}

		/// <summary>
		/// Runs a dotnet command asynchronously, handling environment setup for Linux.
		/// Ensures ~/.dotnet/tools is in PATH and DOTNET_ROOT is set on first invocation.
		/// </summary>
		/// <param name="arguments">DotNet command arguments.</param>
		/// <param name="customProcessResult">Optional custom result handler receiving (exitCode, stdout, stderr).</param>
		/// <returns>True if command succeeded, otherwise false.</returns>
		public static async Task<bool> RunDotNetCommandAsync(string arguments, Func<int, string, string, bool>? customProcessResult = null)
		{
			Console.WriteLine("Running DotNet Command: \r\n" +
							  "dotnet " + arguments);

			bool success = await InstallerProcessHelper.RunDotNetProcessAsync(arguments,
				(exitCode, output, error) =>
				{
					if (!string.IsNullOrWhiteSpace(output))
					{
						Console.WriteLine(output);
					}
					if (!string.IsNullOrWhiteSpace(error))
					{
						_ = Log.Warning("FishMMOInstaller", $"Process Error: {error}");
					}

					if (customProcessResult != null)
					{
						return customProcessResult.Invoke(exitCode, output, error);
					}
					else
					{
						return exitCode == 0;
					}
				});

			if (!success)
			{
				await Log.Error("FishMMOInstaller", $"DotNet command 'dotnet {arguments}' failed.");
			}
			return success;
		}

		/// <summary>
		/// Runs a dotnet ef migrations add command for the given migration name.
		/// Generated migration files land in the shared
		/// <see cref="InstallationConstants.MigrationsOutputDirectory"/> at the
		/// monorepo root rather than inside the FishMMO-DB project directory.
		/// </summary>
		/// <param name="migrationName">Name of the migration to create.</param>
		/// <returns>True if the command succeeded, otherwise false.</returns>
		public static async Task<bool> RunEFMigrationAsync(string migrationName)
		{
			if (!Regex.IsMatch(migrationName, "^[A-Za-z][A-Za-z0-9]*$"))
			{
				await Log.Warning("FishMMOInstaller", "Invalid migration name. Use alphanumeric characters only and start with a letter.");
				return false;
			}

			// Target the source project.  Migration files are written to the
			// monorepo-level Migrations dir (not the project dir), so there is no
			// risk of polluting the source tree.
			string root = InstallationConstants.FishMMOMonorepoRoot;
			string projectPath = Path.GetFullPath(Path.Combine(root, "FishMMO-Database", "FishMMO-DB", "FishMMO-DB.csproj"));
			string startupProject = Path.GetFullPath(Path.Combine(root, "FishMMO-Database", "FishMMO-DB-Migrator", "FishMMO-DB-Migrator.csproj"));

			return await RunDotNetCommandAsync(
				$"ef migrations add {migrationName} -p \"{projectPath}\" -s \"{startupProject}\" --output-dir \"{InstallationConstants.MigrationsOutputDirectory}\"");
		}

		/// <summary>
		/// Runs a dotnet ef database update command to apply pending migrations.
		/// </summary>
		/// <returns>True if the command succeeded, otherwise false.</returns>
		public static async Task<bool> RunEFDatabaseUpdateAsync(string? superuserConnectionString = null)
		{
			string root = InstallationConstants.FishMMOMonorepoRoot;
			string projectPath = Path.GetFullPath(Path.Combine(root, "FishMMO-Database", "FishMMO-DB", "FishMMO-DB.csproj"));
			string startupProject = Path.GetFullPath(Path.Combine(root, "FishMMO-Database", "FishMMO-DB-Migrator", "FishMMO-DB-Migrator.csproj"));

			string connectionArg = string.IsNullOrEmpty(superuserConnectionString)
				? ""
				: $" --connection \"{superuserConnectionString}\"";

			return await RunDotNetCommandAsync(
				$"ef database update -p \"{projectPath}\" -s \"{startupProject}\"{connectionArg}");
		}

		/// <summary>
		/// Installs the ASP.NET Core runtime for the current platform.
		/// On Windows, downloads and runs the official Hosting Bundle EXE.
		/// On Linux (Arch/CachyOS, Debian/Ubuntu, RHEL/Fedora), uses the system package manager
		/// or falls back to dotnet-install.sh --runtime aspnetcore.
		/// </summary>
		/// <returns>True if installation succeeded or runtime was already present.</returns>
		public static async Task<bool> InstallAspNetRuntime()
		{
			if (await IsAspNetRuntimeInstalledAsync())
			{
				await Log.Info("FishMMOInstaller", $"ASP.NET Core {InstallationConstants.AspNetRuntimeMajorVersion} runtime is already installed.");
				return true;
			}

			if (!InstallerProcessHelper.PromptForYesNo($"ASP.NET Core {InstallationConstants.AspNetRuntimeMajorVersion} runtime is not installed. Install it now?"))
			{
				return false;
			}

			await Log.Info("FishMMOInstaller", "Installing ASP.NET Core runtime...");

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				return await InstallAspNetRuntimeWindows();
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				return await InstallAspNetRuntimeLinux();
			}
			else
			{
				await Log.Warning("FishMMOInstaller", "Unsupported operating system for ASP.NET Core runtime installation.");
				return false;
			}
		}

		/// <summary>
		/// Returns true if a compatible ASP.NET Core runtime is present.
		/// </summary>
		public static async Task<bool> IsAspNetRuntimeInstalledAsync()
		{
			return await InstallerProcessHelper.RunDotNetProcessAsync("--list-runtimes", (e, o, err) =>
			{
				if (e != 0) return false;

				if (!int.TryParse(InstallationConstants.AspNetRuntimeMajorVersion.Split('.')[0], out int requiredMajor))
					return false;

				using var reader = new StringReader(o);
				string? line;
				while ((line = reader.ReadLine()) != null)
				{
					// Line format: "Microsoft.AspNetCore.App 8.0.16 [/usr/share/dotnet/shared/...]"
					if (!line.StartsWith("Microsoft.AspNetCore.App", StringComparison.OrdinalIgnoreCase))
						continue;

					string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
					if (parts.Length < 2) continue;

					if (int.TryParse(parts[1].Split('.')[0], out int installedMajor) && installedMajor >= requiredMajor)
						return true;
				}
				return false;
			});
		}

		/// <summary>
		/// Downloads and runs the ASP.NET Core Windows Hosting Bundle installer silently.
		/// </summary>
		private static async Task<bool> InstallAspNetRuntimeWindows()
		{
			string? installerPath = null;
			string? expectedSha512 = null;
			try
			{
				// Try dynamic URL + hash resolution first; fall back to hardcoded constant
				string? resolvedUrl = null;
				try
				{
					(resolvedUrl, expectedSha512) = await DotNetReleaseHelper.ResolveAspNetRuntimeInstallerUrlAsync(
						InstallationConstants.DotNetRuntimeChannel);
					if (!string.IsNullOrWhiteSpace(resolvedUrl))
					{
						await Log.Info("FishMMOInstaller",
							$"Resolved latest ASP.NET Core Hosting Bundle URL for channel {InstallationConstants.DotNetRuntimeChannel}");
						if (!string.IsNullOrWhiteSpace(expectedSha512))
							await Log.Debug("FishMMOInstaller", "SHA512 hash obtained from release metadata for integrity verification.");
					}
				}
				catch
				{
					await Log.Warning("FishMMOInstaller",
						"Could not resolve ASP.NET Core runtime URL dynamically; using hardcoded fallback.");
				}

				// Try resolved URL first, then fall back to hardcoded URL if download fails
				foreach (string? url in new[] { resolvedUrl, InstallationConstants.AspNetRuntimeWindowsUrl })
				{
					if (string.IsNullOrWhiteSpace(url)) continue;

					installerPath = await DownloadHelper.DownloadFileWithProgressAsync(
						url,
						InstallationConstants.AspNetRuntimeWindowsFileName,
						new DownloadHelper.ConsoleProgress());

					if (installerPath != null) break;

					await Log.Warning("FishMMOInstaller",
						$"Download failed from {url}; trying fallback URL.");
				}

				// Verify SHA512 against Microsoft's published hash (when available)
				if (installerPath != null && !string.IsNullOrWhiteSpace(expectedSha512))
				{
					using var stream = File.OpenRead(installerPath);
					byte[] actualHash = System.Security.Cryptography.SHA512.HashData(stream);
					string actualHex = Convert.ToHexString(actualHash).ToLowerInvariant();
					if (!string.Equals(actualHex, expectedSha512, StringComparison.OrdinalIgnoreCase))
					{
						await Log.Error("FishMMOInstaller",
							$"SHA512 mismatch for ASP.NET Core Hosting Bundle! The download may be corrupted or tampered. Expected: {expectedSha512[..16]}..., Actual: {actualHex[..16]}...");
						try { File.Delete(installerPath); } catch { /* best-effort */ }
						return false;
					}
					await Log.Info("FishMMOInstaller", "SHA512 verification passed (verified against Microsoft release metadata).");
				}

				if (installerPath == null)
				{
					await Log.Error("FishMMOInstaller", "Failed to download ASP.NET Core Hosting Bundle from all sources.");
					return false;
				}
			}
			catch (Exception ex)
			{
				await Log.Error("FishMMOInstaller", "Failed to download ASP.NET Core Hosting Bundle", ex);
				return false;
			}

			try
			{
				InstallerProcessHelper.LogElevatedProcessEnvironmentWarning("ASP.NET Core Hosting Bundle installer");

				ProcessStartInfo startInfo = new ProcessStartInfo
				{
					FileName = installerPath,
					Arguments = "/install /quiet /norestart",
					WorkingDirectory = Path.GetDirectoryName(installerPath) ?? InstallerProcessHelper.GetWorkingDirectory(),
					UseShellExecute = true,
					Verb = "runas"
				};

				Process? process = Process.Start(startInfo);
				if (process == null)
				{
					await Log.Error("FishMMOInstaller", "Failed to start ASP.NET Core Hosting Bundle installer process.");
					return false;
				}
				await process.WaitForExitAsync();

				if (process.ExitCode == 0)
				{
					await Log.Info("FishMMOInstaller", "ASP.NET Core Hosting Bundle installation successful.");
					return true;
				}
				else
				{
					await Log.Error("FishMMOInstaller", $"ASP.NET Core Hosting Bundle installer exited with code {process.ExitCode}.");
					return false;
				}
			}
			catch (Exception ex)
			{
				await Log.Error("FishMMOInstaller", "Error installing ASP.NET Core Hosting Bundle", ex);
				return false;
			}
		}

		/// <summary>
		/// Installs the ASP.NET Core runtime on Linux.
		/// Tries the system package manager first; falls back to a direct tarball
		/// download with SHA512 verification against Microsoft's release metadata.
		/// </summary>
		private static async Task<bool> InstallAspNetRuntimeLinux()
		{
			var packages = new Dictionary<string, string>
			{
				["pacman"] = $"aspnet-runtime-{InstallationConstants.AspNetRuntimeMajorVersion}",
				["apt-get"] = $"aspnetcore-runtime-{InstallationConstants.AspNetRuntimeMajorVersion}",
				["dnf"] = $"aspnetcore-runtime-{InstallationConstants.AspNetRuntimeMajorVersion}",
				["yum"] = $"aspnetcore-runtime-{InstallationConstants.AspNetRuntimeMajorVersion}",
			};

			var pm = await LinuxPackageManagerHelper.DetectAsync(packages);
			if (pm != null)
			{
				await Log.Info("FishMMOInstaller", $"Using package manager: {pm.ManagerName}");

				(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();

				bool updated = await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, pm.UpdateCommand,
					$"Package database update failed ({pm.ManagerName}).");
				if (!updated)
				{
					await Log.Warning("FishMMOInstaller", "Package database update failed; attempting install anyway.");
				}

				bool installed = await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, pm.InstallCommand,
					$"ASP.NET Core runtime installation failed via {pm.ManagerName}.");
				if (installed)
				{
					await Log.Info("FishMMOInstaller", "ASP.NET Core runtime installed successfully.");
					return true;
				}
				await Log.Warning("FishMMOInstaller", "Package manager install failed. Falling back to direct download with SHA512 verification.");
			}
			else
			{
				await Log.Warning("FishMMOInstaller", "No supported package manager found. Using direct download with SHA512 verification.");
			}

			// Fallback: resolve Linux tarball URL + SHA512 from Microsoft metadata, download, verify, extract
			try
			{
				(string? tarballUrl, string? expectedSha512) = await DotNetReleaseHelper.ResolveLinuxRuntimeUrlAsync(
					InstallationConstants.DotNetRuntimeChannel);

				string? fallbackUrl = null;
				if (string.IsNullOrWhiteSpace(tarballUrl))
				{
					await Log.Warning("FishMMOInstaller", "Could not resolve Linux runtime URL dynamically. Falling back to dotnet-install.sh.");
				}
				else
				{
					string tarballFile = $"aspnetcore-runtime-{InstallationConstants.AspNetRuntimeLinuxVersion}-linux-x64.tar.gz";
					string? downloaded = await DownloadHelper.DownloadFileWithProgressAsync(
						tarballUrl, tarballFile, new DownloadHelper.ConsoleProgress());

					if (downloaded != null && !string.IsNullOrWhiteSpace(expectedSha512))
					{
						using var stream = File.OpenRead(downloaded);
						byte[] actualHash = System.Security.Cryptography.SHA512.HashData(stream);
						string actualHex = Convert.ToHexString(actualHash).ToLowerInvariant();
						if (!string.Equals(actualHex, expectedSha512, StringComparison.OrdinalIgnoreCase))
						{
							await Log.Error("FishMMOInstaller",
								$"SHA512 mismatch for Linux ASP.NET Core Runtime! Download may be corrupted. Expected: {expectedSha512[..16]}..., Actual: {actualHex[..16]}...");
							try { File.Delete(downloaded); } catch { }
							fallbackUrl = downloaded; // signal fallback needed
						}
						else
						{
							await Log.Info("FishMMOInstaller", "SHA512 verification passed for Linux runtime tarball.");
							// Extract to ~/.dotnet/
							string dotnetRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet");
							Directory.CreateDirectory(dotnetRoot);
							await InstallerProcessHelper.RunProcessAsync("tar", $"-xzf \"{downloaded}\" -C \"{dotnetRoot}\"",
								(exitCode, _, err) => exitCode == 0);
							await Log.Info("FishMMOInstaller", $"ASP.NET Core runtime extracted to {dotnetRoot}.");
							try { File.Delete(downloaded); } catch { }
							return true;
						}
					}
				}

				// Absolute last resort: dotnet-install.sh (no hash verification available)
				await Log.Warning("FishMMOInstaller", "Using dotnet-install.sh as final fallback (no hash verification).");
				string shScriptFile = Path.Combine(InstallerProcessHelper.GetWorkingDirectory(), InstallationConstants.DotNetInstallScriptFileName);
				if (!File.Exists(shScriptFile))
				{
					string scriptContent = await DownloadHelper.Client.GetStringAsync(InstallationConstants.DotNetInstallScriptUrl);
					await File.WriteAllTextAsync(shScriptFile, scriptContent);
					await InstallerProcessHelper.RunProcessAsync("chmod", $"+x \"{shScriptFile}\"");
				}

				bool fallbackResult = await InstallerProcessHelper.RunProcessAsync("/bin/bash",
					$"\"{shScriptFile}\" --runtime aspnetcore --version {InstallationConstants.AspNetRuntimeLinuxVersion}",
					(e, o, err) =>
					{
						if (e != 0)
						{
							_ = Log.Warning("FishMMOInstaller", $"dotnet-install.sh fallback failed: {err}");
							return false;
						}
						return true;
					});

				if (fallbackResult)
					await Log.Info("FishMMOInstaller", "ASP.NET Core runtime installed via dotnet-install.sh.");
				else
					await Log.Error("FishMMOInstaller", "ASP.NET Core runtime installation failed.");
				return fallbackResult;
			}
			catch (Exception ex)
			{
				await Log.Error("FishMMOInstaller", "Error installing ASP.NET Core runtime on Linux", ex);
				return false;
			}
		}
	}
}