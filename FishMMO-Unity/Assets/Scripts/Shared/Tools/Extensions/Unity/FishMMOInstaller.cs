using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Debug = UnityEngine.Debug;
using FishMMO.Database;
using Npgsql;

namespace FishMMO.Shared
{
	public class FishMMOInstaller : MonoBehaviour
	{
		private async void Awake()
		{
			while (true)
			{
				Console.Clear(); // Clear the console at the beginning of each loop iteration
				Console.WriteLine("Welcome to the FishMMO Database Tool.");
				Console.WriteLine("Press a key (1-6):");
				Console.WriteLine("1 : Install Everything");
				Console.WriteLine("2 : Install DotNet");
				Console.WriteLine("3 : Install Python");
				Console.WriteLine("4 : Install Database");
				Console.WriteLine("5 : Create Migration");
				Console.WriteLine("6 : Quit");

				ConsoleKeyInfo key = Console.ReadKey(true); // Read key and don't show it in the console

				switch (key.Key)
				{
					case ConsoleKey.D1:
						await InstallEverything();
						break;
					case ConsoleKey.D2:
						await InstallDotNet();
						break;
					case ConsoleKey.D3:
						await InstallPython();
						break;
					case ConsoleKey.D4:
						await InstallDatabase();
						break;
					case ConsoleKey.D5:
						await CreateMigration();
						break;
					case ConsoleKey.D6:
#if UNITY_EDITOR
						EditorApplication.ExitPlaymode(); // Make sure to include the UnityEditor namespace if using Unity
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

		private async Task<bool> InstallEverything()
		{
			if (!await InstallDotNet())
			{
				Log("DotNet 8 and DotNet-EF Tools 5.0.17 are required by FishMMO. Please install them and try again.");
				return false;
			}
			await InstallPython();
			await InstallDatabase();

			return true;
		}

		private string GetWorkingDirectory()
		{
			return AppDomain.CurrentDomain.BaseDirectory;
		}

		/// <summary>
		/// Runs a process asynchronously.
		/// ProcessResult = ExitCode, Standard Output, Standard Error
		/// </summary>
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
			Console.WriteLine("Press any key to continue...");
			Console.ReadKey(true); // Wait for user to press a key
		}

		private async Task<string> DownloadFileAsync(string url, string fileName = "tempFileName.tmpFile")
		{
			try
			{
				string tempDir = GetWorkingDirectory();
				string outputPath = Path.Combine(tempDir, fileName);

				if (File.Exists(outputPath))
				{
					Console.WriteLine(outputPath + " already exists... Skipping download.");
					return outputPath;
				}

				Console.WriteLine($"Downloading file from {url}");
				Console.WriteLine("Please wait...");
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
					Console.WriteLine($"File successfully downloaded to {outputPath}");
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
			return await RunProcessAsync("systeminfo", "",
								(e, o, err) => { return o.Contains("Virtualization Enabled In Firmware: Yes") || o.Contains("A hypervisor has been detected."); });
		}

		private async Task<bool> IsHyperVEnabledAsync()
		{
			return await RunProcessAsync("powershell.exe", "-Command \"Get-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V-All\"",
								(e, o, err) => { return o.Contains("State : Enabled"); });
		}

		private async Task<bool> IsWSLInstalledAsync()
		{
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				return true;
			}
			return await RunProcessAsync("where", "/q wsl.exe");
		}

		private async Task<bool> InstallWSL()
		{
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				return true;
			}

			bool virtualization = await IsVirtualizationEnabledAsync();
			bool hyperV = await IsHyperVEnabledAsync();

			if (virtualization || hyperV)
			{
				if (!await IsWSLInstalledAsync())
				{
					if (PromptForYesNo("Windows Subsystem for Linux needs to be installed for Docker. Would you like to install it?"))
					{
						Console.WriteLine("Installing WSL...");
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
				// Check for Python install in the registry
				command = "reg";
				arguments = "query HKEY_LOCAL_MACHINE\\SOFTWARE\\Python\\PythonCore /s";

				try
				{
					return await RunProcessAsync(command, arguments);
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
				Console.WriteLine("Downloading Python...");

				if (await RunProcessAsync(command, arguments, (e, o, err) =>
								  {
									  if (e != 0)
									  {
										  // Handle non-zero exit codes if necessary
										  throw new Exception($"Python Download failed with exit code {e}.\nError: {err}");
									  }
									  Log(o);
									  return true;
								  }))
				{
					// Install Python silently on Windows
					if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
					{
						Console.WriteLine("Installing Python...");

						if (await RunProcessAsync("python-installer.exe", "/quiet InstallAllUsers=1 PrependPath=1", (e, o, err) =>
									{
										if (e != 0)
										{
											// Handle non-zero exit codes if necessary
											throw new Exception($"Python Install failed with exit code {e}.\nError: {err}");
										}
										Log(o);
										return true;
									}))
						{
							Log("Python has been installed.");

							await UpdatePip();
						}
					}
					else
					{
						Log("Python has been installed.");

						await UpdatePip();
					}
				}
			}
			catch (Exception ex)
			{
				Log($"Error installing Python: {ex.Message}");
			}
		}

		private async Task UpdatePip()
		{
			try
			{
				Console.WriteLine("Updating pip...");
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
					Console.WriteLine("Installing DotNet...");
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
				Console.WriteLine("DotNet is already installed.");
			}

			if (!await IsDotNetEFInstalledAsync())
			{
				if (PromptForYesNo("DotNet-EF is not installed, would you like to install it?"))
				{
					Console.WriteLine("Installing DotNet-EF v5.0.17...");
					await RunDotNetCommandAsync("tool install --global dotnet-ef --version 5.0.17");

					Log("DotNet-EF is has been installed.");
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

			Console.WriteLine("Running DotNet Command: \r\n" +
				"dotnet " + arguments);

			await RunProcessAsync("dotnet", arguments,
								  (e, o, err) =>
								  {
									  if (e != 0)
									  {
										  // Handle non-zero exit codes if necessary
										  Log($"DotNet command failed with exit code {e}.\nError: {err}");
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

			Console.WriteLine("Installing Docker...");

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
			Console.WriteLine("Running Docker Command: \r\n" +
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
			Console.WriteLine("Running Docker-Compose Command: \r\n" +
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
			string superUsername = "postgres";//PromptForInput("Enter PostgreSQL Superuser Username: ");
			string superPassword = PromptForPassword("Enter new PostgreSQL Superuser Password (Username is default to \"postgres\"): ");

			if (!PromptForYesNo("Install PostgreSQL?"))
			{
				return false;
			}

			Console.WriteLine("Installing PostgreSQL...");

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
					return true;
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

		private async Task<bool> InstallPostgreSQLLinuxMAC()
		{
			if (PromptForYesNo("Install PostgreSQL?"))
			{
				Console.WriteLine("Installing PostgreSQL...");

				try
				{
					// Detect platform
					bool isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

					string updateCommand = isMac ? "brew update" : "sudo apt-get update";
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

					string installCommand = isMac ? "brew install postgresql" : "sudo apt-get install -y postgresql postgresql-contrib";
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

					string startCommand = isMac ? "brew services start postgresql" : "sudo systemctl start postgresql";
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
						string enableCommand = isMac ? null : "sudo systemctl enable postgresql";

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

					if (PromptForYesNo("Update PostgreSQL Superuser Password?"))
					{
						string superUsername = "postgres";
						string superPassword = PromptForPassword("Enter new PostgreSQL Superuser Password (Username is default to \"postgres\"): ");

						string updateUserCommand = $"ALTER USER {superUsername} WITH PASSWORD '{superPassword}';";

						bool updateUserSuccess = await RunProcessAsync("/bin/bash", $"-c \"sudo -u postgres psql -c \\\"{updateUserCommand}\\\"\"",
						(exitCode, output, error) =>
						{
							if (exitCode != 0)
							{
								Log($"Failed to update PostgreSQL superuser. Exit code: {exitCode}\nError: {error}");
								return false;
							}
							return true;
						});

						if (!updateUserSuccess) return false;
					}

					Log("PostgreSQL installation successful.");
				}
				catch (Exception ex)
				{
					Log($"Error installing PostgreSQL: {ex.Message}");
					return false;
				}
			}
			return true;
		}

		public async Task<bool> InstallFishMMODatabase(string superUsername, string superPassword, AppSettings appSettings)
		{
			try
			{
				Console.WriteLine($"Installing FishMMO Database {appSettings.Npgsql.Database} using appsettings.json");
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
			Console.Clear();
			Console.WriteLine("Installing Database...");

			string workingDirectory = GetWorkingDirectory();
			//Log(workingDirectory);

			string appSettingsPath = Path.Combine(workingDirectory, "appsettings.json");
			//Log(appSettingsPath);

			if (File.Exists(appSettingsPath))
			{
				string jsonContent = File.ReadAllText(appSettingsPath);

				//Log(jsonContent);

				AppSettings appSettings = JsonUtility.FromJson<AppSettings>(jsonContent);

				bool skip = false;
				while (!skip)
				{
					Console.WriteLine("Press a key (0-3):");
					//Console.WriteLine($"1 : Install Docker Database");
					Console.WriteLine($"1 : Install PostgreSQL");
					Console.WriteLine($"2 : Install FishMMO Database");
					Console.WriteLine($"3 : Return to Main Menu");
					Console.WriteLine($"0 : Quit");
					ConsoleKeyInfo key = Console.ReadKey(true); // Read key and don't show it in the console

					switch (key.Key)
					{
						/*case ConsoleKey.D1:
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

							skip = true;
							break;*/
						case ConsoleKey.D1:
							if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
							{
								if (!await InstallPostgreSQLWindows(appSettings))
								{
									continue;
								}
							}
							else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
									 RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
							{
								if (!await InstallPostgreSQLLinuxMAC())
								{
									continue;
								}
							}
							break;
						case ConsoleKey.D2:
							string superUsername = "postgres";//RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? PromptForInput("Enter PostgreSQL Superuser Username: ") : "postgres";
							string superPassword = PromptForPassword("Enter PostgreSQL Superuser Password: ");

							if (!await InstallFishMMODatabase(superUsername, superPassword, appSettings))
							{
								continue;
							}
							else
							{
								if (PromptForYesNo("Create Initial Migration?"))
								{
									// Run 'dotnet ef migrations add Initial' command
									Console.WriteLine("Creating Initial database migration...");
									await RunDotNetCommandAsync($"ef migrations add Initial -p {Constants.Configuration.ProjectPath} -s {Constants.Configuration.StartupProject}");

									// Run 'dotnet ef database update' command
									Console.WriteLine("Updating database...");
									await RunDotNetCommandAsync($"ef database update -p {Constants.Configuration.ProjectPath} -s {Constants.Configuration.StartupProject}");

									Log($"Initial Migration completed...");
								}
							}
							break;
						case ConsoleKey.D3:
							skip = true;
							break;
						case ConsoleKey.D0:
#if UNITY_EDITOR
							EditorApplication.ExitPlaymode(); // Make sure to include the UnityEditor namespace if using Unity
#else
                			Application.Quit();
#endif
							return; // Exit the method
						default:
							Console.WriteLine("Invalid input. Please enter a valid number.");
							continue;
					}
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
			
			Console.WriteLine($"Updating the database at {timestamp}...");

			// Run 'dotnet ef database update' command
			await RunDotNetCommandAsync($"ef database update -p {Constants.Configuration.ProjectPath}  -s  {Constants.Configuration.StartupProject}");

			Log($"Database Update completed...");
		}

		private async Task CreateMigration()
		{
			string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

			Console.WriteLine($"Creating a new migration {timestamp}...");

			// Run 'dotnet ef migrations add Initial' command
			await RunDotNetCommandAsync($"ef migrations add {timestamp} -p {Constants.Configuration.ProjectPath} -s {Constants.Configuration.StartupProject}");

			Log($"Updating the database at {timestamp}...");
			
			// Run 'dotnet ef database update' command
			await RunDotNetCommandAsync($"ef database update -p {Constants.Configuration.ProjectPath}  -s  {Constants.Configuration.StartupProject}");

			Log($"Migration completed...");
		}
		#endregion
	}
}