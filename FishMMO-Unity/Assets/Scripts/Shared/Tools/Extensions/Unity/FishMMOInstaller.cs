using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using FishMMO.Database;
using Npgsql;

namespace FishMMO.Shared
{
	/// <summary>
	/// Contains constants for installation URLs, filenames, and default configuration values for FishMMO dependencies.
	/// </summary>
	public static class InstallationConstants
	{
		public const string DotNetSDKUrl = "https://download.visualstudio.microsoft.com/download/pr/b6f19ef3-52ca-40b1-b78b-0712d3c8bf4d/426bd0d376479d551ce4d5ac0ecf63a5/dotnet-sdk-8.0.302-win-x64.exe";
		public const string DotNetSDKFileName = "dotnet-sdk-8.0.302-win-x64.exe";
		public const string DotNetInstallScriptUrl = "https://dot.net/v1/dotnet-install.sh";
		public const string DotNetInstallScriptFileName = "dotnet-install.sh";
		public const string DotNetSDKVersion = "8.0.302"; // Full version string
		public const string DotNetSDKMajorVersion = "8.0"; // Major version for checks
		public const string DotNetEFVersion = "5.0.17";

		public const string PostgreSQLWindowsInstallerUrl = @"https://sbp.enterprisedb.com/getfile.jsp?fileid=1259105";
		public const string PostgreSQLWindowsInstallerFileName = "PostgreSQLInstaller.exe";
		public const string PostgreSQLDefaultSuperuser = "postgres";
		public const string PostgreSQLDefaultAdminDb = "postgres"; // A database to connect to for admin tasks

		// NGINX Constants
		public const string NGINXWindowsDownloadUrl = "http://nginx.org/download/nginx-1.24.0.zip"; // Check for the latest stable version
		public const string NGINXWindowsFileName = "nginx-1.24.0.zip";
		public const string NGINXWindowsExtractPath = "C:\\nginx"; // Default extraction path on Windows

		// Visual Studio Build Tools Constants
		public const string VSBuildToolsUrl = "https://aka.ms/vs/17/release/vs_buildtools.exe";
		public const string VSBuildToolsFileName = "vs_buildtools.exe";
	}

	/// <summary>
	/// Provides installation and configuration utilities for FishMMO dependencies and database setup.
	/// </summary>
	public class FishMMOInstaller : MonoBehaviour
	{
		/// <summary>
		/// Stores the loaded application settings from appsettings.json.
		/// </summary>
		private AppSettings _appSettings;

		/// <summary>
		/// Unity Awake event. Loads appsettings.json and displays the installer menu loop.
		/// </summary>
		private async void Awake()
		{
			// Load appsettings.json once at the start
			string workingDirectory = GetWorkingDirectory();
			string appSettingsPath = Path.Combine(workingDirectory, "appsettings.json");

			if (File.Exists(appSettingsPath))
			{
				try
				{
					string jsonContent = File.ReadAllText(appSettingsPath);
					_appSettings = JsonUtility.FromJson<AppSettings>(jsonContent);
					Console.WriteLine("appsettings.json loaded successfully.");
				}
				catch (Exception ex)
				{
					Log($"Error loading appsettings.json: {ex.Message}. Database operations may be affected.");
					_appSettings = new AppSettings(); // Initialize to prevent NullReferenceException
				}
			}
			else
			{
				Log("appsettings.json file not found. Database operations will be limited or unavailable.");
				_appSettings = new AppSettings(); // Initialize to prevent NullReferenceException
			}

			while (true)
			{
				Console.Clear();
				Console.WriteLine("Welcome to the FishMMO Installer Tool.");
				Console.WriteLine("Press a key (1-9):");
				Console.WriteLine("1 : Install DotNet");
				Console.WriteLine("2 : Install Visual Studio Build Tools (Windows Only)");
				Console.WriteLine("3 : Install NGINX (Web Server/Reverse Proxy)");
				Console.WriteLine("4 : Install PostgreSQL (Database Server)");
				Console.WriteLine("5 : Install FishMMO Database (User/Schema/Initial Migration)");
				Console.WriteLine("6 : Create new database migration");
				Console.WriteLine("7 : Grant User Permissions on Database");
				Console.WriteLine("8 : Delete FishMMO Database (DANGEROUS!)");
				Console.WriteLine("9 : Quit");

				ConsoleKeyInfo key = Console.ReadKey(true);

				switch (key.Key)
				{
					case ConsoleKey.D1:
						await InstallDotNet();
						break;
					case ConsoleKey.D2: // Install Visual Studio Build Tools
						await InstallVSBuildTools();
						break;
					case ConsoleKey.D3: // Install NGINX
						await InstallNGINX();
						break;
					case ConsoleKey.D4: // Install PostgreSQL
						if (_appSettings == null || string.IsNullOrWhiteSpace(_appSettings.Npgsql?.Host))
						{
							Log("appsettings.json is not loaded or Npgsql settings are incomplete. Cannot install PostgreSQL without configuration.");
							break;
						}
						if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
						{
							await InstallPostgreSQLWindows(_appSettings);
						}
						else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
								 RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
						{
							await InstallPostgreSQLLinuxMAC();
						}
						else
						{
							Log("Unsupported operating system for PostgreSQL installation.");
						}
						break;
					case ConsoleKey.D5: // Install FishMMO Database
						if (_appSettings == null || string.IsNullOrWhiteSpace(_appSettings.Npgsql?.Database))
						{
							Log("appsettings.json is not loaded or Npgsql database settings are incomplete. Cannot install FishMMO Database without configuration.");
							break;
						}
						string superUsernameInstall = InstallationConstants.PostgreSQLDefaultSuperuser;
						string superPasswordInstall = PromptForPassword($"Enter PostgreSQL Superuser Password (username is '{superUsernameInstall}'): ");

						if (await InstallFishMMODatabase(superUsernameInstall, superPasswordInstall, _appSettings))
						{
							if (PromptForYesNo("Create Initial Migration and apply to database?"))
							{
								Console.WriteLine("Creating Initial database migration...");
								await RunDotNetCommandAsync($"ef migrations add Initial -p {Constants.Configuration.ProjectPath} -s {Constants.Configuration.StartupProject}");

								Console.WriteLine("Updating database...");
								await RunDotNetCommandAsync($"ef database update -p {Constants.Configuration.ProjectPath} -s {Constants.Configuration.StartupProject}");

								Log($"Initial Migration completed and applied.");
							}
						}
						break;
					case ConsoleKey.D6: // Create Migration
						await CreateMigration();
						break;
					case ConsoleKey.D7: // Grant User Permissions
						if (_appSettings == null || string.IsNullOrWhiteSpace(_appSettings.Npgsql?.Database) || string.IsNullOrWhiteSpace(_appSettings.Npgsql?.Username))
						{
							Log("appsettings.json or Npgsql database/username is not defined. Cannot grant permissions without configuration.");
							break;
						}
						string superUsernameGrant = InstallationConstants.PostgreSQLDefaultSuperuser;
						string superPasswordGrant = PromptForPassword($"Enter PostgreSQL Superuser Password (username is '{superUsernameGrant}'): ");
						await GrantUserPermissions(superUsernameGrant, superPasswordGrant, _appSettings);
						break;
					case ConsoleKey.D8: // Delete Database
						if (_appSettings == null || string.IsNullOrWhiteSpace(_appSettings.Npgsql?.Database))
						{
							Log("appsettings.json or Npgsql database name is not defined. Cannot delete database without configuration.");
							break;
						}
						string superUsernameDelete = InstallationConstants.PostgreSQLDefaultSuperuser;
						string superPasswordDelete = PromptForPassword($"Enter PostgreSQL Superuser Password (username is '{superUsernameDelete}'): ");
						await DeleteFishMMODatabase(superUsernameDelete, superPasswordDelete, _appSettings);
						break;
					case ConsoleKey.D9: // Quit
#if UNITY_EDITOR
						EditorApplication.ExitPlaymode();
#else
						Application.Quit();
#endif
						return; // Exit the method
					default:
						Console.WriteLine("Invalid input. Please enter a valid number.");
						break;
				}

				Console.WriteLine("Press any key to continue...");
				Console.ReadKey(true); // Wait for user to press a key before re-displaying the menu
			}
		}

