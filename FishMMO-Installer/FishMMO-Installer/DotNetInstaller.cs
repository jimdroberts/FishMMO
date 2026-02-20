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
				if (InstallerProcessHelper.PromptForYesNo("DotNet 8 is not installed, would you like to install it?"))
				{
					InstallerProcessHelper.Log("Installing DotNet...");
					await DownloadAndInstallDotNetAsync();
					InstallerProcessHelper.Log("DotNet has been installed.");
					sdkJustInstalled = true;
				}
				else
				{
					return false;
				}
			}
			else
			{
				InstallerProcessHelper.Log("DotNet is already installed.");
			}

			if (!await IsDotNetEFInstalledAsync())
			{
				if (InstallerProcessHelper.PromptForYesNo("DotNet-EF is not installed, would you like to install it?"))
				{
					InstallerProcessHelper.Log($"Installing DotNet-EF v{InstallationConstants.DotNetEFVersion}...");
					bool efInstalled = await RunDotNetCommandAsync($"tool install --global dotnet-ef --version {InstallationConstants.DotNetEFVersion}");
					if (!efInstalled)
					{
						InstallerProcessHelper.Log("DotNet-EF installation failed.");
						return false;
					}

					InstallerProcessHelper.Log("DotNet-EF has been installed.");
					return true;
				}

				return sdkJustInstalled;
			}
			else
			{
				InstallerProcessHelper.Log("DotNet-EF is already installed.");
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

				// Each line looks like: "8.0.302 [/usr/share/dotnet/sdk]"
				using var reader = new StringReader(o);
				string? line;
				while ((line = reader.ReadLine()) != null)
				{
					if (line.StartsWith(InstallationConstants.DotNetSDKMajorVersion + ".", StringComparison.Ordinal))
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
						InstallerProcessHelper.Log("Failed to start DotNet installer process.");
						return;
					}
					await process.WaitForExitAsync();

					int exitCode = process.ExitCode;
					if (exitCode == 0)
					{
						InstallerProcessHelper.Log("DotNet installation successful.");
					}
					else
					{
						InstallerProcessHelper.Log($"DotNet installation failed with exit code {exitCode}.");
					}
				}
				catch (Exception ex)
				{
					InstallerProcessHelper.Log($"Error installing DotNet: {ex.Message}");
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
				InstallerProcessHelper.Log($"Error checking dotnet-ef tool: {ex.Message}");
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
						InstallerProcessHelper.Log($"Process Error: {error}");
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
				InstallerProcessHelper.Log($"DotNet command 'dotnet {arguments}' failed.");
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
				InstallerProcessHelper.Log("Invalid migration name. Use alphanumeric characters only and start with a letter.");
				return false;
			}

			return await RunDotNetCommandAsync(
				$"ef migrations add {migrationName} -p \"{InstallationConstants.ProjectPath}\" -s \"{InstallationConstants.StartupProject}\"");
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
	}
}