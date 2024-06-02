using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;
using TMPro;
using FishMMO.Database;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishMMO.Shared
{
	public class FishMMOInstaller : MonoBehaviour
	{
		public TMP_Text OutputLog;
		public TMP_InputField CommandInput;
		public List<Button> Buttons = new List<Button>();

		private void Awake()
		{
#if !UNITY_EDITOR
			InstallEverything();
#endif
		}

		public void InstallEverything()
		{
			InstallWSL();
			InstallDotNet();
			InstallDocker();
			InstallDatabase();
		}

		private void Log(string message)
		{
			if (string.IsNullOrWhiteSpace(message))
			{
				return;
			}
			Debug.Log($"{DateTime.Now}: {message}");
			if (OutputLog != null)
			{
				OutputLog.text += message + "\r\n";
			}
		}

		private void SetButtonsActive(bool active)
		{
			if (Buttons != null)
			{
				foreach (Button b in Buttons)
				{
					b.enabled = active;
				}
			}
		}

		public async void OnSubmitCommand()
		{
			SetButtonsActive(false);
			if (CommandInput == null)
			{
				SetButtonsActive(true);
				return;
			}
			if (string.IsNullOrWhiteSpace(CommandInput.text))
			{
				SetButtonsActive(true);
				return;
			}
			if (CommandInput.text.StartsWith("docker"))
			{
				string command = CommandInput.text.Substring(6, CommandInput.text.Length - 6).Trim();

				string output = await RunDockerCommandAsync(command);
				Log(output);
			}
			else if (CommandInput.text.StartsWith("dism"))
			{
				string command = CommandInput.text.Substring(4, CommandInput.text.Length - 4).Trim();

				await RunDismCommandAsync(command);
			}
			else if (CommandInput.text.StartsWith("dotnet"))
			{
				string command = CommandInput.text.Substring(6, CommandInput.text.Length - 6).Trim();

				string output = await RunDotNetCommandAsync(command);
				Log(output);
			}
			SetButtonsActive(true);
		}

		#region WSL
		public async void InstallWSL()
		{
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				return;
			}

			SetButtonsActive(false);
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
			SetButtonsActive(true);
		}

		public async Task<bool> IsVirtualizationEnabledAsync()
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

		public async Task<bool> IsWSLInstalledAsync()
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

		public async Task RunDismCommandAsync(string arguments)
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
		#endregion

		#region DotNet
		public async void InstallDotNet()
		{
			SetButtonsActive(false);
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
			SetButtonsActive(true);
		}

		public async Task<bool> IsDotNetInstalledAsync()
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

		public async Task DownloadAndInstallDotNetAsync()
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

		public async Task<bool> IsDotNetEFInstalledAsync()
		{
			using (Process process = new Process())
			{
				process.StartInfo.FileName = "dotnet";
				process.StartInfo.Arguments = "tool list --global";
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.CreateNoWindow = true;

				process.Start();
				string output = await process.StandardOutput.ReadToEndAsync(); // Use asynchronous read
				await process.WaitForExitAsync(); // Use asynchronous wait

				return output.Contains("dotnet-ef");
			}
		}

		private bool pathSet = false;
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
		public async Task<string> RunDotNetCommandAsync(string arguments)
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
		public async void InstallDocker()
		{
			SetButtonsActive(false);
			if (await IsDockerInstalledAsync())
			{
				Log("Docker is already installed.");
				SetButtonsActive(true);
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

			SetButtonsActive(true);
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
		public async Task<string> RunDockerCommandAsync(string commandArgs)
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
		public async Task<string> RunDockerComposeCommandAsync(string commandArgs, Dictionary<string, string> environmentVariables = null)
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

		private async Task<bool> IsEverythingInstalled()
		{
			Log("Verifying installs... Please wait.");

			if (!await IsWSLInstalledAsync())
			{
				Log("WSL needs to be installed.");
				SetButtonsActive(true);
				return false;
			}
			if (!await IsDotNetInstalledAsync())
			{
				Log("DotNet needs to be installed.");
				SetButtonsActive(true);
				return false;
			}
			if (!await IsDotNetEFInstalledAsync())
			{
				Log("DotNet-EF needs to be installed.");
				SetButtonsActive(true);
				return false;
			}
			if (!await IsDockerInstalledAsync())
			{
				Log("Docker needs to be installed.");
				SetButtonsActive(true);
				return false;
			}
			return true;
		}

		#region Database
		public async void InstallDatabase()
		{
			SetButtonsActive(false);

			if (!await IsEverythingInstalled())
			{
				SetButtonsActive(true);
				return;
			}

			Log("Installing database...");

			string workingDirectory = GetWorkingDirectory();
			//Log(workingDirectory);

#if UNITY_EDITOR
			string setup = Path.Combine(workingDirectory, Constants.Configuration.SetupDirectory);
			//Log(setup);

			string envConfigurationPath = Path.Combine(setup, "Development");
			//Log(envConfigurationPath);

			string jsonPath = Path.Combine(envConfigurationPath, "appsettings.json");
			//Log(jsonPath);

			if (File.Exists(jsonPath))
			{
#else
			string jsonPath = Path.Combine(workingDirectory, "appsettings.json");
			//Log(jsonPath);

			if (File.Exists(jsonPath))
			{
#endif
				string jsonContent = File.ReadAllText(jsonPath);

				//Log(jsonContent);

				AppSettings appSettings = JsonUtility.FromJson<AppSettings>(jsonContent);

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
			else
			{
				Log("appsettings.json file not found.");
			}
			SetButtonsActive(true);
		}

		public async void UpdateDatabase()
		{
			SetButtonsActive(false);

			if (!await IsEverythingInstalled())
			{
				SetButtonsActive(true);
				return;
			}

			string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
			Log($"Updating the database at {timestamp}...");

			// Run 'dotnet ef database update' command
			await RunDotNetCommandAsync($"ef database update -p {Constants.Configuration.ProjectPath}  -s  {Constants.Configuration.StartupProject}");

			Log($"Database Update completed...");
			SetButtonsActive(true);
		}

		public async void CreateMigration()
		{
			SetButtonsActive(false);

			if (!await IsEverythingInstalled())
			{
				SetButtonsActive(true);
				return;
			}

			string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
			Log($"Creating a new migration {timestamp}...");

			// Run 'dotnet ef migrations add Initial' command
			await RunDotNetCommandAsync($"ef migrations add {timestamp} -p {Constants.Configuration.ProjectPath} -s {Constants.Configuration.StartupProject}");

			// Run 'dotnet ef database update' command
			await RunDotNetCommandAsync($"ef database update -p {Constants.Configuration.ProjectPath}  -s  {Constants.Configuration.StartupProject}");

			Log($"Migration completed...");
			SetButtonsActive(true);
		}
		#endregion

		public static string GetWorkingDirectory()
		{
#if UNITY_EDITOR
			return Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
#else
			return AppDomain.CurrentDomain.BaseDirectory;
#endif
		}

		public void Quit()
		{
#if UNITY_EDITOR
			EditorApplication.ExitPlaymode();
#else
			Application.Quit();
#endif
		}
	}
}