		/// <summary>
		/// Gets the working directory for the current application domain.
		/// </summary>
		/// <returns>Base directory path.</returns>
		private string GetWorkingDirectory()
		{
			return AppDomain.CurrentDomain.BaseDirectory;
		}

		/// <summary>
		/// Gets the appropriate shell command and argument prefix for the current OS.
		/// </summary>
		/// <summary>
		/// Gets the appropriate shell command and argument prefix for the current OS.
		/// </summary>
		/// <returns>Tuple of shell executable and argument prefix.</returns>
		private (string shell, string argPrefix) GetShellCommand()
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				return ("cmd.exe", "/c");
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				return ("/bin/bash", "-c");
			}
			else
			{
				throw new PlatformNotSupportedException("Unsupported operating system");
			}
		}

		/// <summary>
		/// Runs a process asynchronously.
		/// ProcessResult = ExitCode, Standard Output, Standard Error
		/// </summary>
		/// <summary>
		/// Runs a process asynchronously and returns true if successful.
		/// </summary>
		/// <param name="command">Process executable.</param>
		/// <param name="arguments">Arguments for the process.</param>
		/// <param name="processResult">Optional callback to handle process result.</param>
		/// <returns>True if process succeeded, otherwise false.</returns>
		private async Task<bool> RunProcessAsync(string command, string arguments, Func<int, string, string, bool> processResult = null)
		{
			using (Process process = new Process())
			{
				process.StartInfo.FileName = command;
				process.StartInfo.Arguments = arguments;
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.CreateNoWindow = true;
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.RedirectStandardError = true;

				process.Start();

				var outputTask = process.StandardOutput.ReadToEndAsync();
				var errorTask = process.StandardError.ReadToEndAsync();

				await Task.WhenAll(outputTask, errorTask);

				string output = outputTask.Result;
				string error = errorTask.Result;

				if (processResult != null)
				{
					return processResult.Invoke(process.ExitCode, output, error);
				}
				else
				{
					// Default behavior: return true if exit code is 0 (success)
					return process.ExitCode == 0;
				}
			}
		}

		/// <summary>
		/// Prompts the user for input in the console.
		/// </summary>
		/// <param name="prompt">Prompt message.</param>
		/// <returns>User input string.</returns>
		private string PromptForInput(string prompt)
		{
			Console.Write(prompt);
			return Console.ReadLine();
		}

		/// <summary>
		/// Prompts the user for a yes/no response in the console.
		/// </summary>
		/// <param name="prompt">Prompt message.</param>
		/// <returns>True for yes, false for no.</returns>
		private bool PromptForYesNo(string prompt)
		{
			while (true)
			{
				Console.Write($"{prompt} (Y/N): ");
				ConsoleKeyInfo key = Console.ReadKey();
				Console.WriteLine();

				if (key.Key == ConsoleKey.Y)
				{
					return true;
				}
				else if (key.Key == ConsoleKey.N)
				{
					return false;
				}
				else
				{
					Console.WriteLine("Invalid input. Please enter Y or N.");
				}
			}
		}

		/// <summary>
		/// Prompts the user for a password in the console, masking input.
		/// </summary>
		/// <param name="prompt">Prompt message.</param>
		/// <returns>Password string.</returns>
		private string PromptForPassword(string prompt)
		{
			Console.Write(prompt);
			string password = "";
			ConsoleKeyInfo key;

			do
			{
				key = Console.ReadKey(true);

				// Ignore any key other than Backspace or Enter
				if (key.Key != ConsoleKey.Backspace && key.Key != ConsoleKey.Enter)
				{
					password += key.KeyChar;
					Console.Write("*"); // Print * for each character entered
				}
				else if (key.Key == ConsoleKey.Backspace && password.Length > 0)
				{
					password = password.Substring(0, (password.Length - 1));
					Console.Write("\b \b"); // Erase the last * from console: Backspace, Space (to overwrite), Backspace
				}
			}
			while (key.Key != ConsoleKey.Enter);

			Console.WriteLine(); // Move to next line after password input
			return password;
		}

		/// <summary>
		/// Logs a message to the console.
		/// </summary>
		/// <summary>
		/// Logs a message to the console and debug output.
		/// </summary>
		/// <param name="message">Message to log.</param>
		/// <param name="logTime">Whether to include timestamp.</param>
		private void Log(string message, bool logTime = false)
		{
			if (string.IsNullOrWhiteSpace(message))
			{
				return;
			}
			if (logTime)
			{
				FishMMO.Logging.Log.Debug("FishMMOInstaller", $"{DateTime.Now}: {message}");
				//Console.WriteLine($"{DateTime.Now}: {message}");
			}
			else
			{
				FishMMO.Logging.Log.Debug("FishMMOInstaller", message);
				//Console.WriteLine(message);
			}
		}

		/// <summary>
		/// Downloads a file asynchronously from the specified URL.
		/// </summary>
		/// <param name="url">File URL.</param>
		/// <param name="fileName">Desired filename.</param>
		/// <returns>Path to downloaded file.</returns>
		private async Task<string> DownloadFileAsync(string url, string fileName)
		{
			try
			{
				string tempDir = GetWorkingDirectory();
				string outputPath = Path.Combine(tempDir, fileName);

				if (File.Exists(outputPath))
				{
					Log(outputPath + " already exists... Skipping download.");
					return outputPath;
				}

				Log($"Downloading file from {url}");
				Log("Please wait...");
				using (HttpClient client = new HttpClient())
				using (HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
				{
					response.EnsureSuccessStatusCode();

					if (response.Content.Headers.ContentDisposition != null)
					{
						// Prefer filename from content-disposition if available
						fileName = response.Content.Headers.ContentDisposition.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName ?? fileName;
						outputPath = Path.Combine(tempDir, fileName); // Update path if filename changed
					}

					using (Stream streamToReadFrom = await response.Content.ReadAsStreamAsync())
					{
						using (Stream streamToWriteTo = File.Open(outputPath, FileMode.Create))
						{
							await streamToReadFrom.CopyToAsync(streamToWriteTo);
						}
					}
					Log($"File successfully downloaded to {outputPath}");
					return outputPath;
				}
			}
			catch (Exception ex)
			{
				throw new Exception($"Error downloading file: {ex.Message}");
			}
		}

		#region DotNet
		/// <summary>
		/// Installs DotNet SDK and DotNet-EF tool if not already installed.
		/// </summary>
		/// <returns>True if installation succeeded or already installed.</returns>
		private async Task<bool> InstallDotNet()
		{
			if (!await IsDotNetInstalledAsync())
			{
				if (PromptForYesNo("DotNet 8 is not installed, would you like to install it?"))
				{
					Log("Installing DotNet...");
					await DownloadAndInstallDotNetAsync();
					Log("DotNet has been installed.");
					return true;
				}
				else
				{
					return false;
				}
			}
			else
			{
				Log("DotNet is already installed.");
			}

			if (!await IsDotNetEFInstalledAsync())
			{
				if (PromptForYesNo("DotNet-EF is not installed, would you like to install it?"))
				{
					Log($"Installing DotNet-EF v{InstallationConstants.DotNetEFVersion}...");
					await RunDotNetCommandAsync($"tool install --global dotnet-ef --version {InstallationConstants.DotNetEFVersion}");

					Log("DotNet-EF has been installed.");
					return true;
				}
				return false;
			}
			else
			{
				Log("DotNet-EF is already installed.");
				return true;
			}
		}

		/// <summary>
		/// Checks if DotNet SDK is installed and matches required major version.
		/// </summary>
		/// <returns>True if installed, otherwise false.</returns>
		private async Task<bool> IsDotNetInstalledAsync()
		{
			// Use the GetShellCommand helper
			(string shell, string argPrefix) = GetShellCommand();
			string arguments = $"{argPrefix} \"dotnet --version\"";

			return await RunProcessAsync(shell, arguments, (e, o, err) =>
			{
				// Check if the exit code is 0 AND the output contains the major version
				return e == 0 &&
					   o.Contains(InstallationConstants.DotNetSDKMajorVersion, StringComparison.OrdinalIgnoreCase);
			});
		}

		/// <summary>
		/// Downloads and installs DotNet SDK for the current OS.
		/// </summary>
		private async Task DownloadAndInstallDotNetAsync()
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				string installerPath = await DownloadFileAsync(InstallationConstants.DotNetSDKUrl, InstallationConstants.DotNetSDKFileName);

				try
				{
					ProcessStartInfo startInfo = new ProcessStartInfo
					{
						FileName = installerPath,
						Arguments = "/install /quiet /norestart", // Silent install options
						UseShellExecute = true,
						Verb = "runas" // Request administrator privileges
					};

					Process process = Process.Start(startInfo);

					await process.WaitForExitAsync();

					int exitCode = process.ExitCode;
					if (exitCode == 0)
					{
						Log("DotNet installation successful.");
					}
					else
					{
						Log($"DotNet installation failed with exit code {exitCode}.");
					}
				}
				catch (Exception ex)
				{
					Log($"Error installing DotNet: {ex.Message}");
				}
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
					 RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				string shScriptFile = InstallationConstants.DotNetInstallScriptFileName;

				// Download the shell script
				using (HttpClient client = new HttpClient())
				{
					var scriptContent = await client.GetStringAsync(InstallationConstants.DotNetInstallScriptUrl);
					await File.WriteAllTextAsync(shScriptFile, scriptContent);
				}

				// Make the script executable
				await RunProcessAsync("chmod", $"+x {shScriptFile}");

				// Run the shell script
				await RunProcessAsync("/bin/bash",
									  $"./{shScriptFile} --version {InstallationConstants.DotNetSDKVersion}",
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
				throw new PlatformNotSupportedException("Unsupported operating system");
			}
		}

		/// <summary>
		/// Checks if DotNet-EF tool is installed globally.
		/// </summary>
		/// <returns>True if installed, otherwise false.</returns>
		private async Task<bool> IsDotNetEFInstalledAsync()
		{
			try
			{
				return await RunDotNetCommandAsync(
					"tool list --global",
					(e, o, err) => o.Contains("dotnet-ef", StringComparison.OrdinalIgnoreCase));
			}
			catch (Exception ex)
			{
				Log($"Error checking dotnet-ef tool: {ex.Message}");
				return false;
			}
		}

		private bool _pathSet = false; // Flag to ensure path is set only once for Linux/OSX
		/// <summary>
		/// Runs a dotnet command asynchronously, handling environment setup for Linux/OSX.
		/// </summary>
		/// <param name="arguments">DotNet command arguments.</param>
		/// <param name="customProcessResult">Optional custom result handler.</param>
		/// <returns>True if command succeeded, otherwise false.</returns>
		private async Task<bool> RunDotNetCommandAsync(string arguments, Func<int, string, string, bool> customProcessResult = null)
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
				RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				if (!_pathSet)
				{
					// Ensure ~/.dotnet/tools is in PATH and DOTNET_ROOT is set for non-Windows
					string homePath = Environment.GetEnvironmentVariable("HOME");
					if (string.IsNullOrEmpty(homePath))
					{
						Log("Warning: The HOME environment variable is not set. DotNet commands may fail.");
					}
					else
					{
						string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
						string dotnetToolsPath = Path.Combine(homePath, ".dotnet", "tools");

						if (!currentPath.Split(':').Contains(dotnetToolsPath))
						{
							string newPath = $"{currentPath}:{dotnetToolsPath}";
							Environment.SetEnvironmentVariable("PATH", newPath);
							Log($"Updated PATH to include: {dotnetToolsPath}");
						}
					}
					// For systems where dotnet might not be globally installed via default package managers
					string dotnetRoot = "/usr/share/dotnet"; // Common install location
					if (Directory.Exists(dotnetRoot))
					{
						Environment.SetEnvironmentVariable("DOTNET_ROOT", dotnetRoot);
						Log($"Set DOTNET_ROOT to: {dotnetRoot}");
					}

					_pathSet = true;
				}
			}

			Console.WriteLine("Running DotNet Command: \r\n" +
							  "dotnet " + arguments);

			(string shell, string argPrefix) = GetShellCommand();
			// Quote the entire 'dotnet arguments' part for the shell to interpret it as a single command
			string fullArguments = $"{argPrefix} \"dotnet {arguments}\"";

			bool success = await RunProcessAsync(shell, fullArguments,
				(exitCode, output, error) =>
				{
					// Always log standard output and error from the process
					if (!string.IsNullOrWhiteSpace(output))
					{
						Console.WriteLine(output); // Print standard output directly to console
					}
					if (!string.IsNullOrWhiteSpace(error))
					{
						Log($"Process Error: {error}"); // Use Log for standard error
					}

					// If a custom success criteria is provided, use it.
					if (customProcessResult != null)
					{
						return customProcessResult.Invoke(exitCode, output, error);
					}
					else
					{
						// Default success criteria: exit code is 0
						return exitCode == 0;
					}
				});

			if (!success)
			{
				Log($"DotNet command 'dotnet {arguments}' failed.");
			}
			return success;
		}
		#endregion

		#region Database
		/// <summary>
		/// Installs PostgreSQL on Windows using the official installer.
		/// </summary>
		/// <param name="appSettings">Application settings for database configuration.</param>
		/// <returns>True if installation succeeded, otherwise false.</returns>
		private async Task<bool> InstallPostgreSQLWindows(AppSettings appSettings)
		{
			string superUsername = InstallationConstants.PostgreSQLDefaultSuperuser;
			string superPassword = PromptForPassword($"Enter new PostgreSQL Superuser Password (username is '{superUsername}'): ");

			if (!PromptForYesNo("Install PostgreSQL server?"))
			{
				return false;
			}

			Log("Installing PostgreSQL...");

			string installerPath;
			try
			{
				installerPath = await DownloadFileAsync(InstallationConstants.PostgreSQLWindowsInstallerUrl, InstallationConstants.PostgreSQLWindowsInstallerFileName);
			}
			catch (Exception ex)
			{
				Log($"Failed to download PostgreSQL installer: {ex.Message}");
				return false;
			}

			try
			{
				string arguments = $"--unattendedmodeui minimal " +
								   $"--mode unattended " +
								   $"--superaccount \"{superUsername}\" " +
								   $"--superpassword \"{superPassword}\" " +
								   $"--serverport {appSettings.Npgsql.Port} " +
								   $"--disable-components pgAdmin,stackbuilder";

				ProcessStartInfo startInfo = new ProcessStartInfo
				{
					FileName = installerPath,
					Arguments = arguments,
					CreateNoWindow = true,
					UseShellExecute = true, // Must be true for Verb "runas"
					Verb = "runas" // Request administrator privileges
				};

				Process process = Process.Start(startInfo);

				await process.WaitForExitAsync();

				int exitCode = process.ExitCode;
				if (exitCode == 0)
				{
					Log("PostgreSQL installation successful.");
					return true;
				}
				else
				{
					Log($"PostgreSQL installation failed with exit code {exitCode}. Please check installer logs or try running the installer manually.");
					return false;
				}
			}
			catch (Exception ex)
			{
				Log($"Error installing PostgreSQL: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// Installs PostgreSQL on Linux or macOS using system package managers.
		/// </summary>
		/// <returns>True if installation succeeded, otherwise false.</returns>
		private async Task<bool> InstallPostgreSQLLinuxMAC()
		{
			if (!PromptForYesNo("Install PostgreSQL server?"))
			{
				return false;
			}

			Log("Installing PostgreSQL...");

			try
			{
				bool isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
				string updateCommand = isMac ? "brew update" : "sudo apt-get update";
				string installCommand = isMac ? "brew install postgresql" : "sudo apt-get install -y postgresql postgresql-contrib";
				string startCommand = isMac ? "brew services start postgresql" : "sudo systemctl start postgresql";
				string enableCommand = isMac ? null : "sudo systemctl enable postgresql"; // Linux-specific

				(string shell, string argPrefix) = GetShellCommand();

				if (!await RunProcessAsync(shell, $"{argPrefix} \"{updateCommand}\"", (e, o, err) =>
				{
					if (e != 0) { Log($"Failed to update package lists. Error: {err}"); return false; }
					return true;
				})) return false;

				if (!await RunProcessAsync(shell, $"{argPrefix} \"{installCommand}\"", (e, o, err) =>
				{
					if (e != 0) { Log($"Failed to install PostgreSQL. Error: {err}"); return false; }
					return true;
				})) return false;

				if (!await RunProcessAsync(shell, $"{argPrefix} \"{startCommand}\"", (e, o, err) =>
				{
					if (e != 0) { Log($"Failed to start PostgreSQL. Error: {err}"); return false; }
					return true;
				})) return false;

				if (!isMac && enableCommand != null)
				{
					if (!await RunProcessAsync(shell, $"{argPrefix} \"{enableCommand}\"", (e, o, err) =>
					{
						if (e != 0) { Log($"Failed to enable PostgreSQL to start on boot. Error: {err}"); return false; }
						return true;
					})) return false;
				}

				if (PromptForYesNo("Update PostgreSQL Superuser Password?"))
				{
					string superUsername = InstallationConstants.PostgreSQLDefaultSuperuser;
					string superPassword = PromptForPassword($"Enter new PostgreSQL Superuser Password (username is '{superUsername}'): ");

					string updateUserCommand = $"ALTER USER {superUsername} WITH PASSWORD '{superPassword}';";
					// Using psql -c to execute SQL command as postgres user
					if (!await RunProcessAsync(shell, $"{argPrefix} \"sudo -u postgres psql -c \\\"{updateUserCommand}\\\"\"",
					(e, o, err) =>
					{
						if (e != 0) { Log($"Failed to update PostgreSQL superuser password. Error: {err}"); return false; }
						return true;
					})) return false;
				}

				Log("PostgreSQL installation successful.");
				return true;
			}
			catch (Exception ex)
			{
				Log($"Error installing PostgreSQL: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// Installs the FishMMO database, creates user role, and grants privileges.
		/// </summary>
		/// <param name="superUsername">PostgreSQL superuser name.</param>
		/// <param name="superPassword">PostgreSQL superuser password.</param>
		/// <param name="appSettings">Application settings for database configuration.</param>
		/// <returns>True if installation succeeded, otherwise false.</returns>
		public async Task<bool> InstallFishMMODatabase(string superUsername, string superPassword, AppSettings appSettings)
		{
			try
			{
				Log($"Attempting to connect to PostgreSQL at {appSettings.Npgsql.Host}:{appSettings.Npgsql.Port}");
				// Connect to a default administrative database (like 'postgres') to create the new database
				string connectionString = $"Host={appSettings.Npgsql.Host};Port={appSettings.Npgsql.Port};Username={superUsername};Password={superPassword};Database={InstallationConstants.PostgreSQLDefaultAdminDb}";

				using (var connection = new NpgsqlConnection(connectionString))
				{
					await connection.OpenAsync();
					Log("Successfully connected to PostgreSQL server.");

					// Create database
					if (PromptForYesNo($"Create Database '{appSettings.Npgsql.Database}'?"))
					{
						using (var checkDbCommand = new NpgsqlCommand($"SELECT 1 FROM pg_database WHERE datname = @dbName", connection))
						{
							checkDbCommand.Parameters.AddWithValue("dbName", appSettings.Npgsql.Database);
							var result = await checkDbCommand.ExecuteScalarAsync();
							if (result != null)
							{
								Log($"Database '{appSettings.Npgsql.Database}' already exists. Skipping creation.");
							}
							else
							{
								Log($"Creating database '{appSettings.Npgsql.Database}'...");
								await CreateDatabase(connection, appSettings.Npgsql.Database);
								Log($"Database '{appSettings.Npgsql.Database}' created successfully.");
							}
						}
					}

					// Create user role
					if (PromptForYesNo($"Create User Role '{appSettings.Npgsql.Username}' for database access?"))
					{
						using (var checkUserCommand = new NpgsqlCommand($"SELECT 1 FROM pg_roles WHERE rolname = @username", connection))
						{
							checkUserCommand.Parameters.AddWithValue("username", appSettings.Npgsql.Username);
							var result = await checkUserCommand.ExecuteScalarAsync();
							if (result != null)
							{
								Log($"User role '{appSettings.Npgsql.Username}' already exists. Skipping creation.");
							}
							else
							{
								Log($"Creating user role '{appSettings.Npgsql.Username}'...");
								await CreateUser(connection, appSettings.Npgsql.Username, appSettings.Npgsql.Password);
								Log($"User role '{appSettings.Npgsql.Username}' created successfully.");
							}
						}
						// Grant privileges (always attempt to grant, even if user existed)
						Log($"Granting privileges on database '{appSettings.Npgsql.Database}' to user '{appSettings.Npgsql.Username}'...");
						await GrantPrivileges(connection, appSettings.Npgsql.Username, appSettings.Npgsql.Database);
						Log("Privileges granted successfully.");
					}

					Log("FishMMO Database components installed/configured.");
					return true;
				}
			}
			catch (NpgsqlException npgEx)
			{
				Log($"PostgreSQL connection or database operation error: {npgEx.Message}. Check your appsettings.json and PostgreSQL server status.");
				return false;
			}
			catch (Exception ex)
			{
				Log($"General error installing FishMMO database components: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// Creates a new PostgreSQL database with the specified name.
		/// </summary>
		/// <param name="connection">Open NpgsqlConnection.</param>
		/// <param name="dbName">Database name.</param>
		private async Task CreateDatabase(NpgsqlConnection connection, string dbName)
		{
			if (!Regex.IsMatch(dbName, @"^[a-zA-Z0-9_]+$"))
			{
				throw new ArgumentException("Invalid database name format. Database names can only contain alphanumeric characters and underscores.", nameof(dbName));
			}

			string formatSql = $"SELECT format('CREATE DATABASE %I', @dbNameParam)";
			string createDatabaseCommandText;

			using (var command = new NpgsqlCommand(formatSql, connection))
			{
				command.Parameters.AddWithValue("dbNameParam", dbName);
				createDatabaseCommandText = (string)await command.ExecuteScalarAsync();
			}

			using (var createDbCommand = new NpgsqlCommand(createDatabaseCommandText, connection))
			{
				await createDbCommand.ExecuteNonQueryAsync();
			}
		}

		/// <summary>
		/// Creates a new PostgreSQL user role with the specified username and password.
		/// </summary>
		/// <param name="connection">Open NpgsqlConnection.</param>
		/// <param name="username">Username for the role.</param>
		/// <param name="password">Password for the role.</param>
		private async Task CreateUser(NpgsqlConnection connection, string username, string password)
		{
			if (!Regex.IsMatch(username, @"^[a-zA-Z0-9_]+$"))
			{
				throw new ArgumentException("Invalid username format. Usernames can only contain alphanumeric characters and underscores.", nameof(username));
			}

			string formatSql = $"SELECT format('CREATE ROLE %I WITH LOGIN PASSWORD %L', @usernameParam, @passwordParam)";
			string createRoleCommandText;

			using (var command = new NpgsqlCommand(formatSql, connection))
			{
				command.Parameters.AddWithValue("usernameParam", username);
				command.Parameters.AddWithValue("passwordParam", password);
				createRoleCommandText = (string)await command.ExecuteScalarAsync();
			}

			using (var createRoleCommand = new NpgsqlCommand(createRoleCommandText, connection))
			{
				await createRoleCommand.ExecuteNonQueryAsync();
			}
		}

		/// <summary>
		/// Grants all privileges on the specified database to the specified user.
		/// </summary>
		/// <param name="connection">Open NpgsqlConnection.</param>
		/// <param name="username">Username to grant privileges to.</param>
		/// <param name="dbName">Database name.</param>
		private async Task GrantPrivileges(NpgsqlConnection connection, string username, string dbName)
		{
			if (!Regex.IsMatch(username, @"^[a-zA-Z0-9_]+$"))
			{
				throw new ArgumentException("Invalid username format. Usernames can only contain alphanumeric characters and underscores.", nameof(username));
			}
			if (!Regex.IsMatch(dbName, @"^[a-zA-Z0-9_]+$"))
			{
				throw new ArgumentException("Invalid database name format. Database names can only contain alphanumeric characters and underscores.", nameof(dbName));
			}

			string grantSqlFormat = $"SELECT format('GRANT ALL PRIVILEGES ON DATABASE %I TO %I', @dbNameParam, @usernameParam)";
			string grantCommandText;

			using (var formatCommand = new NpgsqlCommand(grantSqlFormat, connection))
			{
				formatCommand.Parameters.AddWithValue("dbNameParam", dbName);
				formatCommand.Parameters.AddWithValue("usernameParam", username);
				grantCommandText = (string)await formatCommand.ExecuteScalarAsync();
			}

			using (var grantCommand = new NpgsqlCommand(grantCommandText, connection))
			{
				await grantCommand.ExecuteNonQueryAsync();
			}

			string alterOwnerSqlFormat = $"SELECT format('ALTER DATABASE %I OWNER TO %I', @dbNameParam, @usernameParam)";
			string alterOwnerCommandText;

			using (var formatCommand = new NpgsqlCommand(alterOwnerSqlFormat, connection))
			{
				formatCommand.Parameters.AddWithValue("dbNameParam", dbName);
				formatCommand.Parameters.AddWithValue("usernameParam", username);
				alterOwnerCommandText = (string)await formatCommand.ExecuteScalarAsync();
			}

			using (var alterOwnerCommand = new NpgsqlCommand(alterOwnerCommandText, connection))
			{
				await alterOwnerCommand.ExecuteNonQueryAsync();
			}
		}

		/// <summary>
		/// Creates a new database migration and applies it using dotnet ef commands.
		/// </summary>
		private async Task CreateMigration()
		{
			Console.Clear();
			// Check for necessary configuration before proceeding
			if (_appSettings == null ||
				string.IsNullOrWhiteSpace(Constants.Configuration.ProjectPath) ||
				string.IsNullOrWhiteSpace(Constants.Configuration.StartupProject))
			{
				Log("Configuration paths (ProjectPath or StartupProject) are not set. Cannot create migration.");
				return;
			}

			string migrationName = PromptForInput("Enter a name for the new migration (e.g., 'AddPlayerInventory'): ");
			if (string.IsNullOrWhiteSpace(migrationName))
			{
				Log("Migration name cannot be empty. Aborting migration creation.");
				return;
			}

			Log($"Creating a new migration '{migrationName}'...");

			// Run 'dotnet ef migrations add [MigrationName]' command
			bool migrationSuccess = await RunDotNetCommandAsync($"ef migrations add {migrationName} -p {Constants.Configuration.ProjectPath} -s {Constants.Configuration.StartupProject}");

			if (!migrationSuccess)
			{
				Log($"Failed to create migration '{migrationName}'. Please check the console output for details.");
				return;
			}

			Log($"Updating the database with migration '{migrationName}'...");

			// Run 'dotnet ef database update' command
			bool updateSuccess = await RunDotNetCommandAsync($"ef database update -p {Constants.Configuration.ProjectPath} -s {Constants.Configuration.StartupProject}");

			if (updateSuccess)
			{
				Log($"Migration '{migrationName}' created and applied successfully.");
			}
			else
			{
				Log($"Failed to apply migration '{migrationName}' to the database. Please check the console output for details.");
			}
		}

		/// <summary>
		/// Deletes the FishMMO database as defined in appsettings.json.
		/// Requires PostgreSQL superuser credentials. This operation is DANGEROUS and irreversible.
		/// </summary>
		/// <param name="superUsername">PostgreSQL superuser name.</param>
		/// <param name="superPassword">PostgreSQL superuser password.</param>
		/// <param name="appSettings">Application settings for database configuration.</param>
		private async Task DeleteFishMMODatabase(string superUsername, string superPassword, AppSettings appSettings)
		{
			if (appSettings == null || string.IsNullOrWhiteSpace(appSettings.Npgsql?.Database))
			{
				Log("appsettings.json or Npgsql database name is not defined. Cannot proceed with deletion.");
				return;
			}

			string databaseToDelete = appSettings.Npgsql.Database;

			Console.WriteLine($"\n!!! DANGER ZONE: YOU ARE ABOUT TO DELETE THE DATABASE !!!");
			Console.WriteLine($"This action is irreversible and will permanently delete all data in '{databaseToDelete}'.");
			Console.WriteLine($"Are you absolutely sure you want to delete the database '{databaseToDelete}'?");

			string confirmationInput = PromptForInput("Type 'DELETE' (all caps) to confirm: ");
			if (!confirmationInput?.Trim().Equals("DELETE", StringComparison.Ordinal) == true)
			{
				Log("Database deletion cancelled by user.");
				return;
			}

			try
			{
				// Connect to a default administrative database (like 'postgres')
				// You cannot drop a database you are currently connected to.
				string adminConnectionString = $"Host={appSettings.Npgsql.Host};Port={appSettings.Npgsql.Port};Username={superUsername};Password={superPassword};Database={InstallationConstants.PostgreSQLDefaultAdminDb}";

				using (var connection = new NpgsqlConnection(adminConnectionString))
				{
					await connection.OpenAsync();
					Log($"Connected to '{InstallationConstants.PostgreSQLDefaultAdminDb}' database as superuser.");

					// 1. Terminate all active connections to the database we want to drop
					Log($"Terminating active connections to database '{databaseToDelete}'...");
					using (var terminateCommand = new NpgsqlCommand(
						$"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @dbName;", connection))
					{
						terminateCommand.Parameters.AddWithValue("dbName", databaseToDelete);
						await terminateCommand.ExecuteNonQueryAsync();
						Log("Active connections terminated.");
					}

					// 2. Drop the database
					Log($"Attempting to drop database '{databaseToDelete}'...");
					using (var dropCommand = new NpgsqlCommand($"DROP DATABASE \"{databaseToDelete}\";", connection))
					{
						await dropCommand.ExecuteNonQueryAsync();
						Log($"Database '{databaseToDelete}' deleted successfully.");
					}
				}
			}
			catch (NpgsqlException npgEx)
			{
				Log($"PostgreSQL error during database deletion: {npgEx.Message}. Ensure correct superuser password and permissions.");
			}
			catch (Exception ex)
			{
				Log($"An unexpected error occurred during database deletion: {ex.Message}");
			}
		}

		/// <summary>
		/// Grants comprehensive permissions to a specified user on a specific database.
		/// This includes permissions on existing and future tables, sequences, and functions.
		/// </summary>
		/// <param name="superUsername">PostgreSQL superuser name.</param>
		/// <param name="superPassword">PostgreSQL superuser password.</param>
		/// <param name="appSettings">Application settings for database configuration.</param>
		private async Task GrantUserPermissions(string superUsername, string superPassword, AppSettings appSettings)
		{
			Console.Clear();
			Log("--- Grant User Permissions ---");

			if (appSettings == null || string.IsNullOrWhiteSpace(appSettings.Npgsql?.Host) || string.IsNullOrWhiteSpace(appSettings.Npgsql?.Port.ToString()))
			{
				Log("appsettings.json or Npgsql host/port is not defined. Cannot connect to database.");
				return;
			}

			string defaultDbName = appSettings.Npgsql?.Database ?? "fishmmo_database"; // Fallback default
			string dbName = PromptForInput($"Enter database name to grant permissions on (default: {defaultDbName}): ");
			if (string.IsNullOrWhiteSpace(dbName)) dbName = defaultDbName;

			string defaultUsername = appSettings.Npgsql?.Username ?? "fishmmo_user"; // Fallback default
			string usernameToGrant = PromptForInput($"Enter username to grant permissions to (default: {defaultUsername}): ");
			if (string.IsNullOrWhiteSpace(usernameToGrant)) usernameToGrant = defaultUsername;

			Console.WriteLine($"Attempting to grant permissions for user '{usernameToGrant}' on database '{dbName}'.");

			try
			{
				// Connect to the target database directly to grant schema-level permissions.
				// We assume the database already exists at this point.
				string connectionString = $"Host={appSettings.Npgsql.Host};Port={appSettings.Npgsql.Port};Username={superUsername};Password={superPassword};Database={dbName}";

				using (var connection = new NpgsqlConnection(connectionString))
				{
					await connection.OpenAsync();
					Log($"Successfully connected to database '{dbName}' as superuser.");

					// Grant privileges on the database itself (already done during Install, but safe to re-run)
					Log($"Granting ALL PRIVILEGES on DATABASE \"{dbName}\" to \"{usernameToGrant}\"...");
					using (var cmd = new NpgsqlCommand($"GRANT ALL PRIVILEGES ON DATABASE \"{dbName}\" TO \"{usernameToGrant}\";", connection))
					{
						await cmd.ExecuteNonQueryAsync();
					}

					// Grant privileges on existing tables in public schema
					Log($"Granting ALL PRIVILEGES on ALL TABLES in SCHEMA public to \"{usernameToGrant}\"...");
					using (var cmd = new NpgsqlCommand($"GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO \"{usernameToGrant}\";", connection))
					{
						await cmd.ExecuteNonQueryAsync();
					}

					// Grant privileges on existing sequences in public schema
					Log($"Granting ALL PRIVILEGES on ALL SEQUENCES in SCHEMA public to \"{usernameToGrant}\"...");
					using (var cmd = new NpgsqlCommand($"GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO \"{usernameToGrant}\";", connection))
					{
						await cmd.ExecuteNonQueryAsync();
					}

					// Grant privileges on existing functions in public schema (if any)
					Log($"Granting ALL PRIVILEGES on ALL FUNCTIONS in SCHEMA public to \"{usernameToGrant}\"...");
					using (var cmd = new NpgsqlCommand($"GRANT ALL PRIVILEGES ON ALL FUNCTIONS IN SCHEMA public TO \"{usernameToGrant}\";", connection))
					{
						await cmd.ExecuteNonQueryAsync();
					}

					// Set default privileges for future objects in public schema
					Log($"Setting ALTER DEFAULT PRIVILEGES for future TABLES in SCHEMA public to \"{usernameToGrant}\"...");
					using (var cmd = new NpgsqlCommand($"ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL PRIVILEGES ON TABLES TO \"{usernameToGrant}\";", connection))
					{
						await cmd.ExecuteNonQueryAsync();
					}

					Log($"Setting ALTER DEFAULT PRIVILEGES for future SEQUENCES in SCHEMA public to \"{usernameToGrant}\"...");
					using (var cmd = new NpgsqlCommand($"ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL PRIVILEGES ON SEQUENCES TO \"{usernameToGrant}\";", connection))
					{
						await cmd.ExecuteNonQueryAsync();
					}

					Log($"Setting ALTER DEFAULT PRIVILEGES for future FUNCTIONS in SCHEMA public to \"{usernameToGrant}\"...");
					using (var cmd = new NpgsqlCommand($"ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL PRIVILEGES ON FUNCTIONS TO \"{usernameToGrant}\";", connection))
					{
						await cmd.ExecuteNonQueryAsync();
					}

					Log($"Successfully granted comprehensive permissions to user '{usernameToGrant}' on database '{dbName}'.");
				}
			}
			catch (NpgsqlException npgEx)
			{
				Log($"PostgreSQL error granting permissions: {npgEx.Message}. Ensure database '{dbName}' exists and superuser credentials are correct.");
			}
			catch (Exception ex)
			{
				Log($"An unexpected error occurred during permission granting: {ex.Message}");
			}
		}
		#endregion

		#region NGINX
		/// <summary>
		/// Installs NGINX based on the operating system.
		/// </summary>
		private async Task InstallNGINX()
		{
			Console.Clear();
			Log("--- Install NGINX ---");

			if (await IsNGINXInstalledAsync())
			{
				Log("NGINX appears to be already installed. Skipping installation.");
				return;
			}

			if (!PromptForYesNo("NGINX is not detected. Would you like to install it?"))
			{
				Log("NGINX installation cancelled by user.");
				return;
			}

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				await InstallNGINXWindows();
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				await InstallNGINXLinux();
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				await InstallNGINXMac();
			}
			else
			{
				Log("Unsupported operating system for NGINX installation.");
			}
		}

		/// <summary>
		/// Checks if NGINX is installed by trying to run 'nginx -v'.
		/// </summary>
		/// <returns>True if NGINX is installed, otherwise false.</returns>
		private async Task<bool> IsNGINXInstalledAsync()
		{
			(string shell, string argPrefix) = GetShellCommand();
			string arguments = $"{argPrefix} \"nginx -v\""; // Command to get NGINX version

			bool installed = await RunProcessAsync(shell, arguments, (exitCode, output, error) =>
			{
				// NGINX -v usually outputs to stderr with exit code 0 or 1, if found.
				// Check if the output/error contains "nginx version".
				return (exitCode == 0 || exitCode == 1) && (output.Contains("nginx version") || error.Contains("nginx version"));
			});

			if (installed)
			{
				Log("NGINX detected. (Run 'nginx -v' to confirm version)");
			}
			return installed;
		}

		/// <summary>
		/// Installs NGINX on Windows by downloading and extracting the zip file.
		/// </summary>
		private async Task InstallNGINXWindows()
		{
			Log("Installing NGINX on Windows...");
			try
			{
				string downloadPath = await DownloadFileAsync(InstallationConstants.NGINXWindowsDownloadUrl, InstallationConstants.NGINXWindowsFileName);
				string extractDirectory = InstallationConstants.NGINXWindowsExtractPath;

				if (Directory.Exists(extractDirectory))
				{
					Log($"NGINX extraction directory '{extractDirectory}' already exists. Please manually delete it or choose a different path if you want a clean install.");
					if (!PromptForYesNo("Attempt to extract anyway (may overwrite files)?"))
					{
						Log("NGINX installation cancelled.");
						return;
					}
				}
				else
				{
					Directory.CreateDirectory(extractDirectory);
				}

				System.IO.Compression.ZipFile.ExtractToDirectory(downloadPath, extractDirectory);
				Log($"NGINX successfully extracted to '{extractDirectory}'.");
				Log("To start NGINX, navigate to the extracted directory (e.g., C:\\nginx-1.24.0) and run 'nginx.exe'.");
				Log("For production, consider running NGINX as a Windows service.");
			}
			catch (Exception ex)
			{
				Log($"Error installing NGINX on Windows: {ex.Message}");
			}
		}

		/// <summary>
		/// Installs NGINX on Linux using apt, yum, or dnf package managers.
		/// </summary>
		private async Task InstallNGINXLinux()
		{
			Log("Installing NGINX on Linux...");
			(string shell, string argPrefix) = GetShellCommand();
			string updateCommand;
			string installCommand;
			string startCommand = "sudo systemctl start nginx";
			string enableCommand = "sudo systemctl enable nginx";

			// Determine package manager
			if (await RunProcessAsync(shell, $"{argPrefix} \"which apt-get\"", (e, o, err) => e == 0))
			{
				Log("Using apt-get for NGINX installation.");
				updateCommand = "sudo apt-get update";
				installCommand = "sudo apt-get install -y nginx";
			}
			else if (await RunProcessAsync(shell, $"{argPrefix} \"which yum\"", (e, o, err) => e == 0))
			{
				Log("Using yum for NGINX installation.");
				updateCommand = "sudo yum check-update"; // No direct update, just check
				installCommand = "sudo yum install -y nginx";
			}
			else if (await RunProcessAsync(shell, $"{argPrefix} \"which dnf\"", (e, o, err) => e == 0))
			{
				Log("Using dnf for NGINX installation.");
				updateCommand = "sudo dnf check-update"; // No direct update, just check
				installCommand = "sudo dnf install -y nginx";
			}
			else
			{
				Log("No supported package manager (apt, yum, dnf) found. Please install NGINX manually.");
				return;
			}

			try
			{
				Log("Updating package lists...");
				if (!await RunProcessAsync(shell, $"{argPrefix} \"{updateCommand}\""))
				{
					Log("Failed to update package lists. Continuing anyway, but installation might fail.");
				}

				Log("Installing NGINX...");
				if (!await RunProcessAsync(shell, $"{argPrefix} \"{installCommand}\""))
				{
					Log("Failed to install NGINX. Check for errors above.");
					return;
				}

				Log("Starting NGINX service...");
				if (!await RunProcessAsync(shell, $"{argPrefix} \"{startCommand}\""))
				{
					Log("Failed to start NGINX service. You may need to start it manually: 'sudo systemctl start nginx'");
				}
				else
				{
					Log("NGINX service started successfully.");
				}

				Log("Enabling NGINX to start on boot...");
				if (!await RunProcessAsync(shell, $"{argPrefix} \"{enableCommand}\""))
				{
					Log("Failed to enable NGINX to start on boot. You may need to enable it manually: 'sudo systemctl enable nginx'");
				}
				else
				{
					Log("NGINX enabled to start on boot.");
				}

				Log("NGINX installed and configured on Linux. Check its status with 'sudo systemctl status nginx'.");
			}
			catch (Exception ex)
			{
				Log($"Error during NGINX installation on Linux: {ex.Message}");
			}
		}

		/// <summary>
		/// Installs NGINX on macOS using Homebrew.
		/// </summary>
		private async Task InstallNGINXMac()
		{
			Log("Installing NGINX on macOS...");
			(string shell, string argPrefix) = GetShellCommand();

			// Check if Homebrew is installed
			if (!await RunProcessAsync(shell, $"{argPrefix} \"which brew\"", (e, o, err) => e == 0))
			{
				Log("Homebrew (brew) is not installed. Please install Homebrew first from https://brew.sh/, then try again.");
				return;
			}

			try
			{
				Log("Updating Homebrew...");
				if (!await RunProcessAsync(shell, $"{argPrefix} \"brew update\""))
				{
					Log("Failed to update Homebrew. Continuing anyway, but installation might fail.");
				}

				Log("Installing NGINX with Homebrew...");
				if (!await RunProcessAsync(shell, $"{argPrefix} \"brew install nginx\""))
				{
					Log("Failed to install NGINX. Check for errors above.");
					return;
				}

				Log("Starting NGINX service...");
				if (!await RunProcessAsync(shell, $"{argPrefix} \"brew services start nginx\""))
				{
					Log("Failed to start NGINX service. You may need to start it manually: 'brew services start nginx' or 'nginx'.");
				}
				else
				{
					Log("NGINX service started successfully.");
				}

				Log("NGINX installed and configured on macOS. Check its status with 'brew services list' or access http://localhost:8080 (default NGINX port on Homebrew).");
			}
			catch (Exception ex)
			{
				Log($"Error during NGINX installation on macOS: {ex.Message}");
			}
		}
		#endregion

		#region Visual Studio Build Tools
		/// <summary>
		/// Installs Visual Studio Build Tools on Windows.
		/// This method downloads the bootstrapper and launches it, providing manual instructions.
		/// </summary>
		private async Task InstallVSBuildTools()
		{
			Console.Clear();
			Log("--- Install Visual Studio Build Tools ---");

			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				Log("Visual Studio Build Tools can only be installed on Windows. Skipping.");
				return;
			}

			if (!PromptForYesNo("This option will download and launch the Visual Studio Build Tools installer. Would you like to proceed?"))
			{
				Log("Visual Studio Build Tools installation cancelled by user.");
				return;
			}

			try
			{
				string installerPath = await DownloadFileAsync(InstallationConstants.VSBuildToolsUrl, InstallationConstants.VSBuildToolsFileName);

				Log("Automated installation of Visual Studio Build Tools will begin.");
				Log("The following workloads and components will be installed:");
				Log("  1. '.NET desktop development' workload (includes .NET Framework development tools)");
				Log("  2. 'Desktop development with C++' workload (MSVC 64-bit compiler for x86, x64, ARM, and ARM64)");
				Log("  3. Windows 10 SDK");
				Log("After installation, restart your computer if prompted by the installer.");

				string arguments = "--quiet --wait --norestart --nocache " +
					"--add Microsoft.VisualStudio.Workload.ManagedDesktop " +
					"--add Microsoft.VisualStudio.Workload.NativeDesktop " +
					"--add Microsoft.VisualStudio.Component.VC.Tools.x86.x64 " +
					"--add Microsoft.VisualStudio.Component.Windows10SDK.19041";

				ProcessStartInfo startInfo = new ProcessStartInfo
				{
					FileName = installerPath,
					Arguments = arguments,
					UseShellExecute = true,
					Verb = "runas" // Request administrator privileges
				};

				Log($"Launching installer with arguments: {arguments}");
				Process process = Process.Start(startInfo);
				await process.WaitForExitAsync();

				Log("Visual Studio Build Tools automated installation finished. Please verify the installation and restart your computer if required.");
			}
			catch (Exception ex)
			{
				Log($"Error installing Visual Studio Build Tools: {ex.Message}");
			}
		}
		#endregion
	}
}