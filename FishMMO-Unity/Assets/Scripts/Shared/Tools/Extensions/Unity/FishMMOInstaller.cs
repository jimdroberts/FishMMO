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
#if UNITY_EDITOR
using UnityEditor;
#endif

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
			await InstallWSL();
			await InstallDotNet();
			await InstallPython();
			await InstallDatabase();
		}

		private string GetWorkingDirectory()
		{
#if UNITY_EDITOR
			return Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
#else
			return AppDomain.CurrentDomain.BaseDirectory;
#endif
		}

		private async Task RunDismCommandAsync(string arguments)
		{
			using (Process process = new Process())
			{
				process.StartInfo.FileName = "dism.exe";
				process.StartInfo.Arguments = arguments;
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.CreateNoWindow = true;

				process.Start();
				await process.WaitForExitAsync(); // Use asynchronous wait
			}
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
			using (Process process = new Process())
			{
				process.StartInfo.FileName = "powershell.exe";
				process.StartInfo.Arguments = "-Command \"(Get-WmiObject -Namespace 'root\\cimv2' -Class Win32_Processor).VirtualizationFirmwareEnabled\"";
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.CreateNoWindow = true;
				process.Start();

				string output = process.StandardOutput.ReadToEnd();
				await process.WaitForExitAsync();

				return bool.Parse(output.Trim());
			}
		}

		private async Task<bool> IsWSLInstalledAsync()
		{
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				return true;
			}

			using (Process process = new Process())
			{
				process.StartInfo.FileName = "where";
				process.StartInfo.Arguments = "/q wsl.exe";
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.CreateNoWindow = true;

				process.Start();
				string output = await process.StandardOutput.ReadToEndAsync();
				await process.WaitForExitAsync();

				return process.ExitCode == 0;
			}
		}

		private async Task InstallWSL()
		{
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				return;
			}

			if (await IsVirtualizationEnabledAsync())
			{
				if (!await IsWSLInstalledAsync())
				{
					Log("Installing WSL...");
					await RunDismCommandAsync("/online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux /all /norestart");
					Log("WSL has been installed.");
				}
				else
				{
					Log("WSL is already installed.");
				}
			}
			else
			{
				Log("Virtualization is not enabled. Please enable Virtualization in your system BIOS.");
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
				command = "where";
				arguments = "python";
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
					 RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				command = "/bin/bash";
				arguments = "-c \"command -v python3 || command -v python\"";
			}
			else
			{
				throw new PlatformNotSupportedException("Unsupported operating system");
			}

			try
			{
				using (Process process = new Process())
				{
					process.StartInfo.FileName = command;
					process.StartInfo.Arguments = arguments;
					process.StartInfo.UseShellExecute = false;
					process.StartInfo.RedirectStandardOutput = true;
					process.StartInfo.CreateNoWindow = true;

					process.Start();
					string output = await process.StandardOutput.ReadToEndAsync();
					await process.WaitForExitAsync();

					// Check if 'python.exe' is found in the output
					return output.Contains("python");
				}
			}
			catch (Exception ex)
			{
				Log($"Error checking Python installation on Windows: {ex.Message}");
				return false; // Return false if an error occurs
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
				using (Process process = new Process())
				{
					process.StartInfo.FileName = command;
					process.StartInfo.Arguments = arguments;
					process.StartInfo.UseShellExecute = false;
					process.StartInfo.RedirectStandardOutput = true;
					process.StartInfo.CreateNoWindow = true;

					process.Start();
					await process.WaitForExitAsync();

					// Install Python silently on Windows
					if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
					{
						process.StartInfo.Arguments = "python-installer.exe /quiet InstallAllUsers=1 PrependPath=1";
						process.Start();
						await process.WaitForExitAsync();
					}

					await UpdatePip();

					Log("Python has been installed.");
					return; // Installation successful
				}
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
				using (Process process = new Process())
				{
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

					process.StartInfo.FileName = command;
					process.StartInfo.Arguments = arguments;
					process.StartInfo.UseShellExecute = false;
					process.StartInfo.RedirectStandardOutput = true;
					process.StartInfo.CreateNoWindow = true;

					process.Start();
					await process.WaitForExitAsync();

					Log("pip updated successfully.");
				}
			}
			catch (Exception ex)
			{
				Log($"Error updating pip: {ex.Message}");
			}
		}
		#endregion

		#region DotNet
		private async Task InstallDotNet()
		{
			if (!await IsDotNetInstalledAsync())
			{
				Log("Installing DotNet...");
				await DownloadAndInstallDotNetAsync();
				Log("DotNet has been installed.");
			}
			else
			{
				Log("DotNet is already installed.");

				if (!await IsDotNetEFInstalledAsync())
				{
					Log("Installing DotNet-EF v5.0.17...");
					string output = await RunDotNetCommandAsync("tool install --global dotnet-ef --version 5.0.17");
					Log(output);
				}
				else
				{
					Log("DotNet-EF is already installed.");
				}
			}
		}

		private async Task<bool> IsDotNetInstalledAsync()
		{
			string command;
			string arguments = "dotnet";

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				command = "where";
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
					 RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				command = "which";
			}
			else
			{
				throw new PlatformNotSupportedException("Unsupported operating system");
			}

			using (Process process = new Process())
			{
				process.StartInfo.FileName = command;
				process.StartInfo.Arguments = arguments;
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.CreateNoWindow = true;

				process.Start();
				await process.WaitForExitAsync(); // Use asynchronous wait

				return process.ExitCode == 0;
			}
		}

		private async Task DownloadAndInstallDotNetAsync()
		{
			string version = "7.0.202";
			string installDir;

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				installDir = "C:\\Program Files\\dotnet";
				string downloadUrl = "https://dot.net/v1/dotnet-install.ps1";
				string psScriptFile = "dotnet-install.ps1";

				// Download the PowerShell script
				using (HttpClient client = new HttpClient())
				{
					var scriptContent = await client.GetStringAsync(downloadUrl);
					await File.WriteAllTextAsync(psScriptFile, scriptContent);
				}

				// Run the PowerShell script
				using (Process process = new Process())
				{
					process.StartInfo.FileName = "powershell";
					process.StartInfo.Arguments = $"-ExecutionPolicy Bypass -Command \"& .\\{psScriptFile} -Version {version} -InstallDir '{installDir}'\"";
					process.StartInfo.CreateNoWindow = true;
					process.StartInfo.UseShellExecute = false;
					process.StartInfo.RedirectStandardOutput = true;
					process.StartInfo.RedirectStandardError = true;

					process.Start();
					string output = await process.StandardOutput.ReadToEndAsync();
					string error = await process.StandardError.ReadToEndAsync();
					await process.WaitForExitAsync();

					if (process.ExitCode != 0)
					{
						throw new Exception($"PowerShell script failed with exit code {process.ExitCode}: {error}");
					}
				}
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
					 RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				installDir = "/usr/local/share/dotnet";
				string downloadUrl = "https://dot.net/v1/dotnet-install.sh";
				string shScriptFile = "dotnet-install.sh";

				// Download the shell script
				using (HttpClient client = new HttpClient())
				{
					var scriptContent = await client.GetStringAsync(downloadUrl);
					await File.WriteAllTextAsync(shScriptFile, scriptContent);
				}

				// Make the script executable
				using (Process chmodProcess = new Process())
				{
					chmodProcess.StartInfo.FileName = "chmod";
					chmodProcess.StartInfo.Arguments = $"+x {shScriptFile}";
					chmodProcess.StartInfo.CreateNoWindow = true;
					chmodProcess.StartInfo.UseShellExecute = false;
					chmodProcess.StartInfo.RedirectStandardOutput = true;
					chmodProcess.StartInfo.RedirectStandardError = true;

					chmodProcess.Start();
					await chmodProcess.WaitForExitAsync();
				}

				// Run the shell script
				using (Process process = new Process())
				{
					process.StartInfo.FileName = "/bin/bash";
					process.StartInfo.Arguments = $"./{shScriptFile} --version {version} --install-dir {installDir}";
					process.StartInfo.CreateNoWindow = true;
					process.StartInfo.UseShellExecute = false;
					process.StartInfo.RedirectStandardOutput = true;
					process.StartInfo.RedirectStandardError = true;

					process.Start();
					string output = await process.StandardOutput.ReadToEndAsync();
					string error = await process.StandardError.ReadToEndAsync();
					await process.WaitForExitAsync();

					if (process.ExitCode != 0)
					{
						throw new Exception($"Shell script failed with exit code {process.ExitCode}: {error}");
					}
				}
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
				using (Process process = new Process())
				{
					process.StartInfo.FileName = "dotnet";
					process.StartInfo.Arguments = "tool list --global";
					process.StartInfo.UseShellExecute = false;
					process.StartInfo.RedirectStandardOutput = true;
					process.StartInfo.CreateNoWindow = true;

					process.Start();

					// Asynchronously read the standard output of the spawned process.
					string output = await process.StandardOutput.ReadToEndAsync();

					// Asynchronously wait for the process to exit.
					await process.WaitForExitAsync();

					// Check if 'dotnet-ef' is in the list of installed tools.
					return output.Contains("dotnet-ef");
				}
			}
			catch (Exception ex)
			{
				Log($"Error checking dotnet-ef tool: {ex.Message}");
				return false; // Return false if an error occurs
			}
		}

		private bool pathSet = false;
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
		private async Task<string> RunDotNetCommandAsync(string arguments)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
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
#if !UNITY_EDITOR
			Log("Running DotNet Command: \r\n" +
				"dotnet " + arguments);

			using (Process process = new Process())
			{
				process.StartInfo.FileName = "dotnet";
				process.StartInfo.Arguments = arguments;
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.RedirectStandardError = true;
				process.StartInfo.CreateNoWindow = true;

				process.Start();
				string output = await process.StandardOutput.ReadToEndAsync(); // Use asynchronous read
				string error = await process.StandardError.ReadToEndAsync();
				await process.WaitForExitAsync(); // Use asynchronous wait

				if (process.ExitCode != 0)
				{
					// Handle non-zero exit codes if necessary
					Debug.Log($"DotNet command failed with exit code {process.ExitCode}.\nError: {error}");
				}

				return output;
			}
