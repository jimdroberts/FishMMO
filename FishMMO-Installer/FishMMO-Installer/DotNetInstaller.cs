using FishMMO.Logging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace FishMMO.Installer
{
	/// <summary>
	/// Handles installation and verification of the DotNet SDK and DotNet-EF global tool.
	/// Supports Windows and Linux (Arch/CachyOS, Ubuntu).
	/// </summary>
	public static class DotNetInstaller
	{
		/// <summary>
		/// Installs DotNet SDK and DotNet-EF tool if not already installed.
		/// </summary>
		/// <returns>True if installation succeeded or already installed.</returns>
		public static async Task<bool> InstallDotNet()
		{
			bool sdkJustInstalled = false;

			if (!await IsDotNetInstalledAsync())
			{
				if (InstallerProcessHelper.PromptForYesNo($"DotNet {InstallationConstants.DotNetSDKMajorVersion} or later is not installed, would you like to install it?"))
				{
					await Log.Info("FishMMOInstaller", "Installing DotNet...");
					await DownloadAndInstallDotNetAsync();
					await Log.Info("FishMMOInstaller", "DotNet has been installed.");
					sdkJustInstalled = true;
				}
				else
				{
					return false;
				}
			}
			else
			{
				await Log.Info("FishMMOInstaller", "DotNet is already installed.");
			}

			if (!await IsDotNetEFInstalledAsync())
			{
				if (InstallerProcessHelper.PromptForYesNo("DotNet-EF is not installed, would you like to install it?"))
				{
					await Log.Info("FishMMOInstaller", $"Installing DotNet-EF v{InstallationConstants.DotNetEFVersion}...");
					bool efInstalled = await RunDotNetCommandAsync($"tool install --global dotnet-ef --version {InstallationConstants.DotNetEFVersion}");
					if (!efInstalled)
					{
						await Log.Error("FishMMOInstaller", "DotNet-EF installation failed.");
						return false;
					}

					await Log.Info("FishMMOInstaller", "DotNet-EF has been installed.");
					return true;
				}

				return sdkJustInstalled;
			}
			else
			{
				await Log.Info("FishMMOInstaller", "DotNet-EF is already installed.");
				return true;
			}
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
		/// Downloads and installs DotNet SDK for the current OS.
		/// On Windows, downloads and runs the official EXE installer.
		/// On Linux, downloads and runs the dotnet-install.sh script.
		/// </summary>
		private static async Task DownloadAndInstallDotNetAsync()
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				string installerPath = await InstallerProcessHelper.DownloadFileAsync(
					InstallationConstants.DotNetSDKUrl,
					InstallationConstants.DotNetSDKFileName);

				try
				{
					InstallerProcessHelper.LogElevatedProcessEnvironmentWarning("DotNet SDK installer");

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
						await Log.Error("FishMMOInstaller", "Failed to start DotNet installer process.");
						return;
					}
					await process.WaitForExitAsync();

					int exitCode = process.ExitCode;
					if (exitCode == 0)
					{
						await Log.Info("FishMMOInstaller", "DotNet installation successful.");
					}
					else
					{
						await Log.Error("FishMMOInstaller", $"DotNet installation failed with exit code {exitCode}.");
					}
				}
				catch (Exception ex)
				{
					await Log.Error("FishMMOInstaller", "Error installing DotNet", ex);
				}
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				string shScriptFile = Path.Combine(InstallerProcessHelper.GetWorkingDirectory(), InstallationConstants.DotNetInstallScriptFileName);

				var scriptContent = await InstallerProcessHelper.SharedHttpClient.GetStringAsync(InstallationConstants.DotNetInstallScriptUrl);
				await File.WriteAllTextAsync(shScriptFile, scriptContent);

				await InstallerProcessHelper.RunProcessAsync("chmod", $"+x \"{shScriptFile}\"");

				await InstallerProcessHelper.RunProcessAsync("/bin/bash",
					$"\"{shScriptFile}\" --version {InstallationConstants.DotNetSDKVersion}",
					(e, o, err) =>
					{
						if (e != 0)
						{
							throw new Exception($"Shell script failed with exit code {e}: {err}");
						}
						return true;
					});
			}
			else
			{
				throw new PlatformNotSupportedException("Unsupported operating system. Only Windows and Linux are supported.");
			}
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

			return await RunDotNetCommandAsync(
				$"ef migrations add {migrationName} -p \"{InstallationConstants.ProjectPath}\" -s \"{InstallationConstants.StartupProject}\" --output-dir \"{InstallationConstants.MigrationsOutputDirectory}\"");
		}

		/// <summary>
		/// Runs a dotnet ef database update command to apply pending migrations.
		/// </summary>
		/// <returns>True if the command succeeded, otherwise false.</returns>
		public static async Task<bool> RunEFDatabaseUpdateAsync()
		{
			return await RunDotNetCommandAsync(
				$"ef database update -p \"{InstallationConstants.ProjectPath}\" -s \"{InstallationConstants.StartupProject}\"");
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
			string installerPath;
			try
			{
				installerPath = await InstallerProcessHelper.DownloadFileAsync(
					InstallationConstants.AspNetRuntimeWindowsUrl,
					InstallationConstants.AspNetRuntimeWindowsFileName);
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
		/// Tries the system package manager first; falls back to dotnet-install.sh
		/// with --runtime aspnetcore when no supported package manager is found.
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

			var pm = await InstallerProcessHelper.DetectLinuxPackageManagerAsync(packages);
			if (pm.HasValue)
			{
				(string updateCmd, string installCmd, string managerName) = pm.Value;
				await Log.Info("FishMMOInstaller", $"Using package manager: {managerName}");

				(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();

				bool updated = await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, updateCmd,
					$"Package database update failed ({managerName}).");
				if (!updated)
				{
					await Log.Warning("FishMMOInstaller", "Package database update failed; attempting install anyway.");
				}

				bool installed = await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, installCmd,
					$"ASP.NET Core runtime installation failed via {managerName}.");
				if (installed)
				{
					await Log.Info("FishMMOInstaller", "ASP.NET Core runtime installed successfully.");
					return true;
				}
				await Log.Warning("FishMMOInstaller", "Package manager install failed. Falling back to dotnet-install.sh.");
			}
			else
			{
				await Log.Warning("FishMMOInstaller", "No supported package manager found. Falling back to dotnet-install.sh.");
			}

			// Fallback: dotnet-install.sh --runtime aspnetcore
			string shScriptFile = Path.Combine(InstallerProcessHelper.GetWorkingDirectory(), InstallationConstants.DotNetInstallScriptFileName);

			if (!File.Exists(shScriptFile))
			{
				string scriptContent = await InstallerProcessHelper.SharedHttpClient.GetStringAsync(InstallationConstants.DotNetInstallScriptUrl);
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
			{
				await Log.Info("FishMMOInstaller", "ASP.NET Core runtime installed via dotnet-install.sh.");
			}
			else
			{
				await Log.Error("FishMMOInstaller", "ASP.NET Core runtime installation failed.");
			}
			return fallbackResult;
		}
	}
}