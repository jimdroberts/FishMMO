using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;
using FishMMO.Database;
using Npgsql;

namespace FishMMO.Shared
{
	public class FishMMOInstaller : MonoBehaviour
	{
		private void Awake()
		{
#if !UNITY_EDITOR
			InstallEverything();
#endif
		}

		private async void InstallEverything()
		{
			if (!await InstallDotNet())
			{
				Log("DotNet 8 and DotNet-EF Tools 5.0.17 are required by FishMMO. Please install them and try again.");
				return;
			}
			await InstallPython();
			await InstallDatabase();
		}

		private string GetWorkingDirectory()
		{
			return AppDomain.CurrentDomain.BaseDirectory;
		}

		/// <summary>
		/// Runs a process asynchronously.
		/// ProcessResult = ExitCode, Standard Output, Standard Error
		/// </summary>
		private async Task<bool> RunProcessAsync(string command, string arguments, Func<int, string, string, bool> processResult = null, Func<Process, Task> subProcess = null)
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

				await process.WaitForExitAsync(); // Use asynchronous wait

				string output = await process.StandardOutput.ReadToEndAsync();
				string error = await process.StandardError.ReadToEndAsync();

				if (subProcess != null)
				{
					await subProcess.Invoke(process);
				}

				if (processResult != null)
				{
					return processResult.Invoke(process.ExitCode, output, error);
				}
				else
				{
					return process.ExitCode == 0;
				}
			}
		}

		private async Task RunDismCommandAsync(string arguments)
		{
			await RunProcessAsync("dism.exe", arguments);
		}

		private string PromptForInput(string prompt)
		{
			Console.Write(prompt);
			return Console.ReadLine();
		}

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
					Console.Write("\b \b"); // Erase the last * from console
				}
			}
			while (key.Key != ConsoleKey.Enter);

			Console.WriteLine(); // Move to next line after password input
			return password;
		}

		private void Log(string message, bool logTime = false)
		{
			if (string.IsNullOrWhiteSpace(message))
			{
				return;
			}
			if (logTime)
			{
				Debug.Log($"{DateTime.Now}: {message}");
			}
			else
			{
				Debug.Log(message);
			}
		}

		private async Task<string> DownloadFileAsync(string url, string fileName = "tempFileName.tmpFile")
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
						fileName = response.Content.Headers.ContentDisposition.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName;
					}
					else
					{
						//Log("Failed to get filename from content-disposition header.");
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

		#region WSL
		private async Task<bool> IsVirtualizationEnabledAsync()
		{
			return await RunProcessAsync("powershell.exe",
										 "-Command \"(Get-WmiObject -Namespace 'root\\cimv2' -Class Win32_Processor).VirtualizationFirmwareEnabled\"",
										 (e, o, err) =>
										 {
											 return bool.Parse(o.Trim());
										 });
		}

		private async Task<bool> IsWSLInstalledAsync()
		{
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				return true;
			}
			return await RunProcessAsync("where",
										 "/q wsl.exe",
										 (e, o, err) =>
										 {
											 return e == 0;
										 });
		}

		private async Task<bool> InstallWSL()
		{
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				return true;
			}

			if (await IsVirtualizationEnabledAsync())
			{
				if (!await IsWSLInstalledAsync())
				{
					if (PromptForYesNo("Windows Subsystem for Linux needs to be installed for Docker. Would you like to install it?"))
					{
						Log("Installing WSL...");
						await RunDismCommandAsync("/online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux /all /norestart");
						Log("WSL has been installed.");
						return true;
					}
					else
					{
						return false;
					}
				}
				else
				{
					Log("WSL is already installed.");
					return true;
				}
			}
			else
			{
				Log("Virtualization is not enabled. Please enable Virtualization in your systems BIOS.");
				return false;
			}
		}
		#endregion

		#region Python
		private async Task<bool> IsPythonInstalled()
		{
			string command;
			string arguments;

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				// Check for Python in the App Store directory
				command = "where";
				arguments = "\"%LOCALAPPDATA%\\Microsoft\\WindowsApps\\python.exe\"";

				// Also check for Python in the system PATH
				string wherePythonCommand = "where";
				string wherePythonArguments = "python";

				try
				{
					bool isPythonInWindowsApps = await RunProcessAsync(command, arguments, (e, o, err) =>
					{
						return !string.IsNullOrEmpty(o);
					});

					bool isPythonInPath = await RunProcessAsync(wherePythonCommand, wherePythonArguments, (e, o, err) =>
					{
						return !string.IsNullOrEmpty(o);
					});

					// Return false if Python is found in the WindowsApps directory
					if (isPythonInWindowsApps)
					{
						return false;
					}

					// Return true if Python is found in the system PATH
					return isPythonInPath;
				}
				catch (Exception ex)
				{
					Log($"Error checking Python installation on Windows: {ex.Message}");
					return false; // Return false if an error occurs
				}
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
					 RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				// For Linux and macOS, check for python3 or python in the system PATH
				command = "/bin/bash";
				arguments = "-c \"command -v python3 || command -v python\"";

				try
				{
					return await RunProcessAsync(command, arguments, (e, o, err) =>
					{
						return !string.IsNullOrEmpty(o);
					});
				}
				catch (Exception ex)
				{
					Log($"Error checking Python installation: {ex.Message}");
					return false; // Return false if an error occurs
				}
			}
			else
			{
				throw new PlatformNotSupportedException("Unsupported operating system");
			}
		}

		private async Task InstallPython()
		{
			if (await IsPythonInstalled())
			{
				Log("Python is already installed.");
				return;
			}

			if (!PromptForYesNo("Install Python?"))
			{
				return;
			}

			string command;
			string arguments;

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				command = "powershell.exe";
				arguments = "-Command \"[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest https://www.python.org/ftp/python/3.12.4/python-3.12.4-amd64.exe -OutFile python-installer.exe\"";
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
					 RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				command = "/bin/bash";
				arguments = "-c \"sudo apt-get update && sudo apt-get install -y python3\"";
			}
			else
			{
				throw new PlatformNotSupportedException("Unsupported operating system");
			}

			try
			{
				Log("Installing Python...");

				await RunProcessAsync(command, arguments, null, async (p) =>
				{
					// Install Python silently on Windows
					if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
					{
						p.StartInfo.Arguments = "python-installer.exe /quiet InstallAllUsers=1 PrependPath=1";
						p.Start();
						await p.WaitForExitAsync();
					}

					await UpdatePip();
				});

				Log("Python has been installed.");
			}
			catch (Exception ex)
			{
				Log($"Error installing Python: {ex.Message}");
				return; // Return false if an error occurs
			}
		}

		private async Task UpdatePip()
		{
			try
			{
				Log("Updating pip...");
				string command;
				string arguments;

				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				{
					command = "python";
					arguments = "-m pip install --upgrade pip";
				}
				else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
				{
					command = "/bin/bash";
					arguments = $"-c \"sudo python3 -m pip install --upgrade pip || sudo python -m pip install --upgrade pip\"";
				}
				else
				{
					throw new PlatformNotSupportedException("Unsupported operating system");
				}

				await RunProcessAsync(command, arguments);

				Log("pip updated successfully.");
			}
			catch (Exception ex)
			{
				Log($"Error updating pip: {ex.Message}");
			}
		}
		#endregion

		#region DotNet
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
			Log("DotNet is already installed.");

			if (!await IsDotNetEFInstalledAsync())
			{
				if (PromptForYesNo("DotNet-EF is not installed, would you like to install it?"))
				{
					Log("Installing DotNet-EF v5.0.17...");
					await RunDotNetCommandAsync("tool install --global dotnet-ef --version 5.0.17");
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

		private async Task<bool> IsDotNetInstalledAsync()
		{
			string command;
			string arguments = $"dotnet --version";

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				command = "cmd.exe";
				arguments = $"/c {arguments}";
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
					 RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				command = "/bin/bash";
				arguments = $"-c \"{arguments}\"";
			}
			else
			{
				throw new PlatformNotSupportedException("Unsupported operating system");
			}

			return await RunProcessAsync(command, arguments, (e, o, err) =>
			{
				return e == 0 &&
					   o.Contains("8.0", StringComparison.OrdinalIgnoreCase);
			});
		}

		private async Task DownloadAndInstallDotNetAsync()
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				string installerPath = await DownloadFileAsync("https://download.visualstudio.microsoft.com/download/pr/b6f19ef3-52ca-40b1-b78b-0712d3c8bf4d/426bd0d376479d551ce4d5ac0ecf63a5/dotnet-sdk-8.0.302-win-x64.exe", "dotnet-sdk-8.0.302-win-x64.exe");

				try
				{
					ProcessStartInfo startInfo = new ProcessStartInfo();
					startInfo.FileName = installerPath;
					startInfo.UseShellExecute = true;
					startInfo.Verb = "runas";

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
				string version = "8.0.302";
				string installDir = "/usr/local/share/dotnet";
				string downloadUrl = "https://dot.net/v1/dotnet-install.sh";
				string shScriptFile = "dotnet-install.sh";

				// Download the shell script
				using (HttpClient client = new HttpClient())
				{
					var scriptContent = await client.GetStringAsync(downloadUrl);
					await File.WriteAllTextAsync(shScriptFile, scriptContent);
				}

				// Make the script executable
				await RunProcessAsync("chmod", $"+x {shScriptFile}");

				// Run the shell script
				await RunProcessAsync("/bin/bash",
									  $"./{shScriptFile} --version {version} --install-dir {installDir}",
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

		private async Task<bool> IsDotNetEFInstalledAsync()
		{
			try
			{
				return await RunProcessAsync("dotnet",
											 "tool list --global",
											 (e, o, err) =>
											 {
												 return o.Contains("dotnet-ef");
											 });
			}
			catch (Exception ex)
			{
				Log($"Error checking dotnet-ef tool: {ex.Message}");
				return false; // Return false if an error occurs
			}
		}

		private bool pathSet = false;
		private async Task RunDotNetCommandAsync(string arguments)
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
				RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				if (!pathSet)
				{
					string homePath = Environment.GetEnvironmentVariable("HOME");
					if (string.IsNullOrEmpty(homePath))
					{
						throw new Exception("The HOME environment variable is not set.");
					}

					string currentPath = Environment.GetEnvironmentVariable("PATH");
					if (string.IsNullOrEmpty(currentPath))
					{
						throw new Exception("The PATH environment variable is not set.");
					}

					string dotnetToolsPath = Path.Combine(homePath, ".dotnet", "tools");

					if (!currentPath.Split(':').Contains(dotnetToolsPath))
					{
						string newPath = $"{currentPath}:{dotnetToolsPath}";
						Environment.SetEnvironmentVariable("PATH", newPath);
					}

					string dotnetRoot = "/usr/share/dotnet";
					Environment.SetEnvironmentVariable("DOTNET_ROOT", dotnetRoot);

					pathSet = true;
				}
			}

			Log("Running DotNet Command: \r\n" +
				"dotnet " + arguments);

			await RunProcessAsync("dotnet", arguments,
								  (e, o, err) =>
								  {
									  if (e != 0)
									  {
										  // Handle non-zero exit codes if necessary
										  Debug.Log($"DotNet command failed with exit code {e}.\nError: {err}");
									  }
									  Log(o);
									  return true;
								  });
		}
		#endregion

		#region Docker
		private async Task<bool> InstallDocker()
		{
			if (!await InstallWSL())
			{
				return false;
			}

			if (await IsDockerInstalledAsync())
			{
				Log("Docker is already installed.");
				return true;
			}

			Log("Installing Docker...");

			try
			{
				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				{
					await InstallDockerWindowsAsync();
				}
				else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
				{
					await InstallDockerLinuxAsync();
					await InstallPipLinuxAsync();
				}
				else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
				{
					await InstallDockerMacAsync();
				}
				else
				{
					throw new PlatformNotSupportedException("Unsupported operating system");
				}

				Log("Docker has been installed.");
				return true;
			}
			catch (Exception ex)
			{
				Log($"Installation failed: {ex.Message}");
				return false;
			}
		}

		private async Task InstallDockerWindowsAsync()
		{
			await RunProcessAsync("curl", "-L https://desktop.docker.com/win/stable/Docker%20Desktop%20Installer.exe -o DockerInstaller.exe");
			await RunProcessAsync("DockerInstaller.exe", "/quiet /install");

			// Delete DockerInstaller.exe
			if (File.Exists("DockerInstaller.exe"))
			{
				File.Delete("DockerInstaller.exe");
			}
		}

		private async Task InstallDockerLinuxAsync()
		{
			string command = "curl -fsSL https://get.docker.com -o get-docker.sh && sudo sh get-docker.sh";

			await RunProcessAsync("bash",
								  $"-c \"{command}\"",
								  (e, o, err) =>
								  {
									  if (e != 0)
									  {
										  throw new Exception($"Command '{command}' failed with exit code {e}");
									  }
									  return true;
								  });
		}

		private async Task InstallPipLinuxAsync()
		{
			string command = "sudo apt install -y python3-pip";

			await RunProcessAsync("bash",
								  $"-c \"{command}\"",
								  (e, o, err) =>
								  {
									  if (e != 0)
									  {
										  throw new Exception($"Command '{command}' failed with exit code {e}");
									  }
									  return true;
								  });
		}

		private async Task InstallDockerMacAsync()
		{
			await RunProcessAsync("brew", "install --cask docker");
		}

		private async Task<bool> IsDockerInstalledAsync()
		{
			return await RunProcessAsync(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "where" : "which", "docker",
										 (e, o, err) =>
										 {
											 return e == 0 && !string.IsNullOrWhiteSpace(o);
										 });
		}

		private async Task<string> RunDockerCommandAsync(string commandArgs)
		{
			Log("Running Docker Command: \r\n" +
				"docker " + commandArgs);

			using (Process process = new Process())
			{
				process.StartInfo.FileName = "docker";
				process.StartInfo.Arguments = commandArgs;
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.RedirectStandardError = true;
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.CreateNoWindow = true;

				var outputBuilder = new System.Text.StringBuilder();
				var errorOutputBuilder = new System.Text.StringBuilder();

				process.OutputDataReceived += (sender, e) =>
				{
					if (!string.IsNullOrEmpty(e.Data))
					{
						outputBuilder.AppendLine(e.Data);
					}
				};

				process.ErrorDataReceived += (sender, e) =>
				{
					if (!string.IsNullOrEmpty(e.Data))
					{
						errorOutputBuilder.AppendLine(e.Data);
					}
				};

				process.Start();
				process.BeginOutputReadLine();
				process.BeginErrorReadLine();

				await process.WaitForExitAsync();

				string output = outputBuilder.ToString();
				string errorOutput = errorOutputBuilder.ToString();

				return output + "\r\n" + errorOutput;
			}
		}

		/// <summary>
		/// Docker-Compose commands are not available in the editor.
		/// </summary>
		private async Task<string> RunDockerComposeCommandAsync(string commandArgs, Dictionary<string, string> environmentVariables = null)
		{
			Log("Running Docker-Compose Command: \r\n" +
				"docker-compose " + commandArgs);

			using (Process process = new Process())
			{
				process.StartInfo.FileName = "docker-compose";
				process.StartInfo.Arguments = commandArgs;
				if (environmentVariables != null)
				{
					foreach (KeyValuePair<string, string> kvp in environmentVariables)
					{
						process.StartInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
					}
				}
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.RedirectStandardError = true;
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.CreateNoWindow = true;

				var outputBuilder = new System.Text.StringBuilder();
				var errorOutputBuilder = new System.Text.StringBuilder();

				process.OutputDataReceived += (sender, e) =>
				{
					if (!string.IsNullOrEmpty(e.Data))
					{
						outputBuilder.AppendLine(e.Data);
					}
				};

				process.ErrorDataReceived += (sender, e) =>
				{
					if (!string.IsNullOrEmpty(e.Data))
					{
						errorOutputBuilder.AppendLine(e.Data);
					}
				};

				process.Start();
				process.BeginOutputReadLine();
				process.BeginErrorReadLine();

				await process.WaitForExitAsync();

				string output = outputBuilder.ToString();
				string errorOutput = errorOutputBuilder.ToString();

				return output + "\r\n" + errorOutput;
			}
		}
		#endregion

		#region Database
		private async Task<bool> InstallPostgreSQLWindows(AppSettings appSettings)
		{
			if (!PromptForYesNo("Install PostgreSQL?"))
			{
				if (PromptForYesNo("Install FishMMO Database?"))
				{
					string su = PromptForInput("Enter PostgreSQL Superuser Username: ");
					string sp = PromptForPassword("Enter PostgreSQL Superuser Password: ");

					if (await InstallFishMMODatabase(su, sp, appSettings))
					{
						return true;
					}
					else
					{
						return false;
					}
				}
				else
				{
					return false;
				}
			}

			Log("Installing PostgreSQL.");
			string superUsername = PromptForInput("Enter PostgreSQL Superuser Username: ");
			string superPassword = PromptForPassword("Enter PostgreSQL Superuser Password: ");

			string installerPath = await DownloadFileAsync(@"https://sbp.enterprisedb.com/getfile.jsp?fileid=1259105", "PostgreSQLInstaller.exe");

			try
			{
				string arguments = $"--unattendedmodeui minimal " +
								   $"--mode unattended " +
								   $"--superaccount \"{superUsername}\" " +
								   $"--superpassword \"{superPassword}\" " +
								   $"--serverport {appSettings.Npgsql.Port} " +
								   $"--disable-components pgAdmin,stackbuilder";

				ProcessStartInfo startInfo = new ProcessStartInfo();
				startInfo.FileName = installerPath;
				startInfo.Arguments = arguments;
				startInfo.CreateNoWindow = true;
				startInfo.UseShellExecute = true;
				startInfo.Verb = "runas";

				Process process = Process.Start(startInfo);

				await process.WaitForExitAsync();

				int exitCode = process.ExitCode;
				if (exitCode == 0)
				{
					Log("PostgreSQL installation successful.");

					if (await InstallFishMMODatabase(superUsername, superPassword, appSettings))
					{
						return true;
					}
					else
					{
						return false;
					}
				}
				else
				{
					Log($"PostgreSQL installation failed with exit code {exitCode}.");
					return false;
				}
			}
			catch (Exception ex)
			{
				Log($"Error installing PostgreSQL: {ex.Message}");
				return false;
			}
		}

		private async Task<bool> InstallPostgreSQLLinuxMAC(AppSettings appSettings)
		{
			if (!PromptForYesNo("Install PostgreSQL?"))
			{
				return false;
			}

			Log("Installing PostgreSQL.");
			string superUsername = PromptForInput("Enter PostgreSQL Superuser Username: ");
			string superPassword = PromptForPassword("Enter PostgreSQL Superuser Password: ");

			try
			{
				// Detect platform
				bool isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

				string updateCommand = isMac ? "brew update" : "sudo apt-get update";
				string installCommand = isMac ? "brew install postgresql" : "sudo apt-get install -y postgresql postgresql-contrib";
				string startCommand = isMac ? "brew services start postgresql" : "sudo systemctl start postgresql";
				string enableCommand = isMac ? null : "sudo systemctl enable postgresql";
				string createUserCommand = $"-c \"CREATE USER {superUsername} WITH SUPERUSER PASSWORD '{superPassword}';\"";

				bool updateSuccess = await RunProcessAsync("/bin/bash", $"-c \"{updateCommand}\"",
					(exitCode, output, error) =>
					{
						if (exitCode != 0)
						{
							Log($"Failed to update package lists. Exit code: {exitCode}\nError: {error}");
							return false;
						}
						return true;
					});

				if (!updateSuccess) return false;

				bool installSuccess = await RunProcessAsync("/bin/bash", $"-c \"{installCommand}\"",
					(exitCode, output, error) =>
					{
						if (exitCode != 0)
						{
							Log($"Failed to install PostgreSQL. Exit code: {exitCode}\nError: {error}");
							return false;
						}
						return true;
					});

				if (!installSuccess) return false;

				bool startSuccess = await RunProcessAsync("/bin/bash", $"-c \"{startCommand}\"",
					(exitCode, output, error) =>
					{
						if (exitCode != 0)
						{
							Log($"Failed to start PostgreSQL. Exit code: {exitCode}\nError: {error}");
							return false;
						}
						return true;
					});

				if (!startSuccess) return false;

				if (!isMac)
				{
					bool enableSuccess = await RunProcessAsync("/bin/bash", $"-c \"{enableCommand}\"",
						(exitCode, output, error) =>
						{
							if (exitCode != 0)
							{
								Log($"Failed to enable PostgreSQL to start on boot. Exit code: {exitCode}\nError: {error}");
								return false;
							}
							return true;
						});

					if (!enableSuccess) return false;
				}

				if (PromptForYesNo("Create PostgreSQL super user?"))
				{
					bool createUserSuccess = await RunProcessAsync("/bin/bash", $"-c \"sudo -u postgres psql {createUserCommand}\"",
					(exitCode, output, error) =>
					{
						if (exitCode != 0)
						{
							Log($"Failed to create PostgreSQL superuser. Exit code: {exitCode}\nError: {error}");
							return false;
						}
						return true;
					});

					if (!createUserSuccess) return false;
				}

				Log("PostgreSQL installation successful.");

				if (await InstallFishMMODatabase(superUsername, superPassword, appSettings))
				{
					return true;
				}
				else
				{
					return false;
				}
			}
			catch (Exception ex)
			{
				Log($"Error installing PostgreSQL: {ex.Message}");
				return false;
			}
		}

		public async Task<bool> InstallFishMMODatabase(string superUsername, string superPassword, AppSettings appSettings)
		{
			try
			{
				Log($"Installing FishMMO Database {appSettings.Npgsql.Database}");
				string connectionString = $"Host={appSettings.Npgsql.Host};Port={appSettings.Npgsql.Port};Username={superUsername};Password={superPassword};";

				// Connect to PostgreSQL
				using (var connection = new NpgsqlConnection(connectionString))
				{
					await connection.OpenAsync();

					// Create database
					if (PromptForYesNo($"Create Database {appSettings.Npgsql.Database}?"))
					{
						await CreateDatabase(connection, appSettings.Npgsql.Database);
					}

					if (PromptForYesNo($"Create User Role {appSettings.Npgsql.Username}?"))
					{
						// Create user role
						await CreateUser(connection, appSettings.Npgsql.Username, appSettings.Npgsql.Password);

						// Grant privileges
						await GrantPrivileges(connection, appSettings.Npgsql.Username, appSettings.Npgsql.Database);
					}

					Log("FishMMO Database installed.");
					return true;
				}
			}
			catch (Exception ex)
			{
				Log($"Error installing database: {ex.Message}");
				return false;
			}
		}

		private async Task CreateDatabase(NpgsqlConnection connection, string dbName)
		{
			using (var command = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", connection))
			{
				await command.ExecuteNonQueryAsync();
			}
		}

		private async Task CreateUser(NpgsqlConnection connection, string username, string password)
		{
			using (var command = new NpgsqlCommand($"CREATE ROLE \"{username}\" WITH LOGIN PASSWORD '{password}'", connection))
			{
				await command.ExecuteNonQueryAsync();
			}
		}

		private async Task GrantPrivileges(NpgsqlConnection connection, string username, string dbName)
		{
			using (var command = new NpgsqlCommand($"GRANT ALL PRIVILEGES ON DATABASE \"{dbName}\" TO \"{username}\"", connection))
			{
				await command.ExecuteNonQueryAsync();
			}
			using (var command = new NpgsqlCommand($"ALTER DATABASE \"{dbName}\" OWNER TO \"{username}\"", connection))
			{
				await command.ExecuteNonQueryAsync();
			}
		}

		private async Task InstallDatabase()
		{
			Log("Installing database...");

			string workingDirectory = GetWorkingDirectory();
			//Log(workingDirectory);

			string appSettingsPath = Path.Combine(workingDirectory, "appsettings.json");
			//Log(appSettingsPath);

			if (File.Exists(appSettingsPath))
			{
				string jsonContent = File.ReadAllText(appSettingsPath);

				//Log(jsonContent);

				AppSettings appSettings = JsonUtility.FromJson<AppSettings>(jsonContent);

				if (PromptForYesNo("Install Docker Database?"))
				{
					if (!await InstallDocker())
					{
						Log("Failed to install Docker.");
						return;
					}

					// docker-compose up
					string output = await RunDockerComposeCommandAsync("-p " + Constants.Configuration.ProjectName.ToLower() + " up -d", new Dictionary<string, string>()
					{
						{ "POSTGRES_DB", appSettings.Npgsql.Database },
						{ "POSTGRES_USER", appSettings.Npgsql.Username },
						{ "POSTGRES_PASSWORD", appSettings.Npgsql.Password },
						{ "POSTGRES_PORT", appSettings.Npgsql.Port },
						{ "REDIS_PORT", appSettings.Redis.Port },
						{ "REDIS_PASSWORD", appSettings.Redis.Password },
					});
					Log(output);
				}
				else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				{
					if (!await InstallPostgreSQLWindows(appSettings))
					{
						return;
					}
				}
				else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
						 RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
				{
					if (!await InstallPostgreSQLLinuxMAC(appSettings))
					{
						return;
					}
				}

				if (PromptForYesNo("Create Initial Migration?"))
				{
					// Run 'dotnet ef migrations add Initial' command
					Log("Creating Initial database migration...");
					await RunDotNetCommandAsync($"ef migrations add Initial -p {Constants.Configuration.ProjectPath} -s {Constants.Configuration.StartupProject}");

					// Run 'dotnet ef database update' command
					Log("Updating database...");
					await RunDotNetCommandAsync($"ef database update -p {Constants.Configuration.ProjectPath} -s {Constants.Configuration.StartupProject}");

					StartCoroutine(NetHelper.FetchExternalIPAddress((ip) =>
					{
						Log("Databases are ready:\r\n" +
						"Npgsql Database: " + appSettings.Npgsql.Database + "\r\n" +
						"Npgsql Username: " + appSettings.Npgsql.Username + "\r\n" +
						"Npgsql Password: " + appSettings.Npgsql.Password + "\r\n" +
						"Npgsql Host: " + ip + "\r\n" +
						"Npgsql Port: " + appSettings.Npgsql.Port + "\r\n" +
						"Redis Host: " + ip + "\r\n" +
						"Redis Port: " + appSettings.Redis.Port + "\r\n" +
						"Redis Password: " + appSettings.Redis.Password);
					}));
				}
			}
			else
			{
				Log("appsettings.json file not found.");
			}
		}

		private async void UpdateDatabase()
		{
			string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
			Log($"Updating the database at {timestamp}...");

			// Run 'dotnet ef database update' command
			await RunDotNetCommandAsync($"ef database update -p {Constants.Configuration.ProjectPath}  -s  {Constants.Configuration.StartupProject}");

			Log($"Database Update completed...");
		}

		private async void CreateMigration()
		{
			string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
			Log($"Creating a new migration {timestamp}...");

			// Run 'dotnet ef migrations add Initial' command
			await RunDotNetCommandAsync($"ef migrations add {timestamp} -p {Constants.Configuration.ProjectPath} -s {Constants.Configuration.StartupProject}");

			// Run 'dotnet ef database update' command
			await RunDotNetCommandAsync($"ef database update -p {Constants.Configuration.ProjectPath}  -s  {Constants.Configuration.StartupProject}");

			Log($"Migration completed...");
		}
		#endregion
	}
}