#else
			return null;
#endif
		}
		#endregion

		#region Docker
		private async Task InstallDocker()
		{
			if (await IsDockerInstalledAsync())
			{
				Log("Docker is already installed.");
				return;
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
			}
			catch (Exception ex)
			{
				Log($"Installation failed: {ex.Message}");
			}
		}

		private async Task InstallDockerWindowsAsync()
		{
			using (Process process = new Process())
			{
				process.StartInfo.FileName = "curl";
				process.StartInfo.Arguments = "-L https://desktop.docker.com/win/stable/Docker%20Desktop%20Installer.exe -o DockerInstaller.exe";
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.RedirectStandardError = true;
				process.StartInfo.CreateNoWindow = true;
				process.Start();
				await process.WaitForExitAsync();
			}

			using (Process process = new Process())
			{
				process.StartInfo.FileName = "DockerInstaller.exe";
				process.StartInfo.Arguments = "/quiet /install";
				process.StartInfo.UseShellExecute = true;  // Using shell execute to allow for installer GUI if needed
				process.Start();
				process.WaitForExit();
			}

			// Delete DockerInstaller.exe
			if (File.Exists("DockerInstaller.exe"))
			{
				File.Delete("DockerInstaller.exe");
			}
		}

		private async Task InstallDockerLinuxAsync()
		{
			string command = "curl -fsSL https://get.docker.com -o get-docker.sh && sudo sh get-docker.sh";

			using (Process process = new Process())
			{
				process.StartInfo.FileName = "bash";
				process.StartInfo.Arguments = $"-c \"{command}\"";
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.RedirectStandardError = true;
				process.StartInfo.CreateNoWindow = true;
				process.Start();
				await process.WaitForExitAsync();
				if (process.ExitCode != 0)
				{
					throw new Exception($"Command '{command}' failed with exit code {process.ExitCode}");
				}
			}
		}

		private async Task InstallPipLinuxAsync()
		{
			string command = "sudo apt install -y python3-pip";

			using (Process process = new Process())
			{
				process.StartInfo.FileName = "bash";
				process.StartInfo.Arguments = $"-c \"{command}\"";
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.RedirectStandardError = true;
				process.StartInfo.CreateNoWindow = true;
				process.Start();
				await process.WaitForExitAsync();
				if (process.ExitCode != 0)
				{
					throw new Exception($"Command '{command}' failed with exit code {process.ExitCode}");
				}
			}
		}

		private async Task InstallDockerMacAsync()
		{
			using (Process process = new Process())
			{
				process.StartInfo.FileName = "brew";
				process.StartInfo.Arguments = "install --cask docker";
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.RedirectStandardError = true;
				process.StartInfo.CreateNoWindow = true;
				process.Start();
				await process.WaitForExitAsync();
			}
		}

		private async Task<bool> IsDockerInstalledAsync()
		{
			using (Process process = new Process())
			{
				process.StartInfo.FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "where" : "which";
				process.StartInfo.Arguments = "docker";
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.RedirectStandardError = true;
				process.StartInfo.CreateNoWindow = true;

				process.Start();
				await process.WaitForExitAsync();

				string output = await process.StandardOutput.ReadToEndAsync();
				string error = await process.StandardError.ReadToEndAsync();

				return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output);
			}
		}

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
		private async Task<string> RunDockerCommandAsync(string commandArgs)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
		{
#if !UNITY_EDITOR
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
#else
			return null;
#endif
		}

		/// <summary>
		/// Docker-Compose commands are not available in the editor.
		/// </summary>
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
		private async Task<string> RunDockerComposeCommandAsync(string commandArgs, Dictionary<string, string> environmentVariables = null)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
		{
#if !UNITY_EDITOR
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
#else
			return null;
#endif
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
					await CreateDatabase(connection, appSettings.Npgsql.Database);

					// Create user role
					await CreateUser(connection, appSettings.Npgsql.Username, appSettings.Npgsql.Password);

					// Grant privileges
					await GrantPrivileges(connection, appSettings.Npgsql.Username, appSettings.Npgsql.Database);

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

#if UNITY_EDITOR
			string setup = Path.Combine(workingDirectory, Constants.Configuration.SetupDirectory);
			//Log(setup);

			string envConfigurationPath = Path.Combine(setup, "Development");
			//Log(envConfigurationPath);

			string appSettingsPath = Path.Combine(envConfigurationPath, "appsettings.json");
			//Log(appSettingsPath);

			if (File.Exists(appSettingsPath))
			{
#else
			string appSettingsPath = Path.Combine(workingDirectory, "appsettings.json");
			//Log(appSettingsPath);

			if (File.Exists(appSettingsPath))
			{
#endif
				string jsonContent = File.ReadAllText(appSettingsPath);

				//Log(jsonContent);

				AppSettings appSettings = JsonUtility.FromJson<AppSettings>(jsonContent);

				if (PromptForYesNo("Install Docker Database?"))
				{
					await InstallDocker();

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
				else if (!await InstallPostgreSQLWindows(appSettings))
				{
					return;
				}

				if (PromptForYesNo("Create Initial Migration?"))
				{
					// Run 'dotnet ef migrations add Initial' command
					Log("Creating Initial database migration...");
					string migrationOut = await RunDotNetCommandAsync($"ef migrations add Initial -p {Constants.Configuration.ProjectPath} -s {Constants.Configuration.StartupProject}");
					Log(migrationOut);

					// Run 'dotnet ef database update' command
					Log("Updating database...");
					string updateOut = await RunDotNetCommandAsync($"ef database update -p {Constants.Configuration.ProjectPath} -s {Constants.Configuration.StartupProject}");
					Log(updateOut);

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